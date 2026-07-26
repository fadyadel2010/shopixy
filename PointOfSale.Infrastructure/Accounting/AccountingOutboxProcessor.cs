using System.Diagnostics;
using System.Globalization;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Core.Models.Refunds;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.Reliability;
using MarketplaceEvents = PointOfSale.Core.Events.Marketplace;

namespace PointOfSale.Infrastructure.Accounting;

/// <summary>
/// Processor for accounting outbox messages.
/// 
/// PHASE 3.5: Background worker processes pending messages.
/// 
/// Features:
/// - Batch processing for efficiency
/// - Exponential backoff on failures
/// - Idempotency double-check
/// - Audit trail creation
/// - Metrics emission
/// </summary>
public class AccountingOutboxProcessor : IAccountingOutboxProcessor
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AccountingOutboxProcessor> _logger;
    private readonly AccountingOutboxOptions _options;
    private readonly IAccountingMetrics? _metrics;
    private readonly CommerceReliabilityTelemetry? _reliabilityTelemetry;

    // Static collection of financial truth event types for EF-translatable queries.
    // Keep this in lockstep with DispatchMessageAsync: any dispatched event that can
    // post money, debt, cash, expense, purchase, settlement, refund, or adjustment
    // journals must remain live until posted or explicitly reconciled.
    private static readonly HashSet<string> _criticalFinancialTruthEventTypes = new(StringComparer.Ordinal)
    {
        "PosSale",
        "SaleReversal",
        "PosPurchase",
        "PurchaseReversal",
        "SupplierPayment",
        "SupplierOpeningBalance",
        "CustomerOpeningBalance",
        "PartyLedgerAdjustment",
        "PosRefund",
        "PurchaseReturn",
        "PosExpense",
        "ExpenseReversal",
        "OnlinePayment",
        "CodDelivered",
        "WalletApproved",
        "EcommerceRefund",
        "UnresolvedOnlinePayment",
        "ResolvedUnresolvedOnlinePayment",
        "ResolvedUnresolvedCommerceHubPayment",
        "GatewaySettlement",
        "CodSettlement",
        "CommerceHubLedgerPosting"
    };

    // S40: Expose immutable view to prevent runtime mutation
    public static IReadOnlyCollection<string> CriticalFinancialTruthEventTypes => _criticalFinancialTruthEventTypes;

    private static readonly Expression<Func<AccountingOutboxMessage, AccountingOutboxMessage, bool>> MessageOrderingComparisonExpression =
        (candidate, message) =>
            candidate.Id != message.Id &&
            candidate.TenantId == message.TenantId &&
            candidate.BusinessEntityId == message.BusinessEntityId &&
            candidate.ProcessedAtUtc == null &&
            (!candidate.IsFailed || CriticalFinancialTruthEventTypes.Contains(candidate.EventType)) &&
            (!message.AggregateVersion.HasValue
                ? (candidate.OccurredAtUtc < message.OccurredAtUtc ||
                   (candidate.OccurredAtUtc == message.OccurredAtUtc && candidate.CreatedAt < message.CreatedAt) ||
                   (candidate.OccurredAtUtc == message.OccurredAtUtc &&
                    candidate.CreatedAt == message.CreatedAt &&
                    candidate.Id.CompareTo(message.Id) < 0))
                : (!candidate.AggregateVersion.HasValue &&
                   (candidate.OccurredAtUtc < message.OccurredAtUtc ||
                    (candidate.OccurredAtUtc == message.OccurredAtUtc && candidate.CreatedAt < message.CreatedAt) ||
                    (candidate.OccurredAtUtc == message.OccurredAtUtc &&
                     candidate.CreatedAt == message.CreatedAt &&
                     candidate.Id.CompareTo(message.Id) < 0))) ||
                  (candidate.AggregateVersion.HasValue &&
                   (candidate.AggregateVersion.Value < message.AggregateVersion!.Value ||
                    (candidate.AggregateVersion.Value == message.AggregateVersion.Value &&
                     candidate.OccurredAtUtc < message.OccurredAtUtc) ||
                    (candidate.AggregateVersion.Value == message.AggregateVersion.Value &&
                     candidate.OccurredAtUtc == message.OccurredAtUtc &&
                     candidate.CreatedAt < message.CreatedAt) ||
                    (candidate.AggregateVersion.Value == message.AggregateVersion.Value &&
                     candidate.OccurredAtUtc == message.OccurredAtUtc &&
                     candidate.CreatedAt == message.CreatedAt &&
                     candidate.Id.CompareTo(message.Id) < 0))));

    private sealed record PendingAccountingOutboxMessage(
        Guid Id,
        Guid TenantId,
        string EventType,
        Guid BusinessEntityId,
        string IdempotencyKey,
        int? AggregateVersion);
    
    public AccountingOutboxProcessor(
        IServiceProvider serviceProvider,
        ILogger<AccountingOutboxProcessor> logger,
        IOptions<AccountingOutboxOptions>? options = null,
        IAccountingMetrics? metrics = null,
        CommerceReliabilityTelemetry? reliabilityTelemetry = null)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _options = options?.Value ?? new AccountingOutboxOptions();
        _metrics = metrics;
        _reliabilityTelemetry = reliabilityTelemetry;
    }
    
    public async Task<int> ProcessPendingMessagesAsync(int batchSize = 50, CancellationToken ct = default)
    {
        using var batchScope = _serviceProvider.CreateScope();
        var batchDb = batchScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var effectiveBatchSize = batchSize > 0 ? batchSize : _options.BatchSize;
        var nowUtc = DateTime.UtcNow;
        var pendingMessageFilter = BuildPendingMessageFilter(batchDb, nowUtc);

        var pendingMessages = await batchDb.AccountingOutboxMessages
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(pendingMessageFilter)
            .OrderBy(m => m.TenantId)
            .ThenBy(m => m.BusinessEntityId)
            .ThenBy(m => m.AggregateVersion ?? int.MaxValue)
            .ThenBy(m => m.OccurredAtUtc)
            .ThenBy(m => m.CreatedAt)
            .ThenBy(m => m.Id)
            .Take(effectiveBatchSize)
            .Select(m => new PendingAccountingOutboxMessage(
                m.Id,
                m.TenantId,
                m.EventType,
                m.BusinessEntityId,
                m.IdempotencyKey,
                m.AggregateVersion))
            .ToListAsync(ct);
        
        if (pendingMessages.Count == 0)
        {
            _logger.LogDebug("[OUTBOX-DEBUG] Check complete. Found 0 pending messages. Time: {Time}", DateTime.UtcNow);
            return 0;
        }

        _logger.LogInformation("Processing {Count} pending accounting outbox messages", pendingMessages.Count);

        var processedCount = 0;
        
        foreach (var pendingMessage in pendingMessages)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var message = await db.AccountingOutboxMessages
                .AsNoTracking()
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(entry => entry.Id == pendingMessage.Id, ct);

            if (message == null ||
                message.ProcessedAtUtc != null ||
                message.IsFailed ||
                (message.NextRetryAtUtc != null && message.NextRetryAtUtc > nowUtc) ||
                (message.ClaimedUntilUtc != null && message.ClaimedUntilUtc > nowUtc))
            {
                continue;
            }

            using var logScope = _logger.BeginScope(new Dictionary<string, object?>
            {
                ["CorrelationId"] = GetAccountingCorrelationId(message),
                ["TenantId"] = message.TenantId,
                ["EventType"] = message.EventType,
                ["BusinessEntityId"] = message.BusinessEntityId,
                ["OutboxMessageId"] = message.Id,
                ["IdempotencyKey"] = message.IdempotencyKey,
            });

            try
            {
                var strategy = db.Database.CreateExecutionStrategy();
                var claimResult = await strategy.ExecuteAsync(async () =>
                {
                    // PRODUCTION HARDENING: Reload message inside ExecuteAsync to ensure each retry
                    // sees the current DB state, avoiding stale in-memory claim state on retries.
                    // Clear any tracked entities first to force a fresh DB query
                    db.ChangeTracker.Clear();
                    
                    var freshMessage = await db.AccountingOutboxMessages
                        .IgnoreQueryFilters()
                        .FirstOrDefaultAsync(entry => entry.Id == pendingMessage.Id, ct);

                    if (freshMessage == null ||
                        freshMessage.ProcessedAtUtc != null ||
                        freshMessage.IsFailed ||
                        (freshMessage.NextRetryAtUtc != null && freshMessage.NextRetryAtUtc > nowUtc) ||
                        (freshMessage.ClaimedUntilUtc != null && freshMessage.ClaimedUntilUtc > nowUtc))
                    {
                        return ClaimOutcome.MessageNoLongerEligible;
                    }

                    await using var tx = await db.Database.BeginTransactionAsync(ct);
                    try
                    {
                        if (db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true ||
                            db.Database.ProviderName?.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            var lockId = ComputeStableAdvisoryLockKey(freshMessage.TenantId, freshMessage.BusinessEntityId);
                            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({lockId})", ct);
                        }

                        if (await HasUnprocessedEarlierAggregateVersionAsync(db, freshMessage, ct))
                        {
                            await tx.RollbackAsync(ct);
                            return ClaimOutcome.Deferred;
                        }

                        if (!freshMessage.TryClaim(_options.ProcessingLeaseSeconds))
                        {
                            await tx.RollbackAsync(ct);
                            return ClaimOutcome.AlreadyClaimed;
                        }

                        await db.SaveChangesAsync(ct);
                        await tx.CommitAsync(ct);
                        return ClaimOutcome.Claimed;
                    }
                    catch (DbUpdateConcurrencyException)
                    {
                        await tx.RollbackAsync(ct);
                        return ClaimOutcome.ConcurrencyConflict;
                    }
                    catch
                    {
                        await tx.RollbackAsync(ct);
                        throw;
                    }
                });

                if (claimResult == ClaimOutcome.MessageNoLongerEligible)
                {
                    _logger.LogDebug(
                        "Accounting outbox message {MessageId} is no longer eligible for processing",
                        message.Id);
                    continue;
                }

                if (claimResult == ClaimOutcome.Deferred)
                {
                    _logger.LogInformation(
                        "Deferring accounting outbox message {MessageId} until earlier aggregate versions are processed | TenantId: {TenantId} | EventType: {EventType} | BusinessEntityId: {BusinessEntityId} | AggregateVersion: {AggregateVersion}",
                        message.Id,
                        message.TenantId,
                        message.EventType,
                        message.BusinessEntityId,
                        message.AggregateVersion);
                    continue;
                }

                if (claimResult == ClaimOutcome.AlreadyClaimed)
                {
                    _logger.LogDebug(
                        "Accounting outbox message {MessageId} is already claimed or no longer claimable",
                        message.Id);
                    continue;
                }

                if (claimResult == ClaimOutcome.ConcurrencyConflict)
                {
                    _logger.LogDebug(
                        "Concurrency conflict processing message {MessageId} - another processor won the race",
                        message.Id);
                    continue;
                }

                // PRODUCTION HARDENING: Reload message after successful claim to get the updated state
                // (ClaimedUntilUtc, UpdatedAt, etc.) before processing
                // Clear tracked entities first to force a fresh DB query with updated claim fields
                db.ChangeTracker.Clear();
                
                message = await db.AccountingOutboxMessages
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(entry => entry.Id == pendingMessage.Id, ct);

                if (message == null)
                {
                    _logger.LogWarning(
                        "Accounting outbox message {MessageId} disappeared after claim",
                        pendingMessage.Id);
                    continue;
                }

                await ProcessMessageAsync(scope.ServiceProvider, db, message, ct);
                processedCount++;
            }
            catch (OperationCanceledException)
            {
                // Cancellation requested - rethrow to propagate shutdown
                throw;
            }
            catch (Exception)
            {
                // Exception already logged in ProcessMessageAsync with full context
                // No need to log again to avoid duplicate stack traces
            }
        }
        
        // Update metrics
        var remainingPending = await GetPendingCountAsync(ct);
        var failedCount = await GetFailedCountAsync(ct);
        _metrics?.SetOutboxPendingCount(remainingPending);
        _metrics?.SetOutboxFailedCount(failedCount);
        
        return processedCount;
    }

    private static Task<bool> HasUnprocessedEarlierAggregateVersionAsync(
        ApplicationDbContext db,
        AccountingOutboxMessage message,
        CancellationToken ct)
        => db.AccountingOutboxMessages
            .IgnoreQueryFilters()
            .AnyAsync(BuildMessageOrderingPredicate(message), ct);

    private static Expression<Func<AccountingOutboxMessage, bool>> BuildMessageOrderingPredicate(AccountingOutboxMessage message)
    {
        var candidateParameter = MessageOrderingComparisonExpression.Parameters[0];
        var messageExpression = Expression.Constant(message);
        var body = ReplaceExpressions(
            MessageOrderingComparisonExpression.Body,
            new Dictionary<Expression, Expression>
            {
                [MessageOrderingComparisonExpression.Parameters[1]] = messageExpression
            });

        return Expression.Lambda<Func<AccountingOutboxMessage, bool>>(body, candidateParameter);
    }

    private static Expression<Func<AccountingOutboxMessage, bool>> BuildPendingMessageFilter(
        ApplicationDbContext db,
        DateTime nowUtc)
    {
        _ = db;

        Expression<Func<AccountingOutboxMessage, bool>> baseEligibility = message =>
            message.ProcessedAtUtc == null &&
            !message.IsFailed &&
            (message.NextRetryAtUtc == null || message.NextRetryAtUtc <= nowUtc) &&
            (message.ClaimedUntilUtc == null || message.ClaimedUntilUtc <= nowUtc);

        // The per-message claim path performs the authoritative earlier-message check
        // inside the aggregate advisory lock. Keeping the batch prefilter simple avoids
        // provider-specific translation failures while preserving correctness.
        return baseEligibility;
    }

    private static Expression ReplaceExpressions(
        Expression expression,
        IReadOnlyDictionary<Expression, Expression> replacements) =>
        new ReplacementExpressionVisitor(replacements).Visit(expression)!;

    private static long ComputeStableAdvisoryLockKey(Guid tenantId, Guid businessEntityId)
    {
        unchecked
        {
            const ulong fnvOffsetBasis = 14695981039346656037UL;
            const ulong fnvPrime = 1099511628211UL;
            var hash = fnvOffsetBasis;

            foreach (var value in tenantId.ToByteArray())
            {
                hash ^= value;
                hash *= fnvPrime;
            }

            foreach (var value in businessEntityId.ToByteArray())
            {
                hash ^= value;
                hash *= fnvPrime;
            }

            return (long)hash;
        }
    }

    private sealed class ReplacementExpressionVisitor : ExpressionVisitor
    {
        private readonly IReadOnlyDictionary<Expression, Expression> _replacements;

        public ReplacementExpressionVisitor(IReadOnlyDictionary<Expression, Expression> replacements)
        {
            _replacements = replacements;
        }

        public override Expression? Visit(Expression? node)
        {
            if (node != null && _replacements.TryGetValue(node, out var replacement))
            {
                return replacement;
            }

            return base.Visit(node);
        }
    }

    public static bool IsCriticalFinancialTruthEvent(string eventType) =>
        CriticalFinancialTruthEventTypes.Contains(eventType);
    
    private async Task ProcessMessageAsync(
        IServiceProvider services,
        ApplicationDbContext db,
        AccountingOutboxMessage message,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            _logger.LogDebug(
                "Processing accounting outbox message {MessageId} for Tenant {TenantId} Type {EventType}",
                message.Id,
                message.TenantId,
                message.EventType);
            
            var postingService = services.GetRequiredService<IFinancialPostingService>();

            if (await TryMarkProcessedFromExistingPosFinancialTruthAsync(db, message, ct))
            {
                await MarkOrderFinancialPostingPostedAsync(
                    services,
                    db,
                    message,
                    message.JournalId,
                    ct);
                await MarkOrderRefundFinancialPostingPostedAsync(
                    services,
                    db,
                    message,
                    message.JournalId,
                    ct);
                await db.SaveChangesAsync(ct);

                stopwatch.Stop();
                _metrics?.IncrementIdempotentCalls(message.TenantId, message.EventType);
                _reliabilityTelemetry?.RecordAccountingOutboxDelivery(message.TenantId, message.EventType, "idempotent");
                _metrics?.RecordPostingLatency(stopwatch.ElapsedMilliseconds, message.EventType);
                return;
            }

            // Multi-journal commerce events must re-enter their dispatch flow on retry so
            // missing secondary journals can still be posted. Single-journal events can
            // use the cheaper pre-check.
            if (SupportsJournalIdempotencyShortcut(message.EventType))
            {
                var alreadyPosted = await postingService.ExistsByIdempotencyKeyAsync(
                    message.TenantId,
                    message.IdempotencyKey,
                    ct);

                if (alreadyPosted)
                {
                    _logger.LogInformation(
                        "Message {MessageId} already posted (idempotent), marking as processed | TenantId: {TenantId} | EventType: {EventType}",
                        message.Id,
                        message.TenantId,
                        message.EventType);

                    var existingJournal = await db.JournalEntries
                        .IgnoreQueryFilters()
                        .FirstOrDefaultAsync(j => j.TenantId == message.TenantId &&
                                                  j.IdempotencyKey == message.IdempotencyKey, ct);

                    message.MarkProcessed(existingJournal?.Id ?? Guid.Empty);
                    await MarkOrderFinancialPostingPostedAsync(
                        services,
                        db,
                        message,
                        existingJournal?.Id,
                        ct);
                    await MarkOrderRefundFinancialPostingPostedAsync(
                        services,
                        db,
                        message,
                        existingJournal?.Id,
                        ct);

                    await db.SaveChangesAsync(ct);

                    stopwatch.Stop();
                    _metrics?.IncrementIdempotentCalls(message.TenantId, message.EventType);
                    _reliabilityTelemetry?.RecordAccountingOutboxDelivery(message.TenantId, message.EventType, "idempotent");
                    _metrics?.RecordPostingLatency(stopwatch.ElapsedMilliseconds, message.EventType);
                    return;
                }
            }

            var result = await DispatchMessageAsync(services, db, message, ct);

            if (result.Success)
            {
                message.MarkProcessed(result.JournalId ?? Guid.Empty);

                await CreateAuditTrailAsync(db, message, result, ct);
                await MarkOrderFinancialPostingPostedAsync(
                    services,
                    db,
                    message,
                    result.JournalId,
                    ct);
                await MarkOrderRefundFinancialPostingPostedAsync(
                    services,
                    db,
                    message,
                    result.JournalId,
                    ct);

                _logger.LogInformation(
                    "Successfully processed outbox message {MessageId}, Journal: {JournalNumber} | TenantId: {TenantId} | EventType: {EventType} | RetryCount: {RetryCount}",
                    message.Id,
                    result.JournalNumber,
                    message.TenantId,
                    message.EventType,
                    message.AttemptCount);
            }
            else
            {
                throw new InvalidOperationException(result.ErrorMessage ?? "Posting failed");
            }

            await db.SaveChangesAsync(ct);
            
            stopwatch.Stop();
            RecordSuccessMetric(message);
            _reliabilityTelemetry?.RecordAccountingOutboxDelivery(message.TenantId, message.EventType, "processed");
            _metrics?.RecordPostingLatency(stopwatch.ElapsedMilliseconds, message.EventType);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            
            if (IsCriticalFinancialTruthEvent(message.EventType))
            {
                message.RecordNonTerminalFinancialFailure(ex.Message);
                await MarkOrderFinancialPostingFailedAsync(db, message, ex.Message, ct);
                await MarkOrderRefundFinancialPostingFailedAsync(db, message, ex.Message, ct);
            }
            else
            {
                message.RecordFailure(ex.Message, _options.MaxRetryAttempts);
            }
            await db.SaveChangesAsync(ct);
            
            _metrics?.IncrementAccountingFailures(message.TenantId, message.EventType, ex.GetType().Name);

            if (message.IsFailed)
            {
                _metrics?.IncrementOutboxDeadLettered(message.TenantId, message.EventType);
                _reliabilityTelemetry?.RecordAccountingOutboxDelivery(message.TenantId, message.EventType, "dead_lettered");
                _logger.LogError(
                    ex,
                    "Accounting outbox message moved to DLQ-equivalent failed state | MessageId: {MessageId} | TenantId: {TenantId} | EventType: {EventType} | Attempt: {Attempt} | MaxRetryAttempts: {MaxRetryAttempts} | Error: {FailureReason}",
                    message.Id,
                    message.TenantId,
                    message.EventType,
                    message.AttemptCount,
                    _options.MaxRetryAttempts,
                    message.LastError);
            }
            else
            {
                _reliabilityTelemetry?.RecordAccountingOutboxDelivery(message.TenantId, message.EventType, "retry");
                _logger.LogWarning(
                    ex,
                    "Accounting outbox message failed and will retry | MessageId: {MessageId} | TenantId: {TenantId} | EventType: {EventType} | Attempt: {Attempt} | NextRetryAtUtc: {NextRetryAtUtc} | Error: {FailureReason}",
                    message.Id,
                    message.TenantId,
                    message.EventType,
                    message.AttemptCount,
                    message.NextRetryAtUtc,
                    message.LastError);
            }
            
            throw; // Re-throw to signal failure in batch processing
        }
    }

    private static async Task<bool> TryMarkProcessedFromExistingPosFinancialTruthAsync(
        ApplicationDbContext db,
        AccountingOutboxMessage message,
        CancellationToken ct)
    {
        if (!message.JournalId.HasValue)
        {
            return false;
        }

        if (message.EventType == "PosSale")
        {
            var revenueJournalExists = await db.JournalEntries
                .IgnoreQueryFilters()
                .AnyAsync(journal =>
                    journal.Id == message.JournalId.Value &&
                    journal.TenantId == message.TenantId &&
                    journal.SourceId == message.BusinessEntityId &&
                    journal.SourceType == "SalesInvoice",
                    ct);

            if (!revenueJournalExists)
            {
                return false;
            }

            var cogsRequired = await db.SalesInvoiceItems
                .IgnoreQueryFilters()
                .AnyAsync(item =>
                    item.SalesInvoiceId == message.BusinessEntityId &&
                    item.CostAmountSnapshot.HasValue &&
                    item.CostAmountSnapshot.Value > 0m,
                    ct);

            if (cogsRequired)
            {
                var cogsJournalExists = await db.JournalEntries
                    .IgnoreQueryFilters()
                    .AnyAsync(journal =>
                        journal.TenantId == message.TenantId &&
                        journal.SourceId == message.BusinessEntityId &&
                        journal.SourceType == "SalesInvoice_COGS",
                        ct);

                if (!cogsJournalExists)
                {
                    return false;
                }
            }

            message.MarkProcessed(message.JournalId.Value);
            return true;
        }

        if (message.EventType == "PosRefund")
        {
            var refundJournalExists = await db.JournalEntries
                .IgnoreQueryFilters()
                .AnyAsync(journal =>
                    journal.Id == message.JournalId.Value &&
                    journal.TenantId == message.TenantId &&
                    journal.SourceId == message.BusinessEntityId &&
                    journal.SourceType == "Return",
                    ct);

            if (!refundJournalExists)
            {
                return false;
            }

            var cogsRequired = await db.ReturnItems
                .IgnoreQueryFilters()
                .AnyAsync(item =>
                    item.ReturnId == message.BusinessEntityId &&
                    item.CostAmountSnapshot.HasValue &&
                    item.CostAmountSnapshot.Value > 0m,
                    ct);

            if (cogsRequired)
            {
                var cogsJournalExists = await db.JournalEntries
                    .IgnoreQueryFilters()
                    .AnyAsync(journal =>
                        journal.TenantId == message.TenantId &&
                        journal.SourceId == message.BusinessEntityId &&
                        journal.SourceType == "Return_COGS",
                        ct);

                if (!cogsJournalExists)
                {
                    return false;
                }
            }

            message.MarkProcessed(message.JournalId.Value);
            return true;
        }

        return false;
    }
    
    private async Task<PostingResult> DispatchMessageAsync(
        IServiceProvider services,
        ApplicationDbContext db,
        AccountingOutboxMessage message,
        CancellationToken ct)
    {
        var commerceAccounting = services.GetRequiredService<ICommerceAccountingService>();
        var postingService = services.GetRequiredService<IFinancialPostingService>();
        var payload = JsonDocument.Parse(message.PayloadJson);
        
        return message.EventType switch
        {
            "PosSale"                   => await ProcessPosSaleAsync(db, commerceAccounting, payload, message.TenantId, ct),
            "SaleReversal"              => await ProcessSaleReversalAsync(db, commerceAccounting, postingService, payload, message.TenantId, ct),
            "PosPurchase"               => await ProcessPosPurchaseAsync(db, commerceAccounting, payload, message.TenantId, ct),
            "PurchaseReversal"          => await ProcessPurchaseReversalAsync(db, commerceAccounting, postingService, payload, message.TenantId, ct),
            "SupplierPayment"           => await ProcessSupplierPaymentAsync(db, commerceAccounting, payload, message.TenantId, ct),
            "SupplierOpeningBalance"    => await ProcessSupplierOpeningBalanceAsync(db, commerceAccounting, message, payload, message.TenantId, ct),
            "CustomerOpeningBalance"    => await ProcessCustomerOpeningBalanceAsync(db, commerceAccounting, message, payload, message.TenantId, ct),
            "PartyLedgerAdjustment"     => await ProcessPartyLedgerAdjustmentAsync(db, commerceAccounting, payload, message.TenantId, ct),
            "PosRefund"                 => await ProcessPosRefundAsync(db, commerceAccounting, payload, message.TenantId, ct),
            "PurchaseReturn"            => await ProcessPurchaseReturnAsync(db, commerceAccounting, payload, message.TenantId, ct),
            "PosExpense"                => await ProcessPosExpenseAsync(db, commerceAccounting, payload, message.TenantId, ct),
            "ExpenseReversal"           => await ProcessExpenseReversalAsync(db, commerceAccounting, postingService, payload, message.TenantId, ct),
            "OnlinePayment"             => await ProcessOnlinePaymentAsync(db, commerceAccounting, payload, message.TenantId, ct),
            "CodDelivered"              => await ProcessCodDeliveredAsync(db, commerceAccounting, payload, message.TenantId, ct),
            "WalletApproved"            => await ProcessWalletApprovedAsync(db, commerceAccounting, payload, message.TenantId, ct),
            "EcommerceRefund"           => await ProcessEcommerceRefundAsync(db, commerceAccounting, payload, message.TenantId, ct),
            "UnresolvedOnlinePayment"   => await ProcessUnresolvedPaymentAsync(commerceAccounting, payload, message.TenantId, ct),
            "ResolvedUnresolvedOnlinePayment" => await ProcessResolvedUnresolvedPaymentAsync(db, commerceAccounting, payload, message.TenantId, ct),
            "ResolvedUnresolvedCommerceHubPayment" => await ProcessResolvedUnresolvedCommerceHubPaymentAsync(commerceAccounting, payload, message.TenantId, ct),
            "GatewaySettlement"         => await ProcessGatewaySettlementAsync(db, commerceAccounting, payload, message.TenantId, ct),
            "CodSettlement"             => await ProcessCodSettlementAsync(db, commerceAccounting, payload, message.TenantId, ct),
            // CommerceHub: posts each operational ledger entry to the GL so the ReadinessGate
            // can verify accounting coverage before authorising outward money movement.
            "CommerceHubLedgerPosting"  => await ProcessCommerceHubLedgerPostingAsync(commerceAccounting, payload, message.TenantId, ct),
            _                           => throw new InvalidOperationException($"Unknown event type: {message.EventType}")
        };
    }
    
    private async Task<PostingResult> ProcessUnresolvedPaymentAsync(
        ICommerceAccountingService accounting, JsonDocument payload, Guid tenantId, CancellationToken ct)
    {
        var root = payload.RootElement;
        
        // Validate required properties with clear error messages
        if (!root.TryGetProperty("Gateway", out var gatewayElement) || gatewayElement.ValueKind == JsonValueKind.Null)
        {
            return PostingResult.Failed("Missing or null required property: Gateway");
        }
        if (gatewayElement.ValueKind != JsonValueKind.String)
        {
            return PostingResult.Failed("Missing or null required property: Gateway");
        }
        var gateway = gatewayElement.GetString();
        if (string.IsNullOrWhiteSpace(gateway))
        {
            return PostingResult.Failed("Required property 'Gateway' is empty");
        }
        
        if (!root.TryGetProperty("Amount", out var amountElement))
        {
            return PostingResult.Failed("Missing required property: Amount");
        }
        if (!amountElement.TryGetDecimal(out var amount))
        {
            return PostingResult.Failed($"Invalid decimal value for property 'Amount': {amountElement}");
        }
        
        if (!root.TryGetProperty("Currency", out var currencyElement) || currencyElement.ValueKind == JsonValueKind.Null)
        {
            return PostingResult.Failed("Missing or null required property: Currency");
        }
        if (currencyElement.ValueKind != JsonValueKind.String)
        {
            return PostingResult.Failed("Missing or null required property: Currency");
        }
        var currency = currencyElement.GetString();
        if (string.IsNullOrWhiteSpace(currency))
        {
            return PostingResult.Failed("Required property 'Currency' is empty");
        }
        
        if (!root.TryGetProperty("ProviderEventId", out var providerEventIdElement) || providerEventIdElement.ValueKind == JsonValueKind.Null)
        {
            return PostingResult.Failed("Missing or null required property: ProviderEventId");
        }
        if (providerEventIdElement.ValueKind != JsonValueKind.String)
        {
            return PostingResult.Failed("Missing or null required property: ProviderEventId");
        }
        var providerEventId = providerEventIdElement.GetString();
        if (string.IsNullOrWhiteSpace(providerEventId))
        {
            return PostingResult.Failed("Required property 'ProviderEventId' is empty");
        }
        
        return await accounting.PostUnresolvedPaymentAsync(
            tenantId, gateway, amount, currency, providerEventId, ct);
    }

    private async Task<PostingResult> ProcessResolvedUnresolvedPaymentAsync(
        ApplicationDbContext db,
        ICommerceAccountingService accounting,
        JsonDocument payload,
        Guid tenantId,
        CancellationToken ct)
    {
        var root = payload.RootElement;
        var orderId = root.GetProperty("OrderId").GetGuid();
        var amount = root.GetProperty("Amount").GetDecimal();
        
        // Handle legacy messages that may not have Gateway field (pre-upgrade outbox rows)
        // Preserve null/empty for legacy messages to maintain idempotency key lineage
        string? gateway = null;
        if (root.TryGetProperty("Gateway", out var gatewayElement) && gatewayElement.ValueKind == JsonValueKind.String)
        {
            gateway = gatewayElement.GetString();
        }
        
        var providerEventId = root.GetProperty("ProviderEventId").GetString()!;

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            try
            {
                var order = await LoadOrderForAccountingPostAsync(db, tenantId, orderId, ct);
                if (order == null)
                {
                    throw new InvalidOperationException($"Order {orderId} not found");
                }

                var result = await accounting.PostResolvedUnresolvedPaymentAsync(order, amount, gateway, providerEventId, ct);
                if (!result.Success)
                {
                    await transaction.RollbackAsync(ct);
                    return result;
                }

                await transaction.CommitAsync(ct);
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        });
    }

    private static async Task<PostingResult> ProcessResolvedUnresolvedCommerceHubPaymentAsync(
        ICommerceAccountingService accounting,
        JsonDocument payload,
        Guid tenantId,
        CancellationToken ct)
    {
        var root = payload.RootElement;
        
        // Validate required properties with clear error messages
        if (!root.TryGetProperty("PaymentId", out var paymentIdElement))
        {
            return PostingResult.Failed("Missing required property: PaymentId");
        }
        if (!paymentIdElement.TryGetGuid(out var paymentId))
        {
            return PostingResult.Failed($"Invalid Guid value for property 'PaymentId': {paymentIdElement}");
        }
        
        if (!root.TryGetProperty("Amount", out var amountElement))
        {
            return PostingResult.Failed("Missing required property: Amount");
        }
        if (!amountElement.TryGetDecimal(out var amount))
        {
            return PostingResult.Failed($"Invalid decimal value for property 'Amount': {amountElement}");
        }
        
        if (!root.TryGetProperty("Gateway", out var gatewayElement) || gatewayElement.ValueKind == JsonValueKind.Null)
        {
            return PostingResult.Failed("Missing or null required property: Gateway");
        }
        if (gatewayElement.ValueKind != JsonValueKind.String)
        {
            return PostingResult.Failed("Missing or null required property: Gateway");
        }
        var gateway = gatewayElement.GetString();
        if (string.IsNullOrWhiteSpace(gateway))
        {
            return PostingResult.Failed("Required property 'Gateway' is empty");
        }
        
        if (!root.TryGetProperty("ProviderEventId", out var providerEventIdElement) || providerEventIdElement.ValueKind == JsonValueKind.Null)
        {
            return PostingResult.Failed("Missing or null required property: ProviderEventId");
        }
        if (providerEventIdElement.ValueKind != JsonValueKind.String)
        {
            return PostingResult.Failed("Missing or null required property: ProviderEventId");
        }
        var providerEventId = providerEventIdElement.GetString();
        if (string.IsNullOrWhiteSpace(providerEventId))
        {
            return PostingResult.Failed("Required property 'ProviderEventId' is empty");
        }

        return await accounting.PostResolvedUnresolvedCommerceHubPaymentAsync(
            tenantId,
            paymentId,
            amount,
            gateway,
            providerEventId,
            ct);
    }

    private static async Task<PostingResult> ProcessCommerceHubLedgerPostingAsync(
        ICommerceAccountingService accounting,
        JsonDocument payload,
        Guid tenantId,
        CancellationToken ct)
    {
        var root            = payload.RootElement;
        var transactionId   = root.GetProperty("TransactionId").GetGuid();
        var transactionType = root.GetProperty("TransactionType").GetString()!;
        var direction       = root.GetProperty("Direction").GetString()!;
        var entityType      = root.GetProperty("EntityType").GetString()!;
        var financialBucket = root.GetProperty("FinancialBucket").GetString()!;
        var amount          = root.GetProperty("Amount").GetDecimal();
        var currency        = root.GetProperty("Currency").GetString()!;
        var description     = root.GetProperty("Description").GetString()!;
        var occurredAtUtc   = root.GetProperty("OccurredAtUtc").GetDateTime();

        return await accounting.PostCommerceHubFinancialTransactionAsync(
            tenantId,
            transactionId,
            transactionType,
            direction,
            entityType,
            financialBucket,
            amount,
            currency,
            description,
            occurredAtUtc,
            ct);
    }

    private async Task<PostingResult> ProcessPosSaleAsync(
        ApplicationDbContext db, ICommerceAccountingService accounting,
        JsonDocument payload, Guid tenantId, CancellationToken ct)
    {
        var invoiceId = payload.RootElement.GetProperty("InvoiceId").GetGuid();
        var mutationVersion = TryGetMutationVersion(payload.RootElement);

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            try
            {
                var invoice = await LoadSalesInvoiceForAccountingPostAsync(db, tenantId, invoiceId, ct);
        
                if (invoice == null)
                    throw new InvalidOperationException($"SalesInvoice {invoiceId} not found");

                if (mutationVersion.HasValue && invoice.AccountingMutationVersion != mutationVersion.Value)
                {
                    _logger.LogInformation(
                        "Skipping stale POS sale accounting message for invoice {InvoiceId}. Message version {MessageVersion}, current version {CurrentVersion}",
                        invoiceId,
                        mutationVersion.Value,
                        invoice.AccountingMutationVersion);

                    await transaction.CommitAsync(ct);
                    return PostingResult.Succeeded(Guid.Empty, "SKIPPED");
                }

                // Legacy-stale guard: reject unversioned messages if aggregate has any versioned mutations
                if (!mutationVersion.HasValue && invoice.AccountingMutationVersion > 0)
                {
                    _logger.LogInformation(
                        "Skipping legacy unversioned POS sale accounting message for invoice {InvoiceId}. Current version {CurrentVersion}",
                        invoiceId,
                        invoice.AccountingMutationVersion);

                    await transaction.CommitAsync(ct);
                    return PostingResult.Succeeded(Guid.Empty, "SKIPPED");
                }
        
                var result = await accounting.PostPosSaleAsync(invoice, ct);
                if (!result.Success)
                {
                    await transaction.RollbackAsync(ct);
                    return result;
                }

                await transaction.CommitAsync(ct);
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        });
    }

    private async Task<SalesInvoice?> LoadSalesInvoiceForAccountingPostAsync(
        ApplicationDbContext db,
        Guid tenantId,
        Guid invoiceId,
        CancellationToken ct)
    {
        IQueryable<SalesInvoice> query;

        if (IsPostgresProvider(db))
        {
            query = db.SalesInvoices
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM ""SalesInvoices""
                    WHERE ""TenantId"" = {tenantId}
                      AND ""Id"" = {invoiceId}
                      AND ""IsDeleted"" = FALSE
                    FOR UPDATE")
                .IgnoreQueryFilters();
        }
        else if (IsSqlServerProvider(db))
        {
            query = db.SalesInvoices
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM [SalesInvoices] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [TenantId] = {tenantId}
                      AND [Id] = {invoiceId}
                      AND [IsDeleted] = 0")
                .IgnoreQueryFilters();
        }
        else
        {
            query = db.SalesInvoices
                .IgnoreQueryFilters()
                .Where(invoice =>
                    invoice.TenantId == tenantId &&
                    invoice.Id == invoiceId &&
                    !invoice.IsDeleted);
        }

        var invoice = await query.FirstOrDefaultAsync(ct);
        if (invoice == null)
        {
            return null;
        }

        await db.Entry(invoice)
            .Collection(entry => entry.Items)
            .Query()
            .Include(item => item.Product)
            .Include(item => item.ProductVariant)
            .LoadAsync(ct);

        return invoice;
    }

    /// <summary>
    /// Loads a PurchaseInvoice with a database-level row lock, preventing concurrent
    /// accounting posts or reversals from racing on the same invoice.
    ///
    /// PostgreSQL : SELECT ... FOR UPDATE
    /// SQL Server : SELECT ... WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
    ///
    /// Must be called inside an active database transaction.
    /// Mirrors LoadSalesInvoiceForAccountingPostAsync.
    /// </summary>
    private static async Task<PurchaseInvoice?> LoadPurchaseInvoiceForAccountingPostAsync(
        ApplicationDbContext db,
        Guid tenantId,
        Guid invoiceId,
        CancellationToken ct)
    {
        IQueryable<PurchaseInvoice> query;

        var providerName = db.Database.ProviderName ?? string.Empty;

        if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            query = db.PurchaseInvoices
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM ""PurchaseInvoices""
                    WHERE ""TenantId"" = {tenantId}
                      AND ""Id"" = {invoiceId}
                      AND ""IsDeleted"" = FALSE
                    FOR UPDATE")
                .IgnoreQueryFilters();
        }
        else if (providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) ||
                 providerName.Contains("MicrosoftSqlServer", StringComparison.OrdinalIgnoreCase))
        {
            query = db.PurchaseInvoices
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM [PurchaseInvoices] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [TenantId] = {tenantId}
                      AND [Id] = {invoiceId}
                      AND [IsDeleted] = 0")
                .IgnoreQueryFilters();
        }
        else
        {
            // Non-production provider (e.g. in-memory, SQLite) — fall back to plain query.
            // Row-level locking is not supported but is not required outside production.
            query = db.PurchaseInvoices
                .IgnoreQueryFilters()
                .Where(invoice =>
                    invoice.TenantId == tenantId &&
                    invoice.Id == invoiceId &&
                    !invoice.IsDeleted);
        }

        var invoice = await query.FirstOrDefaultAsync(ct);
        if (invoice == null)
        {
            return null;
        }

        await db.Entry(invoice)
            .Collection(entry => entry.Items)
            .LoadAsync(ct);

        return invoice;
    }

    private static async Task<Order?> LoadOrderForAccountingPostAsync(
        ApplicationDbContext db,
        Guid tenantId,
        Guid orderId,
        CancellationToken ct)
    {
        IQueryable<Order> query;
        var providerName = db.Database.ProviderName ?? string.Empty;

        if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            query = db.Orders
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM ""Orders""
                    WHERE ""TenantId"" = {tenantId}
                      AND ""Id"" = {orderId}
                    FOR UPDATE")
                .IgnoreQueryFilters();
        }
        else if (providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) ||
                 providerName.Contains("MicrosoftSqlServer", StringComparison.OrdinalIgnoreCase))
        {
            query = db.Orders
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM [Orders] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [TenantId] = {tenantId}
                      AND [Id] = {orderId}")
                .IgnoreQueryFilters();
        }
        else
        {
            query = db.Orders
                .IgnoreQueryFilters()
                .Where(o => o.TenantId == tenantId && o.Id == orderId);
        }

        return await query.FirstOrDefaultAsync(ct);
    }

    private static async Task<AccountLedger?> LoadAccountLedgerForAccountingPostAsync(
        ApplicationDbContext db,
        Guid tenantId,
        Guid ledgerId,
        CancellationToken ct)
    {
        IQueryable<AccountLedger> query;
        var providerName = db.Database.ProviderName ?? string.Empty;

        if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            query = db.AccountLedgers
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM ""AccountLedgers""
                    WHERE ""TenantId"" = {tenantId}
                      AND ""Id"" = {ledgerId}
                      AND ""IsDeleted"" = FALSE
                    FOR UPDATE")
                .IgnoreQueryFilters();
        }
        else if (providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) ||
                 providerName.Contains("MicrosoftSqlServer", StringComparison.OrdinalIgnoreCase))
        {
            query = db.AccountLedgers
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM [AccountLedgers] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [TenantId] = {tenantId}
                      AND [Id] = {ledgerId}
                      AND [IsDeleted] = 0")
                .IgnoreQueryFilters();
        }
        else
        {
            query = db.AccountLedgers
                .IgnoreQueryFilters()
                .Where(o => o.TenantId == tenantId && o.Id == ledgerId && !o.IsDeleted);
        }

        return await query.FirstOrDefaultAsync(ct);
    }

    private static async Task<PartyLedgerAdjustment?> LoadPartyLedgerAdjustmentForAccountingPostAsync(
        ApplicationDbContext db,
        Guid tenantId,
        Guid adjustmentId,
        CancellationToken ct)
    {
        IQueryable<PartyLedgerAdjustment> query;
        var providerName = db.Database.ProviderName ?? string.Empty;

        if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            query = db.PartyLedgerAdjustments
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM ""PartyLedgerAdjustments""
                    WHERE ""TenantId"" = {tenantId}
                      AND ""Id"" = {adjustmentId}
                      AND ""IsDeleted"" = FALSE
                    FOR UPDATE")
                .IgnoreQueryFilters();
        }
        else if (providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) ||
                 providerName.Contains("MicrosoftSqlServer", StringComparison.OrdinalIgnoreCase))
        {
            query = db.PartyLedgerAdjustments
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM [PartyLedgerAdjustments] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [TenantId] = {tenantId}
                      AND [Id] = {adjustmentId}
                      AND [IsDeleted] = 0")
                .IgnoreQueryFilters();
        }
        else
        {
            query = db.PartyLedgerAdjustments
                .IgnoreQueryFilters()
                .Where(o => o.TenantId == tenantId && o.Id == adjustmentId && !o.IsDeleted);
        }

        return await query.FirstOrDefaultAsync(ct);
    }

    private static async Task<Return?> LoadReturnForAccountingPostAsync(
        ApplicationDbContext db,
        Guid tenantId,
        Guid returnId,
        CancellationToken ct)
    {
        IQueryable<Return> query;
        var providerName = db.Database.ProviderName ?? string.Empty;

        if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            query = db.Returns
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM ""Returns""
                    WHERE ""TenantId"" = {tenantId}
                      AND ""Id"" = {returnId}
                      AND ""IsDeleted"" = FALSE
                    FOR UPDATE")
                .IgnoreQueryFilters();
        }
        else if (providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) ||
                 providerName.Contains("MicrosoftSqlServer", StringComparison.OrdinalIgnoreCase))
        {
            query = db.Returns
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM [Returns] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [TenantId] = {tenantId}
                      AND [Id] = {returnId}
                      AND [IsDeleted] = 0")
                .IgnoreQueryFilters();
        }
        else
        {
            query = db.Returns
                .IgnoreQueryFilters()
                .Where(o => o.TenantId == tenantId && o.Id == returnId && !o.IsDeleted);
        }

        var returnObj = await query.FirstOrDefaultAsync(ct);
        if (returnObj != null)
        {
            await db.Entry(returnObj)
                .Collection(r => r.Items)
                .LoadAsync(ct);
            
            foreach (var item in returnObj.Items)
            {
                await db.Entry(item).Reference(i => i.Product).LoadAsync(ct);
                await db.Entry(item).Reference(i => i.ProductVariant).LoadAsync(ct);
            }

            // PRODUCTION HARDENING: Ignore query filters to ensure soft-deleted invoices
            // are visible during outbox replay, keeping behavior consistent with ResolveLegacyRefundAllocation
            await db.Entry(returnObj)
                .Reference(r => r.OriginalInvoice)
                .Query()
                .IgnoreQueryFilters()
                .LoadAsync(ct);
        }

        return returnObj;
    }

    private static async Task<SupplierPayment?> LoadSupplierPaymentForAccountingPostAsync(
        ApplicationDbContext db,
        Guid tenantId,
        Guid paymentId,
        CancellationToken ct)
    {
        IQueryable<SupplierPayment> query;
        var providerName = db.Database.ProviderName ?? string.Empty;

        if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            query = db.SupplierPayments
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM ""SupplierPayments""
                    WHERE ""TenantId"" = {tenantId}
                      AND ""Id"" = {paymentId}
                      AND ""IsDeleted"" = FALSE
                    FOR UPDATE")
                .IgnoreQueryFilters();
        }
        else if (providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) ||
                 providerName.Contains("MicrosoftSqlServer", StringComparison.OrdinalIgnoreCase))
        {
            query = db.SupplierPayments
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM [SupplierPayments] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [TenantId] = {tenantId}
                      AND [Id] = {paymentId}
                      AND [IsDeleted] = 0")
                .IgnoreQueryFilters();
        }
        else
        {
            query = db.SupplierPayments
                .IgnoreQueryFilters()
                .Where(p => p.TenantId == tenantId && p.Id == paymentId && !p.IsDeleted);
        }

        return await query.FirstOrDefaultAsync(ct);
    }

    private async Task<PostingResult> ProcessSaleReversalAsync(
        ApplicationDbContext db,
        ICommerceAccountingService accounting,
        IFinancialPostingService postingService,
        JsonDocument payload,
        Guid tenantId,
        CancellationToken ct)
    {
        var invoiceId = payload.RootElement.GetProperty("InvoiceId").GetGuid();
        var operationKey = payload.RootElement.GetProperty("OperationKey").GetString();

        if (string.IsNullOrWhiteSpace(operationKey))
            throw new InvalidOperationException("Sale update accounting payload is missing OperationKey");

        var mutationVersion = TryGetMutationVersion(payload.RootElement);
        // ═══════════════════════════════════════════════════════════════════
        // ATOMICITY FIX: Wrap reversal loop + repost in a single transaction
        // to prevent partial unwind if repost fails after some reversals succeed.
        // ═══════════════════════════════════════════════════════════════════
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            
            try
            {
                var invoice = await LoadSalesInvoiceForAccountingPostAsync(db, tenantId, invoiceId, ct);

                if (invoice == null)
                    throw new InvalidOperationException($"SalesInvoice {invoiceId} not found");

                if (mutationVersion.HasValue && invoice.AccountingMutationVersion != mutationVersion.Value)
                {
                    _logger.LogInformation(
                        "Skipping stale sale mutation accounting message for invoice {InvoiceId}. Message version {MessageVersion}, current version {CurrentVersion}",
                        invoiceId,
                        mutationVersion.Value,
                        invoice.AccountingMutationVersion);

                    await transaction.CommitAsync(ct);
                    return PostingResult.Succeeded(Guid.Empty, "SKIPPED");
                }

                if (!mutationVersion.HasValue && invoice.AccountingMutationVersion > 0)
                {
                    _logger.LogInformation(
                        "Skipping legacy unversioned sale mutation accounting message for invoice {InvoiceId}. Current version {CurrentVersion}",
                        invoiceId,
                        invoice.AccountingMutationVersion);

                    await transaction.CommitAsync(ct);
                    return PostingResult.Succeeded(Guid.Empty, "SKIPPED");
                }

                var journalsToReverse = await db.JournalEntries
                    .IgnoreQueryFilters()
                    .Where(j =>
                        j.TenantId == tenantId &&
                        j.SourceId == invoiceId &&
                        (j.SourceType == "SalesInvoice" || j.SourceType == "SalesInvoice_COGS") &&
                        j.Status == JournalStatus.Posted &&
                        (string.IsNullOrEmpty(j.IdempotencyKey) || !j.IdempotencyKey.EndsWith($":{operationKey}")))
                    .OrderBy(j => j.PostedAtUtc)
                    .ThenBy(j => j.JournalNumber)
                    .ToListAsync(ct);

                // Reverse every still-active sale journal before reposting the updated sale.
                foreach (var journal in journalsToReverse)
                {
                    await postingService.ReverseJournalAsync(
                        tenantId,
                        journal.Id,
                        $"Sale update for {invoice.InvoiceNumber} ({operationKey})",
                        ct: ct);
                }

                var result = await accounting.PostPosSaleAsync(invoice, ct, operationKey);
                
                if (!result.Success)
                {
                    await transaction.RollbackAsync(ct);
                    return result;
                }
                
                await transaction.CommitAsync(ct);
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        });
    }
    
    private async Task<PostingResult> ProcessPosPurchaseAsync(
        ApplicationDbContext db, ICommerceAccountingService accounting,
        JsonDocument payload, Guid tenantId, CancellationToken ct)
    {
        var invoiceId = payload.RootElement.GetProperty("InvoiceId").GetGuid();
        var cashAmount = payload.RootElement.TryGetProperty("CashAmount", out var cashAmountElement)
            ? (decimal?)cashAmountElement.GetDecimal()
            : null;
        var cardAmount = payload.RootElement.TryGetProperty("CardAmount", out var cardAmountElement)
            ? (decimal?)cardAmountElement.GetDecimal()
            : null;
        var mutationVersion = TryGetMutationVersion(payload.RootElement);

        // NEW-1 FIX: Wrap inside a transaction and use FOR UPDATE row-level lock on the
        // PurchaseInvoice, matching the hardened pattern used by ProcessPosSaleAsync.
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            try
            {
                var invoice = await LoadPurchaseInvoiceForAccountingPostAsync(db, tenantId, invoiceId, ct);

                if (invoice == null)
                    throw new InvalidOperationException($"PurchaseInvoice {invoiceId} not found");

                if (mutationVersion.HasValue && invoice.AccountingMutationVersion != mutationVersion.Value)
                {
                    _logger.LogInformation(
                        "Skipping stale POS purchase accounting message for invoice {InvoiceId}. Message version {MessageVersion}, current version {CurrentVersion}",
                        invoiceId,
                        mutationVersion.Value,
                        invoice.AccountingMutationVersion);

                    await transaction.CommitAsync(ct);
                    return PostingResult.Succeeded(Guid.Empty, "SKIPPED");
                }

                if (!mutationVersion.HasValue && invoice.AccountingMutationVersion > 0)
                {
                    _logger.LogInformation(
                        "Skipping legacy unversioned POS purchase accounting message for invoice {InvoiceId}. Current version {CurrentVersion}",
                        invoiceId,
                        invoice.AccountingMutationVersion);

                    await transaction.CommitAsync(ct);
                    return PostingResult.Succeeded(Guid.Empty, "SKIPPED");
                }

                var result = await accounting.PostPosPurchaseAsync(invoice, ct, null, cashAmount, cardAmount);

                if (!result.Success)
                {
                    await transaction.RollbackAsync(ct);
                    return result;
                }

                await transaction.CommitAsync(ct);
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        });
    }

    private async Task<PostingResult> ProcessPurchaseReversalAsync(
        ApplicationDbContext db,
        ICommerceAccountingService accounting,
        IFinancialPostingService postingService,
        JsonDocument payload,
        Guid tenantId,
        CancellationToken ct)
    {
        var invoiceId = payload.RootElement.GetProperty("InvoiceId").GetGuid();
        var operationKey = payload.RootElement.GetProperty("OperationKey").GetString();
        var cashAmount = payload.RootElement.TryGetProperty("CashAmount", out var cashAmountElement)
            ? (decimal?)cashAmountElement.GetDecimal()
            : null;
        var cardAmount = payload.RootElement.TryGetProperty("CardAmount", out var cardAmountElement)
            ? (decimal?)cardAmountElement.GetDecimal()
            : null;
        var mutationVersion = TryGetMutationVersion(payload.RootElement);

        if (string.IsNullOrWhiteSpace(operationKey))
            throw new InvalidOperationException("Purchase update accounting payload is missing OperationKey");

        // CRITICAL-1 FIX: The invoice load and journal reversal now happen INSIDE a single
        // transaction with a row-level lock (FOR UPDATE / UPDLOCK), eliminating the race
        // condition where two concurrent reversals could both load the same journal state
        // and double-reverse entries.  This mirrors the pattern in ProcessSaleReversalAsync.
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            try
            {
                // Lock the invoice row before reading journals so no concurrent reversal
                // can race between the journal query and the subsequent reversal writes.
                var invoice = await LoadPurchaseInvoiceForAccountingPostAsync(db, tenantId, invoiceId, ct);

                if (invoice == null)
                    throw new InvalidOperationException($"PurchaseInvoice {invoiceId} not found");

                if (mutationVersion.HasValue && invoice.AccountingMutationVersion != mutationVersion.Value)
                {
                    _logger.LogInformation(
                        "Skipping stale purchase mutation accounting message for invoice {InvoiceId}. Message version {MessageVersion}, current version {CurrentVersion}",
                        invoiceId,
                        mutationVersion.Value,
                        invoice.AccountingMutationVersion);

                    await transaction.CommitAsync(ct);
                    return PostingResult.Succeeded(Guid.Empty, "SKIPPED");
                }

                if (!mutationVersion.HasValue && invoice.AccountingMutationVersion > 0)
                {
                    _logger.LogInformation(
                        "Skipping legacy unversioned purchase mutation accounting message for invoice {InvoiceId}. Current version {CurrentVersion}",
                        invoiceId,
                        invoice.AccountingMutationVersion);

                    await transaction.CommitAsync(ct);
                    return PostingResult.Succeeded(Guid.Empty, "SKIPPED");
                }

                var journalsToReverse = await db.JournalEntries
                    .IgnoreQueryFilters()
                    .Where(j =>
                        j.TenantId == tenantId &&
                        j.SourceId == invoiceId &&
                        j.SourceType == "PurchaseInvoice" &&
                        j.Status == JournalStatus.Posted &&
                        (string.IsNullOrEmpty(j.IdempotencyKey) || !j.IdempotencyKey.EndsWith($":{operationKey}")))
                    .OrderBy(j => j.PostedAtUtc)
                    .ThenBy(j => j.JournalNumber)
                    .ToListAsync(ct);

                foreach (var journal in journalsToReverse)
                {
                    await postingService.ReverseJournalAsync(
                        tenantId,
                        journal.Id,
                        $"Purchase update for {invoice.InvoiceNumber} ({operationKey})",
                        ct: ct);
                }

                var result = await accounting.PostPosPurchaseAsync(
                    invoice,
                    ct,
                    operationKey,
                    cashAmount,
                    cardAmount);

                if (!result.Success)
                {
                    await transaction.RollbackAsync(ct);
                    return result;
                }

                await transaction.CommitAsync(ct);
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        });
    }
    
    private async Task<PostingResult> ProcessSupplierPaymentAsync(
        ApplicationDbContext db, ICommerceAccountingService accounting,
        JsonDocument payload, Guid tenantId, CancellationToken ct)
    {
        var paymentId = payload.RootElement.GetProperty("PaymentId").GetGuid();
        
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            try
            {
                var payment = await LoadSupplierPaymentForAccountingPostAsync(db, tenantId, paymentId, ct);
                if (payment == null)
                    throw new InvalidOperationException($"SupplierPayment {paymentId} not found");
                
                var result = await accounting.PostSupplierPaymentAsync(payment, ct);
                if (!result.Success)
                {
                    await transaction.RollbackAsync(ct);
                    return result;
                }

                await transaction.CommitAsync(ct);
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        });
    }

    private async Task<PostingResult> ProcessSupplierOpeningBalanceAsync(
        ApplicationDbContext db,
        ICommerceAccountingService accounting,
        AccountingOutboxMessage message,
        JsonDocument payload,
        Guid tenantId,
        CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            try
            {
                Guid? openingBalanceId = null;

                if (payload.RootElement.TryGetProperty("OpeningBalanceId", out var openingBalanceIdElement))
                {
                    openingBalanceId = openingBalanceIdElement.GetGuid();
                }
                else
                {
                    openingBalanceId = message.BusinessEntityId;
                    var exists = await db.AccountLedgers.IgnoreQueryFilters().AnyAsync(l => l.Id == openingBalanceId && l.TenantId == tenantId, ct);
                    if (!exists)
                    {
                        var supplierId = payload.RootElement.TryGetProperty("SupplierId", out var supplierIdElement)
                            ? supplierIdElement.GetGuid()
                            : message.BusinessEntityId;

                        openingBalanceId = await db.AccountLedgers
                            .IgnoreQueryFilters()
                            .Where(entry =>
                                entry.TenantId == tenantId &&
                                entry.PartyType == PartyType.Supplier &&
                                entry.PartyId == supplierId &&
                                entry.TransactionType == TransactionType.OpeningBalance)
                            .OrderByDescending(entry => entry.CreatedAt)
                            .ThenByDescending(entry => entry.Id)
                            .Select(entry => entry.Id)
                            .FirstOrDefaultAsync(ct);
                    }
                }

                if (openingBalanceId == null || openingBalanceId == Guid.Empty)
                {
                    throw new InvalidOperationException("Supplier opening balance not found");
                }

                var openingBalance = await LoadAccountLedgerForAccountingPostAsync(db, tenantId, openingBalanceId.Value, ct);
                if (openingBalance == null || openingBalance.PartyType != PartyType.Supplier || openingBalance.TransactionType != TransactionType.OpeningBalance)
                {
                    throw new InvalidOperationException($"Supplier opening balance entry {openingBalanceId} not found or invalid");
                }

                var result = await accounting.PostSupplierOpeningBalanceAsync(openingBalance, ct);
                if (!result.Success)
                {
                    await transaction.RollbackAsync(ct);
                    return result;
                }

                await transaction.CommitAsync(ct);
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        });
    }

    private async Task<PostingResult> ProcessPartyLedgerAdjustmentAsync(
        ApplicationDbContext db,
        ICommerceAccountingService accounting,
        JsonDocument payload,
        Guid tenantId,
        CancellationToken ct)
    {
        var adjustmentId = payload.RootElement.GetProperty("AdjustmentId").GetGuid();

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            try
            {
                var adjustment = await LoadPartyLedgerAdjustmentForAccountingPostAsync(db, tenantId, adjustmentId, ct);
                if (adjustment == null)
                    throw new InvalidOperationException($"PartyLedgerAdjustment {adjustmentId} not found");

                var result = await accounting.PostPartyLedgerAdjustmentAsync(adjustment, ct);
                if (!result.Success)
                {
                    await transaction.RollbackAsync(ct);
                    return result;
                }

                await transaction.CommitAsync(ct);
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        });
    }
    
    private async Task<PostingResult> ProcessPosRefundAsync(
        ApplicationDbContext db, ICommerceAccountingService accounting,
        JsonDocument payload, Guid tenantId, CancellationToken ct)
    {
        var returnId = payload.RootElement.GetProperty("ReturnId").GetGuid();

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            try
            {
                var returnObj = await LoadReturnForAccountingPostAsync(db, tenantId, returnId, ct);
                if (returnObj == null)
                    throw new InvalidOperationException($"Return {returnId} not found");

                var allocation = ResolveRefundAllocation(payload.RootElement, returnObj, returnObj.OriginalInvoice);
                var result = await accounting.PostPosRefundAsync(returnObj, allocation, ct);

                if (!result.Success)
                {
                    await transaction.RollbackAsync(ct);
                    return result;
                }

                await transaction.CommitAsync(ct);
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        });
    }

    private async Task<PostingResult> ProcessPurchaseReturnAsync(
        ApplicationDbContext db,
        ICommerceAccountingService accounting,
        JsonDocument payload,
        Guid tenantId,
        CancellationToken ct)
    {
        var returnId = payload.RootElement.GetProperty("ReturnId").GetGuid();
        var originalInvoiceId = payload.RootElement.TryGetProperty("OriginalInvoiceId", out var originalInvoiceIdProperty) &&
                                originalInvoiceIdProperty.ValueKind == JsonValueKind.String
            ? originalInvoiceIdProperty.GetGuid()
            : (Guid?)null;
        var mutationVersion = TryGetMutationVersion(payload.RootElement);

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            try
            {
                if (originalInvoiceId.HasValue)
                {
                    var invoice = await LoadPurchaseInvoiceForAccountingPostAsync(db, tenantId, originalInvoiceId.Value, ct);
                    if (invoice == null)
                    {
                        throw new InvalidOperationException($"PurchaseInvoice {originalInvoiceId.Value} not found");
                    }

                    if (mutationVersion.HasValue && invoice.AccountingMutationVersion != mutationVersion.Value)
                    {
                        _logger.LogInformation(
                            "Skipping stale purchase return accounting message for invoice {InvoiceId}. Message version {MessageVersion}, current version {CurrentVersion}",
                            originalInvoiceId.Value,
                            mutationVersion.Value,
                            invoice.AccountingMutationVersion);

                        await transaction.CommitAsync(ct);
                        return PostingResult.Succeeded(Guid.Empty, "SKIPPED");
                    }

                    if (!mutationVersion.HasValue && invoice.AccountingMutationVersion > 0)
                    {
                        _logger.LogInformation(
                            "Skipping legacy unversioned purchase return accounting message for invoice {InvoiceId}. Current version {CurrentVersion}",
                            originalInvoiceId.Value,
                            invoice.AccountingMutationVersion);

                        await transaction.CommitAsync(ct);
                        return PostingResult.Succeeded(Guid.Empty, "SKIPPED");
                    }
                }

                var returnObj = await LoadReturnForAccountingPostAsync(db, tenantId, returnId, ct);
                if (returnObj == null || returnObj.ReturnType != ReturnType.PurchaseReturn)
                    throw new InvalidOperationException($"PurchaseReturn {returnId} not found or invalid");

                var result = await accounting.PostPurchaseReturnAsync(returnObj, ct);

                if (!result.Success)
                {
                    await transaction.RollbackAsync(ct);
                    return result;
                }

                await transaction.CommitAsync(ct);
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        });
    }

    private RefundAllocation ResolveRefundAllocation(JsonElement payload, Return returnObj, SalesInvoice? originalInvoice)
    {
        var hasAnyRefundAllocationField = HasAnyRefundAllocationField(payload);
        var hasExplicitRefundAllocation = HasExplicitRefundAllocation(payload);

        if (!hasExplicitRefundAllocation)
        {
            if (hasAnyRefundAllocationField)
            {
                throw new InvalidOperationException(
                    $"POS refund payload for return {returnObj.Id} contains a partial refund allocation. " +
                    "RefundMethod, CashAmount, and CardAmount must be provided together.");
            }

            return ResolveLegacyRefundAllocation(returnObj, originalInvoice);
        }

        var refundMethod = TryParseRefundMethod(payload);
        var cashAmount = TryReadDecimal(payload, "CashAmount");
        var cardAmount = TryReadDecimal(payload, "CardAmount");
        var resolution = RefundAllocationSupport.Resolve(refundMethod, cashAmount, cardAmount);

        if (!resolution.Success)
        {
            throw new InvalidOperationException(
                $"Invalid POS refund allocation payload for return {returnObj.Id}: {resolution.ErrorMessage}");
        }

        return resolution.Allocation;
    }

    private RefundAllocation ResolveLegacyRefundAllocation(Return returnObj, SalesInvoice? originalInvoice)
    {
        if (originalInvoice == null)
        {
            throw new InvalidOperationException(
                $"POS refund outbox payload for return {returnObj.Id} is missing explicit refund allocation " +
                "and the original invoice could not be loaded for safe reconstruction.");
        }

        var refundTotal = RefundAllocationSupport.RoundCurrency(returnObj.TotalAmount);
        if (refundTotal < 0m)
        {
            throw new InvalidOperationException(
                $"Return {returnObj.Id} has a negative refund total {refundTotal}.");
        }

        if (originalInvoice.PaymentMethod == PointOfSale.Core.Enums.PaymentMethod.Credit || originalInvoice.PaidAmount <= 0m)
        {
            _logger.LogWarning(
                "POS refund outbox payload for return {ReturnId} is missing explicit refund allocation. " +
                "Routing refund to account credit using original invoice {InvoiceId}.",
                returnObj.Id,
                originalInvoice.Id);

            return RefundAllocation.AccountCredit;
        }

        var invoiceCashAmount = RefundAllocationSupport.RoundCurrency(originalInvoice.CashAmount ?? 0m);
        var invoiceCardAmount = RefundAllocationSupport.RoundCurrency(originalInvoice.CardAmount ?? 0m);
        if (invoiceCashAmount < 0m || invoiceCardAmount < 0m)
        {
            throw new InvalidOperationException(
                $"Original invoice {originalInvoice.Id} has invalid negative payment split values.");
        }

        RefundAllocation allocation;
        if (invoiceCashAmount == 0m && invoiceCardAmount == 0m)
        {
            var paidTotal = RefundAllocationSupport.RoundCurrency(originalInvoice.PaidAmount);
            var cappedRefundAmount = Math.Min(refundTotal, paidTotal);

            if (refundTotal > paidTotal)
            {
                _logger.LogWarning(
                    "Refund amount clamped for return {ReturnId}: refundTotal={RefundTotal} exceeds paidTotal={PaidTotal} for original invoice {InvoiceId}",
                    returnObj.Id, refundTotal, paidTotal, originalInvoice.Id);
            }

            allocation = originalInvoice.PaymentMethod switch
            {
                PointOfSale.Core.Enums.PaymentMethod.Cash => new RefundAllocation(RefundMethod.Cash, cappedRefundAmount, 0m),
                PointOfSale.Core.Enums.PaymentMethod.Card => new RefundAllocation(RefundMethod.Card, 0m, cappedRefundAmount),
                PointOfSale.Core.Enums.PaymentMethod.Mixed => RefundAllocation.AccountCredit,
                _ => throw new InvalidOperationException(
                    $"POS refund outbox payload for return {returnObj.Id} is missing explicit refund allocation " +
                    $"and original invoice {originalInvoice.Id} does not contain a reconstructable cash/card split.")
            };

            if (allocation.Method == RefundMethod.AccountCredit)
            {
                _logger.LogWarning(
                    "Legacy POS refund for return {ReturnId} routed to Account Credit to protect cash drawer integrity because original invoice {InvoiceId} was mixed without explicit split.",
                    returnObj.Id,
                    originalInvoice.Id);
            }
        }
        else
        {
            var totalPaidByAllocation = RefundAllocationSupport.RoundCurrency(invoiceCashAmount + invoiceCardAmount);
            if (refundTotal > totalPaidByAllocation)
            {
                throw new InvalidOperationException(
                    $"Return {returnObj.Id} total {refundTotal} exceeds original invoice payment split {totalPaidByAllocation}.");
            }

            if (invoiceCashAmount > 0m && invoiceCardAmount > 0m)
            {
                var cashAmount = RefundAllocationSupport.RoundCurrency(
                    refundTotal * invoiceCashAmount / totalPaidByAllocation);
                var cardAmount = RefundAllocationSupport.RoundCurrency(refundTotal - cashAmount);

                allocation = cashAmount <= 0m
                    ? new RefundAllocation(RefundMethod.Card, 0m, refundTotal)
                    : cardAmount <= 0m
                        ? new RefundAllocation(RefundMethod.Cash, refundTotal, 0m)
                        : new RefundAllocation(RefundMethod.Mixed, cashAmount, cardAmount);
            }
            else if (invoiceCashAmount > 0m)
            {
                allocation = new RefundAllocation(RefundMethod.Cash, refundTotal, 0m);
            }
            else
            {
                allocation = new RefundAllocation(RefundMethod.Card, 0m, refundTotal);
            }
        }

        _logger.LogWarning(
            "POS refund outbox payload for return {ReturnId} is missing explicit refund allocation. " +
            "Derived allocation from original invoice {InvoiceId}: Method={Method}, Cash={CashAmount}, Card={CardAmount}.",
            returnObj.Id,
            originalInvoice.Id,
            allocation.Method,
            allocation.CashAmount,
            allocation.CardAmount);

        return allocation;
    }

    private static RefundMethod? TryParseRefundMethod(JsonElement payload)
    {
        if (!payload.TryGetProperty("RefundMethod", out var property) ||
            property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            var rawValue = property.GetString();
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return null;
            }

            if (Enum.TryParse<RefundMethod>(rawValue, ignoreCase: true, out var parsedMethod))
            {
                return parsedMethod;
            }
        }

        if (property.ValueKind == JsonValueKind.Number &&
            property.TryGetInt32(out var numericValue) &&
            Enum.IsDefined(typeof(RefundMethod), numericValue))
        {
            return (RefundMethod)numericValue;
        }

        throw new InvalidOperationException("RefundMethod payload value is invalid.");
    }

    private static decimal? TryReadDecimal(JsonElement payload, string propertyName)
    {
        if (!payload.TryGetProperty(propertyName, out var property) ||
            property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out var decimalValue))
        {
            return decimalValue;
        }

        throw new InvalidOperationException($"{propertyName} payload value is invalid.");
    }

    private static bool HasAnyRefundAllocationField(JsonElement payload) =>
        payload.TryGetProperty("RefundMethod", out _) ||
        payload.TryGetProperty("CashAmount", out _) ||
        payload.TryGetProperty("CardAmount", out _);

    private static bool HasExplicitRefundAllocation(JsonElement payload) =>
        payload.TryGetProperty("RefundMethod", out var refundMethodProp) &&
        refundMethodProp.ValueKind != JsonValueKind.Null &&
        payload.TryGetProperty("CashAmount", out var cashAmountProp) &&
        cashAmountProp.ValueKind != JsonValueKind.Null &&
        payload.TryGetProperty("CardAmount", out var cardAmountProp) &&
        cardAmountProp.ValueKind != JsonValueKind.Null;
    
    /// <summary>
    /// PHASE LCR: Process expense posting.
    /// </summary>
    private async Task<PostingResult> ProcessPosExpenseAsync(
        ApplicationDbContext db, ICommerceAccountingService accounting,
        JsonDocument payload, Guid tenantId, CancellationToken ct)
    {
        var expenseId = payload.RootElement.GetProperty("ExpenseId").GetGuid();
        var operationKey = payload.RootElement.TryGetProperty("OperationKey", out var operationKeyProperty) &&
                           operationKeyProperty.ValueKind == JsonValueKind.String
            ? operationKeyProperty.GetString()
            : null;
        var mutationVersion = payload.RootElement.TryGetProperty("MutationVersion", out var mutationVersionProperty) &&
                              mutationVersionProperty.ValueKind == JsonValueKind.Number
            ? mutationVersionProperty.GetInt32()
            : (int?)null;
        var expense = await db.Expenses
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.Id == expenseId && e.TenantId == tenantId, ct);
        
        if (expense == null)
            throw new InvalidOperationException($"Expense {expenseId} not found");

        if (mutationVersion.HasValue && expense.AccountingMutationVersion != mutationVersion.Value)
        {
            _logger.LogInformation(
                "Skipping stale expense posting message for expense {ExpenseId}. Message version {MessageVersion}, current version {CurrentVersion}",
                expenseId,
                mutationVersion.Value,
                expense.AccountingMutationVersion);

            return PostingResult.Succeeded(Guid.Empty, "SKIPPED");
        }

        // Legacy-stale guard: reject unversioned messages if aggregate has any versioned mutations
        if (!mutationVersion.HasValue && expense.AccountingMutationVersion > 0)
        {
            _logger.LogInformation(
                "Skipping legacy unversioned expense posting message for expense {ExpenseId}. Current version {CurrentVersion}",
                expenseId,
                expense.AccountingMutationVersion);

            return PostingResult.Succeeded(Guid.Empty, "SKIPPED");
        }

        if (expense.IsDeleted || !expense.IsApproved)
        {
            _logger.LogInformation(
                "Skipping expense posting for expense {ExpenseId}. IsDeleted={IsDeleted}, IsApproved={IsApproved}",
                expense.Id,
                expense.IsDeleted,
                expense.IsApproved);

            return PostingResult.Succeeded(Guid.Empty, "SKIPPED");
        }
        
        return await accounting.PostPosExpenseAsync(expense, ct, operationKey);
    }

    private async Task<PostingResult> ProcessExpenseReversalAsync(
        ApplicationDbContext db,
        ICommerceAccountingService accounting,
        IFinancialPostingService postingService,
        JsonDocument payload,
        Guid tenantId,
        CancellationToken ct)
    {
        var expenseId = payload.RootElement.GetProperty("ExpenseId").GetGuid();
        var operationKey = payload.RootElement.GetProperty("OperationKey").GetString();
        var mutationVersion = payload.RootElement.GetProperty("MutationVersion").GetInt32();

        if (string.IsNullOrWhiteSpace(operationKey))
            throw new InvalidOperationException("Expense mutation payload is missing OperationKey");

        var expense = await db.Expenses
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.Id == expenseId && e.TenantId == tenantId, ct);

        if (expense == null)
            throw new InvalidOperationException($"Expense {expenseId} not found");

        if (expense.AccountingMutationVersion != mutationVersion)
        {
            _logger.LogInformation(
                "Skipping stale expense mutation message for expense {ExpenseId}. Message version {MessageVersion}, current version {CurrentVersion}",
                expenseId,
                mutationVersion,
                expense.AccountingMutationVersion);

            return PostingResult.Succeeded(Guid.Empty, "SKIPPED");
        }

        var journalsToReverse = await db.JournalEntries
            .IgnoreQueryFilters()
            .Where(j =>
                j.TenantId == tenantId &&
                j.SourceId == expenseId &&
                j.SourceType == "Expense" &&
                j.Status == JournalStatus.Posted &&
                (string.IsNullOrEmpty(j.IdempotencyKey) || !j.IdempotencyKey.EndsWith($":{operationKey}")))
            .OrderBy(j => j.PostedAtUtc)
            .ThenBy(j => j.JournalNumber)
            .ToListAsync(ct);

        // ═══════════════════════════════════════════════════════════════════
        // ATOMICITY FIX: Wrap reversal loop + repost in a single transaction
        // to prevent partial unwind if repost fails after some reversals succeed.
        // ═══════════════════════════════════════════════════════════════════
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            
            try
            {
                JournalEntry? lastReversal = null;
                foreach (var journal in journalsToReverse)
                {
                    lastReversal = await postingService.ReverseJournalAsync(
                        tenantId,
                        journal.Id,
                        $"Expense mutation for {expense.ReferenceNumber ?? expense.Id.ToString()} ({operationKey})",
                        ct: ct);
                }

                PostingResult result;
                if (expense.IsDeleted || !expense.IsApproved)
                {
                    result = lastReversal != null
                        ? PostingResult.Succeeded(lastReversal.Id, lastReversal.JournalNumber)
                        : PostingResult.Succeeded(Guid.Empty, "SKIPPED");
                }
                else
                {
                    result = await accounting.PostPosExpenseAsync(expense, ct, operationKey);
                }
                
                if (!result.Success)
                {
                    await transaction.RollbackAsync(ct);
                    return result;
                }
                
                await transaction.CommitAsync(ct);
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        });
    }
    
    private async Task<PostingResult> ProcessOnlinePaymentAsync(
        ApplicationDbContext db, ICommerceAccountingService accounting,
        JsonDocument payload, Guid tenantId, CancellationToken ct)
    {
        var orderId = payload.RootElement.GetProperty("OrderId").GetGuid();
        var paymentIntentId = payload.RootElement.GetProperty("PaymentIntentId").GetString()!;
        var gateway = payload.RootElement.GetProperty("Gateway").GetString()!;
        
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            try
            {
                var order = await LoadOrderForAccountingPostAsync(db, tenantId, orderId, ct);
                if (order == null)
                    throw new InvalidOperationException($"Order {orderId} not found");

                var authority = AccountingLedgerAuthority.EvaluateOrderCollectionPosting(order, "OnlinePayment");
                if (!authority.ShouldPost)
                {
                    if (authority.ShouldRetry)
                    {
                        throw new InvalidOperationException(authority.Reason);
                    }

                    _logger.LogInformation(
                        "Collapsing stale online payment accounting replay for order {OrderId}: {Reason}",
                        orderId,
                        authority.Reason);
                    await transaction.CommitAsync(ct);
                    return PostingResult.Succeeded(Guid.Empty, "SKIPPED");
                }
                
                var result = await accounting.PostOnlinePaymentCapturedAsync(order, paymentIntentId, gateway, ct);
                if (!result.Success)
                {
                    await transaction.RollbackAsync(ct);
                    return result;
                }

                await transaction.CommitAsync(ct);
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        });
    }
    
    private async Task<PostingResult> ProcessCodDeliveredAsync(
        ApplicationDbContext db, ICommerceAccountingService accounting,
        JsonDocument payload, Guid tenantId, CancellationToken ct)
    {
        var orderId = payload.RootElement.GetProperty("OrderId").GetGuid();
        
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            try
            {
                var order = await LoadOrderForAccountingPostAsync(db, tenantId, orderId, ct);
                if (order == null)
                    throw new InvalidOperationException($"Order {orderId} not found");

                var authority = AccountingLedgerAuthority.EvaluateOrderCollectionPosting(order, "CodDelivered");
                if (!authority.ShouldPost)
                {
                    if (authority.ShouldRetry)
                    {
                        throw new InvalidOperationException(authority.Reason);
                    }

                    _logger.LogInformation(
                        "Collapsing stale COD accounting replay for order {OrderId}: {Reason}",
                        orderId,
                        authority.Reason);
                    await transaction.CommitAsync(ct);
                    return PostingResult.Succeeded(Guid.Empty, "SKIPPED");
                }
                
                var result = await accounting.PostCodDeliveredAsync(order, ct);
                if (!result.Success)
                {
                    await transaction.RollbackAsync(ct);
                    return result;
                }

                await transaction.CommitAsync(ct);
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        });
    }
    
    private async Task<PostingResult> ProcessWalletApprovedAsync(
        ApplicationDbContext db, ICommerceAccountingService accounting,
        JsonDocument payload, Guid tenantId, CancellationToken ct)
    {
        var orderId = payload.RootElement.GetProperty("OrderId").GetGuid();
        
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            try
            {
                var order = await LoadOrderForAccountingPostAsync(db, tenantId, orderId, ct);
                if (order == null)
                    throw new InvalidOperationException($"Order {orderId} not found");

                var authority = AccountingLedgerAuthority.EvaluateOrderCollectionPosting(order, "WalletApproved");
                if (!authority.ShouldPost)
                {
                    if (authority.ShouldRetry)
                    {
                        throw new InvalidOperationException(authority.Reason);
                    }

                    _logger.LogInformation(
                        "Collapsing stale wallet accounting replay for order {OrderId}: {Reason}",
                        orderId,
                        authority.Reason);
                    await transaction.CommitAsync(ct);
                    return PostingResult.Succeeded(Guid.Empty, "SKIPPED");
                }
                
                var result = await accounting.PostWalletApprovedAsync(order, ct);
                if (!result.Success)
                {
                    await transaction.RollbackAsync(ct);
                    return result;
                }

                await transaction.CommitAsync(ct);
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        });
    }
    
    private async Task<PostingResult> ProcessEcommerceRefundAsync(
        ApplicationDbContext db, ICommerceAccountingService accounting,
        JsonDocument payload, Guid tenantId, CancellationToken ct)
    {
        var orderId = payload.RootElement.GetProperty("OrderId").GetGuid();
        var refundId = payload.RootElement.GetProperty("RefundId").GetString()!;
        
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            try
            {
                var order = await LoadOrderForAccountingPostAsync(db, tenantId, orderId, ct);
                if (order == null)
                    throw new InvalidOperationException($"Order {orderId} not found");

                var refundAuthority = await AccountingLedgerAuthority.LoadCurrentRefundAsync(
                    db,
                    tenantId,
                    orderId,
                    refundId,
                    ct);

                var authority = AccountingLedgerAuthority.EvaluateRefundPosting(refundAuthority);
                if (!authority.ShouldPost)
                {
                    if (authority.ShouldRetry)
                    {
                        throw new InvalidOperationException(authority.Reason);
                    }

                    _logger.LogInformation(
                        "Collapsing stale e-commerce refund accounting replay for order {OrderId} refund {RefundId}: {Reason}",
                        orderId,
                        refundId,
                        authority.Reason);
                    await transaction.CommitAsync(ct);
                    return PostingResult.Succeeded(Guid.Empty, "SKIPPED");
                }

                var refundContext = AccountingLedgerAuthority.ToRefundAccountingContext(refundAuthority!);
                
                var result = await accounting.PostEcommerceRefundAsync(
                    order,
                    refundAuthority!.Amount,
                    refundAuthority.RefundId,
                    refundContext,
                    ct);
                if (!result.Success)
                {
                    await transaction.RollbackAsync(ct);
                    return result;
                }

                await transaction.CommitAsync(ct);
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        });
    }

    private static EcommerceRefundAccountingContext? ResolveEcommerceRefundAccountingContext(JsonElement payload)
    {
        if (!payload.TryGetProperty("IsPostSettlementRefund", out var isPostSettlementElement) ||
            isPostSettlementElement.ValueKind != JsonValueKind.True)
        {
            return null;
        }

        return new EcommerceRefundAccountingContext(
            IsPostSettlementRefund: true,
            SettlementBatchId: ReadNullableGuid(payload, "SettlementBatchId"),
            SettlementType: ReadNullableString(payload, "SettlementType"),
            SettlementId: ReadNullableString(payload, "SettlementId"),
            PayoutBatchId: ReadNullableString(payload, "PayoutBatchId"),
            SettledAtUtc: ReadNullableDateTime(payload, "SettledAtUtc"));
    }

    private static Guid? ReadNullableGuid(JsonElement payload, string propertyName)
    {
        if (!payload.TryGetProperty(propertyName, out var element) ||
            element.ValueKind == JsonValueKind.Null ||
            element.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        return element.GetGuid();
    }

    private static string? ReadNullableString(JsonElement payload, string propertyName)
    {
        if (!payload.TryGetProperty(propertyName, out var element) ||
            element.ValueKind == JsonValueKind.Null ||
            element.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        return element.GetString();
    }

    private static DateTime? ReadNullableDateTime(JsonElement payload, string propertyName)
    {
        if (!payload.TryGetProperty(propertyName, out var element) ||
            element.ValueKind == JsonValueKind.Null ||
            element.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        return element.GetDateTime();
    }
    
    private async Task<PostingResult> ProcessGatewaySettlementAsync(
        ApplicationDbContext db,
        ICommerceAccountingService accounting,
        JsonDocument payload, Guid tenantId, CancellationToken ct)
    {
        var settlementBatchId = payload.RootElement.GetProperty("SettlementBatchId").GetGuid();
        var gateway = payload.RootElement.GetProperty("Gateway").GetString()!;
        var settlementId = payload.RootElement.GetProperty("SettlementId").GetString()!;

        var settlement = await AccountingLedgerAuthority.LoadCurrentSettlementAsync(
            db,
            tenantId,
            settlementBatchId,
            "GATEWAY",
            settlementId,
            ct);

        var authority = AccountingLedgerAuthority.EvaluateSettlementPosting(settlement);
        if (!authority.ShouldPost)
        {
            if (authority.ShouldRetry)
            {
                throw new InvalidOperationException(authority.Reason);
            }

            _logger.LogInformation(
                "Collapsing stale gateway settlement accounting replay for settlement {SettlementId}: {Reason}",
                settlementId,
                authority.Reason);
            return PostingResult.Succeeded(Guid.Empty, "SKIPPED");
        }
        
        return await accounting.PostGatewaySettlementAsync(
            tenantId,
            settlement!.Provider ?? gateway,
            settlement.GrossAmount,
            settlement.Fees,
            settlement.SettlementId,
            ct);
    }

    private async Task<PostingResult> ProcessCustomerOpeningBalanceAsync(
        ApplicationDbContext db,
        ICommerceAccountingService accounting,
        AccountingOutboxMessage message,
        JsonDocument payload,
        Guid tenantId,
        CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            try
            {
                Guid? openingBalanceId = null;

                if (payload.RootElement.TryGetProperty("OpeningBalanceId", out var openingBalanceIdElement))
                {
                    openingBalanceId = openingBalanceIdElement.GetGuid();
                }
                else
                {
                    openingBalanceId = message.BusinessEntityId;
                }

                if (openingBalanceId == null || openingBalanceId == Guid.Empty)
                {
                    throw new InvalidOperationException("Customer opening balance not found");
                }

                var openingBalance = await LoadAccountLedgerForAccountingPostAsync(db, tenantId, openingBalanceId.Value, ct);
                if (openingBalance == null || openingBalance.PartyType != PartyType.Customer || openingBalance.TransactionType != TransactionType.OpeningBalance)
                {
                    throw new InvalidOperationException($"Customer opening balance entry {openingBalanceId} not found or invalid");
                }

                var result = await accounting.PostCustomerOpeningBalanceAsync(openingBalance, ct);
                if (!result.Success)
                {
                    await transaction.RollbackAsync(ct);
                    return result;
                }

                await transaction.CommitAsync(ct);
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        });
    }
    
    private async Task<PostingResult> ProcessCodSettlementAsync(
        ApplicationDbContext db,
        ICommerceAccountingService accounting,
        JsonDocument payload, Guid tenantId, CancellationToken ct)
    {
        var settlementBatchId = payload.RootElement.GetProperty("SettlementBatchId").GetGuid();
        var settlementId = payload.RootElement.GetProperty("SettlementId").GetString()!;

        var settlement = await AccountingLedgerAuthority.LoadCurrentSettlementAsync(
            db,
            tenantId,
            settlementBatchId,
            "COD",
            settlementId,
            ct);

        var authority = AccountingLedgerAuthority.EvaluateSettlementPosting(settlement);
        if (!authority.ShouldPost)
        {
            if (authority.ShouldRetry)
            {
                throw new InvalidOperationException(authority.Reason);
            }

            _logger.LogInformation(
                "Collapsing stale COD settlement accounting replay for settlement {SettlementId}: {Reason}",
                settlementId,
                authority.Reason);
            return PostingResult.Succeeded(Guid.Empty, "SKIPPED");
        }
        
        return await accounting.PostCodSettlementAsync(
            tenantId,
            settlement!.GrossAmount,
            settlement.Fees,
            settlement.SettlementId,
            ct);
    }
    
    private async Task CreateAuditTrailAsync(
        ApplicationDbContext db,
        AccountingOutboxMessage message,
        PostingResult result,
        CancellationToken ct)
    {
        var (businessType, operationType) = message.EventType switch
        {
            "SaleReversal" => ("SalesInvoice", "SaleUpdate"),
            "PurchaseReversal" => ("PurchaseInvoice", "PurchaseMutation"),
            "ExpenseReversal" => ("Expense", "ExpenseMutation"),
            _ => (message.EventType, message.EventType)
        };

        var audit = CommerceAccountingAudit.Create(
            tenantId: message.TenantId,
            businessType: businessType,
            businessId: message.BusinessEntityId,
            businessReference: null,
            journalId: result.JournalId ?? Guid.Empty,
            journalNumber: result.JournalNumber,
            operationType: operationType,
            amount: 0, // Would need to extract from payload
            idempotencyKey: message.IdempotencyKey,
            wasIdempotent: result.WasIdempotent);
        
        db.CommerceAccountingAudits.Add(audit);
        // SaveChanges called by caller
    }
    
    private void RecordSuccessMetric(AccountingOutboxMessage message)
    {
        switch (message.EventType)
        {
            case "PosSale":
            case "SaleReversal":
                _metrics?.IncrementPosSalesPosted(message.TenantId);
                break;
            case "PosPurchase":
            case "PurchaseReversal":
            case "PurchaseReturn":
                _metrics?.IncrementPosPurchasesPosted(message.TenantId);
                break;
            case "OnlinePayment":
            case "CodDelivered":
            case "WalletApproved":
                _metrics?.IncrementEcommercePaymentsPosted(message.TenantId, message.EventType);
                break;
            case "PosRefund":
            case "EcommerceRefund":
                _metrics?.IncrementRefundsPosted(message.TenantId);
                break;
            case "PartyLedgerAdjustment":
            case "SupplierOpeningBalance":
            case "PosExpense":
            case "ExpenseReversal":
                break;
            case "GatewaySettlement":
            case "CodSettlement":
                _metrics?.IncrementSettlementsPosted(message.TenantId, message.EventType);
                break;
        }
    }

    private async Task MarkOrderFinancialPostingPostedAsync(
        IServiceProvider services,
        ApplicationDbContext db,
        AccountingOutboxMessage message,
        Guid? journalId,
        CancellationToken ct)
    {
        if (!IsOrderPaymentPostingEvent(message.EventType))
        {
            return;
        }

        var order = await db.Orders
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(candidate =>
                candidate.TenantId == message.TenantId &&
                candidate.Id == message.BusinessEntityId,
                ct);

        if (order == null)
        {
            return;
        }

        if (journalId.HasValue && journalId.Value != Guid.Empty)
        {
            order.FinancialPostingJournalId = journalId.Value;
        }

        var effectiveJournalId = order.FinancialPostingJournalId;
        if (!effectiveJournalId.HasValue || effectiveJournalId.Value == Guid.Empty)
        {
            return;
        }

        var transitionedToPosted = order.FinancialPostingStatus != FinancialPostingStatus.Posted;

        order.FinancialPostingStatus = FinancialPostingStatus.Posted;

        if (transitionedToPosted || order.FinancialPostedAtUtc == null)
        {
            order.FinancialPostedAtUtc ??= DateTime.UtcNow;
        }

        order.FinancialPostingFailureReason = null;

        await PublishOrderCollectionAccountingPostedEventAsync(
            services,
            order,
            message,
            effectiveJournalId.Value,
            ct);

        // PRODUCTION HARDENING: Always call EnqueueAccountingConfirmedMarketplacePaymentCompletedAsync
        // regardless of transitionedToPosted flag. The helper has internal deduplication logic that
        // checks if the event is already enqueued. Gating on transitionedToPosted causes failed
        // enqueues to be permanently skipped on retry (because the order status was already set to
        // Posted in a previous attempt that rolled back).
        await EnqueueAccountingConfirmedMarketplacePaymentCompletedAsync(
            services,
            db,
            order,
            message,
            ct);
    }

    private static async Task PublishOrderCollectionAccountingPostedEventAsync(
        IServiceProvider services,
        Order order,
        AccountingOutboxMessage message,
        Guid journalId,
        CancellationToken ct)
    {
        var eventPublisher = services.GetRequiredService<IEventPublisher>();
        var postedAtUtc = order.FinancialPostedAtUtc ?? DateTime.UtcNow;
        var eventVersion = PointOfSale.Core.Helpers.ProductEventVersionHelper.ToProductEventVersion(
            order.ProjectionMutationVersion + 1);

        await eventPublisher.PublishAsync(new OrderCollectionAccountingPostedEvent
        {
            EventId = BuildDeterministicEventId(
                "storefront:collection-accounting-posted",
                message.Id),
            TenantId = order.TenantId,
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            Provider = ResolveStorefrontAccountingProvider(order, message),
            PaymentMethod = ResolveMarketplacePaymentMethod(order, message),
            AmountPosted = ResolvePostedAmount(order, message),
            Currency = order.Currency,
            FinancialPostingJournalId = journalId,
            NewFinancialPostingStatus = order.FinancialPostingStatus,
            AccountingEventType = message.EventType,
            ExternalReference = ResolvePaymentExternalReference(order, message),
            PostedAt = postedAtUtc,
            Version = eventVersion,
            CorrelationId = message.CorrelationId ?? message.IdempotencyKey,
            IdempotencyKey = $"ORDER:ACCOUNTING_POSTED:{message.Id:N}",
            OccurredAtUtc = postedAtUtc
        }, ct);
    }

    private async Task EnqueueAccountingConfirmedMarketplacePaymentCompletedAsync(
        IServiceProvider services,
        ApplicationDbContext db,
        Order order,
        AccountingOutboxMessage message,
        CancellationToken ct)
    {
        var eventId = BuildDeterministicEventId(
            "marketplace:payment-completed:accounting-posted",
            message.Id);

        var alreadyEnqueued = await db.Set<MarketplaceEventOutboxMessage>()
            .IgnoreQueryFilters()
            .AnyAsync(candidate =>
                candidate.TenantId == order.TenantId &&
                candidate.EntityId == order.Id &&
                candidate.EventType == nameof(MarketplaceEvents.PaymentCompletedEvent) &&
                candidate.EventId == eventId &&
                !candidate.IsDeleted,
                ct);

        if (alreadyEnqueued)
        {
            _logger.LogInformation(
                "Skipping accounting-confirmed marketplace payment event because one already exists | OrderId: {OrderId} | TenantId: {TenantId} | AccountingOutboxMessageId: {MessageId}",
                order.Id,
                order.TenantId,
                message.Id);
            return;
        }

        var marketplaceOutbox = services.GetRequiredService<IMarketplaceEventOutboxService>();
        var financialPostedAtUtc = order.FinancialPostedAtUtc ?? DateTime.UtcNow;
        var eventVersion = PointOfSale.Core.Helpers.ProductEventVersionHelper.ToProductEventVersion(
            order.ProjectionMutationVersion + 1);

        await marketplaceOutbox.EnqueueAsync(new MarketplaceEvents.PaymentCompletedEvent
        {
            EventId = eventId,
            CorrelationId = message.CorrelationId ?? message.IdempotencyKey,
            EntityId = order.Id,
            AggregateRootId = order.Id,
            TenantId = order.TenantId,
            Version = eventVersion,
            OrderNumber = order.OrderNumber,
            PaymentMethod = ResolveMarketplacePaymentMethod(order, message),
            ExternalReference = ResolvePaymentExternalReference(order, message),
            Amount = order.Total,
            Currency = order.Currency,
            CompletionSource = $"AccountingPosted:{message.EventType}",
            CompletedAtUtc = financialPostedAtUtc,
            OccurredAtUtc = financialPostedAtUtc
        }, ct);
    }

    private static Guid BuildDeterministicEventId(string purpose, Guid sourceId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{purpose}:{sourceId:N}"));
        return new Guid(hash[..16]);
    }

    private static string ResolveMarketplacePaymentMethod(Order order, AccountingOutboxMessage message)
    {
        if (!string.IsNullOrWhiteSpace(order.PaymentMethodCode))
        {
            return order.PaymentMethodCode;
        }

        return message.EventType switch
        {
            "CodDelivered" => "COD",
            "WalletApproved" => "WALLET",
            "OnlinePayment" => TryGetPayloadString(message.PayloadJson, "Gateway") ?? "ONLINE",
            _ => message.EventType
        };
    }

    private static string? ResolvePaymentExternalReference(Order order, AccountingOutboxMessage message)
    {
        return TryGetPayloadString(message.PayloadJson, "PaymentIntentId") ??
               order.PaymentReference ??
               message.IdempotencyKey;
    }

    private static string ResolveStorefrontAccountingProvider(Order order, AccountingOutboxMessage message)
    {
        return message.EventType switch
        {
            "CodDelivered" => "COD",
            "WalletApproved" => "WALLET",
            "OnlinePayment" => TryGetPayloadString(message.PayloadJson, "Gateway") ??
                               TryGetPayloadString(message.PayloadJson, "Provider") ??
                               order.PaymentMethodCode ??
                               "ONLINE",
            _ => TryGetPayloadString(message.PayloadJson, "Provider") ??
                 order.PaymentMethodCode ??
                 message.EventType
        };
    }

    private static decimal ResolvePostedAmount(Order order, AccountingOutboxMessage message)
    {
        return TryGetPayloadDecimal(message.PayloadJson, "AmountPaid") ??
               TryGetPayloadDecimal(message.PayloadJson, "AmountCaptured") ??
               TryGetPayloadDecimal(message.PayloadJson, "AmountCollected") ??
               TryGetPayloadDecimal(message.PayloadJson, "AmountApproved") ??
               TryGetPayloadDecimal(message.PayloadJson, "Amount") ??
               order.Total;
    }

    private static string? TryGetPayloadString(string payloadJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            return document.RootElement.TryGetProperty(propertyName, out var property)
                ? property.ValueKind == JsonValueKind.String
                    ? property.GetString()
                    : property.ToString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static decimal? TryGetPayloadDecimal(string payloadJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (!document.RootElement.TryGetProperty(propertyName, out var property))
            {
                return null;
            }

            if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out var number))
            {
                return number;
            }

            if (property.ValueKind == JsonValueKind.String &&
                decimal.TryParse(property.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task MarkOrderRefundFinancialPostingPostedAsync(
        IServiceProvider services,
        ApplicationDbContext db,
        AccountingOutboxMessage message,
        Guid? journalId,
        CancellationToken ct)
    {
        if (!IsOrderRefundPostingEvent(message.EventType))
        {
            return;
        }

        var refundId = TryGetPayloadString(message.PayloadJson, "RefundId");
        if (string.IsNullOrWhiteSpace(refundId))
        {
            return;
        }

        var refund = await db.OrderRefunds
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(candidate =>
                candidate.TenantId == message.TenantId &&
                candidate.OrderId == message.BusinessEntityId &&
                candidate.RefundId == refundId &&
                !candidate.IsDeleted,
                ct);

        if (refund == null)
        {
            return;
        }

        if (journalId.HasValue && journalId.Value != Guid.Empty)
        {
            refund.FinancialPostingJournalId = journalId.Value;
        }

        if (!refund.FinancialPostingJournalId.HasValue ||
            refund.FinancialPostingJournalId.Value == Guid.Empty)
        {
            return;
        }

        // PRODUCTION HARDENING: Do NOT mark as Posted until marketplace event is successfully enqueued.
        // Moving status mutation after enqueue prevents silent data loss if order is missing.
        await EnqueueAccountingConfirmedMarketplaceRefundProcessedAsync(
            services,
            db,
            refund,
            message,
            ct);

        // Only mark as Posted after successful event enqueue
        refund.FinancialPostingStatus = FinancialPostingStatus.Posted;
        refund.FinancialPostedAtUtc ??= DateTime.UtcNow;
        refund.FinancialPostingFailureReason = null;
    }

    private async Task EnqueueAccountingConfirmedMarketplaceRefundProcessedAsync(
        IServiceProvider services,
        ApplicationDbContext db,
        OrderRefund refund,
        AccountingOutboxMessage message,
        CancellationToken ct)
    {
        var eventId = BuildDeterministicEventId(
            "marketplace:refund-processed:accounting-posted",
            message.Id);

        var alreadyEnqueued = await db.Set<MarketplaceEventOutboxMessage>()
            .IgnoreQueryFilters()
            .AnyAsync(candidate =>
                candidate.TenantId == refund.TenantId &&
                candidate.EntityId == refund.OrderId &&
                candidate.EventType == nameof(MarketplaceEvents.RefundProcessedEvent) &&
                candidate.EventId == eventId &&
                !candidate.IsDeleted,
                ct);

        if (alreadyEnqueued)
        {
            _logger.LogInformation(
                "Skipping accounting-confirmed marketplace refund event because the deterministic event already exists | OrderId: {OrderId} | RefundId: {RefundId} | TenantId: {TenantId} | AccountingOutboxMessageId: {MessageId}",
                refund.OrderId,
                refund.RefundId,
                refund.TenantId,
                message.Id);
            return;
        }

        var order = await db.Orders
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate =>
                candidate.TenantId == refund.TenantId &&
                candidate.Id == refund.OrderId,
                ct);

        if (order == null)
        {
            // PRODUCTION HARDENING: Throw exception instead of silent skip to ensure retry.
            // Missing orders should not cause silent data loss - the accounting outbox
            // message will retry until the order is available or manually resolved.
            _logger.LogError(
                "Cannot enqueue accounting-confirmed marketplace refund event because the order is missing - will retry | OrderId: {OrderId} | RefundId: {RefundId} | TenantId: {TenantId} | AccountingOutboxMessageId: {MessageId}",
                refund.OrderId,
                refund.RefundId,
                refund.TenantId,
                message.Id);
            
            throw new InvalidOperationException(
                $"Cannot enqueue marketplace refund event for OrderId {refund.OrderId} because the order is missing. " +
                "This accounting outbox message will retry.");
        }

        var marketplaceOutbox = services.GetRequiredService<IMarketplaceEventOutboxService>();
        var postedAtUtc = refund.FinancialPostedAtUtc ?? DateTime.UtcNow;
        var eventVersion = PointOfSale.Core.Helpers.ProductEventVersionHelper.ToProductEventVersion(
            order.ProjectionMutationVersion + 1);

        await marketplaceOutbox.EnqueueAsync(new MarketplaceEvents.RefundProcessedEvent
        {
            EventId = eventId,
            CorrelationId = message.CorrelationId ?? message.IdempotencyKey,
            EntityId = refund.OrderId,
            AggregateRootId = refund.OrderId,
            TenantId = refund.TenantId,
            Version = eventVersion,
            OrderNumber = order.OrderNumber,
            RefundId = refund.RefundId,
            Amount = refund.Amount,
            Currency = refund.Currency,
            IsFullRefund = refund.IsFullRefund,
            Reason = refund.Reason,
            PaymentMethod = refund.PaymentMethod,
            ProcessedAtUtc = postedAtUtc,
            OccurredAtUtc = postedAtUtc
        }, ct);
    }

    private static async Task MarkOrderRefundFinancialPostingFailedAsync(
        ApplicationDbContext db,
        AccountingOutboxMessage message,
        string error,
        CancellationToken ct)
    {
        if (!IsOrderRefundPostingEvent(message.EventType))
        {
            return;
        }

        var refundId = TryGetPayloadString(message.PayloadJson, "RefundId");
        if (string.IsNullOrWhiteSpace(refundId))
        {
            return;
        }

        var refund = await db.OrderRefunds
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(candidate =>
                candidate.TenantId == message.TenantId &&
                candidate.OrderId == message.BusinessEntityId &&
                candidate.RefundId == refundId &&
                !candidate.IsDeleted,
                ct);

        if (refund == null || refund.FinancialPostingStatus == FinancialPostingStatus.Posted)
        {
            return;
        }

        refund.FinancialPostingStatus = FinancialPostingStatus.PostingFailed;
        refund.FinancialPostingFailureReason = error.Length <= 1000 ? error : error[..1000];
    }

    private static async Task MarkOrderFinancialPostingFailedAsync(
        ApplicationDbContext db,
        AccountingOutboxMessage message,
        string error,
        CancellationToken ct)
    {
        if (!IsOrderPaymentPostingEvent(message.EventType))
        {
            return;
        }

        var order = await db.Orders
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(candidate =>
                candidate.TenantId == message.TenantId &&
                candidate.Id == message.BusinessEntityId,
                ct);

        if (order == null || order.FinancialPostingStatus == FinancialPostingStatus.Posted)
        {
            return;
        }

        order.FinancialPostingStatus = FinancialPostingStatus.PostingFailed;
        order.FinancialPostingFailureReason = error.Length <= 1000 ? error : error[..1000];
    }

    private static bool IsOrderPaymentPostingEvent(string eventType) =>
        eventType is "OnlinePayment" or "CodDelivered" or "WalletApproved" or "ResolvedUnresolvedOnlinePayment";

    private static bool IsOrderRefundPostingEvent(string eventType) =>
        eventType is "EcommerceRefund";

    private static int? TryGetMutationVersion(JsonElement payload)
    {
        return payload.TryGetProperty("MutationVersion", out var mutationVersionProperty) &&
               mutationVersionProperty.ValueKind == JsonValueKind.Number
            ? mutationVersionProperty.GetInt32()
            : null;
    }

    private static bool IsPostgresProvider(ApplicationDbContext db) =>
        db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsSqlServerProvider(ApplicationDbContext db) =>
        db.Database.ProviderName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) == true;

    private static bool SupportsJournalIdempotencyShortcut(string eventType) =>
        eventType is not "PosSale" and
        not "PosRefund" and
        not "SaleReversal" and
        not "PurchaseReversal" and
        not "ExpenseReversal" and
        // Each CommerceHub ledger entry is a distinct transaction; no multi-journal fan-out
        // occurs, so the shortcut IS eligible — but we still exclude it here to guarantee
        // the full dispatch path runs and produces the required GL entry on every retry.
        not "CommerceHubLedgerPosting";

    private static string GetAccountingCorrelationId(AccountingOutboxMessage message) =>
        !string.IsNullOrWhiteSpace(message.CorrelationId)
            ? message.CorrelationId
            : string.IsNullOrWhiteSpace(message.IdempotencyKey)
                ? message.Id.ToString("N")
                : message.IdempotencyKey;
    
    public async Task<int> GetPendingCountAsync(CancellationToken ct = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        
        return await db.AccountingOutboxMessages
            .IgnoreQueryFilters()
            .CountAsync(m => m.ProcessedAtUtc == null && !m.IsFailed, ct);
    }
    
    public async Task<int> GetFailedCountAsync(CancellationToken ct = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        
        return await db.AccountingOutboxMessages
            .IgnoreQueryFilters()
            .CountAsync(m => m.IsFailed, ct);
    }
    
    public async Task<bool> RetryFailedMessageAsync(Guid messageId, CancellationToken ct = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        
        var message = await db.AccountingOutboxMessages
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.Id == messageId && m.IsFailed, ct);
        
        if (message == null)
            return false;
        
        // Reset for retry
        message.IsFailed = false;
        message.AttemptCount = 0;
        message.NextRetryAtUtc = null;
        message.LastError = null;
        
        await db.SaveChangesAsync(ct);
        
        _logger.LogInformation("Reset failed message {MessageId} for retry", messageId);
        return true;
    }

    public async Task<AccountingOutboxCleanupResult> CleanupAsync(CancellationToken ct = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var nowUtc = DateTime.UtcNow;
        var processedCutoff = nowUtc.AddDays(-_options.ProcessedRetentionDays);
        var failedCutoff = nowUtc.AddDays(-_options.FailedRetentionDays);

        var processedDeleted = await DeleteExpiredBatchAsync(
            db,
            db.AccountingOutboxMessages
                .IgnoreQueryFilters()
                .Where(message => message.ProcessedAtUtc != null && message.ProcessedAtUtc < processedCutoff)
                .OrderBy(message => message.ProcessedAtUtc),
            ct);

        var failedDeleted = await DeleteExpiredBatchAsync(
            db,
            db.AccountingOutboxMessages
                .IgnoreQueryFilters()
                .Where(message =>
                    message.IsFailed &&
                    !CriticalFinancialTruthEventTypes.Contains(message.EventType) &&
                    (message.UpdatedAt ?? message.CreatedAt) < failedCutoff)
                .OrderBy(message => message.UpdatedAt ?? message.CreatedAt),
            ct);

        _metrics?.RecordOutboxCleanup(processedDeleted, failedDeleted);
        _metrics?.SetOutboxPendingCount(await GetPendingCountAsync(ct));
        _metrics?.SetOutboxFailedCount(await GetFailedCountAsync(ct));

        return new AccountingOutboxCleanupResult(processedDeleted, failedDeleted);
    }

    private async Task<int> DeleteExpiredBatchAsync(
        ApplicationDbContext db,
        IOrderedQueryable<AccountingOutboxMessage> query,
        CancellationToken ct)
    {
        var totalDeleted = 0;

        // Fast path: Use EF Core 7+ bulk deletion when configured
        if (_options.UseExecuteDelete)
        {
            for (var batchIndex = 0; batchIndex < _options.CleanupMaxBatchesPerRun; batchIndex++)
            {
                var expiredIds = await query
                    .Take(_options.CleanupBatchSize)
                    .Select(message => message.Id)
                    .ToListAsync(ct);

                if (expiredIds.Count == 0)
                {
                    break;
                }

                var deletedCount = await db.AccountingOutboxMessages
                    .IgnoreQueryFilters()
                    .Where(message => expiredIds.Contains(message.Id))
                    .ExecuteDeleteAsync(ct);

                totalDeleted += deletedCount;

                if (deletedCount == 0)
                {
                    break;
                }
            }

            return totalDeleted;
        }

        // Standard path: Load entities and use RemoveRange to preserve EF interceptors and cascade behaviors
        for (var batchIndex = 0; batchIndex < _options.CleanupMaxBatchesPerRun; batchIndex++)
        {
            var expiredIds = await query
                .Take(_options.CleanupBatchSize)
                .Select(message => message.Id)
                .ToListAsync(ct);

            if (expiredIds.Count == 0)
            {
                break;
            }

            var entities = await db.AccountingOutboxMessages
                .IgnoreQueryFilters()
                .Where(message => expiredIds.Contains(message.Id))
                .ToListAsync(ct);

            if (entities.Count == 0)
            {
                break;
            }

            db.AccountingOutboxMessages.RemoveRange(entities);
            totalDeleted += entities.Count;
            await db.SaveChangesAsync(ct);
        }

        return totalDeleted;
    }
}

/// <summary>
/// Internal result type for the atomic claim transaction.
/// </summary>
internal enum ClaimOutcome
{
    Claimed = 0,
    Deferred = 1,
    AlreadyClaimed = 2,
    ConcurrencyConflict = 3,
    MessageNoLongerEligible = 4
}
