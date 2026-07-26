using System.Data;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Financial;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.CommerceHub;

public sealed record CommerceHubFinanceReconciliationResult(
    string Status,
    decimal PlatformNetBalance,
    decimal VendorsNetBalance,
    int TotalTransactions,
    int MissingIdempotencyKeyCount,
    int MissingCorrelationIdCount,
    int DuplicateIdempotencyKeyCount,
    int DuplicateReferenceConstraintCount,
    int MissingPairCount,
    int DuplicateEntryCount,
    int CorrelationIntegrityMismatchCount,
    int PayableMismatchCount,
    int ReceivableMismatchCount,
    int OrphanTransactionsCount,
    int OrderFlowMismatchCount,
    int RefundFlowMismatchCount,
    int PayoutFlowMismatchCount,
    int AdjustmentFlowMismatchCount,
    int MultiCurrencyReferenceCount,
    int InvalidEntityScopeCount,
    DateTime ExecutedAtUtc)
{
    public Guid? LatestReportId { get; init; }
    public string LatestReportStatus { get; init; } = "Healthy";
    public int LatestReportMismatchCount { get; init; }
    public DateTime? LatestReportTimestampUtc { get; init; }
    public string? LatestReportDetailsJson { get; init; }
}

public class CommerceHubFinanceReconciliationJob
{
    private const int UnresolvedPaymentResolutionBatchSize = 100;
    private const string PendingTenantResolutionStatus = "PendingTenantResolution";
    private const string RoutedToSuspenseStatus = "RoutedToSuspense";
    private const string ResolvedToPaymentStatus = "ResolvedToPayment";
    private const string SuspenseMatchedPendingClearanceStatus = "SuspenseMatchedPaymentPendingClearance";

    private readonly ApplicationDbContext _dbContext;
    private readonly IAccountingOutboxService _accountingOutbox;
    private readonly IFinancialReconciliationService _financialReconciliationService;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<CommerceHubFinanceReconciliationJob> _logger;

    public CommerceHubFinanceReconciliationJob(
        ApplicationDbContext dbContext,
        IAccountingOutboxService accountingOutbox,
        IFinancialReconciliationService financialReconciliationService,
        IEventPublisher eventPublisher,
        ILogger<CommerceHubFinanceReconciliationJob> logger)
    {
        _dbContext = dbContext;
        _accountingOutbox = accountingOutbox;
        _financialReconciliationService = financialReconciliationService;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public async Task<CommerceHubFinanceReconciliationResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var execution = await _financialReconciliationService.RunAsync(cancellationToken);
        var result = execution.Snapshot;

        var description =
            $"Transactions={result.TotalTransactions} | PlatformNet={result.PlatformNetBalance:N2} | VendorsNet={result.VendorsNetBalance:N2} | " +
            $"MissingIdempotency={result.MissingIdempotencyKeyCount} | MissingCorrelation={result.MissingCorrelationIdCount} | " +
            $"DuplicateIdempotency={result.DuplicateIdempotencyKeyCount} | DuplicateReferenceConstraint={result.DuplicateReferenceConstraintCount} | " +
            $"MissingPairs={result.MissingPairCount} | DuplicateEntries={result.DuplicateEntryCount} | CorrelationIntegrity={result.CorrelationIntegrityMismatchCount} | " +
            $"PayableMismatch={result.PayableMismatchCount} | ReceivableMismatch={result.ReceivableMismatchCount} | " +
            $"Orphans={result.OrphanTransactionsCount} | OrderFlowMismatch={result.OrderFlowMismatchCount} | RefundFlowMismatch={result.RefundFlowMismatchCount} | " +
            $"PayoutFlowMismatch={result.PayoutFlowMismatchCount} | AdjustmentFlowMismatch={result.AdjustmentFlowMismatchCount} | " +
            $"MultiCurrencyReferences={result.MultiCurrencyReferenceCount} | InvalidEntityScope={result.InvalidEntityScopeCount} | " +
            $"LatestReportStatus={result.LatestReportStatus} | LatestReportMismatchCount={result.LatestReportMismatchCount}";

        if (result.Status == "OK")
        {
            _logger.LogInformation("CommerceHub finance reconciliation OK | {Description}", description);
        }
        else
        {
            _logger.LogWarning("CommerceHub finance reconciliation mismatch | {Description}", description);
        }

        var resolvedPayments = await ResolveLinkedUnresolvedCapturedPaymentsAsync(cancellationToken);
        if (resolvedPayments > 0)
        {
            _logger.LogInformation(
                "CommerceHub unresolved captured-payment reconciliation resolved {ResolvedCount} webhook record(s) to paid local payment rows.",
                resolvedPayments);
        }

        return result;
    }

