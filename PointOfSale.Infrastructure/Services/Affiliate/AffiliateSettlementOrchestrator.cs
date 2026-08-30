using System.Data;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Entities.Affiliate;
using PointOfSale.Core.Events;
using PointOfSale.Core.Events.Affiliate;
using PointOfSale.Core.Exceptions.Accounting;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Interfaces.Affiliate;
using PointOfSale.Core.Interfaces.Treasury;
using PointOfSale.Core.Models.Accounting;
using PointOfSale.Infrastructure.Accounting;
using PointOfSale.Infrastructure.Data;
using MassTransitOutboxMessage = MassTransit.EntityFrameworkCoreIntegration.OutboxMessage;

namespace PointOfSale.Infrastructure.Services.Affiliate;

/// <summary>
/// AF-38H: Unified Affiliate Payout Orchestrator.
/// AF-38Y: Hardened with all V-1..V-5 structural fixes.
/// 
/// SINGLE AUTHORITATIVE PAYOUT EXECUTION PIPELINE.
/// 
/// Both Legacy Settlement and Withdrawal Engine MUST call ExecuteAffiliatePayoutAsync.
/// No exceptions. No alternative paths.
/// 
/// EXECUTION PIPELINE (MANDATORY ORDER):
/// 1. Begin SERIALIZABLE Transaction (V-2: eliminates TOCTOU race)
/// 2. Validate Available Balance (V-1: uses RawAvailable, not clamped)
/// 3. Create BalanceLedger.Withdrawal (monetary authority)
/// 4. Mark CommissionLedger entries as Paid (FIFO lifecycle sync, V-4: tenant-filtered)
/// 5. Auto-Reconcile Withdrawal Requests
/// 6. Mark PayoutRequest as Paid (V-3: inside transaction)
/// 7. Commit Transaction
/// 
/// CONCURRENCY MODEL (V-2):
/// - IsolationLevel.Serializable ensures PostgreSQL detects write skew
/// - Built-in retry (MAX_RETRIES=3) handles serialization_failure (40001)
/// - No manual row-lock hacks — true financial isolation guarantee
/// 
/// INVARIANTS AFTER EXECUTION:
/// - BalanceLedger.Withdrawal always exists
/// - CommissionLedger.Paid entries are created only for the commission amount
///   settled by the current payout execution
/// - PayoutRequests are always reconciled
/// - PayoutRequest.Status = Paid (if payoutRequestId provided)
/// - No lifecycle drift between monetary truth and commission lifecycle
/// - No cross-tenant commission marking (V-4: StoreTenantId enforced)
/// </summary>
public class AffiliateSettlementOrchestrator : IAffiliateSettlementOrchestrator
{
    private readonly ApplicationDbContext _db;
    private readonly IAffiliatePayoutService _payoutService;
    private readonly IEventPublisher _eventPublisher;
    private readonly IFinancialPostingService _postingService;
    private readonly IAccountResolver _accountResolver;
    private readonly ICashboxLedgerAccountService _cashboxLedgerAccountService;
    private readonly ILogger<AffiliateSettlementOrchestrator> _logger;
    private readonly AffiliateSettlementOptions _options;

    /// <summary>
    /// AF-38Y V-2: Maximum retries for Serializable transaction conflicts.
    /// PostgreSQL throws SqlState 40001 (serialization_failure) when write skew detected.
    /// </summary>
    private const int MAX_RETRIES = 3;
    private const int ReadinessInspectionLimit = 500;
    private const string AffiliatePayoutRailNamespace = "AFFILIATE_PAYOUT";
    private static readonly string[] BlockingUnresolvedPaymentStatuses =
    [
        "PendingResolution",
        "PendingTenantResolution",
        "RoutedToSuspense",
        "SuspenseMatchedPaymentPendingClearance"
    ];
    private static readonly string[] BlockingUnresolvedRefundStatuses =
    [
        "ProviderRefundPendingReconciliation"
    ];

