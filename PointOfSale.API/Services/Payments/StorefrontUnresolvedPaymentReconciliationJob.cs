using System.Data;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Interfaces.Payments;
using PointOfSale.Core.Payments;
using PointOfSale.Infrastructure.Data;
using PointOfSale.API.Services.Payments.Paymob;

namespace PointOfSale.API.Services.Payments;

public sealed class StorefrontUnresolvedPaymentReconciliationJob
{
    private const int BatchSize = 50;

    private readonly ApplicationDbContext _dbContext;
    private readonly IPaymentWebhookTenantResolver _tenantResolver;
    private readonly IPaymentLifecycleService _paymentLifecycleService;
    private readonly IAccountingOutboxService _accountingOutbox;
    private readonly IPaymobSensitiveArtifactProtector _paymobArtifactProtector;
    private readonly ILogger<StorefrontUnresolvedPaymentReconciliationJob> _logger;

    public StorefrontUnresolvedPaymentReconciliationJob(
        ApplicationDbContext dbContext,
        IPaymentWebhookTenantResolver tenantResolver,
        IPaymentLifecycleService paymentLifecycleService,
        IAccountingOutboxService accountingOutbox,
        IPaymobSensitiveArtifactProtector paymobArtifactProtector,
        ILogger<StorefrontUnresolvedPaymentReconciliationJob> logger)
    {
        _dbContext = dbContext;
        _tenantResolver = tenantResolver;
        _paymentLifecycleService = paymentLifecycleService;
        _accountingOutbox = accountingOutbox;
        _paymobArtifactProtector = paymobArtifactProtector;
        _logger = logger;
    }