    public async Task<int> ResolveLinkedUnresolvedCapturedPaymentsAsync(CancellationToken cancellationToken)
    {
        using var orderLifecycleBoundary = CommerceHubBoundaryMutationScope.BeginOrderLifecycle();

        var candidateIds = await _dbContext.CommerceHubUnresolvedPaymentWebhooks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(webhook =>
                !webhook.IsDeleted &&
                (webhook.Status == PendingTenantResolutionStatus || 
                 webhook.Status == RoutedToSuspenseStatus ||
                 webhook.Status == SuspenseMatchedPendingClearanceStatus) &&
                (
                    webhook.LocalPaymentId.HasValue ||
                    webhook.ProviderReference != null ||
                    webhook.ProviderIntentId != null ||
                    webhook.ProviderTransactionId != null))
            .OrderBy(webhook => webhook.CreatedAt)
            .Select(webhook => webhook.Id)
            .Take(UnresolvedPaymentResolutionBatchSize)
            .ToListAsync(cancellationToken);

        var resolvedCount = 0;

        foreach (var candidateId in candidateIds)
        {
            _dbContext.ChangeTracker.Clear();
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            try
            {
                var nowUtc = DateTime.UtcNow;
                var webhook = await LoadUnresolvedPaymentWebhookForRecoveryAsync(candidateId, cancellationToken);
                if (webhook == null)
                {
                    await transaction.CommitAsync(cancellationToken);
                    continue;
                }

                webhook.LastResolutionAttemptAtUtc = nowUtc;
                webhook.ResolutionAttemptCount += 1;

                var payment = await ResolvePaidPaymentForWebhookAsync(webhook, cancellationToken);
                if (payment == null)
                {
                    webhook.LastError = "No paid local CommerceHub payment row matched this provider-success webhook yet.";
                    webhook.UpdatedAt = nowUtc;
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    continue;
                }

                if (!PaymentAmountMatches(webhook, payment))
                {
                    webhook.LastError = $"Matched payment {payment.Id:D}, but amount/currency did not match the provider webhook.";
                    webhook.UpdatedAt = nowUtc;
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    continue;
                }

                var marketplaceOrder = await LoadMarketplaceOrderForRecoveryAsync(payment.OrderId, cancellationToken);
                if (marketplaceOrder == null)
                {
                    webhook.LastError = $"Matched payment {payment.Id:D}, but the current marketplace order could not be locked for reconciliation.";
                    webhook.UpdatedAt = nowUtc;
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    continue;
                }

                var affectedStoreIds = marketplaceOrder.GetAffectedStoreIds();
                webhook.TenantId ??= affectedStoreIds.Count == 1
                    ? affectedStoreIds[0]
                    : null;
                webhook.OrderId ??= payment.OrderId;
                webhook.LocalPaymentId ??= payment.Id;

                if (string.Equals(webhook.Status, RoutedToSuspenseStatus, StringComparison.Ordinal) ||
                    string.Equals(webhook.Status, SuspenseMatchedPendingClearanceStatus, StringComparison.Ordinal))
                {
                    var clearanceQueued = await QueueSuspenseClearanceAsync(webhook, payment, marketplaceOrder, cancellationToken);
                    if (!clearanceQueued)
                    {
                        webhook.Status = SuspenseMatchedPendingClearanceStatus;
                        webhook.LastError =
                            "Provider cash now matches a paid CommerceHub payment row, but the system could not reconstruct every tenant suspense reversal needed to clear the earlier suspense posting.";
                        webhook.UpdatedAt = nowUtc;
                        await _dbContext.SaveChangesAsync(cancellationToken);
                        await transaction.CommitAsync(cancellationToken);
                        continue;
                    }

                    await ReleaseMarketplaceHoldAsync(marketplaceOrder, cancellationToken);
                }

                webhook.Status = ResolvedToPaymentStatus;
                webhook.LastError = null;
                webhook.UpdatedAt = nowUtc;
                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                resolvedCount++;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }

        return resolvedCount;
    }

    private async Task<CommerceHubUnresolvedPaymentWebhook?> LoadUnresolvedPaymentWebhookForRecoveryAsync(
        Guid webhookId,
        CancellationToken cancellationToken)
    {
        var providerName = _dbContext.Database.ProviderName ?? string.Empty;
        CommerceHubUnresolvedPaymentWebhook? webhook;

        if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            webhook = await _dbContext.CommerceHubUnresolvedPaymentWebhooks
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM ""CommerceHubUnresolvedPaymentWebhooks""
                    WHERE ""Id"" = {webhookId}
                      AND ""IsDeleted"" = FALSE
                    FOR UPDATE")
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(cancellationToken);
        }
        else if (providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) ||
                 providerName.Contains("MicrosoftSqlServer", StringComparison.OrdinalIgnoreCase))
        {
            webhook = await _dbContext.CommerceHubUnresolvedPaymentWebhooks
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM [CommerceHubUnresolvedPaymentWebhooks] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [Id] = {webhookId}
                      AND [IsDeleted] = 0")
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(cancellationToken);
        }
        else
        {
            webhook = await _dbContext.CommerceHubUnresolvedPaymentWebhooks
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(item => item.Id == webhookId && !item.IsDeleted, cancellationToken);
        }

