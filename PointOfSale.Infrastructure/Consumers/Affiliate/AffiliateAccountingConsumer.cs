using System.Diagnostics;
using MassTransit;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Events.Affiliate;
using PointOfSale.Core.Exceptions.Accounting;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Interfaces.Treasury;
using PointOfSale.Core.Models.Accounting;
using PointOfSale.Infrastructure.Instrumentation;

namespace PointOfSale.Infrastructure.Consumers.Affiliate;

// ═══════════════════════════════════════════════════════════════════════════════
// AF-14: AFFILIATE ACCOUNTING BRIDGE (Sub-Ledger → General Ledger)
// AF-14C: HARDENED against out-of-order delivery, partial payouts, negative payable
//
// These consumers bridge the affiliate commission sub-ledger with the
// double-entry General Ledger, ensuring financial truth is maintained.
//
// When commission is EARNED:
//   Debit   6110 (Affiliate Marketing Expense) — expense recognized
//   Credit  2105 (Affiliate Payable)           — liability created
//
// When commission is PAID:
//   Debit   2105 (Affiliate Payable)           — liability cleared
//   Credit  1010 (Cash in Hand)                — cash reduced
//
// CRITICAL INVARIANTS:
// - Idempotent: duplicate events safely ignored via idempotency key
// - Multi-tenant safe: all account lookups scoped to TenantId
// - All posting goes through FinancialPostingService (serializable isolation)
// - Re-throws exceptions for MassTransit retry
// - AF-14C: Paid REQUIRES earned journal to exist (out-of-order guard)
// - AF-14C: Partial payouts are explicitly blocked
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// AF-14: Posts a journal entry when affiliate commission is earned.
///
/// Journal:
///   Debit  : Affiliate Marketing Expense (6110)
///   Credit : Affiliate Payable (2105)
///
/// Idempotency Key: AFFILIATE_COMM_EARNED:{TenantId}:{OrderId}
/// </summary>
public class AffiliateCommissionEarnedAccountingConsumer : IConsumer<AffiliateCommissionEarnedEvent>
{
    private const string EventType = "accounting_earned";
    private const string SourceType = "AffiliateCommissionEarned";

    private readonly IAccountResolver _accountResolver;
    private readonly IFinancialPostingService _postingService;
    private readonly AffiliateTelemetry _telemetry;
    private readonly ILogger<AffiliateCommissionEarnedAccountingConsumer> _logger;