    public AffiliateSettlementOrchestrator(
        ApplicationDbContext db,
        IAffiliatePayoutService payoutService,
        IEventPublisher eventPublisher,
        IFinancialPostingService postingService,
        IAccountResolver accountResolver,
        ICashboxLedgerAccountService cashboxLedgerAccountService,
        ILogger<AffiliateSettlementOrchestrator> logger,
        IOptions<AffiliateSettlementOptions> options)
    {
        _db = db;
        _payoutService = payoutService;
        _eventPublisher = eventPublisher;
        _postingService = postingService;
        _accountResolver = accountResolver;
        _cashboxLedgerAccountService = cashboxLedgerAccountService;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<PayoutExecutionResult> ExecuteAffiliatePayoutAsync(
        Guid affiliateAccountId,
        Guid storeTenantId,
        decimal amount,
        SettlementSource source,
        string paymentReference,
        Guid? orderId = null,
        Guid? payoutRequestId = null,
        string? reference = null,
        string? adminNotes = null,
        decimal? externalTransferAmount = null,
        Guid? paidByUserId = null,
        CancellationToken ct = default)
    {
        if (affiliateAccountId == Guid.Empty)
            throw new ArgumentException("AffiliateAccountId is required", nameof(affiliateAccountId));
        if (storeTenantId == Guid.Empty)
            throw new ArgumentException("StoreTenantId is required", nameof(storeTenantId));
        if (amount <= 0)
            throw new ArgumentException("Payout amount must be positive", nameof(amount));
        if (externalTransferAmount.HasValue && externalTransferAmount.Value < amount)
            throw new ArgumentException("External transfer amount cannot be less than the payout allocation amount.", nameof(externalTransferAmount));

        var externalPaymentReference = EnsureExternalPaymentReference(paymentReference);
        var confirmedTransferAmount = externalTransferAmount ?? amount;
        var sourceLabel = source == SettlementSource.LegacySettlement ? "LegacySettlement" : "WithdrawalEngine";
        await EnsureVerifiedExternalPayoutEvidenceAllowedAsync(
            affiliateAccountId,
            storeTenantId,
            confirmedTransferAmount,
            externalPaymentReference,
            sourceLabel,
            ct);

        // ═══════════════════════════════════════════════════════════════════
        // AF-38Y V-2: SERIALIZABLE RETRY LOOP
        //
        // PostgreSQL Serializable Snapshot Isolation (SSI) will detect 
        // concurrent transactions that read the same data and both write.
        // One transaction succeeds, the other gets SqlState 40001.
        // We retry with exponential backoff.
        // ═══════════════════════════════════════════════════════════════════
        for (int attempt = 1; attempt <= MAX_RETRIES; attempt++)
        {
            try
            {
                var result = await ExecutePayoutCoreAsync(
                    affiliateAccountId, storeTenantId, amount, source,
                    orderId, payoutRequestId, reference,
                    externalPaymentReference, confirmedTransferAmount, adminNotes, paidByUserId, sourceLabel, ct);

                // Success — exit retry loop
                return result;
            }
            catch (Exception ex) when (IsSerializationFailure(ex))
            {
                if (attempt == MAX_RETRIES)
                {
                    _logger.LogError(ex,
                        "[AF-38Y:Orchestrator] Serialization failure exhausted retries | " +
                        "Source={Source} | AffiliateId={AffiliateId} | Attempt={Attempt}/{Max}",
                        sourceLabel, affiliateAccountId, attempt, MAX_RETRIES);
                    throw;
                }

                _logger.LogWarning(
                    "[AF-38Y:Orchestrator] Serialization failure, retrying | " +
                    "Source={Source} | AffiliateId={AffiliateId} | Attempt={Attempt}/{Max}",
                    sourceLabel, affiliateAccountId, attempt, MAX_RETRIES);

                // Exponential backoff: 50ms, 100ms, 150ms
                await Task.Delay(50 * attempt, ct);
            }
        }

        // Should never reach here (loop either returns or throws)
        throw new InvalidOperationException("[AF-38Y] Retry loop exited without success or failure");
    }

    /// <summary>
    /// Core payout execution — runs inside a SERIALIZABLE transaction.
    /// Separated from retry loop for clarity.
    /// Returns PayoutExecutionResult with info about which orders were paid.
    /// </summary>
    private async Task<PayoutExecutionResult> ExecutePayoutCoreAsync(
        Guid affiliateAccountId,
        Guid storeTenantId,
        decimal amount,
        SettlementSource source,
        Guid? orderId,
        Guid? payoutRequestId,
        string? reference,
        string paymentReference,
        decimal externalTransferAmount,
        string? adminNotes,
        Guid? paidByUserId,
        string sourceLabel,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "[AF-38H:Orchestrator] Starting payout | Source={Source} | AffiliateId={AffiliateId} | " +
            "StoreTenantId={StoreTenantId} | Amount={Amount} | OrderId={OrderId} | " +
            "PayoutRequestId={PayoutRequestId} | Reference={Reference}",
            sourceLabel, affiliateAccountId, storeTenantId, amount, orderId, payoutRequestId, reference);

        // ═══════════════════════════════════════════════════════════════════
        // STEP 1 — Begin SERIALIZABLE Transaction (V-2 FIX)
        // ═══════════════════════════════════════════════════════════════════
        await using var tx = await _db.Database
            .BeginTransactionAsync(IsolationLevel.Serializable, ct);

        try
        {
            // Cross-system readiness is part of the payout authority boundary.
            // Keep it inside the same serializable retry scope as balance, evidence,
            // withdrawal, commission lifecycle, payout request, and accounting writes.
            await EnsureAffiliateOutwardMoneyMovementReadyAsync(storeTenantId, ct);

            var payoutRequest = payoutRequestId.HasValue
                ? await EnsurePayoutRequestDestinationAuthorityAsync(
                    payoutRequestId.Value,
                    affiliateAccountId,
                    storeTenantId,
                    amount,
                    ct)
                : null;

            if (payoutRequestId.HasValue && (!paidByUserId.HasValue || paidByUserId.Value == Guid.Empty))
            {
                throw new InvalidOperationException(
                    "AFFILIATE_PAYOUT_EXECUTOR_IDENTITY_REQUIRED: Current backend admin identity is required before a payout request can be marked paid.");
            }

            // ═══════════════════════════════════════════════════════════════
            // STEP 2 — Validate Available Balance
            //
            // CRITICAL DISTINCTION (AF-38Y):
            // - RawAvailable = Approved + Adjustments + Withdrawals - PendingRequests
            //   → This is for the AFFILIATE PORTAL (prevents over-requesting)
            //
            // - Net Actual Balance = Approved + Adjustments + Withdrawals
            //   → This is for PAYOUT EXECUTION (no reservation deduction)
            //
            // WHY: Pending requests are "reservations" — they prevent the affiliate
            // from requesting MORE withdrawals. But when the admin/system is actually
            // EXECUTING a payout, those reservations are irrelevant:
            //
            // 1. WithdrawalEngine: The PayoutRequest being paid IS the reservation.
            //    Deducting it would create a self-referential deadlock (request
            //    reserves 120, then paying it sees 0 available).
            //
            // 2. LegacySettlement: Admin is explicitly paying commissions.
            //    Pending requests should not block admin from settling.
            //    AutoReconcile will fulfill the requests afterward.
            //
            // CONCURRENCY: Serializable isolation prevents double-spend.
            // If two payouts race, PostgreSQL SSI detects write skew → one retries.
            // ═══════════════════════════════════════════════════════════════
            if (payoutRequestId.HasValue)
            {
                var referenceAlreadyUsed = await _db.AffiliatePayoutRequests
                    .IgnoreQueryFilters()
                    .AnyAsync(request =>
                        request.StoreTenantId == storeTenantId &&
                        request.PaymentReference == paymentReference &&
                        request.Id != payoutRequestId.Value &&
                        request.Status == PayoutRequestStatus.Paid,
                        ct);

                if (referenceAlreadyUsed)
                {
                    throw new InvalidOperationException(
                        "AFFILIATE_PAYOUT_EXTERNAL_EVIDENCE_ALREADY_USED: This external payment reference has already closed another paid affiliate payout request.");
                }
            }

            var balance = await _payoutService.GetAvailableBalanceAsync(affiliateAccountId, storeTenantId, ct);

            // Net actual balance = RawAvailable + PendingRequests (un-reserve)
            // This represents the TRUE amount of money in the affiliate's wallet.
            var netActualBalance = balance.RawAvailable + balance.TotalPendingRequests;

            if (amount > netActualBalance)
            {
                _logger.LogWarning(
                    "[AF-38H:Orchestrator] BLOCKED — Insufficient balance | Source={Source} | " +
                    "AffiliateId={AffiliateId} | Required={Required} {Currency} | " +
                    "NetActualBalance={NetBalance} {Currency} | RawAvailable={RawAvailable} | " +
                    "PendingReservations={Pending} | Shortfall={Shortfall}",
                    sourceLabel, affiliateAccountId, amount, balance.Currency,
                    netActualBalance, balance.Currency, balance.RawAvailable,
                    balance.TotalPendingRequests, amount - netActualBalance);

                throw new InvalidOperationException(
                    $"Insufficient balance for affiliate payout. " +
                    $"Available: {netActualBalance:F2} {balance.Currency}, " +
                    $"Required: {amount:F2} {balance.Currency}, " +
                    $"Shortfall: {(amount - netActualBalance):F2} {balance.Currency}. " +
                    $"Source: {sourceLabel}.");
            }

            _logger.LogInformation(
                "[AF-38H:Orchestrator] Balance check passed | Source={Source} | AffiliateId={AffiliateId} | " +
                "NetActualBalance={NetBalance} | PendingReservations={Pending} | Required={Required}",
                sourceLabel, affiliateAccountId, netActualBalance, balance.TotalPendingRequests, amount);

            // ═══════════════════════════════════════════════════════════════
            // STEP 3 — Create BalanceLedger.Withdrawal (monetary authority)
            // ═══════════════════════════════════════════════════════════════

            // Determine the ReferenceId for the BalanceLedger entry
            // - For WithdrawalEngine: PayoutRequestId
            // - For LegacySettlement: OrderId (backward compatible)
            var referenceId = payoutRequestId ?? orderId
                ?? throw new InvalidOperationException(
                    "Either payoutRequestId or orderId must be provided for BalanceLedger reference.");

            // Idempotency check: already has BalanceLedger withdrawal for this reference?
            var existingWithdrawal = await _db.AffiliateBalanceLedger
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(l =>
                    l.ReferenceId == referenceId &&
                    l.EntryType == BalanceEntryType.Withdrawal &&
                    l.AffiliateAccountId == affiliateAccountId, ct);

            if (existingWithdrawal != null)
            {
                // Verify critical fields match to ensure true idempotency
                var amountMatches = existingWithdrawal.Amount == amount;
                var tenantMatches = existingWithdrawal.StoreTenantId == storeTenantId;
                var currencyMatches = string.Equals(
                    existingWithdrawal.Currency,
                    balance.Currency,
                    StringComparison.OrdinalIgnoreCase);
                var accountMatches = existingWithdrawal.AffiliateAccountId == affiliateAccountId;

                if (amountMatches && tenantMatches && currencyMatches && accountMatches)
                {
                    _logger.LogWarning(
                        "[AF-38H:Orchestrator] Idempotent skip — BalanceLedger withdrawal already exists with matching fields | " +
                        "Source={Source} | AffiliateId={AffiliateId} | ReferenceId={ReferenceId} | Amount={Amount} | Currency={Currency}",
                        sourceLabel, affiliateAccountId, referenceId, amount, balance.Currency);

                    await tx.CommitAsync(ct);
                    return new PayoutExecutionResult { WasIdempotentSkip = true };
                }
                else
                {
                    // Financial inconsistency detected - log all mismatches
                    var mismatches = new List<string>();
                    if (!amountMatches)
                        mismatches.Add($"Amount (existing: {existingWithdrawal.Amount}, incoming: {amount})");
                    if (!tenantMatches)
                        mismatches.Add($"TenantId (existing: {existingWithdrawal.StoreTenantId}, incoming: {storeTenantId})");
                    if (!currencyMatches)
                        mismatches.Add($"Currency (existing: {existingWithdrawal.Currency}, incoming: {balance.Currency})");
                    if (!accountMatches)
                        mismatches.Add($"AffiliateAccountId (existing: {existingWithdrawal.AffiliateAccountId}, incoming: {affiliateAccountId})");

                    _logger.LogError(
                        "[AF-38H:Orchestrator] IDEMPOTENCY CONFLICT — Withdrawal exists with different fields | " +
                        "Source={Source} | AffiliateId={AffiliateId} | ReferenceId={ReferenceId} | " +
                        "Mismatches: {Mismatches}",
                        sourceLabel, affiliateAccountId, referenceId, string.Join("; ", mismatches));

                    throw new InvalidOperationException(
                        $"Idempotency conflict: A withdrawal with reference {referenceId} already exists but with different values. " +
                        $"Mismatches: {string.Join("; ", mismatches)}. This indicates a financial data inconsistency.");
                }
            }

            await AllocateExternalTransferEvidenceAsync(
                storeTenantId,
                affiliateAccountId,
                referenceId,
                source,
                paymentReference,
                externalTransferAmount,
                amount,
                balance.Currency,
                payoutRequestId,
                ct);

            var approvedEntries = await _db.AffiliateCommissionLedger
                .IgnoreQueryFilters()
                .Where(e =>
                    e.AffiliateAccountId == affiliateAccountId &&
                    e.StoreTenantId == storeTenantId &&
                    e.EntryType == CommissionEntryType.Approved &&
                    // AF-38H FIX: For LegacySettlement with orderId, pay ONLY that order's commissions
                    // For WithdrawalEngine (orderId=null), use FIFO across all orders
                    (source != SettlementSource.LegacySettlement || !orderId.HasValue || e.OrderId == orderId.Value))
                .OrderBy(e => e.RecordedAtUtc)
                .ThenBy(e => e.Id)
                .ToListAsync(ct);

            // Use the immutable checkout-time cap snapshot. Current product policy is
            // not payout authority for a historical approved commission.
            var approvedOrderItemIds = approvedEntries
                .Select(entry => entry.OrderItemId)
                .Distinct()
                .ToList();

            var maxPerItemByOrderItem = approvedOrderItemIds.Count == 0
                ? new Dictionary<Guid, decimal?>()
                : await _db.OrderItems
                    .IgnoreQueryFilters()
                    .Where(orderItem => approvedOrderItemIds.Contains(orderItem.Id))
                    .Select(orderItem => new
                    {
                        orderItem.Id,
                        MaxCommissionPerItem = orderItem.AffiliateMaxCommissionPerItemSnapshot
                    })
                    .ToDictionaryAsync(x => x.Id, x => x.MaxCommissionPerItem, ct);

            var reversalAmountsByOrderItem = approvedOrderItemIds.Count == 0
                ? new Dictionary<Guid, AffiliateCommissionReversalTotals>()
                : await _db.AffiliateCommissionLedger
                    .IgnoreQueryFilters()
                    .Where(e =>
                        e.AffiliateAccountId == affiliateAccountId &&
                        e.StoreTenantId == storeTenantId &&
                        e.EntryType == CommissionEntryType.Reversed &&
                        approvedOrderItemIds.Contains(e.OrderItemId))
                    .GroupBy(e => e.OrderItemId)
                    .Select(group => new
                    {
                        OrderItemId = group.Key,
                        BaseAmount = group.Sum(entry => entry.BaseAmount),
                        CommissionAmount = group.Sum(entry => entry.CommissionAmount)
                    })
                    .ToDictionaryAsync(
                        item => item.OrderItemId,
                        item => new AffiliateCommissionReversalTotals(
                            item.BaseAmount,
                            item.CommissionAmount),
                        ct);

            var alreadyPaidOrderItemIds = await _db.AffiliateCommissionLedger
                .IgnoreQueryFilters()
                .Where(e =>
                    e.AffiliateAccountId == affiliateAccountId &&
                    e.StoreTenantId == storeTenantId &&
                    e.EntryType == CommissionEntryType.Paid)
                .Select(e => e.OrderItemId)
                .ToHashSetAsync(ct);

            var payoutPlan = BuildExactPayoutPlan(
                approvedEntries,
                alreadyPaidOrderItemIds,
                reversalAmountsByOrderItem,
                maxPerItemByOrderItem,
                amount,
                affiliateAccountId,
                storeTenantId);

            var description = source switch
            {
                SettlementSource.LegacySettlement => $"Legacy settlement - {reference ?? $"Order #{orderId}"} | ExternalRef: {paymentReference}",
                SettlementSource.WithdrawalEngine => $"Withdrawal payout - {reference ?? $"Request #{payoutRequestId}"} | ExternalRef: {paymentReference}",
                _ => $"Affiliate payout - {reference ?? "Unknown source"} | ExternalRef: {paymentReference}"
            };

            var withdrawalEntry = AffiliateBalanceLedger.CreateWithdrawal(
                storeTenantId: storeTenantId,
                affiliateAccountId: affiliateAccountId,
                payoutRequestId: referenceId,
                amount: amount,
                currency: balance.Currency,
                description: description);

            _db.AffiliateBalanceLedger.Add(withdrawalEntry);

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                // Unique constraint violation - fetch the existing withdrawal to verify idempotency
                var existingWithdrawalEntry = await _db.AffiliateBalanceLedger
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(l =>
                        l.ReferenceId == referenceId &&
                        l.EntryType == BalanceEntryType.Withdrawal &&
                        l.AffiliateAccountId == affiliateAccountId, ct);

                if (existingWithdrawalEntry != null)
                {
                    // Verify critical fields match
                    var amountMatches = existingWithdrawalEntry.Amount == amount;
                    var tenantMatches = existingWithdrawalEntry.StoreTenantId == storeTenantId;
                    var currencyMatches = string.Equals(
                        existingWithdrawalEntry.Currency,
                        balance.Currency,
                        StringComparison.OrdinalIgnoreCase);
                    var accountMatches = existingWithdrawalEntry.AffiliateAccountId == affiliateAccountId;

                    if (amountMatches && tenantMatches && currencyMatches && accountMatches)
                    {
                        _logger.LogInformation(
                            "[AF-38H:Orchestrator] Duplicate prevented by unique constraint (idempotent skip) | " +
                            "Source={Source} | AffiliateId={AffiliateId} | ReferenceId={ReferenceId} | Amount={Amount} | Currency={Currency}",
                            sourceLabel, affiliateAccountId, referenceId, amount, balance.Currency);
                        await tx.RollbackAsync(ct);
                        return new PayoutExecutionResult { WasIdempotentSkip = true };
                    }
                    else
                    {
                        // Financial inconsistency detected
                        var mismatches = new List<string>();
                        if (!amountMatches)
                            mismatches.Add($"Amount (existing: {existingWithdrawalEntry.Amount}, incoming: {amount})");
                        if (!tenantMatches)
                            mismatches.Add($"TenantId (existing: {existingWithdrawalEntry.StoreTenantId}, incoming: {storeTenantId})");
                        if (!currencyMatches)
                            mismatches.Add($"Currency (existing: {existingWithdrawalEntry.Currency}, incoming: {balance.Currency})");
                        if (!accountMatches)
                            mismatches.Add($"AffiliateAccountId (existing: {existingWithdrawalEntry.AffiliateAccountId}, incoming: {affiliateAccountId})");

                        _logger.LogError(
                            "[AF-38H:Orchestrator] IDEMPOTENCY CONFLICT on unique constraint — Withdrawal exists with different fields | " +
                            "Source={Source} | AffiliateId={AffiliateId} | ReferenceId={ReferenceId} | " +
                            "Mismatches: {Mismatches}",
                            sourceLabel, affiliateAccountId, referenceId, string.Join("; ", mismatches));

                        await tx.RollbackAsync(ct);
                        throw new InvalidOperationException(
                            $"Idempotency conflict on unique constraint: A withdrawal with reference {referenceId} already exists but with different values. " +
                            $"Mismatches: {string.Join("; ", mismatches)}. This indicates a financial data inconsistency.",
                            ex);
                    }
                }
                else
                {
                    // Unique violation but no matching record found - unexpected state
                    _logger.LogError(
                        "[AF-38H:Orchestrator] Unique constraint violation but no existing withdrawal found | " +
                        "Source={Source} | AffiliateId={AffiliateId} | ReferenceId={ReferenceId}",
                        sourceLabel, affiliateAccountId, referenceId);
                    await tx.RollbackAsync(ct);
                    throw new InvalidOperationException(
                        $"Unique constraint violation for withdrawal reference {referenceId}, but no existing record found. This indicates a database inconsistency.",
                        ex);
                }
            }

            _logger.LogInformation(
                "[AF-38H:Orchestrator] BalanceLedger.Withdrawal created | Source={Source} | " +
                "AffiliateId={AffiliateId} | Amount={Amount} | LedgerEntryId={LedgerEntryId}",
                sourceLabel, affiliateAccountId, amount, withdrawalEntry.Id);

            // ═══════════════════════════════════════════════════════════════
            // STEP 4 — Mark CommissionLedger Entries as Paid (CRITICAL)
            //
            // This is the MISSING LIFECYCLE SYNC that AF-38H fixes.
            //
            // AF-38Y V-4: All queries filter by StoreTenantId for defense-in-depth.
            //
            // Logic:
            // - Fetch Approved CommissionLedger entries (FIFO by RecordedAtUtc)
            // - Net each Approved entry against refund Reversed entries
            // - For each net-payable Approved entry without a Paid twin,
            //   create a CommissionLedger.Paid entry for the unreversed remainder
            // - Budget is the CURRENT payout amount only.
            //   Historical payout drift must be repaired by an explicit reconciliation job,
            //   not by inventing CommissionLedger.Paid history during a live payout.
            // - This is APPEND-ONLY: never modify Approved entries
            // ═══════════════════════════════════════════════════════════════

            var paidCount = 0;

            // AF-38Y: Track which orders were paid for event publishing
            var paidOrderTracker = new Dictionary<Guid, (decimal Total, int Count, string Currency)>();

            foreach (var selectedEntry in payoutPlan.SelectedEntries)
            {
                var approved = selectedEntry.ApprovedEntry;
                var paidEntry = AffiliateCommissionLedger.CreatePaidNetOfReversals(
                    approved,
                    selectedEntry.PayableBaseAmount,
                    selectedEntry.PayableCommissionAmount,
                    selectedEntry.ApprovedMaxPerItem);

                try
                {
                    _db.AffiliateCommissionLedger.Add(paidEntry);
                    await _db.SaveChangesAsync(ct);
                    paidCount++;
                    // Track paid amount per order for event publishing
                    if (paidOrderTracker.TryGetValue(approved.OrderId, out var existing))
                        paidOrderTracker[approved.OrderId] = (existing.Total + selectedEntry.PayableCommissionAmount, existing.Count + 1, approved.Currency);
                    else
                        paidOrderTracker[approved.OrderId] = (selectedEntry.PayableCommissionAmount, 1, approved.Currency);

                    _logger.LogDebug(
                        "[AF-38H:Orchestrator] CommissionLedger.Paid created | " +
                        "OrderItemId={OrderItemId} | Amount={Amount} | ReversedAmount={ReversedAmount}",
                        approved.OrderItemId,
                        selectedEntry.PayableCommissionAmount,
                        selectedEntry.ReversedCommissionAmount);
                }
                catch (DbUpdateException ex) when (IsUniqueViolation(ex))
                {
                    _db.Entry(paidEntry).State = EntityState.Detached;
                    throw new InvalidOperationException(
                        $"AFFILIATE_PAYOUT_COMMISSION_AUTHORITY_CHANGED: OrderItem {approved.OrderItemId} was already marked Paid while payout {referenceId} was executing. Retry with refreshed commission authority.",
                        ex);
                }
            }

            _logger.LogInformation(
                "[AF-38H:Orchestrator] CommissionLedger sync complete | Source={Source} | " +
                "AffiliateId={AffiliateId} | PaidEntries={PaidCount} | PlannedAmount={PlannedAmount}",
                sourceLabel, affiliateAccountId, paidCount, payoutPlan.TotalAmount);

            if (paidOrderTracker.Count > 0)
            {
                await EnsureAffiliateEarnedAccountingPostedAsync(
                    storeTenantId,
                    paidOrderTracker.Keys,
                    ct);

                await PostAffiliatePaidAccountingAsync(
                    storeTenantId,
                    affiliateAccountId,
                    referenceId,
                    paidOrderTracker,
                    ct);
            }

            // ═══════════════════════════════════════════════════════════════
            // STEP 5 — Auto-Reconcile Withdrawal Requests
            // ═══════════════════════════════════════════════════════════════
            // A selected withdrawal request owns its own payout lineage. Reconciling all
            // open requests here could mark an older request paid and then also mark the
            // selected request paid from the same transfer. FIFO reconciliation remains
            // valid only for legacy settlement executions that have no selected request.
            if (!payoutRequestId.HasValue)
            {
                await _payoutService.AutoReconcileWithdrawalRequestsAsync(
                    affiliateAccountId,
                    storeTenantId,
                    amount,
                    paymentReference,
                    ct);
            }

            // ═══════════════════════════════════════════════════════════════
            // STEP 6 — Mark PayoutRequest as Paid (V-3 FIX: inside transaction)
            //
            // AF-38Y: Previously done OUTSIDE the transaction in the controller,
            // causing lifecycle drift if the post-orchestrator SaveChanges failed.
            // Now handled atomically within the same SERIALIZABLE transaction.
            // ═══════════════════════════════════════════════════════════════
            if (payoutRequestId.HasValue)
            {
                // FAIL-FAST: If the payout request is missing, abort before committing the transaction.
                // Withdrawal and commission updates have already been made in this transaction, so
                // silently continuing would orphan those updates without a corresponding payout request.
                if (payoutRequest == null)
                {
                    throw new InvalidOperationException(
                        $"AFFILIATE_PAYOUT_REQUEST_NOT_FOUND: PayoutRequest {payoutRequestId.Value} not found for " +
                        $"AffiliateAccount {affiliateAccountId} and StoreTenant {storeTenantId}. " +
                        $"Cannot complete settlement without a valid payout request.");
                }

                if (payoutRequest.Status != PayoutRequestStatus.Paid)
                {
                    payoutRequest.MarkPaid(paidByUserId!.Value, paymentReference, adminNotes);
                    
                    try
                    {
                        await _db.SaveChangesAsync(ct);
                    }
                    catch (DbUpdateException ex) when (IsUniqueViolation(ex))
                    {
                        // Unique constraint violation on PaymentReference - another paid request already uses this reference
                        throw new InvalidOperationException(
                            "AFFILIATE_PAYOUT_EXTERNAL_EVIDENCE_ALREADY_USED: This external payment reference has already closed another paid affiliate payout request.",
                            ex);
                    }

                    _logger.LogInformation(
                        "[AF-38Y:Orchestrator] PayoutRequest marked Paid inside transaction | " +
                        "RequestId={RequestId} | PaymentRef={PaymentRef}",
                        payoutRequestId.Value, paymentReference);
                }
            }

            // ═══════════════════════════════════════════════════════════════
            // STEP 7 — Commit Transaction
            // ═══════════════════════════════════════════════════════════════
            if (paidOrderTracker.Count > 0)
            {
                var affiliateAuthorityTenantId = await ResolveAffiliateAuthorityTenantIdAsync(
                    affiliateAccountId,
                    ct);
                var paidOrderIds = paidOrderTracker.Keys.ToList();
                var orderNumbers = await _db.Orders
                    .IgnoreQueryFilters()
                    .Where(order => paidOrderIds.Contains(order.Id) && !order.IsDeleted)
                    .Select(order => new { order.Id, order.OrderNumber })
                    .ToDictionaryAsync(order => order.Id, order => order.OrderNumber, ct);

                foreach (var paidOrder in paidOrderTracker)
                {
                    await _eventPublisher.PublishAsync(new AffiliateCommissionPaidEvent
                    {
                        TenantId = storeTenantId,
                        StoreTenantId = storeTenantId,
                        AffiliateAuthorityTenantId = affiliateAuthorityTenantId,
                        OrderId = paidOrder.Key,
                        OrderNumber = orderNumbers.GetValueOrDefault(paidOrder.Key) ?? string.Empty,
                        AffiliateAccountId = affiliateAccountId,
                        SettlementReferenceId = referenceId,
                        TotalPaid = paidOrder.Value.Total,
                        Currency = paidOrder.Value.Currency,
                        ItemCount = paidOrder.Value.Count,
                        CorrelationId = referenceId.ToString()
                    }, ct);
                }

                await _db.SaveChangesAsync(ct);
            }

            await tx.CommitAsync(ct);

            _logger.LogInformation(
                "[AF-38H:Orchestrator] Payout COMMITTED | Source={Source} | " +
                "AffiliateId={AffiliateId} | StoreTenantId={StoreTenantId} | Amount={Amount} | " +
                "BalanceLedgerId={LedgerId} | CommissionsPaid={PaidCount} | OrdersPaid={OrderCount}",
                sourceLabel, affiliateAccountId, storeTenantId, amount, withdrawalEntry.Id, paidCount,
                paidOrderTracker.Count);

            // Build result with paid order info for event publishing
            return new PayoutExecutionResult
            {
                PaidOrders = paidOrderTracker.Select(kvp => new PayoutOrderInfo(
                    OrderId: kvp.Key,
                    TotalPaid: kvp.Value.Total,
                    ItemCount: kvp.Value.Count,
                    Currency: kvp.Value.Currency
                )).ToList(),
                WasIdempotentSkip = false
            };
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<PayoutExecutionResult> ExecuteAffiliateBatchPayoutAsync(
        Guid affiliateAccountId,
        Guid storeTenantId,
        IReadOnlyCollection<Guid> orderIds,
        decimal amount,
        string paymentReference,
        string? reference = null,
        string? adminNotes = null,
        decimal? externalTransferAmount = null,
        CancellationToken ct = default)
    {
        if (affiliateAccountId == Guid.Empty)
            throw new ArgumentException("AffiliateAccountId is required", nameof(affiliateAccountId));
        if (storeTenantId == Guid.Empty)
            throw new ArgumentException("StoreTenantId is required", nameof(storeTenantId));
        if (orderIds == null || orderIds.Count == 0)
            throw new ArgumentException("At least one OrderId is required for batch payout", nameof(orderIds));
        if (amount <= 0)
            throw new ArgumentException("Payout amount must be positive", nameof(amount));
        if (externalTransferAmount.HasValue && externalTransferAmount.Value < amount)
            throw new ArgumentException("External transfer amount cannot be less than the payout allocation amount.", nameof(externalTransferAmount));

        var externalPaymentReference = EnsureExternalPaymentReference(paymentReference);
        var confirmedTransferAmount = externalTransferAmount ?? amount;
        var sourceLabel = "BatchLegacySettlement";
        await EnsureVerifiedExternalPayoutEvidenceAllowedAsync(
            affiliateAccountId,
            storeTenantId,
            confirmedTransferAmount,
            externalPaymentReference,
            sourceLabel,
            ct);

        for (int attempt = 1; attempt <= MAX_RETRIES; attempt++)
        {
            try
            {
                var result = await ExecuteBatchPayoutCoreAsync(
                    affiliateAccountId, storeTenantId, orderIds, amount,
                    reference, externalPaymentReference, confirmedTransferAmount, adminNotes, sourceLabel, ct);

                return result;
            }
            catch (Exception ex) when (IsSerializationFailure(ex))
            {
                if (attempt == MAX_RETRIES)
                {
                    _logger.LogError(ex,
                        "[AF-38Y:Orchestrator] Serialization failure exhausted retries for batch | " +
                        "Source={Source} | AffiliateId={AffiliateId} | Attempt={Attempt}/{Max}",
                        sourceLabel, affiliateAccountId, attempt, MAX_RETRIES);
                    throw;
                }

                _logger.LogWarning(
                    "[AF-38Y:Orchestrator] Serialization failure for batch, retrying | " +
                    "Source={Source} | AffiliateId={AffiliateId} | Attempt={Attempt}/{Max}",
                    sourceLabel, affiliateAccountId, attempt, MAX_RETRIES);

                await Task.Delay(50 * attempt, ct);
            }
        }

        throw new InvalidOperationException("[AF-38Y] Retry loop exited without success or failure for batch");
    }

    private async Task<PayoutExecutionResult> ExecuteBatchPayoutCoreAsync(
        Guid affiliateAccountId,
        Guid storeTenantId,
        IReadOnlyCollection<Guid> orderIds,
        decimal amount,
        string? reference,
        string paymentReference,
        decimal externalTransferAmount,
        string? adminNotes,
        string sourceLabel,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "[AF-38H:Orchestrator] Starting batch payout | Source={Source} | AffiliateId={AffiliateId} | " +
            "StoreTenantId={StoreTenantId} | Amount={Amount} | OrdersCount={OrdersCount}",
            sourceLabel, affiliateAccountId, storeTenantId, amount, orderIds.Count);

        await using var tx = await _db.Database
            .BeginTransactionAsync(IsolationLevel.Serializable, ct);

        try
        {
            await EnsureAffiliateOutwardMoneyMovementReadyAsync(storeTenantId, ct);

            var balance = await _payoutService.GetAvailableBalanceAsync(affiliateAccountId, storeTenantId, ct);
            var netActualBalance = balance.RawAvailable + balance.TotalPendingRequests;

            if (amount > netActualBalance)
            {
                _logger.LogWarning(
                    "[AF-38H:Orchestrator] BLOCKED — Insufficient balance for batch | Source={Source} | " +
                    "AffiliateId={AffiliateId} | Required={Required} {Currency} | " +
                    "NetActualBalance={NetBalance} {Currency}",
                    sourceLabel, affiliateAccountId, amount, balance.Currency, netActualBalance, balance.Currency);

                throw new InvalidOperationException(
                    $"Insufficient balance for affiliate batch payout. " +
                    $"Available: {netActualBalance:F2} {balance.Currency}, " +
                    $"Required: {amount:F2} {balance.Currency}. " +
                    $"Source: {sourceLabel}.");
            }

            var referenceId = GenerateDeterministicGuid(orderIds);

            var existingWithdrawal = await _db.AffiliateBalanceLedger
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(l =>
                    l.ReferenceId == referenceId &&
                    l.EntryType == BalanceEntryType.Withdrawal &&
                    l.AffiliateAccountId == affiliateAccountId, ct);

            if (existingWithdrawal != null)
            {
                var amountMatches = existingWithdrawal.Amount == amount;
                var tenantMatches = existingWithdrawal.StoreTenantId == storeTenantId;
                var currencyMatches = string.Equals(
                    existingWithdrawal.Currency,
                    balance.Currency,
                    StringComparison.OrdinalIgnoreCase);
                var accountMatches = existingWithdrawal.AffiliateAccountId == affiliateAccountId;

                if (amountMatches && tenantMatches && currencyMatches && accountMatches)
                {
                    _logger.LogWarning(
                        "[AF-38H:Orchestrator] Idempotent skip — BalanceLedger batch withdrawal already exists | " +
                        "Source={Source} | AffiliateId={AffiliateId} | ReferenceId={ReferenceId}",
                        sourceLabel, affiliateAccountId, referenceId);

                    await tx.CommitAsync(ct);
                    return new PayoutExecutionResult { WasIdempotentSkip = true };
                }
                else
                {
                    var mismatches = new List<string>();
                    if (!amountMatches)
                        mismatches.Add($"Amount (existing: {existingWithdrawal.Amount}, incoming: {amount})");
                    if (!tenantMatches)
                        mismatches.Add($"TenantId (existing: {existingWithdrawal.StoreTenantId}, incoming: {storeTenantId})");
                    if (!currencyMatches)
                        mismatches.Add($"Currency (existing: {existingWithdrawal.Currency}, incoming: {balance.Currency})");
                    if (!accountMatches)
                        mismatches.Add($"AffiliateAccountId (existing: {existingWithdrawal.AffiliateAccountId}, incoming: {affiliateAccountId})");

                    _logger.LogError(
                        "[AF-38H:Orchestrator] IDEMPOTENCY CONFLICT on batch withdrawal | " +
                        "Mismatches: {Mismatches}", string.Join("; ", mismatches));

                    throw new InvalidOperationException(
                        $"Idempotency conflict: A batch withdrawal with reference {referenceId} already exists but with different values.");
                }
            }

            await AllocateExternalTransferEvidenceAsync(
                storeTenantId,
                affiliateAccountId,
                referenceId,
                SettlementSource.LegacySettlement,
                paymentReference,
                externalTransferAmount,
                amount,
                balance.Currency,
                payoutRequestId: null,
                ct);

            var approvedEntries = await _db.AffiliateCommissionLedger
                .IgnoreQueryFilters()
                .Where(e =>
                    e.AffiliateAccountId == affiliateAccountId &&
                    e.StoreTenantId == storeTenantId &&
                    e.EntryType == CommissionEntryType.Approved &&
                    orderIds.Contains(e.OrderId))
                .OrderBy(e => e.RecordedAtUtc)
                .ThenBy(e => e.Id)
                .ToListAsync(ct);

            var approvedOrderItemIds = approvedEntries
                .Select(entry => entry.OrderItemId)
                .Distinct()
                .ToList();

            var maxPerItemByOrderItem = approvedOrderItemIds.Count == 0
                ? new Dictionary<Guid, decimal?>()
                : await _db.OrderItems
                    .IgnoreQueryFilters()
                    .Where(orderItem => approvedOrderItemIds.Contains(orderItem.Id))
                    .Select(orderItem => new
                    {
                        orderItem.Id,
                        MaxCommissionPerItem = orderItem.AffiliateMaxCommissionPerItemSnapshot
                    })
                    .ToDictionaryAsync(x => x.Id, x => x.MaxCommissionPerItem, ct);

            var reversalAmountsByOrderItem = approvedOrderItemIds.Count == 0
                ? new Dictionary<Guid, AffiliateCommissionReversalTotals>()
                : await _db.AffiliateCommissionLedger
                    .IgnoreQueryFilters()
                    .Where(e =>
                        e.AffiliateAccountId == affiliateAccountId &&
                        e.StoreTenantId == storeTenantId &&
                        e.EntryType == CommissionEntryType.Reversed &&
                        approvedOrderItemIds.Contains(e.OrderItemId))
                    .GroupBy(e => e.OrderItemId)
                    .Select(group => new
                    {
                        OrderItemId = group.Key,
                        BaseAmount = group.Sum(entry => entry.BaseAmount),
                        CommissionAmount = group.Sum(entry => entry.CommissionAmount)
                    })
                    .ToDictionaryAsync(
                        item => item.OrderItemId,
                        item => new AffiliateCommissionReversalTotals(
                            item.BaseAmount,
                            item.CommissionAmount),
                        ct);

            var alreadyPaidOrderItemIds = await _db.AffiliateCommissionLedger
                .IgnoreQueryFilters()
                .Where(e =>
                    e.AffiliateAccountId == affiliateAccountId &&
                    e.StoreTenantId == storeTenantId &&
                    e.EntryType == CommissionEntryType.Paid)
                .Select(e => e.OrderItemId)
                .ToHashSetAsync(ct);

            var payoutPlan = BuildExactPayoutPlan(
                approvedEntries,
                alreadyPaidOrderItemIds,
                reversalAmountsByOrderItem,
                maxPerItemByOrderItem,
                amount,
                affiliateAccountId,
                storeTenantId);

            var description = $"Batch legacy settlement - {reference ?? $"Orders ({orderIds.Count})"} | ExternalRef: {paymentReference}";

            var withdrawalEntry = AffiliateBalanceLedger.CreateWithdrawal(
                storeTenantId: storeTenantId,
                affiliateAccountId: affiliateAccountId,
                payoutRequestId: referenceId,
                amount: amount,
                currency: balance.Currency,
                description: description);

            _db.AffiliateBalanceLedger.Add(withdrawalEntry);

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                var existingWithdrawalEntry = await _db.AffiliateBalanceLedger
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(l =>
                        l.ReferenceId == referenceId &&
                        l.EntryType == BalanceEntryType.Withdrawal &&
                        l.AffiliateAccountId == affiliateAccountId, ct);

                if (existingWithdrawalEntry != null)
                {
                    var amountMatches = existingWithdrawalEntry.Amount == amount;
                    var tenantMatches = existingWithdrawalEntry.StoreTenantId == storeTenantId;
                    var currencyMatches = string.Equals(
                        existingWithdrawalEntry.Currency,
                        balance.Currency,
                        StringComparison.OrdinalIgnoreCase);
                    var accountMatches = existingWithdrawalEntry.AffiliateAccountId == affiliateAccountId;

                    if (amountMatches && tenantMatches && currencyMatches && accountMatches)
                    {
                        await tx.RollbackAsync(ct);
                        return new PayoutExecutionResult { WasIdempotentSkip = true };
                    }
                }
                throw new InvalidOperationException("Idempotency conflict on unique constraint during batch payout save.", ex);
            }

            var paidCount = 0;
            var paidOrderTracker = new Dictionary<Guid, (decimal Total, int Count, string Currency)>();

            foreach (var selectedEntry in payoutPlan.SelectedEntries)
            {
                var approved = selectedEntry.ApprovedEntry;
                var paidEntry = AffiliateCommissionLedger.CreatePaidNetOfReversals(
                    approved,
                    selectedEntry.PayableBaseAmount,
                    selectedEntry.PayableCommissionAmount,
                    selectedEntry.ApprovedMaxPerItem);

                try
                {
                    _db.AffiliateCommissionLedger.Add(paidEntry);
                    await _db.SaveChangesAsync(ct);
                    paidCount++;
                    if (paidOrderTracker.TryGetValue(approved.OrderId, out var existing))
                        paidOrderTracker[approved.OrderId] = (existing.Total + selectedEntry.PayableCommissionAmount, existing.Count + 1, approved.Currency);
                    else
                        paidOrderTracker[approved.OrderId] = (selectedEntry.PayableCommissionAmount, 1, approved.Currency);
                }
                catch (DbUpdateException ex) when (IsUniqueViolation(ex))
                {
                    _db.Entry(paidEntry).State = EntityState.Detached;
                    throw new InvalidOperationException(
                        $"AFFILIATE_PAYOUT_COMMISSION_AUTHORITY_CHANGED: OrderItem {approved.OrderItemId} was already marked Paid in batch. Retry with refreshed commission authority.",
                        ex);
                }
            }

            if (paidOrderTracker.Count > 0)
            {
                await EnsureAffiliateEarnedAccountingPostedAsync(
                    storeTenantId,
                    paidOrderTracker.Keys,
                    ct);

                await PostAffiliatePaidAccountingAsync(
                    storeTenantId,
                    affiliateAccountId,
                    referenceId,
                    paidOrderTracker,
                    ct);
            }

            await _payoutService.AutoReconcileWithdrawalRequestsAsync(
                affiliateAccountId,
                storeTenantId,
                amount,
                paymentReference,
                ct);

            if (paidOrderTracker.Count > 0)
            {
                var affiliateAuthorityTenantId = await ResolveAffiliateAuthorityTenantIdAsync(
                    affiliateAccountId,
                    ct);
                var paidOrderIds = paidOrderTracker.Keys.ToList();
                var orderNumbers = await _db.Orders
                    .IgnoreQueryFilters()
                    .Where(order => paidOrderIds.Contains(order.Id) && !order.IsDeleted)
                    .Select(order => new { order.Id, order.OrderNumber })
                    .ToDictionaryAsync(order => order.Id, order => order.OrderNumber, ct);

                foreach (var paidOrder in paidOrderTracker)
                {
                    await _eventPublisher.PublishAsync(new AffiliateCommissionPaidEvent
                    {
                        TenantId = storeTenantId,
                        StoreTenantId = storeTenantId,
                        AffiliateAuthorityTenantId = affiliateAuthorityTenantId,
                        OrderId = paidOrder.Key,
                        OrderNumber = orderNumbers.GetValueOrDefault(paidOrder.Key) ?? string.Empty,
                        AffiliateAccountId = affiliateAccountId,
                        SettlementReferenceId = referenceId,
                        TotalPaid = paidOrder.Value.Total,
                        Currency = paidOrder.Value.Currency,
                        ItemCount = paidOrder.Value.Count,
                        CorrelationId = referenceId.ToString()
                    }, ct);
                }

                await _db.SaveChangesAsync(ct);
            }

            await tx.CommitAsync(ct);

            return new PayoutExecutionResult
            {
                PaidOrders = paidOrderTracker.Select(kvp => new PayoutOrderInfo(
                    OrderId: kvp.Key,
                    TotalPaid: kvp.Value.Total,
                    ItemCount: kvp.Value.Count,
                    Currency: kvp.Value.Currency
                )).ToList(),
                WasIdempotentSkip = false
            };
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    private static Guid GenerateDeterministicGuid(IEnumerable<Guid> guids)
    {
        var sortedBytes = guids.OrderBy(g => g).SelectMany(g => g.ToByteArray()).ToArray();
        using (var sha256 = System.Security.Cryptography.SHA256.Create())
        {
            var hash = sha256.ComputeHash(sortedBytes);
            var guidBytes = new byte[16];
            Array.Copy(hash, guidBytes, 16);
            return new Guid(guidBytes);
        }
    }

    /// <summary>
    /// AF-38Y V-2: Detects PostgreSQL serialization_failure (40001).
    /// This is the expected exception when SERIALIZABLE isolation detects write skew.
    /// </summary>
    private static bool IsSerializationFailure(Exception ex)
    {
        // Direct PostgresException
        if (ex is PostgresException pg && pg.SqlState == "40001")
            return true;

        // Wrapped in DbUpdateException
        if (ex is DbUpdateException dbEx && dbEx.InnerException is PostgresException innerPg && innerPg.SqlState == "40001")
            return true;

        return false;
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        return ex.InnerException is PostgresException pg && pg.SqlState == "23505";
    }

    private async Task AllocateExternalTransferEvidenceAsync(
        Guid storeTenantId,
        Guid affiliateAccountId,
        Guid referenceId,
        SettlementSource source,
        string paymentReference,
        decimal externalTransferAmount,
        decimal allocationAmount,
        string currency,
        Guid? payoutRequestId,
        CancellationToken ct)
    {
        var allocationIdempotencyKey = AffiliatePayoutTransferAllocation.BuildIdempotencyKey(
            source,
            referenceId);

        var existingAllocation = await _db.AffiliatePayoutTransferAllocations
            .IgnoreQueryFilters()
            .Include(allocation => allocation.Evidence)
            .FirstOrDefaultAsync(
                allocation => allocation.IdempotencyKey == allocationIdempotencyKey,
                ct);

        if (existingAllocation != null)
        {
            var amountMatches = existingAllocation.Amount == allocationAmount;
            var tenantMatches = existingAllocation.StoreTenantId == storeTenantId;
            var accountMatches = existingAllocation.AffiliateAccountId == affiliateAccountId;
            var currencyMatches = string.Equals(existingAllocation.Currency, currency, StringComparison.OrdinalIgnoreCase);
            var referenceMatches = string.Equals(
                existingAllocation.Evidence?.NormalizedPaymentReference,
                AffiliatePayoutTransferEvidence.NormalizePaymentReference(paymentReference),
                StringComparison.OrdinalIgnoreCase);

            if (amountMatches && tenantMatches && accountMatches && currencyMatches && referenceMatches)
            {
                return;
            }

            throw new InvalidOperationException(
                $"AFFILIATE_PAYOUT_EXTERNAL_EVIDENCE_IDEMPOTENCY_CONFLICT: Payout reference {referenceId} already has an external evidence allocation with different authority fields.");
        }

        var railNamespace = AffiliatePayoutTransferEvidence.NormalizeRailNamespace(AffiliatePayoutRailNamespace);
        var normalizedReference = AffiliatePayoutTransferEvidence.NormalizePaymentReference(paymentReference);

        var evidence = await _db.AffiliatePayoutTransferEvidence
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                item => item.RailNamespace == railNamespace &&
                        item.NormalizedPaymentReference == normalizedReference,
                ct);

        if (evidence == null)
        {
            await EnsureLegacyExternalReferenceNotAlreadySpentAsync(
                storeTenantId,
                paymentReference,
                payoutRequestId,
                ct);

            evidence = AffiliatePayoutTransferEvidence.Create(
                storeTenantId,
                railNamespace,
                paymentReference,
                externalTransferAmount,
                currency,
                DateTime.UtcNow,
                "MANUAL_BREAK_GLASS",
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                    $"{storeTenantId:N}|{railNamespace}|{normalizedReference}|{externalTransferAmount}|{currency}"))));

            _db.AffiliatePayoutTransferEvidence.Add(evidence);
        }
        else
        {
            var currencyMatches = string.Equals(evidence.Currency, currency, StringComparison.OrdinalIgnoreCase);
            if (!currencyMatches)
            {
                throw new InvalidOperationException(
                    $"AFFILIATE_PAYOUT_EXTERNAL_EVIDENCE_CURRENCY_CONFLICT: External transfer {paymentReference} is recorded in {evidence.Currency} and cannot fund {currency}.");
            }

            if (externalTransferAmount > evidence.ExpectedAmount)
            {
                throw new InvalidOperationException(
                    $"AFFILIATE_PAYOUT_EXTERNAL_EVIDENCE_AMOUNT_CONFLICT: External transfer {paymentReference} was recorded for {evidence.ExpectedAmount:F2} {evidence.Currency}, not {externalTransferAmount:F2} {currency}.");
            }
        }

        evidence.Allocate(allocationAmount, currency);

        var allocation = AffiliatePayoutTransferAllocation.Create(
            evidence.Id,
            storeTenantId,
            affiliateAccountId,
            referenceId,
            source,
            allocationAmount,
            currency);

        _db.AffiliatePayoutTransferAllocations.Add(allocation);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new InvalidOperationException(
                "AFFILIATE_PAYOUT_EXTERNAL_EVIDENCE_ALREADY_USED: This external payout transfer reference or payout allocation was claimed concurrently. Retry with a refreshed payout authority view.",
                ex);
        }
    }

    private async Task EnsureLegacyExternalReferenceNotAlreadySpentAsync(
        Guid storeTenantId,
        string paymentReference,
        Guid? payoutRequestId,
        CancellationToken ct)
    {
        var paidRequestAlreadyUsesReference = await _db.AffiliatePayoutRequests
            .IgnoreQueryFilters()
            .AnyAsync(request =>
                request.PaymentReference == paymentReference &&
                request.Status == PayoutRequestStatus.Paid &&
                (!payoutRequestId.HasValue || request.Id != payoutRequestId.Value),
                ct);

        if (paidRequestAlreadyUsesReference)
        {
            throw new InvalidOperationException(
                "AFFILIATE_PAYOUT_EXTERNAL_EVIDENCE_ALREADY_USED: This external payment reference already closed a paid affiliate payout request.");
        }

        var externalRefMarker = $"ExternalRef: {paymentReference}";
        var balanceLedgerAlreadyUsesReference = await _db.AffiliateBalanceLedger
            .IgnoreQueryFilters()
            .AnyAsync(entry =>
                entry.EntryType == BalanceEntryType.Withdrawal &&
                entry.Description != null &&
                entry.Description.Contains(externalRefMarker),
                ct);

        if (balanceLedgerAlreadyUsesReference)
        {
            throw new InvalidOperationException(
                "AFFILIATE_PAYOUT_EXTERNAL_EVIDENCE_ALREADY_USED: This external payment reference already appears on a legacy affiliate withdrawal.");
        }
    }

    private static ExactPayoutPlan BuildExactPayoutPlan(
        IReadOnlyList<AffiliateCommissionLedger> approvedEntries,
        IReadOnlySet<Guid> alreadyPaidOrderItemIds,
        IReadOnlyDictionary<Guid, AffiliateCommissionReversalTotals> reversalAmountsByOrderItem,
        IReadOnlyDictionary<Guid, decimal?> maxPerItemByOrderItem,
        decimal requestedAmount,
        Guid affiliateAccountId,
        Guid storeTenantId)
    {
        var remaining = requestedAmount;
        var selectedEntries = new List<PayableApprovedCommission>();

        foreach (var approved in approvedEntries)
        {
            if (alreadyPaidOrderItemIds.Contains(approved.OrderItemId))
            {
                continue;
            }

            if (approved.CommissionAmount <= 0m)
            {
                throw new InvalidOperationException(
                    $"AFFILIATE_PAYOUT_INVALID_COMMISSION_AMOUNT: Approved commission {approved.Id} for affiliate {affiliateAccountId} in tenant {storeTenantId} has a non-positive amount {approved.CommissionAmount:F2}.");
            }

            reversalAmountsByOrderItem.TryGetValue(approved.OrderItemId, out var reversalTotals);
            var reversedBaseAmount = reversalTotals?.BaseAmount ?? 0m;
            var reversedCommissionAmount = reversalTotals?.CommissionAmount ?? 0m;

            if (reversedBaseAmount > approved.BaseAmount ||
                reversedCommissionAmount > approved.CommissionAmount)
            {
                throw new InvalidOperationException(
                    $"AFFILIATE_PAYOUT_REVERSED_AMOUNT_EXCEEDS_APPROVED: Reversed commission for order item {approved.OrderItemId} exceeds approved authority for affiliate {affiliateAccountId} in tenant {storeTenantId}. ApprovedBase={approved.BaseAmount:F4}, ReversedBase={reversedBaseAmount:F4}, ApprovedCommission={approved.CommissionAmount:F4}, ReversedCommission={reversedCommissionAmount:F4}.");
            }

            var payableBaseAmount = approved.BaseAmount - reversedBaseAmount;
            var payableCommissionAmount = approved.CommissionAmount - reversedCommissionAmount;

            if (payableBaseAmount == 0m && payableCommissionAmount == 0m)
            {
                continue;
            }

            if (payableBaseAmount <= 0m || payableCommissionAmount <= 0m)
            {
                throw new InvalidOperationException(
                    $"AFFILIATE_PAYOUT_INVALID_NET_COMMISSION_AMOUNT: Net payable commission for order item {approved.OrderItemId} is inconsistent for affiliate {affiliateAccountId} in tenant {storeTenantId}. PayableBase={payableBaseAmount:F4}, PayableCommission={payableCommissionAmount:F4}.");
            }

            if (payableCommissionAmount > remaining)
            {
                break;
            }

            maxPerItemByOrderItem.TryGetValue(approved.OrderItemId, out var approvedMaxPerItem);

            selectedEntries.Add(new PayableApprovedCommission(
                approved,
                payableBaseAmount,
                payableCommissionAmount,
                reversedCommissionAmount,
                approvedMaxPerItem));
            remaining -= payableCommissionAmount;

            if (remaining == 0m)
            {
                break;
            }
        }

        if (selectedEntries.Count == 0 || remaining != 0m)
        {
            var mappedAmount = requestedAmount - remaining;
            throw new InvalidOperationException(
                $"AFFILIATE_PAYOUT_AMOUNT_NOT_EXACTLY_PAYABLE: Requested payout amount {requestedAmount:F2} does not match an exact FIFO set of approved commission entries for affiliate {affiliateAccountId} in tenant {storeTenantId}. ExactMappedAmount={mappedAmount:F2}, RemainingUnmappedAmount={remaining:F2}.");
        }

        return new ExactPayoutPlan(selectedEntries, requestedAmount);
    }

    private sealed record ExactPayoutPlan(
        IReadOnlyList<PayableApprovedCommission> SelectedEntries,
        decimal TotalAmount);

    private sealed record AffiliateCommissionReversalTotals(
        decimal BaseAmount,
        decimal CommissionAmount);

    private sealed record PayableApprovedCommission(
        AffiliateCommissionLedger ApprovedEntry,
        decimal PayableBaseAmount,
        decimal PayableCommissionAmount,
        decimal ReversedCommissionAmount,
        decimal? ApprovedMaxPerItem);

    private async Task<Guid?> ResolveAffiliateAuthorityTenantIdAsync(Guid affiliateAccountId, CancellationToken ct)
    {
        var tenantId = await _db.AffiliateAccounts
            .IgnoreQueryFilters()
            .Where(account => account.Id == affiliateAccountId && !account.IsDeleted)
            .Select(account => account.TenantId)
            .FirstOrDefaultAsync(ct);

        return tenantId == Guid.Empty ? null : tenantId;
    }

    private async Task EnsureAffiliateEarnedAccountingPostedAsync(
        Guid storeTenantId,
        IEnumerable<Guid> paidOrderIds,
        CancellationToken ct)
    {
        var orderIds = paidOrderIds
            .Where(orderId => orderId != Guid.Empty)
            .Distinct()
            .ToList();

        if (orderIds.Count == 0)
        {
            return;
        }

        var earnedKeys = orderIds
            .Select(orderId => $"AFFILIATE_COMM_EARNED:{storeTenantId}:{orderId}")
            .ToHashSet(StringComparer.Ordinal);

        // Chunk the lookup to avoid SQL parameter limits and poor performance with large IN lists
        const int chunkSize = 2000;
        var postedKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var chunk in earnedKeys.Chunk(chunkSize))
        {
            var chunkPostedKeys = await _db.JournalEntries
                .IgnoreQueryFilters()
                .Where(journal =>
                    journal.TenantId == storeTenantId &&
                    chunk.Contains(journal.IdempotencyKey) &&
                    !journal.IsDeleted)
                .Select(journal => journal.IdempotencyKey)
                .ToListAsync(ct);

            postedKeys.UnionWith(chunkPostedKeys);
        }

        earnedKeys.ExceptWith(postedKeys);
        if (earnedKeys.Count == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            "AFFILIATE_PAYOUT_ACCOUNTING_NOT_READY: Cannot mark affiliate commissions paid until earned commission journals are posted. " +
            $"MissingEarnedJournalKeys={string.Join(",", earnedKeys.OrderBy(key => key))}");
    }

    private async Task PostAffiliatePaidAccountingAsync(
        Guid storeTenantId,
        Guid affiliateAccountId,
        Guid settlementReferenceId,
        IReadOnlyDictionary<Guid, (decimal Total, int Count, string Currency)> paidOrderTracker,
        CancellationToken ct)
    {
        var accounts = await _accountResolver.GetByCodesAsync(
            storeTenantId,
            new[] { AccountCodes.AffiliatePayable, AccountCodes.CashboxMain },
            ct);

        if (!accounts.TryGetValue(AccountCodes.AffiliatePayable, out var payableAccount))
        {
            throw new InvalidOperationException(
                $"Account {AccountCodes.AffiliatePayable} not found for tenant {storeTenantId}");
        }

        string cashAccountCode;
        try
        {
            var cashboxAccount = await _cashboxLedgerAccountService.ResolveDefaultCashboxAccountAsync(storeTenantId, ct);
            cashAccountCode = cashboxAccount.AccountCode;
        }
        catch (NoCashboxAccountException ex)
        {
            _logger.LogWarning(
                ex,
                "[AF-38H:Orchestrator] No active cashbox found for tenant {TenantId}; falling back to generic cash account {AccountCode}",
                storeTenantId,
                AccountCodes.CashboxMain);
            cashAccountCode = AccountCodes.CashboxMain;
        }

        var cashAccount = await _accountResolver.GetByCodeAsync(storeTenantId, cashAccountCode, ct);
        var orderIds = paidOrderTracker.Keys.ToList();
        var orderNumbers = await _db.Orders
            .IgnoreQueryFilters()
            .Where(order => orderIds.Contains(order.Id) && !order.IsDeleted)
            .Select(order => new { order.Id, order.OrderNumber })
            .ToDictionaryAsync(order => order.Id, order => order.OrderNumber, ct);

        foreach (var paidOrder in paidOrderTracker)
        {
            var idempotencyKey = $"AFFILIATE_COMM_PAID:{storeTenantId}:{paidOrder.Key}:{settlementReferenceId}";
            var orderNumber = orderNumbers.GetValueOrDefault(paidOrder.Key) ?? string.Empty;

            var request = new PostingRequest
            {
                TenantId = storeTenantId,
                JournalDate = DateTime.UtcNow,
                SourceType = "AffiliateCommissionPaid",
                SourceId = settlementReferenceId,
                SourceReference = $"AFFILIATE_COMM_PAID:{paidOrder.Key}:{settlementReferenceId}",
                Description = $"Affiliate commission paid for order {orderNumber} " +
                              $"(Affiliate: {affiliateAccountId}, Amount: {paidOrder.Value.Total:F2} {paidOrder.Value.Currency})",
                IdempotencyKey = idempotencyKey,
                UserId = null,
                Lines = new List<PostingLine>
                {
                    PostingLine.Debit(
                        payableAccount.Id,
                        paidOrder.Value.Total,
                        $"Affiliate payable cleared - Order {orderNumber}"),
                    PostingLine.Credit(
                        cashAccount.Id,
                        paidOrder.Value.Total,
                        $"Cash paid to affiliate - Order {orderNumber}")
                }
            };

            await _postingService.PostJournalAsync(request, ct);
        }
    }

    private async Task EnsureAffiliateOutwardMoneyMovementReadyAsync(Guid storeTenantId, CancellationToken ct)
    {
        var nowUtc = DateTime.UtcNow;
        var accountingStaleCutoffUtc = nowUtc.AddMinutes(-_options.AccountingStaleMinutes);
        var integrationStaleCutoffUtc = nowUtc.AddMinutes(-_options.IntegrationStaleMinutes);
        var storeTenantIdText = storeTenantId.ToString();

        await EnsureAffiliateUnscopedExternalMoneyReadyAsync(integrationStaleCutoffUtc, ct);
        await EnsureAffiliateAmbiguousMarketplaceAggregateBackboneReadyAsync(storeTenantId, integrationStaleCutoffUtc, ct);

        var accountingBlocker = await _db.Set<AccountingOutboxMessage>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(message =>
                message.TenantId == storeTenantId &&
                message.ProcessedAtUtc == null &&
                AccountingOutboxProcessor.CriticalFinancialTruthEventTypes.Contains(message.EventType) &&
                (message.IsFailed ||
                 message.LastError != null ||
                 message.OccurredAtUtc < accountingStaleCutoffUtc))
            .OrderByDescending(message => message.IsFailed)
            .ThenBy(message => message.OccurredAtUtc)
            .FirstOrDefaultAsync(ct);

        if (accountingBlocker != null)
        {
            throw new InvalidOperationException(
                "AFFILIATE_PAYOUT_ACCOUNTING_BACKBONE_NOT_READY: Critical accounting outbox is failed or stale. " +
                $"MessageId={accountingBlocker.Id}; EventType={accountingBlocker.EventType}; IsFailed={accountingBlocker.IsFailed}; OccurredAtUtc={accountingBlocker.OccurredAtUtc:o}");
        }

        var unhealthyAuthoritativeEvents = _db.Set<OutboxMessage>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(message =>
                message.ProcessedAtUtc == null &&
                (message.RetryCount >= OutboxMessage.MaxRetries ||
                 message.LastErrorAtUtc != null ||
                 message.CreatedAtUtc < integrationStaleCutoffUtc))
            .Where(BuildCriticalEventTypeFilter())
            .Where(message =>
                message.TenantId == storeTenantId ||
                EF.Functions.ILike(message.EventPayload, $"%{storeTenantIdText}%"))
            .OrderByDescending(message => message.RetryCount >= OutboxMessage.MaxRetries)
            .ThenBy(message => message.CreatedAtUtc)
            .Select(message => new
            {
                message.Id,
                message.EventType,
                message.TenantId,
                message.EventPayload,
                message.AggregateId,
                message.RetryCount,
                message.CreatedAtUtc
            })
            .AsAsyncEnumerable();

        var authoritativeEventInspectionCount = 0;
        await foreach (var authoritativeEventBlocker in unhealthyAuthoritativeEvents.WithCancellation(ct))
        {
            authoritativeEventInspectionCount++;
            if (EventMayAffectTenant(authoritativeEventBlocker.TenantId, authoritativeEventBlocker.EventPayload, storeTenantId))
            {
                throw new InvalidOperationException(
                    "AFFILIATE_PAYOUT_EVENT_BACKBONE_NOT_READY: Authoritative integration event outbox is failed or stale. " +
                    $"MessageId={authoritativeEventBlocker.Id}; EventType={authoritativeEventBlocker.EventType}; AggregateId={authoritativeEventBlocker.AggregateId}; RetryCount={authoritativeEventBlocker.RetryCount}; CreatedAtUtc={authoritativeEventBlocker.CreatedAtUtc:o}");
            }
        }

        LogLargeAffiliateReadinessInspection(
            "AuthoritativeIntegrationOutbox",
            storeTenantId,
            authoritativeEventInspectionCount,
            integrationStaleCutoffUtc);

        await EnsureAffiliatePaymentCompensationReadyAsync(storeTenantId, integrationStaleCutoffUtc, ct);
        await EnsureAffiliateUnresolvedCapturedPaymentsReadyAsync(storeTenantId, ct);
        await EnsureAffiliateUnresolvedProviderRefundsReadyAsync(storeTenantId, ct);
        await EnsureAffiliateMassTransitBackboneReadyAsync(storeTenantId, integrationStaleCutoffUtc, ct);
        await EnsureAffiliateMarketplaceBackboneReadyAsync(storeTenantId, integrationStaleCutoffUtc, ct);
        await EnsureAffiliateStockBackboneReadyAsync(storeTenantId, integrationStaleCutoffUtc, ct);
    }

    private async Task EnsureAffiliateUnscopedExternalMoneyReadyAsync(DateTime staleCutoffUtc, CancellationToken ct)
    {
        await foreach (var message in _db.CommerceHubPaymentCompensationOutboxMessages
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(message =>
                message.ProcessedAtUtc == null &&
                !message.IsDeleted &&
                (message.TenantId == null || message.TenantId == Guid.Empty) &&
                (message.IsFailed ||
                 message.AttemptCount >= 3 ||
                 message.CreatedAt < staleCutoffUtc))
            .OrderBy(message => message.CreatedAt)
            .Select(message => new
            {
                message.Id,
                message.PaymentId,
                message.OrderId,
                message.AffectedStoreIdsJson,
                message.CreatedAt
            })
            .AsAsyncEnumerable()
            .WithCancellation(ct))
        {
            if (HasExplicitTenantScope(message.AffectedStoreIdsJson))
            {
                continue;
            }

            throw new InvalidOperationException(
                "AFFILIATE_PAYOUT_PAYMENT_COMPENSATION_SCOPE_UNDETERMINED: Captured payment compensation has no durable store scope. " +
                $"MessageId={message.Id}; PaymentId={message.PaymentId}; OrderId={message.OrderId}; CreatedAtUtc={message.CreatedAt:o}");
        }

        await EnsureAffiliateUnscopedUnresolvedWebhookReadyAsync(
            BlockingUnresolvedPaymentStatuses,
            "AFFILIATE_PAYOUT_UNRESOLVED_CAPTURED_PAYMENT_SCOPE_UNDETERMINED",
            "Captured provider cash has no durable store scope.",
            ct);

        await EnsureAffiliateUnscopedUnresolvedWebhookReadyAsync(
            BlockingUnresolvedRefundStatuses,
            "AFFILIATE_PAYOUT_UNRESOLVED_PROVIDER_REFUND_SCOPE_UNDETERMINED",
            "Provider refund authority has no durable store scope.",
            ct);
    }

    private async Task EnsureAffiliateUnscopedUnresolvedWebhookReadyAsync(
        IReadOnlyCollection<string> blockingStatuses,
        string errorCode,
        string authorityDescription,
        CancellationToken ct)
    {
        await foreach (var webhook in _db.CommerceHubUnresolvedPaymentWebhooks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(webhook =>
                !webhook.IsDeleted &&
                webhook.Amount > 0m &&
                blockingStatuses.Contains(webhook.Status) &&
                (webhook.TenantId == null || webhook.TenantId == Guid.Empty))
            .OrderBy(webhook => webhook.CreatedAt)
            .Select(webhook => new
            {
                webhook.Id,
                webhook.Provider,
                webhook.ProviderEventId,
                webhook.AffectedStoreIdsJson,
                webhook.SuspenseRoutedStoreIdsJson,
                webhook.Amount,
                webhook.Currency,
                webhook.Status,
                webhook.CreatedAt
            })
            .AsAsyncEnumerable()
            .WithCancellation(ct))
        {
            if (HasDurableTenantScope(null, webhook.AffectedStoreIdsJson, webhook.SuspenseRoutedStoreIdsJson))
            {
                continue;
            }

            throw new InvalidOperationException(
                $"{errorCode}: {authorityDescription} " +
                $"WebhookId={webhook.Id}; Provider={webhook.Provider}; ProviderEventId={webhook.ProviderEventId}; Status={webhook.Status}; Amount={webhook.Amount} {webhook.Currency}; CreatedAtUtc={webhook.CreatedAt:o}");
        }
    }

    private async Task EnsureAffiliateAmbiguousMarketplaceAggregateBackboneReadyAsync(
        Guid storeTenantId,
        DateTime staleCutoffUtc,
        CancellationToken ct)
    {
        var authoritativeEvents = _db.Set<OutboxMessage>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(message =>
                message.ProcessedAtUtc == null &&
                (message.RetryCount >= OutboxMessage.MaxRetries ||
                 message.LastErrorAtUtc != null ||
                 message.CreatedAtUtc < staleCutoffUtc))
            .Where(BuildCriticalEventTypeFilter())
            // Match CommerceHub readiness: do not prefilter by a literal payload marker.
            // Malformed or wrapper-shaped critical payloads must still reach the
            // fail-closed ambiguity detector before affiliate money leaves the platform.
            .OrderByDescending(message => message.RetryCount >= OutboxMessage.MaxRetries)
            .ThenBy(message => message.CreatedAtUtc)
            .Select(message => new
            {
                message.Id,
                message.EventType,
                message.TenantId,
                message.EventPayload,
                message.AggregateId,
                message.RetryCount,
                message.CreatedAtUtc
            })
            .AsAsyncEnumerable();

        var authoritativeEventInspectionCount = 0;
        await foreach (var blocker in authoritativeEvents.WithCancellation(ct))
        {
            authoritativeEventInspectionCount++;
            if (!IsAmbiguousCriticalMarketplaceAggregate(blocker.TenantId, blocker.EventPayload))
            {
                continue;
            }

            throw new InvalidOperationException(
                "AFFILIATE_PAYOUT_EVENT_BACKBONE_SCOPE_UNDETERMINED: Ambiguous critical marketplace aggregate event is failed or stale and cannot prove affected store scope. " +
                $"StoreTenantId={storeTenantId}; MessageId={blocker.Id}; EventType={blocker.EventType}; AggregateId={blocker.AggregateId}; RetryCount={blocker.RetryCount}; CreatedAtUtc={blocker.CreatedAtUtc:o}");
        }

        LogLargeAffiliateReadinessInspection(
            "AmbiguousAuthoritativeIntegrationOutbox",
            storeTenantId,
            authoritativeEventInspectionCount,
            staleCutoffUtc);

        var staleBusOutboxEvents = (
            from message in _db.Set<MassTransitOutboxMessage>().AsNoTracking()
            join state in _db.Set<MassTransit.EntityFrameworkCoreIntegration.OutboxState>().AsNoTracking()
                on message.OutboxId equals state.OutboxId into stateGroup
            from state in stateGroup.DefaultIfEmpty()
            where message.SentTime < staleCutoffUtc &&
                  (state == null || message.SequenceNumber > state.LastSequenceNumber)
            select message)
            .Where(BuildCriticalMassTransitOutboxMessageTypeFilter())
            // No literal payload prefilter: the ambiguity detector owns scope proof.
            .OrderBy(message => message.SentTime)
            .Select(message => new
            {
                message.SequenceNumber,
                message.MessageId,
                message.MessageType,
                message.SentTime,
                message.Body
            })
            .AsAsyncEnumerable();

        var busOutboxInspectionCount = 0;
        await foreach (var blocker in staleBusOutboxEvents.WithCancellation(ct))
        {
            busOutboxInspectionCount++;
            if (!IsAmbiguousCriticalMarketplaceAggregate(null, blocker.Body))
            {
                continue;
            }

            throw new InvalidOperationException(
                "AFFILIATE_PAYOUT_MASS_TRANSIT_BACKBONE_SCOPE_UNDETERMINED: Ambiguous critical marketplace aggregate bus outbox message is stale and cannot prove affected store scope. " +
                $"StoreTenantId={storeTenantId}; SequenceNumber={blocker.SequenceNumber}; MessageId={blocker.MessageId}; MessageType={blocker.MessageType}; SentTimeUtc={blocker.SentTime:o}");
        }

        LogLargeAffiliateReadinessInspection(
            "AmbiguousMassTransitOutbox",
            storeTenantId,
            busOutboxInspectionCount,
            staleCutoffUtc);

        var marketplaceEvents = _db.Set<MarketplaceEventOutboxMessage>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(message =>
                message.PublishedAtUtc == null &&
                (message.IsFailed || message.OccurredAtUtc < staleCutoffUtc))
            // No literal payload prefilter: malformed marketplace payloads fail closed.
            .OrderBy(message => message.OccurredAtUtc)
            .Select(message => new
            {
                message.Id,
                message.EventType,
                message.EntityId,
                message.TenantId,
                message.PayloadJson,
                message.IsFailed,
                message.OccurredAtUtc
            })
            .AsAsyncEnumerable();

        var marketplaceOutboxInspectionCount = 0;
        await foreach (var blocker in marketplaceEvents.WithCancellation(ct))
        {
            marketplaceOutboxInspectionCount++;
            if (!IsAmbiguousCriticalMarketplaceAggregate(blocker.TenantId, blocker.PayloadJson))
            {
                continue;
            }

            throw new InvalidOperationException(
                "AFFILIATE_PAYOUT_MARKETPLACE_BACKBONE_SCOPE_UNDETERMINED: Ambiguous marketplace event backbone message is failed or stale and cannot prove affected store scope. " +
                $"StoreTenantId={storeTenantId}; MessageId={blocker.Id}; EventType={blocker.EventType}; EntityId={blocker.EntityId}; IsFailed={blocker.IsFailed}; OccurredAtUtc={blocker.OccurredAtUtc:o}");
        }

        LogLargeAffiliateReadinessInspection(
            "AmbiguousMarketplaceEventOutbox",
            storeTenantId,
            marketplaceOutboxInspectionCount,
            staleCutoffUtc);
    }

    private async Task EnsureAffiliatePaymentCompensationReadyAsync(
        Guid storeTenantId,
        DateTime staleCutoffUtc,
        CancellationToken ct)
    {
        var storeTenantIdText = storeTenantId.ToString();
        var blockers = _db.CommerceHubPaymentCompensationOutboxMessages
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(message =>
                message.ProcessedAtUtc == null &&
                !message.IsDeleted &&
                (message.IsFailed ||
                 message.AttemptCount >= 3 ||
                 message.CreatedAt < staleCutoffUtc))
            .Where(message =>
                message.TenantId == storeTenantId ||
                (message.AffectedStoreIdsJson != null && EF.Functions.ILike(message.AffectedStoreIdsJson, $"%{storeTenantIdText}%")))
            .OrderBy(message => message.CreatedAt)
            .Select(message => new
            {
                message.Id,
                message.TenantId,
                message.AffectedStoreIdsJson,
                message.PaymentId,
                message.OrderId,
                message.CreatedAt
            })
            .AsAsyncEnumerable();

        var inspectionCount = 0;
        await foreach (var blocker in blockers.WithCancellation(ct))
        {
            inspectionCount++;
            if (TenantJsonScopeMayAffectStore(blocker.TenantId, blocker.AffectedStoreIdsJson, storeTenantId))
            {
                throw new InvalidOperationException(
                    "AFFILIATE_PAYOUT_PAYMENT_COMPENSATION_NOT_READY: Captured payment compensation is unresolved for this store. " +
                    $"MessageId={blocker.Id}; PaymentId={blocker.PaymentId}; OrderId={blocker.OrderId}; CreatedAtUtc={blocker.CreatedAt:o}");
            }
        }

        LogLargeAffiliateReadinessInspection(
            "CommerceHubPaymentCompensation",
            storeTenantId,
            inspectionCount,
            staleCutoffUtc);
    }

    private async Task EnsureAffiliateUnresolvedCapturedPaymentsReadyAsync(Guid storeTenantId, CancellationToken ct)
    {
        var storeTenantIdText = storeTenantId.ToString();
        var blockers = _db.CommerceHubUnresolvedPaymentWebhooks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(webhook =>
                !webhook.IsDeleted &&
                webhook.Amount > 0m &&
                BlockingUnresolvedPaymentStatuses.Contains(webhook.Status))
            .Where(webhook =>
                webhook.TenantId == storeTenantId ||
                (webhook.AffectedStoreIdsJson != null && EF.Functions.ILike(webhook.AffectedStoreIdsJson, $"%{storeTenantIdText}%")) ||
                (webhook.SuspenseRoutedStoreIdsJson != null && EF.Functions.ILike(webhook.SuspenseRoutedStoreIdsJson, $"%{storeTenantIdText}%")))
            .OrderBy(webhook => webhook.CreatedAt)
            .Select(webhook => new
            {
                webhook.Id,
                webhook.Provider,
                webhook.ProviderEventId,
                webhook.TenantId,
                webhook.AffectedStoreIdsJson,
                webhook.SuspenseRoutedStoreIdsJson,
                webhook.Amount,
                webhook.Currency,
                webhook.Status,
                webhook.CreatedAt
            })
            .AsAsyncEnumerable();

        var inspectionCount = 0;
        await foreach (var blocker in blockers.WithCancellation(ct))
        {
            inspectionCount++;
            if (TenantJsonScopeMayAffectStore(blocker.TenantId, blocker.AffectedStoreIdsJson, storeTenantId) ||
                TenantJsonScopeMayAffectStore(null, blocker.SuspenseRoutedStoreIdsJson, storeTenantId))
            {
                throw new InvalidOperationException(
                    "AFFILIATE_PAYOUT_UNRESOLVED_CAPTURED_PAYMENT_NOT_READY: Captured provider cash is unresolved for this store. " +
                    $"WebhookId={blocker.Id}; Provider={blocker.Provider}; ProviderEventId={blocker.ProviderEventId}; Status={blocker.Status}; Amount={blocker.Amount} {blocker.Currency}; CreatedAtUtc={blocker.CreatedAt:o}");
            }
        }

        LogLargeAffiliateReadinessInspection(
            "CommerceHubUnresolvedCapturedPayments",
            storeTenantId,
            inspectionCount,
            null);
    }

    private async Task EnsureAffiliateUnresolvedProviderRefundsReadyAsync(Guid storeTenantId, CancellationToken ct)
    {
        var storeTenantIdText = storeTenantId.ToString();
        var blockers = _db.CommerceHubUnresolvedPaymentWebhooks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(webhook =>
                !webhook.IsDeleted &&
                webhook.Amount > 0m &&
                BlockingUnresolvedRefundStatuses.Contains(webhook.Status))
            .Where(webhook =>
                webhook.TenantId == storeTenantId ||
                (webhook.AffectedStoreIdsJson != null && EF.Functions.ILike(webhook.AffectedStoreIdsJson, $"%{storeTenantIdText}%")) ||
                (webhook.SuspenseRoutedStoreIdsJson != null && EF.Functions.ILike(webhook.SuspenseRoutedStoreIdsJson, $"%{storeTenantIdText}%")))
            .OrderBy(webhook => webhook.CreatedAt)
            .Select(webhook => new
            {
                webhook.Id,
                webhook.Provider,
                webhook.ProviderEventId,
                webhook.TenantId,
                webhook.AffectedStoreIdsJson,
                webhook.SuspenseRoutedStoreIdsJson,
                webhook.Amount,
                webhook.Currency,
                webhook.Status,
                webhook.CreatedAt
            })
            .AsAsyncEnumerable();

        var inspectionCount = 0;
        await foreach (var blocker in blockers.WithCancellation(ct))
        {
            inspectionCount++;
            if (TenantJsonScopeMayAffectStore(blocker.TenantId, blocker.AffectedStoreIdsJson, storeTenantId) ||
                TenantJsonScopeMayAffectStore(null, blocker.SuspenseRoutedStoreIdsJson, storeTenantId))
            {
                throw new InvalidOperationException(
                    "AFFILIATE_PAYOUT_UNRESOLVED_PROVIDER_REFUND_NOT_READY: Provider refund authority is unresolved for this store. " +
                    $"WebhookId={blocker.Id}; Provider={blocker.Provider}; ProviderEventId={blocker.ProviderEventId}; Status={blocker.Status}; Amount={blocker.Amount} {blocker.Currency}; CreatedAtUtc={blocker.CreatedAt:o}");
            }
        }

        LogLargeAffiliateReadinessInspection(
            "CommerceHubUnresolvedProviderRefunds",
            storeTenantId,
            inspectionCount,
            null);
    }

    private async Task EnsureAffiliateMassTransitBackboneReadyAsync(
        Guid storeTenantId,
        DateTime staleCutoffUtc,
        CancellationToken ct)
    {
        var storeTenantIdText = storeTenantId.ToString();
        var staleBusOutboxEvents = (
            from message in _db.Set<MassTransitOutboxMessage>().AsNoTracking()
            join state in _db.Set<MassTransit.EntityFrameworkCoreIntegration.OutboxState>().AsNoTracking()
                on message.OutboxId equals state.OutboxId into stateGroup
            from state in stateGroup.DefaultIfEmpty()
            where message.SentTime < staleCutoffUtc &&
                  (state == null || message.SequenceNumber > state.LastSequenceNumber)
            select message)
            .Where(BuildCriticalMassTransitOutboxMessageTypeFilter())
            .Where(message => EF.Functions.ILike(message.Body, $"%{storeTenantIdText}%"))
            .OrderBy(message => message.SentTime)
            .Select(message => new
            {
                message.SequenceNumber,
                message.MessageId,
                message.MessageType,
                message.SentTime,
                message.Body
            })
            .AsAsyncEnumerable();

        var inspectionCount = 0;
        await foreach (var blocker in staleBusOutboxEvents.WithCancellation(ct))
        {
            inspectionCount++;
            if (EventMayAffectTenant(null, blocker.Body, storeTenantId))
            {
                throw new InvalidOperationException(
                    "AFFILIATE_PAYOUT_MASS_TRANSIT_BACKBONE_NOT_READY: Authoritative bus outbox message is stale for this store. " +
                    $"SequenceNumber={blocker.SequenceNumber}; MessageId={blocker.MessageId}; MessageType={blocker.MessageType}; SentTimeUtc={blocker.SentTime:o}");
            }
        }

        LogLargeAffiliateReadinessInspection(
            "MassTransitOutbox",
            storeTenantId,
            inspectionCount,
            staleCutoffUtc);
    }

    private async Task EnsureAffiliateMarketplaceBackboneReadyAsync(
        Guid storeTenantId,
        DateTime staleCutoffUtc,
        CancellationToken ct)
    {
        var storeTenantIdText = storeTenantId.ToString();
        var failedMarketplaceEvents = _db.Set<MarketplaceEventOutboxMessage>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(message =>
                message.PublishedAtUtc == null &&
                (message.IsFailed || message.OccurredAtUtc < staleCutoffUtc))
            .Where(message =>
                message.TenantId == storeTenantId ||
                EF.Functions.ILike(message.PayloadJson, $"%{storeTenantIdText}%"))
            .OrderBy(message => message.OccurredAtUtc)
            .Select(message => new
            {
                message.Id,
                message.EventType,
                message.EntityId,
                message.TenantId,
                message.PayloadJson,
                message.IsFailed,
                message.OccurredAtUtc
            })
            .AsAsyncEnumerable();

        var inspectionCount = 0;
        await foreach (var blocker in failedMarketplaceEvents.WithCancellation(ct))
        {
            inspectionCount++;
            if (EventMayAffectTenant(blocker.TenantId, blocker.PayloadJson, storeTenantId))
            {
                throw new InvalidOperationException(
                    "AFFILIATE_PAYOUT_MARKETPLACE_EVENT_BACKBONE_NOT_READY: Marketplace event backbone is failed or stale for this store. " +
                    $"MessageId={blocker.Id}; EventType={blocker.EventType}; EntityId={blocker.EntityId}; IsFailed={blocker.IsFailed}; OccurredAtUtc={blocker.OccurredAtUtc:o}");
            }
        }

        LogLargeAffiliateReadinessInspection(
            "MarketplaceEventOutbox",
            storeTenantId,
            inspectionCount,
            staleCutoffUtc);
    }

    private async Task EnsureAffiliateStockBackboneReadyAsync(
        Guid storeTenantId,
        DateTime staleCutoffUtc,
        CancellationToken ct)
    {
        var blocker = await _db.Set<StockSyncOutboxMessage>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(message =>
                message.TenantId == storeTenantId &&
                message.PublishedAtUtc == null &&
                (message.IsFailed || message.OccurredAtUtc < staleCutoffUtc))
            .OrderBy(message => message.StockUpdatedAtUtc)
            .Select(message => new
            {
                message.Id,
                message.VariantId,
                message.IsFailed,
                message.OccurredAtUtc
            })
            .FirstOrDefaultAsync(ct);

        if (blocker != null)
        {
            throw new InvalidOperationException(
                "AFFILIATE_PAYOUT_STOCK_SYNC_BACKBONE_NOT_READY: Stock sync backbone is failed or stale for this store. " +
                $"MessageId={blocker.Id}; VariantId={blocker.VariantId}; IsFailed={blocker.IsFailed}; OccurredAtUtc={blocker.OccurredAtUtc:o}");
        }
    }

    private void LogLargeAffiliateReadinessInspection(
        string backlogName,
        Guid storeTenantId,
        int inspectedCount,
        DateTime? staleCutoffUtc)
    {
        if (inspectedCount < ReadinessInspectionLimit)
        {
            return;
        }

        _logger.LogWarning(
            "[Affiliate:PayoutReadiness] Large tenant-prefiltered readiness scan completed | Backlog={BacklogName} | StoreTenantId={StoreTenantId} | InspectedCount={InspectedCount} | StaleCutoffUtc={StaleCutoffUtc}",
            backlogName,
            storeTenantId,
            inspectedCount,
            staleCutoffUtc);
    }

    private static bool TenantJsonScopeMayAffectStore(Guid? baseTenantId, string? tenantIdsJson, Guid storeTenantId)
    {
        if (baseTenantId == storeTenantId)
        {
            return true;
        }

        if (baseTenantId.HasValue && baseTenantId.Value != Guid.Empty)
        {
            if (string.IsNullOrWhiteSpace(tenantIdsJson))
            {
                return false;
            }
        }

        if (string.IsNullOrWhiteSpace(tenantIdsJson))
        {
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(tenantIdsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return true;
            }

            // An empty affected-store array is unknown/global only when no base
            // tenant scope exists. With a different base tenant, it means there
            // is no additive scope for this store.
            var arrayLength = 0;
            foreach (var item in document.RootElement.EnumerateArray())
            {
                arrayLength++;
                
                if (item.ValueKind != JsonValueKind.String ||
                    !Guid.TryParse(item.GetString(), out var tenantId) ||
                    tenantId == Guid.Empty)
                {
                    return true;
                }

                if (tenantId == storeTenantId)
                {
                    return true;
                }
            }

            if (arrayLength == 0)
            {
                return !baseTenantId.HasValue || baseTenantId.Value == Guid.Empty;
            }

            // Array was non-empty and storeTenantId was not found (non-blocking)
            return false;
        }
        catch (JsonException)
        {
            return true;
        }
    }

    private static bool HasDurableTenantScope(
        Guid? baseTenantId,
        string? affectedStoreIdsJson,
        string? suspenseRoutedStoreIdsJson)
    {
        if (baseTenantId.HasValue && baseTenantId.Value != Guid.Empty)
        {
            return true;
        }

        return HasExplicitTenantScope(affectedStoreIdsJson) ||
               HasExplicitTenantScope(suspenseRoutedStoreIdsJson);
    }

    private static bool HasExplicitTenantScope(string? tenantIdsJson)
    {
        if (string.IsNullOrWhiteSpace(tenantIdsJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(tenantIdsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var foundAnyScope = false;
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String ||
                    !Guid.TryParse(item.GetString(), out var tenantId) ||
                    tenantId == Guid.Empty)
                {
                    return false;
                }

                foundAnyScope = true;
            }

            return foundAnyScope;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool EventMayAffectTenant(Guid? baseTenantId, string? eventPayload, Guid tenantId)
    {
        try
        {
            if (AffectedTenantScopeResolver.ContainsTenant(baseTenantId, eventPayload, tenantId))
            {
                return true;
            }

            return AffectedTenantScopeResolver.Resolve(baseTenantId, eventPayload).Count == 0 ||
                   AffectedTenantScopeResolver.RequiresGlobalFailClosedForAmbiguousMarketplaceAggregate(
                       baseTenantId,
                       eventPayload);
        }
        catch (InvalidEventPayloadException)
        {
            return true;
        }
    }

    private static bool IsAmbiguousCriticalMarketplaceAggregate(Guid? baseTenantId, string? eventPayload)
    {
        try
        {
            return AffectedTenantScopeResolver.RequiresGlobalFailClosedForAmbiguousMarketplaceAggregate(
                baseTenantId,
                eventPayload);
        }
        catch (InvalidEventPayloadException)
        {
            return true;
        }
    }

    private static Expression<Func<OutboxMessage, bool>> BuildCriticalEventTypeFilter()
    {
        var parameter = Expression.Parameter(typeof(OutboxMessage), "message");
        var eventType = Expression.Property(parameter, nameof(OutboxMessage.EventType));

        // Use ToLower() for case-insensitive comparison that EF Core can translate
        var toLowerMethod = typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!;
        var eventTypeLower = Expression.Call(eventType, toLowerMethod);

        var containsMethod = typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!;
        var endsWithMethod = typeof(string).GetMethod(nameof(string.EndsWith), [typeof(string)])!;

        Expression? includedExpression = null;
        foreach (var fragment in IntegrationEventAuthority.CriticalEventNameFragments)
        {
            var fragmentLower = fragment.ToLowerInvariant();
            var fragmentConstant = Expression.Constant(fragmentLower);
            var containsCall = Expression.Call(eventTypeLower, containsMethod, fragmentConstant);
            includedExpression = includedExpression == null
                ? containsCall
                : Expression.OrElse(includedExpression, containsCall);
        }

        Expression? excludedExpression = null;
        foreach (var eventTypeName in IntegrationEventAuthority.NonAuthoritativeEventTypeNames)
        {
            var eventTypeNameLower = eventTypeName.ToLowerInvariant();
            var eventTypeConstant = Expression.Constant(eventTypeNameLower);
            var excludedTypeCall = Expression.Call(eventTypeLower, endsWithMethod, eventTypeConstant);
            excludedExpression = excludedExpression == null
                ? excludedTypeCall
                : Expression.OrElse(excludedExpression, excludedTypeCall);
        }

        if (excludedExpression != null)
        {
            includedExpression = Expression.AndAlso(includedExpression!, Expression.Not(excludedExpression));
        }

        return Expression.Lambda<Func<OutboxMessage, bool>>(includedExpression!, parameter);
    }

    private static Expression<Func<MassTransitOutboxMessage, bool>> BuildCriticalMassTransitOutboxMessageTypeFilter()
    {
        var parameter = Expression.Parameter(typeof(MassTransitOutboxMessage), "message");
        var messageType = Expression.Property(parameter, nameof(MassTransitOutboxMessage.MessageType));

        // Use ToLower() for case-insensitive comparison that EF Core can translate
        var toLowerMethod = typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!;
        var messageTypeLower = Expression.Call(messageType, toLowerMethod);

        var containsMethod = typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!;

        Expression? includedExpression = null;
        foreach (var fragment in IntegrationEventAuthority.CriticalEventNameFragments)
        {
            var fragmentLower = fragment.ToLowerInvariant();
            var fragmentConstant = Expression.Constant(fragmentLower);
            var containsCall = Expression.Call(messageTypeLower, containsMethod, fragmentConstant);
            includedExpression = includedExpression == null
                ? containsCall
                : Expression.OrElse(includedExpression, containsCall);
        }

        foreach (var eventTypeName in IntegrationEventAuthority.NonAuthoritativeEventTypeNames)
        {
            var eventTypeNameLower = eventTypeName.ToLowerInvariant();
            var eventTypeConstant = Expression.Constant(eventTypeNameLower);
            var excludedContainsCall = Expression.Call(messageTypeLower, containsMethod, eventTypeConstant);
            includedExpression = Expression.AndAlso(includedExpression!, Expression.Not(excludedContainsCall));
        }

        return Expression.Lambda<Func<MassTransitOutboxMessage, bool>>(includedExpression!, parameter);
    }

    private static string EnsureExternalPaymentReference(string paymentReference)
    {
        // Defensive null check before calling Trim to prevent NullReferenceException
        if (string.IsNullOrWhiteSpace(paymentReference))
        {
            throw new InvalidOperationException(
                "AFFILIATE_PAYOUT_EXTERNAL_CONFIRMATION_REQUIRED: A bank transfer id, wallet transaction id, or other immutable external payment reference is required before marking affiliate money as paid.");
        }

        var normalized = paymentReference.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException(
                "AFFILIATE_PAYOUT_EXTERNAL_CONFIRMATION_REQUIRED: A bank transfer id, wallet transaction id, or other immutable external payment reference is required before marking affiliate money as paid.");
        }

        return normalized;
    }

    private async Task<AffiliatePayoutRequest> EnsurePayoutRequestDestinationAuthorityAsync(
        Guid payoutRequestId,
        Guid affiliateAccountId,
        Guid storeTenantId,
        decimal amount,
        CancellationToken ct)
    {
        var request = await _db.AffiliatePayoutRequests
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                r => r.Id == payoutRequestId &&
                     r.AffiliateAccountId == affiliateAccountId &&
                     r.StoreTenantId == storeTenantId,
                ct)
            ?? throw new InvalidOperationException(
                $"AFFILIATE_PAYOUT_REQUEST_NOT_FOUND: PayoutRequest {payoutRequestId} not found for AffiliateAccount {affiliateAccountId} and StoreTenant {storeTenantId}.");

        if (request.Status != PayoutRequestStatus.Approved &&
            request.Status != PayoutRequestStatus.Paid)
        {
            throw new InvalidOperationException(
                $"AFFILIATE_PAYOUT_REQUEST_STATUS_INVALID: PayoutRequest {payoutRequestId} is {request.Status}; expected Approved before payment execution.");
        }

        if (request.ApprovedAmount != amount)
        {
            throw new InvalidOperationException(
                $"AFFILIATE_PAYOUT_REQUEST_AMOUNT_MISMATCH: PayoutRequest {payoutRequestId} approved amount {request.ApprovedAmount:F2} does not match execution amount {amount:F2}.");
        }

        var account = await _db.AffiliateAccounts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Id == affiliateAccountId && !a.IsDeleted, ct)
            ?? throw new InvalidOperationException($"Affiliate account {affiliateAccountId} not found.");

        request.EnsurePayoutWalletSnapshotMatches(
            account.PayoutWalletNumber,
            account.PayoutWalletType,
            account.PayoutWalletUpdatedAtUtc);

        return request;
    }

    private async Task EnsureVerifiedExternalPayoutEvidenceAllowedAsync(
        Guid affiliateAccountId,
        Guid storeTenantId,
        decimal externalTransferAmount,
        string paymentReference,
        string sourceLabel,
        CancellationToken ct)
    {
        if (!_options.RequireVerifiedExternalPayoutEvidence)
        {
            return;
        }

        var railNamespace = AffiliatePayoutTransferEvidence.NormalizeRailNamespace(AffiliatePayoutRailNamespace);
        var normalizedReference = AffiliatePayoutTransferEvidence.NormalizePaymentReference(paymentReference);
        var verifiedEvidence = await _db.AffiliatePayoutTransferEvidence
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.RailNamespace == railNamespace &&
                        item.NormalizedPaymentReference == normalizedReference,
                ct);

        var hasTrustedVerification =
            verifiedEvidence != null &&
            verifiedEvidence.VerifiedAtUtc != default &&
            string.Equals(
                verifiedEvidence.VerificationSource,
                AffiliatePayoutEvidenceService.TrustedVerificationSource,
                StringComparison.Ordinal) &&
            verifiedEvidence.VerificationFingerprint.Length == 64 &&
            verifiedEvidence.VerificationFingerprint.All(Uri.IsHexDigit);

        if (hasTrustedVerification)
        {
            if (verifiedEvidence.StoreTenantId != storeTenantId)
            {
                throw new InvalidOperationException(
                    "AFFILIATE_PAYOUT_EXTERNAL_EVIDENCE_TENANT_MISMATCH: Verified payout evidence belongs to a different store tenant.");
            }

            var unallocatedAmount = verifiedEvidence.ExpectedAmount - verifiedEvidence.AllocatedAmount;
            if (unallocatedAmount < externalTransferAmount)
            {
                throw new InvalidOperationException(
                    $"AFFILIATE_PAYOUT_EXTERNAL_EVIDENCE_INSUFFICIENT: Verified payout evidence has {unallocatedAmount:F2} {verifiedEvidence.Currency} unallocated, but this payout requires {externalTransferAmount:F2}.");
            }

            return;
        }

        if (_options.AllowManualPayoutEvidenceOverride)
        {
            _logger.LogCritical(
                "[Affiliate:Payout:BreakGlass] Manual payout evidence override is enabled. " +
                "AffiliateId={AffiliateId} | StoreTenantId={StoreTenantId} | Amount={Amount} | PaymentReference={PaymentReference} | Source={Source}",
                affiliateAccountId,
                storeTenantId,
                externalTransferAmount,
                paymentReference,
                sourceLabel);
            return;
        }

        throw new InvalidOperationException(
            "AFFILIATE_PAYOUT_VERIFIED_EXTERNAL_EVIDENCE_REQUIRED: Affiliate payouts require verified external transfer evidence from a trusted payment provider or bank reconciliation authority before commissions can be marked paid. Enable AllowManualPayoutEvidenceOverride only as an audited break-glass rollout setting.");
    }
}