        return webhook != null && IsUnresolvedPaymentRecoveryCandidate(webhook)
            ? webhook
            : null;
    }

    private async Task<CommerceHubPayment?> ResolvePaidPaymentForWebhookAsync(
        CommerceHubUnresolvedPaymentWebhook webhook,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.CommerceHubPayments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(payment =>
                payment.Provider == webhook.Provider &&
                payment.Status == CommerceHubPaymentRecordStatus.Paid &&
                payment.IsProcessed &&
                !payment.IsDeleted);

        if (webhook.LocalPaymentId.HasValue)
        {
            var byLocalPaymentId = await query
                .Where(payment => payment.Id == webhook.LocalPaymentId.Value)
                .Select(payment => (Guid?)payment.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (byLocalPaymentId.HasValue)
            {
                return await LoadPaidPaymentForRecoveryAsync(byLocalPaymentId.Value, webhook, cancellationToken);
            }
        }

        if (!string.IsNullOrWhiteSpace(webhook.ProviderReference))
        {
            var byReference = await query
                .Where(payment => payment.ProviderReference == webhook.ProviderReference)
                .Select(payment => (Guid?)payment.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (byReference.HasValue)
            {
                return await LoadPaidPaymentForRecoveryAsync(byReference.Value, webhook, cancellationToken);
            }
        }

        if (!string.IsNullOrWhiteSpace(webhook.ProviderIntentId))
        {
            var byIntent = await query
                .Where(payment => payment.ProviderIntentId == webhook.ProviderIntentId)
                .Select(payment => (Guid?)payment.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (byIntent.HasValue)
            {
                return await LoadPaidPaymentForRecoveryAsync(byIntent.Value, webhook, cancellationToken);
            }
        }

        if (!string.IsNullOrWhiteSpace(webhook.ProviderTransactionId))
        {
            var byTransaction = await query
                .Where(payment =>
                    payment.ProviderTransactionId == webhook.ProviderTransactionId ||
                    payment.Attempts.Any(attempt => attempt.ProviderTransactionId == webhook.ProviderTransactionId))
                .Select(payment => (Guid?)payment.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (byTransaction.HasValue)
            {
                return await LoadPaidPaymentForRecoveryAsync(byTransaction.Value, webhook, cancellationToken);
            }
        }

        return null;
    }

    private async Task<CommerceHubPayment?> LoadPaidPaymentForRecoveryAsync(
        Guid paymentId,
        CommerceHubUnresolvedPaymentWebhook webhook,
        CancellationToken cancellationToken)
    {
        await LockCommerceHubPaymentForRecoveryAsync(paymentId, cancellationToken);

        var payment = await _dbContext.CommerceHubPayments
            .IgnoreQueryFilters()
            .Include(item => item.Attempts)
            .FirstOrDefaultAsync(
                item => item.Id == paymentId &&
                        item.Provider == webhook.Provider &&
                        item.Status == CommerceHubPaymentRecordStatus.Paid &&
                        item.IsProcessed &&
                        !item.IsDeleted,
                cancellationToken);

        if (payment == null)
        {
            return null;
        }

        if (webhook.LocalPaymentId.HasValue && payment.Id == webhook.LocalPaymentId.Value)
        {
            return payment;
        }

        if (!string.IsNullOrWhiteSpace(webhook.ProviderReference) &&
            string.Equals(payment.ProviderReference, webhook.ProviderReference, StringComparison.Ordinal))
        {
            return payment;
        }

        if (!string.IsNullOrWhiteSpace(webhook.ProviderIntentId) &&
            string.Equals(payment.ProviderIntentId, webhook.ProviderIntentId, StringComparison.Ordinal))
        {
            return payment;
        }

        if (!string.IsNullOrWhiteSpace(webhook.ProviderTransactionId) &&
            (string.Equals(payment.ProviderTransactionId, webhook.ProviderTransactionId, StringComparison.Ordinal) ||
             payment.Attempts.Any(attempt => string.Equals(attempt.ProviderTransactionId, webhook.ProviderTransactionId, StringComparison.Ordinal))))
        {
            return payment;
        }

        return null;
    }

    private async Task LockCommerceHubPaymentForRecoveryAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        var providerName = _dbContext.Database.ProviderName ?? string.Empty;
        if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $@"SELECT 1 FROM ""CommerceHubPayments""
                   WHERE ""Id"" = {paymentId}
                   FOR UPDATE",
                cancellationToken);
            return;
        }

        if (providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("MicrosoftSqlServer", StringComparison.OrdinalIgnoreCase))
        {
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $@"SELECT 1 FROM [CommerceHubPayments] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                   WHERE [Id] = {paymentId}",
                cancellationToken);
        }
    }

    private async Task<CommerceHubMarketplaceOrder?> LoadMarketplaceOrderForRecoveryAsync(
        Guid marketplaceOrderId,
        CancellationToken cancellationToken)
    {
        var providerName = _dbContext.Database.ProviderName ?? string.Empty;
        CommerceHubMarketplaceOrder? marketplaceOrder;

        if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            marketplaceOrder = await _dbContext.CommerceHubMarketplaceOrders
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM ""CommerceHubMarketplaceOrders""
                    WHERE ""Id"" = {marketplaceOrderId}
                      AND ""IsDeleted"" = FALSE
                    FOR UPDATE")
                .IgnoreQueryFilters()
                .Include(order => order.SubOrders)
                .FirstOrDefaultAsync(cancellationToken);
        }
        else if (providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) ||
                 providerName.Contains("MicrosoftSqlServer", StringComparison.OrdinalIgnoreCase))
        {
            marketplaceOrder = await _dbContext.CommerceHubMarketplaceOrders
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM [CommerceHubMarketplaceOrders] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [Id] = {marketplaceOrderId}
                      AND [IsDeleted] = 0")
                .IgnoreQueryFilters()
                .Include(order => order.SubOrders)
                .FirstOrDefaultAsync(cancellationToken);
        }
        else
        {
            marketplaceOrder = await _dbContext.CommerceHubMarketplaceOrders
                .IgnoreQueryFilters()
                .Include(order => order.SubOrders)
                .FirstOrDefaultAsync(order => order.Id == marketplaceOrderId && !order.IsDeleted, cancellationToken);
        }

        return marketplaceOrder;
    }

    private static bool IsUnresolvedPaymentRecoveryCandidate(CommerceHubUnresolvedPaymentWebhook webhook) =>
        !webhook.IsDeleted &&
        (string.Equals(webhook.Status, PendingTenantResolutionStatus, StringComparison.Ordinal) ||
         string.Equals(webhook.Status, RoutedToSuspenseStatus, StringComparison.Ordinal) ||
         string.Equals(webhook.Status, SuspenseMatchedPendingClearanceStatus, StringComparison.Ordinal)) &&
        (webhook.LocalPaymentId.HasValue ||
         webhook.ProviderReference != null ||
         webhook.ProviderIntentId != null ||
         webhook.ProviderTransactionId != null);

    private static bool PaymentAmountMatches(CommerceHubUnresolvedPaymentWebhook webhook, CommerceHubPayment payment)
    {
        if (!TryRequireRecoveryEgpCurrency(
                webhook.Currency,
                "commercehub.unresolved_payment_recovery.webhook.currency",
                $"webhookId:{webhook.Id:D};provider:{webhook.Provider}",
                out var webhookCurrency) ||
            !TryRequireRecoveryEgpCurrency(
                payment.Currency,
                "commercehub.unresolved_payment_recovery.payment.currency",
                $"paymentId:{payment.Id:D};orderId:{payment.OrderId:D}",
                out var paymentCurrency))
        {
            return false;
        }

        return string.Equals(webhookCurrency, paymentCurrency, StringComparison.Ordinal) &&
               Math.Round(webhook.Amount, 2, MidpointRounding.AwayFromZero) ==
               Math.Round(payment.Amount, 2, MidpointRounding.AwayFromZero);
    }

    private static bool TryRequireRecoveryEgpCurrency(
        string? currency,
        string component,
        string context,
        out string normalizedCurrency)
    {
        try
        {
            normalizedCurrency = FinancialCurrencyGuard.RequireEgpCurrency(currency, component, context);
            return true;
        }
        catch (InvalidOperationException)
        {
            normalizedCurrency = string.Empty;
            return false;
        }
    }

    private async Task<bool> QueueSuspenseClearanceAsync(
        CommerceHubUnresolvedPaymentWebhook webhook,
        CommerceHubPayment payment,
        CommerceHubMarketplaceOrder marketplaceOrder,
        CancellationToken cancellationToken)
    {
        var allocations = BuildSuspenseAllocations(webhook, marketplaceOrder);
        if (allocations.Count == 0)
        {
            return false;
        }

        var providerCode = payment.Provider.ToString().ToLowerInvariant();
        foreach (var allocation in allocations)
        {
            await _accountingOutbox.EnqueueResolvedUnresolvedCommerceHubPaymentAsync(
                allocation.TenantId,
                payment.Id,
                allocation.Amount,
                providerCode,
                webhook.ProviderEventId,
                cancellationToken);
        }

        return true;
    }

    private async Task ReleaseMarketplaceHoldAsync(
        CommerceHubMarketplaceOrder marketplaceOrder,
        CancellationToken cancellationToken)
    {
        if (marketplaceOrder.IsDeleted || marketplaceOrder.Status != OrderStatus.OnHold)
        {
            return;
        }

        marketplaceOrder.Status = CommerceHubOrderStateHelper.AggregateMarketplaceStatus(
            marketplaceOrder.SubOrders,
            CommerceHubOrderStateHelper.ConfirmAfterPayment(marketplaceOrder.Status));
        marketplaceOrder.TouchProjectionMutationVersion();

        var affectedStoreIds = marketplaceOrder.GetAffectedStoreIds();
        if (affectedStoreIds.Count == 0)
        {
            return;
        }

        var eventVersion = CommerceHubOrderEventMetadata.ResolveAggregateEventVersion(marketplaceOrder);
        await _eventPublisher.PublishAsync(new CommerceHubMarketplaceOrderStatusUpdatedEvent
        {
            MarketplaceOrderId = marketplaceOrder.Id,
            TenantId = affectedStoreIds.First(),
            AffectedStoreIds = affectedStoreIds,
            CorrelationId = $"commercehub-unresolved-payment-clearance:{marketplaceOrder.Id:N}",
            IdempotencyKey = CommerceHubOrderEventMetadata.BuildMarketplacePaymentIdempotencyKey(
                marketplaceOrder,
                eventVersion),
            OccurredAtUtc = DateTime.UtcNow,
            EventVersion = eventVersion
        }, cancellationToken);
    }

    private static List<SuspenseAllocation> BuildSuspenseAllocations(
        CommerceHubUnresolvedPaymentWebhook webhook,
        CommerceHubMarketplaceOrder marketplaceOrder)
    {
        var groupedTotals = marketplaceOrder.SubOrders
            .Where(subOrder => !subOrder.IsDeleted && subOrder.StoreId != Guid.Empty)
            .GroupBy(subOrder => subOrder.StoreId)
            .Select(group => new
            {
                TenantId = group.Key,
                Total = group.Sum(subOrder => Math.Max(0m, subOrder.Total))
            })
            .Where(row => row.Total > 0m)
            .OrderBy(row => row.TenantId)
            .ToList();

        if (groupedTotals.Count > 0)
        {
            var total = groupedTotals.Sum(row => row.Total);
            var allocated = 0m;
            var allocations = new List<SuspenseAllocation>(groupedTotals.Count);
            for (var index = 0; index < groupedTotals.Count; index++)
            {
                var row = groupedTotals[index];
                var amount = index == groupedTotals.Count - 1
                    ? Math.Round(webhook.Amount - allocated, 2, MidpointRounding.AwayFromZero)
                    : Math.Round(webhook.Amount * (row.Total / total), 2, MidpointRounding.AwayFromZero);

                allocated += amount;
                if (amount > 0m)
                {
                    allocations.Add(new SuspenseAllocation(row.TenantId, amount));
                }
            }

            return allocations;
        }

        return ReconstructSuspenseAllocationsFromJson(webhook.Amount, webhook.SuspenseRoutedStoreIdsJson);
    }

    private static List<SuspenseAllocation> ReconstructSuspenseAllocationsFromJson(
        decimal amount,
        string? suspenseRoutedStoreIdsJson)
    {
        if (string.IsNullOrWhiteSpace(suspenseRoutedStoreIdsJson) || suspenseRoutedStoreIdsJson == "[]")
        {
            return [];
        }

        try
        {
            var routedTenantIds = System.Text.Json.JsonSerializer.Deserialize<List<Guid>>(suspenseRoutedStoreIdsJson);
            if (routedTenantIds == null)
            {
                return [];
            }

            var validTenantIds = routedTenantIds
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();

            if (validTenantIds.Count == 0)
            {
                return [];
            }

            var equalShare = Math.Round(amount / validTenantIds.Count, 2, MidpointRounding.ToZero);
            var remainder = amount - (equalShare * validTenantIds.Count);

            return validTenantIds
                .Select((tenantId, index) => new SuspenseAllocation(
                    tenantId,
                    index == 0 ? equalShare + remainder : equalShare))
                .ToList();
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }

    private sealed record SuspenseAllocation(Guid TenantId, decimal Amount);
}