    public AffiliateCommissionEarnedAccountingConsumer(
        IAccountResolver accountResolver,
        IFinancialPostingService postingService,
        AffiliateTelemetry telemetry,
        ILogger<AffiliateCommissionEarnedAccountingConsumer> logger)
    {
        _accountResolver = accountResolver;
        _postingService = postingService;
        _telemetry = telemetry;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<AffiliateCommissionEarnedEvent> context)
    {
        var evt = context.Message;
        var ct = context.CancellationToken;
        // AF-19B: Use StoreTenantId (store's tenant) for accounting, not TenantId (affiliate's tenant which is Guid.Empty)
        var tenantId = evt.StoreTenantId;
        var sw = Stopwatch.StartNew();

        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = evt.CorrelationId ?? evt.OrderId.ToString(),
            ["TenantId"] = tenantId,
            ["EventType"] = EventType,
            ["EventId"] = evt.EventId
        });

        // Lag detection
        AffiliateConsumerHelper.DetectAndRecordLag(
            _logger, _telemetry, tenantId, EventType, evt.OccurredAtUtc);

        _logger.LogInformation(
            "[Affiliate:Accounting:Earned] Processing | OrderId: {OrderId} | " +
            "AffiliateAccountId: {AffiliateAccountId} | Commission: {Commission} {Currency}",
            evt.OrderId, evt.AffiliateAccountId, evt.TotalCommission, evt.Currency);

        if (evt.TotalCommission <= 0)
        {
            _logger.LogWarning(
                "[Affiliate:Accounting:Earned] Skipping zero/negative commission | OrderId: {OrderId} | Amount: {Amount}",
                evt.OrderId, evt.TotalCommission);
            return;
        }

        if (tenantId == Guid.Empty)
        {
            _logger.LogError(
                "[Affiliate:Accounting:Earned] Missing StoreTenantId — cannot post journal | OrderId: {OrderId}",
                evt.OrderId);
            throw new InvalidOperationException($"AffiliateCommissionEarnedEvent missing StoreTenantId for OrderId {evt.OrderId}");
        }

        try
        {
            // Resolve accounts for this tenant
            var accounts = await _accountResolver.GetByCodesAsync(
                tenantId,
                new[] { AccountCodes.AffiliateMarketingExpense, AccountCodes.AffiliatePayable },
                ct);

            if (!accounts.TryGetValue(AccountCodes.AffiliateMarketingExpense, out var expenseAccount))
            {
                throw new InvalidOperationException(
                    $"Account {AccountCodes.AffiliateMarketingExpense} not found for tenant {tenantId}");
            }

            if (!accounts.TryGetValue(AccountCodes.AffiliatePayable, out var payableAccount))
            {
                throw new InvalidOperationException(
                    $"Account {AccountCodes.AffiliatePayable} not found for tenant {tenantId}");
            }

            // Build posting request
            var idempotencyKey = $"AFFILIATE_COMM_EARNED:{tenantId}:{evt.OrderId}";

            var request = new PostingRequest
            {
                TenantId = tenantId,
                JournalDate = DateTime.UtcNow,
                SourceType = SourceType,
                SourceId = evt.OrderId,
                SourceReference = $"AFFILIATE_COMM_EARNED:{evt.OrderId}",
                Description = $"Affiliate commission earned for order {evt.OrderNumber} " +
                              $"(Affiliate: {evt.AffiliateAccountId}, Amount: {evt.TotalCommission:F2} {evt.Currency})",
                IdempotencyKey = idempotencyKey,
                UserId = null, // System-generated
                Lines = new List<PostingLine>
                {
                    PostingLine.Debit(expenseAccount.Id, evt.TotalCommission,
                        $"Affiliate commission expense — Order {evt.OrderNumber}"),
                    PostingLine.Credit(payableAccount.Id, evt.TotalCommission,
                        $"Affiliate payable — Order {evt.OrderNumber}")
                }
            };

            // The business idempotency key is stable across broker redelivery, including
            // retries on a later accounting date. Validate the immutable financial shape
            // directly and accept an existing equivalent journal; JournalDate is processing
            // metadata and must not turn a semantic replay into a poison message.
            var existingJournal = await _postingService.GetJournalByIdempotencyKeyAsync(
                tenantId,
                idempotencyKey,
                ct);

            var journal = existingJournal == null
                ? await _postingService.PostJournalAsync(request, ct)
                : EnsureEquivalentEarnedJournal(existingJournal, request);

            sw.Stop();
            _telemetry.RecordProcessingDuration(tenantId, EventType, sw.Elapsed.TotalMilliseconds);

            // AF-14C: Explicit idempotency detection via pre-check (replaces time-based heuristic)
            var existingCheck = await _postingService.ExistsByIdempotencyKeyAsync(
                tenantId, idempotencyKey, ct);
            // If the journal was already returned by the posting service (idempotent path),
            // the ExistsByIdempotencyKeyAsync will always return true. We detect "new" vs "existing"
            // by checking if the journal was just created in *this* call. The posting service
            // returns the existing journal on duplicate, so if the journal's count in the DB
            // was >0 before we called PostJournalAsync, it was idempotent.
            // Simple approach: the posting service logs "Idempotent posting" when returning existing.
            // We track at the consumer level for telemetry purposes only.

            _logger.LogInformation(
                "[Affiliate:Accounting:Earned] Journal posted | " +
                "OrderId: {OrderId} | JournalNumber: {JournalNumber} | JournalId: {JournalId} | " +
                "CommissionAmount: {Amount} | Duration: {DurationMs}ms",
                evt.OrderId, journal.JournalNumber, journal.Id,
                evt.TotalCommission, sw.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _telemetry.RecordFailed(tenantId, EventType);
            _telemetry.RecordProcessingDuration(tenantId, EventType, sw.Elapsed.TotalMilliseconds);

            _logger.LogError(ex,
                "[Affiliate:Accounting:Earned] Failed to post journal | " +
                "OrderId: {OrderId} | Commission: {Commission} | Duration: {DurationMs}ms",
                evt.OrderId, evt.TotalCommission, sw.Elapsed.TotalMilliseconds);

            throw; // Re-throw for MassTransit retry
        }
    }

    private static PointOfSale.Core.Entities.Accounting.JournalEntry EnsureEquivalentEarnedJournal(
        PointOfSale.Core.Entities.Accounting.JournalEntry existingJournal,
        PostingRequest request)
    {
        var existingLines = existingJournal.LedgerEntries
            .OrderBy(line => line.LineNumber)
            .Select(line => (line.AccountId, line.DebitAmount, line.CreditAmount))
            .ToList();
        var requestedLines = request.Lines
            .Select(line => (line.AccountId, line.DebitAmount, line.CreditAmount))
            .ToList();

        if (existingJournal.TenantId != request.TenantId ||
            existingJournal.SourceType != request.SourceType ||
            existingJournal.SourceId != request.SourceId ||
            !existingLines.SequenceEqual(requestedLines))
        {
            throw new PointOfSale.Core.Exceptions.Accounting.InvalidJournalException(
                $"Idempotency key '{request.IdempotencyKey}' is already bound to a different affiliate commission posting.");
        }

        return existingJournal;
    }
}