    public async Task<int> ExecuteAsync(CancellationToken cancellationToken)
    {
        var candidateIds = await _dbContext.PaymentWebhookLogs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(log =>
                !log.IsDeleted &&
                log.ProcessingStatus == WebhookProcessingStatus.Completed &&
                log.ProcessingError != null &&
                (log.ProcessingError == "Payment intent not found - Routed to Suspense Ledger" ||
                 EF.Functions.Like(log.ProcessingError, "UNRESOLVED_SUSPENSE_ROUTED%")))
            .OrderBy(log => log.CreatedAt)
            .Select(log => log.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        var reconciledCount = 0;

        foreach (var candidateId in candidateIds)
        {
            _dbContext.ChangeTracker.Clear();
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            var candidate = await LoadWebhookLogForRecoveryAsync(candidateId, cancellationToken);
            if (candidate == null)
            {
                await transaction.CommitAsync(cancellationToken);
                continue;
            }

            string recoveryPayload;
            try
            {
                if (string.Equals(candidate.Provider, "paymob", StringComparison.OrdinalIgnoreCase))
                {
                    if (candidate.RawPayload.StartsWith(
                            PaymobSensitiveArtifactProtector.WebhookPayloadPrefix,
                            StringComparison.Ordinal))
                    {
                        recoveryPayload = _paymobArtifactProtector.UnprotectWebhookPayload(candidate.RawPayload);
                    }
                    else
                    {
                        recoveryPayload = candidate.RawPayload;
                        candidate.RawPayload = _paymobArtifactProtector.ProtectWebhookPayload(recoveryPayload);
                        if (!string.IsNullOrWhiteSpace(candidate.Signature) &&
                            !candidate.Signature.StartsWith(
                                PaymobSensitiveArtifactProtector.SignatureDigestPrefix,
                                StringComparison.Ordinal))
                        {
                            candidate.Signature = _paymobArtifactProtector.DigestHmacSignature(candidate.Signature);
                        }

                        await _dbContext.SaveChangesAsync(cancellationToken);
                    }
                }
                else
                {
                    recoveryPayload = candidate.RawPayload;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "[StorefrontPaymentRecovery] Protected Paymob webhook payload could not be decrypted | EventId: {EventId}",
                    candidate.ProviderEventId);
                await MarkManualReviewAsync(
                    candidate,
                    "PAYMOB_WEBHOOK_EVIDENCE_UNAVAILABLE",
                    "Protected Paymob webhook evidence could not be decrypted.",
                    cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                continue;
            }

            var webhookEvent = StorefrontPaymentWebhookParser.Parse(candidate.Provider, recoveryPayload, _logger);
            if (webhookEvent == null || !StorefrontPaymentWebhookParser.IsSuccessEvent(webhookEvent.EventType))
            {
                await MarkManualReviewAsync(
                    candidate,
                    "WEBHOOK_PARSE_FAILED",
                    "Could not parse the routed suspense webhook payload into a success payment event.",
                    cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                continue;
            }

            var resolution = await _tenantResolver.ResolveAsync(candidate.Provider, recoveryPayload, cancellationToken);
            if (resolution == null)
            {
                _logger.LogDebug(
                    "[StorefrontPaymentRecovery] Suspense-routed webhook still unresolved | Provider: {Provider} | EventId: {EventId}",
                    candidate.Provider,
                    candidate.ProviderEventId);
                await transaction.CommitAsync(cancellationToken);
                continue;
            }

            var paymentIntent = await LoadPaymentIntentForRecoveryAsync(
                resolution.TenantId,
                resolution.PaymentIntentId,
                cancellationToken);
            var order = paymentIntent == null
                ? null
                : await LoadOrderForRecoveryAsync(paymentIntent.TenantId, paymentIntent.OrderId, cancellationToken);

            if (paymentIntent == null || order == null || order.IsDeleted)
            {
                // Payment intent or order is missing/deleted - mark for manual review to prevent retry blocking
                await MarkManualReviewAsync(
                    candidate,
                    "PAYMENT_INTENT_OR_ORDER_MISSING",
                    paymentIntent == null
                        ? $"Payment intent {resolution.PaymentIntentId} not found or deleted."
                        : $"Order for payment intent {resolution.PaymentIntentId} not found or deleted.",
                    cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                continue;
            }

            var providerGateway = paymentIntent.Provider.ToString().ToLowerInvariant();

            if (!PaymentStatusAuthority.IsPaidState(order.PaymentStatus))
            {
                if (!webhookEvent.Amount.HasValue || string.IsNullOrWhiteSpace(webhookEvent.Currency))
                {
                    await MarkManualReviewAsync(
                        candidate,
                        "MISSING_PROVIDER_AUTHORITY",
                        "Suspense-routed webhook became resolvable, but the provider amount/currency is missing so POS cannot safely finalize the order automatically.",
                        cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    continue;
                }

                var lifecycleResult = await _paymentLifecycleService.HandlePaymentResultAsync(
                    new HandlePaymentResultRequest
                    {
                        OrderId = order.Id,
                        PaymentIntentId = paymentIntent.Id,
                        Outcome = PaymentLifecycleOutcome.Succeeded,
                        Source = PaymentLifecycleSource.GatewayWebhook,
                        Gateway = providerGateway,
                        PaymentReference = webhookEvent.ProviderIntentId ??
                                           paymentIntent.ProviderIntentId ??
                                           paymentIntent.PaymentReference,
                        ProviderPaymentId = webhookEvent.ProviderPaymentId,
                        ProviderAmount = webhookEvent.Amount,
                        ProviderCurrency = webhookEvent.Currency,
                        WebhookEventId = candidate.ProviderEventId
                    },
                    cancellationToken);

                if (!lifecycleResult.Success && !lifecycleResult.RequiresManualReview)
                {
                    _logger.LogWarning(
                        "[StorefrontPaymentRecovery] Lifecycle replay for suspense-routed webhook did not succeed yet | Provider: {Provider} | EventId: {EventId} | OrderId: {OrderId} | Error: {Error}",
                        candidate.Provider,
                        candidate.ProviderEventId,
                        order.Id,
                        lifecycleResult.ErrorMessage);
                    continue;
                }

                if (lifecycleResult.RequiresManualReview)
                {
                    await MarkManualReviewAsync(
                        candidate,
                        lifecycleResult.FailureCode ?? "MANUAL_REVIEW_REQUIRED",
                        lifecycleResult.ErrorMessage,
                        cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    continue;
                }

                await _dbContext.Entry(order).ReloadAsync(cancellationToken);
            }

            if (!PaymentStatusAuthority.IsPaidState(order.PaymentStatus))
            {
                await transaction.CommitAsync(cancellationToken);
                continue;
            }

            await _accountingOutbox.EnqueueResolvedUnresolvedOnlinePaymentAsync(
                resolution.TenantId,
                order.Id,
                webhookEvent.Amount ?? paymentIntent.Amount,
                providerGateway,
                candidate.ProviderEventId,
                cancellationToken);

            candidate.PaymentIntentId = paymentIntent.Id;
            candidate.OrderId = order.Id;
            candidate.ProcessedAt = DateTime.UtcNow;
            candidate.ProcessingError = StorefrontUnresolvedPaymentLogState.ClearedToOrder(order.Id, order.OrderNumber);
            candidate.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            reconciledCount++;
        }

        return reconciledCount;
    }

    private async Task<PaymentWebhookLog?> LoadWebhookLogForRecoveryAsync(
        Guid webhookLogId,
        CancellationToken cancellationToken)
    {
        var providerName = _dbContext.Database.ProviderName ?? string.Empty;
        PaymentWebhookLog? log;

        if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            log = await _dbContext.PaymentWebhookLogs
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM ""PaymentWebhookLogs""
                    WHERE ""Id"" = {webhookLogId}
                      AND ""IsDeleted"" = FALSE
                    FOR UPDATE")
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(cancellationToken);
        }
        else if (providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) ||
                 providerName.Contains("MicrosoftSqlServer", StringComparison.OrdinalIgnoreCase))
        {
            log = await _dbContext.PaymentWebhookLogs
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM [PaymentWebhookLogs] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [Id] = {webhookLogId}
                      AND [IsDeleted] = 0")
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(cancellationToken);
        }
        else
        {
            log = await _dbContext.PaymentWebhookLogs
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(item => item.Id == webhookLogId && !item.IsDeleted, cancellationToken);
        }

        return log != null && IsRecoverableSuspenseLog(log)
            ? log
            : null;
    }

    private async Task<PaymentIntent?> LoadPaymentIntentForRecoveryAsync(
        Guid tenantId,
        Guid paymentIntentId,
        CancellationToken cancellationToken)
    {
        var providerName = _dbContext.Database.ProviderName ?? string.Empty;

        if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            return await _dbContext.PaymentIntents
                .FromSqlInterpolated($@"
                    SELECT xmin, *
                    FROM ""PaymentIntents""
                    WHERE ""Id"" = {paymentIntentId}
                      AND ""TenantId"" = {tenantId}
                      AND ""IsDeleted"" = FALSE
                    FOR UPDATE")
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("MicrosoftSqlServer", StringComparison.OrdinalIgnoreCase))
        {
            return await _dbContext.PaymentIntents
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM [PaymentIntents] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [Id] = {paymentIntentId}
                      AND [TenantId] = {tenantId}
                      AND [IsDeleted] = 0")
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(cancellationToken);
        }

        return await _dbContext.PaymentIntents
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                item => item.Id == paymentIntentId &&
                        item.TenantId == tenantId &&
                        !item.IsDeleted,
                cancellationToken);
    }

    private async Task<Order?> LoadOrderForRecoveryAsync(
        Guid tenantId,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var providerName = _dbContext.Database.ProviderName ?? string.Empty;

        if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            return await _dbContext.Orders
                .FromSqlInterpolated($@"
                    SELECT xmin, *
                    FROM ""Orders""
                    WHERE ""Id"" = {orderId}
                      AND ""TenantId"" = {tenantId}
                      AND ""IsDeleted"" = FALSE
                    FOR UPDATE")
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("MicrosoftSqlServer", StringComparison.OrdinalIgnoreCase))
        {
            return await _dbContext.Orders
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM [Orders] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [Id] = {orderId}
                      AND [TenantId] = {tenantId}
                      AND [IsDeleted] = 0")
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(cancellationToken);
        }

        return await _dbContext.Orders
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                item => item.Id == orderId &&
                        item.TenantId == tenantId &&
                        !item.IsDeleted,
                cancellationToken);
    }

    private static bool IsRecoverableSuspenseLog(PaymentWebhookLog log) =>
        !log.IsDeleted &&
        log.ProcessingStatus == WebhookProcessingStatus.Completed &&
        log.ProcessingError != null &&
        (log.ProcessingError == "Payment intent not found - Routed to Suspense Ledger" ||
         log.ProcessingError.StartsWith("UNRESOLVED_SUSPENSE_ROUTED", StringComparison.Ordinal));

    private async Task MarkManualReviewAsync(
        PaymentWebhookLog trackedLog,
        string failureCode,
        string? message,
        CancellationToken cancellationToken)
    {
        trackedLog.ProcessedAt = DateTime.UtcNow;
        trackedLog.ProcessingError = StorefrontUnresolvedPaymentLogState.ManualReview(failureCode, message);
        trackedLog.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