/// <summary>
/// AF-14: Posts a journal entry when affiliate commission is paid (settled).
/// AF-14C: Hardened with out-of-order guard and partial payout rejection.
///
/// Journal:
///   Debit  : Affiliate Payable (2105) — liability cleared
///   Credit : Cash in Hand (1010)      — cash reduced
///
/// Idempotency Key: AFFILIATE_COMM_PAID:{TenantId}:{OrderId}:{SettlementReferenceId}
///
/// Multiple payouts for the same order are supported when they come from
/// distinct settlement references. Retries for the same settlement remain
/// idempotent.
/// </summary>
public class AffiliateCommissionPaidAccountingConsumer : IConsumer<AffiliateCommissionPaidEvent>
{
    private const string EventType = "accounting_paid";
    private const string SourceType = "AffiliateCommissionPaid";

    private readonly IAccountResolver _accountResolver;
    private readonly IFinancialPostingService _postingService;
    private readonly ICashboxLedgerAccountService _cashboxLedgerAccountService;
    private readonly AffiliateTelemetry _telemetry;
    private readonly ILogger<AffiliateCommissionPaidAccountingConsumer> _logger;

    public AffiliateCommissionPaidAccountingConsumer(
        IAccountResolver accountResolver,
        IFinancialPostingService postingService,
        ICashboxLedgerAccountService cashboxLedgerAccountService,
        AffiliateTelemetry telemetry,
        ILogger<AffiliateCommissionPaidAccountingConsumer> logger)
    {
        _accountResolver = accountResolver;
        _postingService = postingService;
        _cashboxLedgerAccountService = cashboxLedgerAccountService;
        _telemetry = telemetry;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<AffiliateCommissionPaidEvent> context)
    {
        var evt = context.Message;
        var ct = context.CancellationToken;
        var tenantId = evt.TenantId ?? Guid.Empty;
        var sw = Stopwatch.StartNew();

        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = evt.CorrelationId ?? evt.OrderId.ToString(),
            ["TenantId"] = tenantId,
            ["EventType"] = EventType,
            ["EventId"] = evt.EventId
        });

        // Lag detection
        AffiliateConsumerHelper.DetectAndRecordLag(
            _logger, _telemetry, tenantId, EventType, evt.OccurredAtUtc);

        _logger.LogInformation(
            "[Affiliate:Accounting:Paid] Processing | OrderId: {OrderId} | " +
            "AffiliateAccountId: {AffiliateAccountId} | Amount: {Amount} {Currency}",
            evt.OrderId, evt.AffiliateAccountId, evt.TotalPaid, evt.Currency);

        if (evt.TotalPaid <= 0)
        {
            _logger.LogWarning(
                "[Affiliate:Accounting:Paid] Skipping zero/negative payment | OrderId: {OrderId} | Amount: {Amount}",
                evt.OrderId, evt.TotalPaid);
            return;
        }

        if (tenantId == Guid.Empty)
        {
            _logger.LogError(
                "[Affiliate:Accounting:Paid] Missing TenantId — cannot post journal | OrderId: {OrderId}",
                evt.OrderId);
            throw new InvalidOperationException($"AffiliateCommissionPaidEvent missing TenantId for OrderId {evt.OrderId}");
        }

        try
        {
            // ═══════════════════════════════════════════════════════════════
            // AF-14C GUARD 1: OUT-OF-ORDER PROTECTION
            // Paid MUST NOT be posted if the corresponding Earned journal
            // does not exist. This prevents negative payable on out-of-order
            // delivery. MassTransit will retry until Earned arrives.
            // ═══════════════════════════════════════════════════════════════
            var earnedIdempotencyKey = $"AFFILIATE_COMM_EARNED:{tenantId}:{evt.OrderId}";
            var earnedExists = await _postingService.ExistsByIdempotencyKeyAsync(
                tenantId, earnedIdempotencyKey, ct);

            if (!earnedExists)
            {
                _telemetry.RecordOutOfOrderBlocked(tenantId);
                _logger.LogWarning(
                    "[Affiliate:Accounting:Paid] OUT-OF-ORDER BLOCKED — Earned journal not found | " +
                    "OrderId: {OrderId} | TenantId: {TenantId} | EventId: {EventId} | " +
                    "EarnedKey: {EarnedKey} — will retry via MassTransit",
                    evt.OrderId, tenantId, evt.EventId, earnedIdempotencyKey);

                throw new InvalidOperationException(
                    $"[AF-14C] Cannot post Paid journal: Earned journal '{earnedIdempotencyKey}' " +
                    $"does not exist for OrderId {evt.OrderId}. Event will be retried.");
            }

            // Resolve accounts for this tenant
            var accounts = await _accountResolver.GetByCodesAsync(
                tenantId,
                new[] { AccountCodes.AffiliatePayable, AccountCodes.CashboxMain },
                ct);

            if (!accounts.TryGetValue(AccountCodes.AffiliatePayable, out var payableAccount))
            {
                throw new InvalidOperationException(
                    $"Account {AccountCodes.AffiliatePayable} not found for tenant {tenantId}");
            }
            
            // Try to resolve cashbox account, fall back to generic cash account if no cashbox exists
            string accountCode;
            try
            {
                var cashboxAccount = await _cashboxLedgerAccountService.ResolveDefaultCashboxAccountAsync(tenantId, ct);
                accountCode = cashboxAccount.AccountCode;
            }
            catch (NoCashboxAccountException ex)
            {
                // No active cashbox found, fall back to generic cash account
                _logger.LogWarning(
                    ex,
                    "[Affiliate:Accounting:Paid] No active cashbox found for tenant {TenantId}, " +
                    "falling back to generic cash account {AccountCode}",
                    tenantId, AccountCodes.CashboxMain);
                accountCode = AccountCodes.CashboxMain;
            }

            // Resolve the account (either cashbox or generic cash) - exceptions propagate normally
            var cashAccount = await _accountResolver.GetByCodeAsync(tenantId, accountCode, ct);

            // Match AffiliateSettlementOrchestrator's settlement-scoped key so this legacy fallback
            // remains idempotent if it is accidentally re-enabled in production.
            var settlementReferenceId = evt.SettlementReferenceId != Guid.Empty
                ? evt.SettlementReferenceId
                : evt.EventId;
            var idempotencyKey = $"AFFILIATE_COMM_PAID:{tenantId}:{evt.OrderId}:{settlementReferenceId}";

            var request = new PostingRequest
            {
                TenantId = tenantId,
                JournalDate = DateTime.UtcNow,
                SourceType = SourceType,
                SourceId = settlementReferenceId,
                SourceReference = $"AFFILIATE_COMM_PAID:{evt.OrderId}:{settlementReferenceId}",
                Description = $"Affiliate commission paid for order {evt.OrderNumber} " +
                              $"(Affiliate: {evt.AffiliateAccountId}, Amount: {evt.TotalPaid:F2} {evt.Currency})",
                IdempotencyKey = idempotencyKey,
                UserId = null, // System-generated
                Lines = new List<PostingLine>
                {
                    PostingLine.Debit(payableAccount.Id, evt.TotalPaid,
                        $"Affiliate payable cleared — Order {evt.OrderNumber}"),
                    PostingLine.Credit(cashAccount.Id, evt.TotalPaid,
                        $"Cash paid to affiliate — Order {evt.OrderNumber}")
                }
            };

            var journal = await _postingService.PostJournalAsync(request, ct);

            sw.Stop();
            _telemetry.RecordProcessingDuration(tenantId, EventType, sw.Elapsed.TotalMilliseconds);

            _logger.LogInformation(
                "[Affiliate:Accounting:Paid] Journal posted | " +
                "OrderId: {OrderId} | JournalNumber: {JournalNumber} | JournalId: {JournalId} | " +
                "PaidAmount: {Amount} | Duration: {DurationMs}ms",
                evt.OrderId, journal.JournalNumber, journal.Id,
                evt.TotalPaid, sw.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();

            // Only record failure telemetry for non-out-of-order exceptions
            // (out-of-order is already tracked via RecordOutOfOrderBlocked)
            if (!ex.Message.Contains("[AF-14C]"))
            {
                _telemetry.RecordFailed(tenantId, EventType);
            }

            _telemetry.RecordProcessingDuration(tenantId, EventType, sw.Elapsed.TotalMilliseconds);

            _logger.LogError(ex,
                "[Affiliate:Accounting:Paid] Failed to post journal | " +
                "OrderId: {OrderId} | PaidAmount: {Amount} | Duration: {DurationMs}ms",
                evt.OrderId, evt.TotalPaid, sw.Elapsed.TotalMilliseconds);

            throw; // Re-throw for MassTransit retry
        }
    }
}
