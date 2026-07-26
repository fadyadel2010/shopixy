using System.Security;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Entities.Affiliate;
using PointOfSale.Core.Exceptions.Affiliate;
using PointOfSale.Core.Interfaces.Affiliate;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Services.Affiliate;

/// <summary>
/// AF-38: Affiliate Payout Service.
/// Implements the Hybrid Withdrawal Engine on top of the Commission Ledger.
///
/// ARCHITECTURAL RULES:
/// - Commission engine is NEVER touched (AffiliateService.cs is untouched)
/// - Commission lifecycle (Earned → Approved → Paid) continues unchanged
/// - This service ONLY manages AffiliatePayoutRequest + Payout ledger entries
/// - AvailableForWithdrawal is computed from existing ledger (Approved - Paid - Payout)
/// - All state changes happen inside transactions to prevent race conditions
/// - Legacy settlement flow (AdminAffiliateController /approve + /pay) is preserved
/// </summary>
public class AffiliatePayoutService : IAffiliatePayoutService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<AffiliatePayoutService> _logger;

    public AffiliatePayoutService(
        ApplicationDbContext db,
        ILogger<AffiliatePayoutService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ── Available Balance ─────────────────────────────────────────────────────

    public async Task<AffiliateAvailableBalanceResult> GetAvailableBalanceAsync(
        Guid affiliateAccountId,
        CancellationToken ct = default) =>
        await GetAvailableBalanceCoreAsync(affiliateAccountId, storeTenantId: null, ct);

    public async Task<AffiliateAvailableBalanceResult> GetAvailableBalanceAsync(
        Guid affiliateAccountId,
        Guid storeTenantId,
        CancellationToken ct = default)
    {
        if (storeTenantId == Guid.Empty)
        {
            throw new ArgumentException("StoreTenantId is required for store-scoped affiliate payout balance.", nameof(storeTenantId));
        }

        return await GetAvailableBalanceCoreAsync(affiliateAccountId, storeTenantId, ct);
    }

    private async Task<AffiliateAvailableBalanceResult> GetAvailableBalanceCoreAsync(
        Guid affiliateAccountId,
        Guid? storeTenantId,
        CancellationToken ct)
    {
        var ledgerCurrencies = await ResolveAuthoritativeBalanceCurrenciesAsync(
            affiliateAccountId,
            storeTenantId,
            ct);

        // AF-38D PARTIAL REVERSAL FIX: Compute net approved amount per OrderItemId by aggregating
        // approved and reversed amounts. The previous logic incorrectly excluded entire approved
        // rows whenever ANY reversal existed for that OrderItemId, which zeroed out partial reversals.
        //
        // Correct behavior for partial reversals:
        // - OrderItem has $100 approved commission
        // - $30 is reversed (partial refund)
        // - Net approved should be $70, not $0
        //
        // Implementation: Group by OrderItemId, sum approved amounts, sum reversed amounts,
        // then compute net as Max(approved - reversed, 0) per item.
        var ledgerByOrderItem = await _db.AffiliateCommissionLedger
            .IgnoreQueryFilters()
            .Where(l =>
                l.AffiliateAccountId == affiliateAccountId &&
                (!storeTenantId.HasValue || l.StoreTenantId == storeTenantId.Value) &&
                (l.EntryType == CommissionEntryType.Approved || l.EntryType == CommissionEntryType.Reversed))
            .GroupBy(l => l.OrderItemId)
            .Select(g => new
            {
                OrderItemId = g.Key,
                ApprovedSum = g.Where(l => l.EntryType == CommissionEntryType.Approved)
                               .Sum(l => (decimal?)l.CommissionAmount) ?? 0m,
                ReversedSum = g.Where(l => l.EntryType == CommissionEntryType.Reversed)
                               .Sum(l => (decimal?)l.CommissionAmount) ?? 0m
            })
            .ToListAsync(ct);

        // Compute net approved: sum of (approved - reversed) per OrderItemId, floored at zero
        var netApproved = ledgerByOrderItem
            .Sum(item => Math.Max(item.ApprovedSum - item.ReversedSum, 0m));

        // Total reversed amount (for reporting purposes)
        var totalReversed = ledgerByOrderItem.Sum(item => item.ReversedSum);

        // AF-38D: Sum of BalanceLedger withdrawals (negative amounts)
        // IMPORTANT: Withdrawals are already negative, representing money leaving the account
        var totalWithdrawals = await _db.AffiliateBalanceLedger
            .IgnoreQueryFilters()
            .Where(l =>
                l.AffiliateAccountId == affiliateAccountId &&
                (!storeTenantId.HasValue || l.StoreTenantId == storeTenantId.Value) &&
                l.EntryType == BalanceEntryType.Withdrawal)
            .SumAsync(l => (decimal?)l.Amount, ct) ?? 0m;
        
        // Payouts may consume earned commission but may never manufacture earned value.
        // Positive adjustments and bonuses remain balance-history records, but they are
        // not payout authority. Only negative corrections reduce payout eligibility.
        var totalPayoutReductions = await _db.AffiliateBalanceLedger
            .IgnoreQueryFilters()
            .Where(l =>
                l.AffiliateAccountId == affiliateAccountId &&
                (!storeTenantId.HasValue || l.StoreTenantId == storeTenantId.Value) &&
                l.EntryType != BalanceEntryType.Withdrawal &&
                l.Amount < 0m)
            .SumAsync(l => (decimal?)l.Amount, ct) ?? 0m;

        // AF-38D: Sum of active (Pending + Approved) payout requests — reserved amount
        var totalPendingRequests = await _db.AffiliatePayoutRequests
            .IgnoreQueryFilters()
            .Where(r =>
                r.AffiliateAccountId == affiliateAccountId &&
                (!storeTenantId.HasValue || r.StoreTenantId == storeTenantId.Value) &&
                (r.Status == PayoutRequestStatus.Pending ||
                 r.Status == PayoutRequestStatus.Approved))
            .SumAsync(r => (decimal?)r.RequestedAmount, ct) ?? 0m;

        // AF-38D: CRITICAL FIX - Double Accounting Correction
        // ARCHITECTURAL PRINCIPLE:
        // - CommissionLedger.Paid = Lifecycle state transition (Approved → Paid)
        // - BalanceLedger.Withdrawal = Actual money leaving affiliate wallet
        // - Only BalanceLedger affects available balance calculation
        // - CommissionLedger.Paid does NOT reduce available balance
        //
        // CORRECT FORMULA:
        // Available = NetApproved + PayoutReductions + Withdrawals (negative) - Pending
        //
        // WHY THIS IS CORRECT:
        // - netApproved: Money earned from commerce (Approved - Reversed for partial refunds)
        // - totalPayoutReductions: Penalties and negative corrections only
        // - positive adjustments/bonuses: Never payout authority
        // - totalWithdrawals: Money withdrawn (already negative, so adding subtracts)
        // - totalPendingRequests: Money reserved for pending withdrawals
        //
        // REMOVED: totalCommissionPaid (was causing double deduction)
        //
        // AF-38Y V-1 FIX: Split raw and display values.
        // RawAvailable is the REAL balance (can be negative after penalties).
        // AvailableForWithdrawal is clamped to 0 for UI display only.
        // Guards MUST use RawAvailable to prevent over-withdrawal.
        var rawAvailable = 
            netApproved +
            totalPayoutReductions +
            totalWithdrawals +  // Already negative
            (-totalPendingRequests);

        var displayAvailable = Math.Max(0, rawAvailable);

        // AF-38E: DEFENSIVE GUARD - Detect inconsistent settlement state
        // Check if CommissionLedger.Paid entries exist without corresponding BalanceLedger entries
        var hasPaidCommissions = await _db.AffiliateCommissionLedger
            .IgnoreQueryFilters()
            .AnyAsync(l =>
                l.AffiliateAccountId == affiliateAccountId &&
                (!storeTenantId.HasValue || l.StoreTenantId == storeTenantId.Value) &&
                l.EntryType == CommissionEntryType.Paid, ct);

        if (hasPaidCommissions)
        {
            // Count how many Paid entries exist
            var paidCount = await _db.AffiliateCommissionLedger
                .IgnoreQueryFilters()
                .Where(l =>
                    l.AffiliateAccountId == affiliateAccountId &&
                    (!storeTenantId.HasValue || l.StoreTenantId == storeTenantId.Value) &&
                    l.EntryType == CommissionEntryType.Paid)
                .CountAsync(ct);

            // Count how many BalanceLedger withdrawals exist
            var withdrawalCount = await _db.AffiliateBalanceLedger
                .IgnoreQueryFilters()
                .Where(l =>
                    l.AffiliateAccountId == affiliateAccountId &&
                    (!storeTenantId.HasValue || l.StoreTenantId == storeTenantId.Value) &&
                    l.EntryType == BalanceEntryType.Withdrawal)
                .CountAsync(ct);

            // If there are Paid entries but no withdrawals, this indicates pre-AF-38E data
            if (withdrawalCount == 0)
            {
                _logger.LogWarning(
                    "[AF-38E] INCONSISTENT STATE DETECTED | AffiliateId={AffiliateId} | " +
                    "Has {PaidCount} CommissionLedger.Paid entries but NO BalanceLedger.Withdrawal entries | " +
                    "This indicates legacy settlement without monetary ledger | " +
                    "Run AF-38E migration to backfill BalanceLedger entries",
                    affiliateAccountId, paidCount);
            }
        }

        var currency = ledgerCurrencies.Count == 0
            ? storeTenantId.HasValue
                ? await ResolveStoreBaseCurrencyAsync(storeTenantId.Value, ct)
                : "EGP"
            : ledgerCurrencies[0];

        if (storeTenantId.HasValue)
        {
            var storeBaseCurrency = await ResolveStoreBaseCurrencyAsync(storeTenantId.Value, ct);
            if (!string.Equals(currency, storeBaseCurrency, StringComparison.Ordinal))
            {
                throw new AffiliatePayoutAuthorityException(
                    $"Affiliate payout currency {currency} does not match store base currency {storeBaseCurrency}. " +
                    $"StoreTenantId={storeTenantId.Value}.");
            }
        }

        // AF-38D: TotalPaid now represents actual money withdrawn (not lifecycle state)
        var totalPaid = Math.Abs(totalWithdrawals);

        return new AffiliateAvailableBalanceResult(
            TotalApproved: netApproved,
            TotalPaid: totalPaid,
            TotalPendingRequests: totalPendingRequests,
            RawAvailable: rawAvailable,
            AvailableForWithdrawal: displayAvailable,
            MinimumThreshold: AffiliatePayoutRequest.MinimumThreshold,
            CanWithdraw: rawAvailable >= AffiliatePayoutRequest.MinimumThreshold,
            Currency: currency
        );
    }

    private async Task<List<string>> ResolveAuthoritativeBalanceCurrenciesAsync(
        Guid affiliateAccountId,
        Guid? storeTenantId,
        CancellationToken ct)
    {
        // AF-38D: Detect missing/blank currency as immediate authority failure
        // WHITESPACE FIX: Use string.IsNullOrWhiteSpace equivalent to catch whitespace-only currencies
        // that would otherwise slip through and default to "EGP"
        var hasBlankCommissionCurrency = await _db.AffiliateCommissionLedger
            .IgnoreQueryFilters()
            .Where(l =>
                l.AffiliateAccountId == affiliateAccountId &&
                (!storeTenantId.HasValue || l.StoreTenantId == storeTenantId.Value) &&
                (l.Currency == null || l.Currency == string.Empty || l.Currency.Trim() == string.Empty))
            .AnyAsync(ct);

        var hasBlankBalanceCurrency = await _db.AffiliateBalanceLedger
            .IgnoreQueryFilters()
            .Where(l =>
                l.AffiliateAccountId == affiliateAccountId &&
                (!storeTenantId.HasValue || l.StoreTenantId == storeTenantId.Value) &&
                (l.Currency == null || l.Currency == string.Empty || l.Currency.Trim() == string.Empty))
            .AnyAsync(ct);

        var hasBlankPayoutCurrency = await _db.AffiliatePayoutRequests
            .IgnoreQueryFilters()
            .Where(r =>
                r.AffiliateAccountId == affiliateAccountId &&
                (!storeTenantId.HasValue || r.StoreTenantId == storeTenantId.Value) &&
                (r.Status == PayoutRequestStatus.Pending ||
                 r.Status == PayoutRequestStatus.Approved ||
                 r.Status == PayoutRequestStatus.Paid) &&
                (r.Currency == null || r.Currency == string.Empty || r.Currency.Trim() == string.Empty))
            .AnyAsync(ct);

        if (hasBlankCommissionCurrency || hasBlankBalanceCurrency || hasBlankPayoutCurrency)
        {
            var scope = storeTenantId.HasValue
                ? $"AffiliateId={affiliateAccountId}, StoreTenantId={storeTenantId.Value}"
                : $"AffiliateId={affiliateAccountId}, Scope=Global";

            var message =
                $"Affiliate payout authority conflict: payout ledger entries are missing currency snapshots. " +
                $"Payouts are blocked until the ledger is repaired. Context: {scope}.";

            _logger.LogError(
                "{Message}",
                message);

            throw new AffiliatePayoutAuthorityException(message);
        }

        // AF-38D: Fetch distinct non-empty currencies from all three sources
        // Materialize all currencies first, then normalize and deduplicate to handle
        // unnormalized variants (e.g., "usd" vs " USD ") and whitespace-only values
        // Filter out null, empty, and whitespace-only currencies at the database level
        var commissionCurrencies = _db.AffiliateCommissionLedger
            .IgnoreQueryFilters()
            .Where(l =>
                l.AffiliateAccountId == affiliateAccountId &&
                (!storeTenantId.HasValue || l.StoreTenantId == storeTenantId.Value) &&
                l.Currency != null && l.Currency != string.Empty && l.Currency.Trim() != string.Empty)
            .Select(l => l.Currency)
            .Distinct();

        var balanceCurrencies = _db.AffiliateBalanceLedger
            .IgnoreQueryFilters()
            .Where(l =>
                l.AffiliateAccountId == affiliateAccountId &&
                (!storeTenantId.HasValue || l.StoreTenantId == storeTenantId.Value) &&
                l.Currency != null && l.Currency != string.Empty && l.Currency.Trim() != string.Empty)
            .Select(l => l.Currency)
            .Distinct();

        var payoutRequestCurrencies = _db.AffiliatePayoutRequests
            .IgnoreQueryFilters()
            .Where(r =>
                r.AffiliateAccountId == affiliateAccountId &&
                (!storeTenantId.HasValue || r.StoreTenantId == storeTenantId.Value) &&
                (r.Status == PayoutRequestStatus.Pending ||
                 r.Status == PayoutRequestStatus.Approved ||
                 r.Status == PayoutRequestStatus.Paid) &&
                r.Currency != null && r.Currency != string.Empty && r.Currency.Trim() != string.Empty)
            .Select(r => r.Currency)
            .Distinct();

        // Materialize all distinct currencies from database (no Take limit yet)
        var rawCurrencies = await commissionCurrencies
            .Concat(balanceCurrencies)
            .Concat(payoutRequestCurrencies)
            .Distinct()
            .ToListAsync(ct);

        // Normalize currencies and filter out whitespace-only values before deduplication
        var currencies = rawCurrencies
            .Select(NormalizeBalanceCurrency)
            .Where(currency => !string.IsNullOrWhiteSpace(currency))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(currency => currency, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (currencies.Count > 1)
        {
            var scope = storeTenantId.HasValue
                ? $"AffiliateId={affiliateAccountId}, StoreTenantId={storeTenantId.Value}"
                : $"AffiliateId={affiliateAccountId}, Scope=Global";

            var message =
                $"Affiliate payout authority conflict: multiple currencies exist in the authoritative balance stream ({string.Join(", ", currencies)}). " +
                $"Payouts are blocked until the ledger is reconciled. Context: {scope}.";

            _logger.LogError(
                "{Message}",
                message);

            throw new AffiliatePayoutAuthorityException(message);
        }

        return currencies;
    }

    private static string NormalizeBalanceCurrency(string? currency) =>
        string.IsNullOrWhiteSpace(currency)
            ? string.Empty
            : currency.Trim().ToUpperInvariant();

    private async Task<string> ResolveStoreBaseCurrencyAsync(Guid storeTenantId, CancellationToken ct)
    {
        var currency = await _db.Tenants
            .IgnoreQueryFilters()
            .Where(tenant => tenant.Id == storeTenantId)
            .Select(tenant => tenant.Currency)
            .FirstOrDefaultAsync(ct);

        var normalized = NormalizeBalanceCurrency(currency);
        if (normalized.Length != 3)
        {
            throw new AffiliatePayoutAuthorityException(
                $"Store {storeTenantId} has no valid ISO 4217 base currency. Affiliate payouts are blocked.");
        }

        return normalized;
    }

    private static CurrentPayoutWalletSnapshot RequireCurrentPayoutWallet(AffiliateAccount account)
    {
        if (string.IsNullOrWhiteSpace(account.PayoutWalletNumber) ||
            string.IsNullOrWhiteSpace(account.PayoutWalletType))
        {
            throw new InvalidOperationException(
                "AFFILIATE_PAYOUT_WALLET_REQUIRED: A payout wallet must be registered before requesting, approving, or paying affiliate payout.");
        }

        var wallet = PayoutWallet.Create(account.PayoutWalletNumber, account.PayoutWalletType);

        return new CurrentPayoutWalletSnapshot(
            wallet.Number,
            wallet.Type,
            account.PayoutWalletUpdatedAtUtc);
    }

    private sealed record CurrentPayoutWalletSnapshot(
        string Number,
        string Type,
        DateTime? UpdatedAtUtc);

    // ── Affiliate Portal ──────────────────────────────────────────────────────

    public async Task<AffiliatePayoutRequest> RequestPayoutAsync(
        Guid affiliateAccountId,
        decimal amount,
        CancellationToken ct = default) =>
        await RequestPayoutCoreAsync(affiliateAccountId, amount, requestedStoreTenantId: null, ct);

    public async Task<AffiliatePayoutRequest> RequestPayoutAsync(
        Guid affiliateAccountId,
        decimal amount,
        Guid storeTenantId,
        CancellationToken ct = default) =>
        await RequestPayoutCoreAsync(affiliateAccountId, amount, storeTenantId, ct);

    private async Task<AffiliatePayoutRequest> RequestPayoutCoreAsync(
        Guid affiliateAccountId,
        decimal amount,
        Guid? requestedStoreTenantId,
        CancellationToken ct)
    {
        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

            // Load affiliate account for fallback currency and immutable payout destination snapshot.
            var account = await _db.AffiliateAccounts
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(a => a.Id == affiliateAccountId, ct)
                ?? throw new InvalidOperationException($"Affiliate account {affiliateAccountId} not found.");
            var payoutWallet = RequireCurrentPayoutWallet(account);

            var storeTenantId = await ResolvePayoutStoreTenantIdAsync(
                affiliateAccountId,
                account.TenantId,
                requestedStoreTenantId,
                ct);

            await AcquireAffiliatePayoutBalanceLockAsync(affiliateAccountId, storeTenantId, ct);

            // Recompute available balance inside transaction (race-condition guard)
            var balance = await GetAvailableBalanceAsync(affiliateAccountId, storeTenantId, ct);

            _logger.LogInformation(
                "[AF-38:PayoutRequest] AffiliateId={AffiliateId} | Requested={Amount} | " +
                "RawAvailable={RawAvailable} | DisplayAvailable={DisplayAvailable} | " +
                "MinThreshold={Threshold} | StoreTenantId={Store}",
                affiliateAccountId, amount, balance.RawAvailable, balance.AvailableForWithdrawal,
                balance.MinimumThreshold, storeTenantId);

            var activeDuplicate = await FindActiveDuplicatePayoutRequestAsync(
                affiliateAccountId,
                storeTenantId,
                amount,
                ct);

            if (activeDuplicate != null)
            {
                _logger.LogInformation(
                    "[AF-38:PayoutRequest] Active duplicate replay returned existing request | RequestId={RequestId} | Amount={Amount} | AffiliateId={AffiliateId} | StoreTenantId={StoreTenantId}",
                    activeDuplicate.Id, amount, affiliateAccountId, storeTenantId);

                await tx.CommitAsync(ct);
                return activeDuplicate;
            }

            // AF-38Y V-1: Use RawAvailable for guard validation (not clamped display value)
            var request = AffiliatePayoutRequest.Create(
                storeTenantId: storeTenantId,
                affiliateAccountId: affiliateAccountId,
                requestedAmount: amount,
                availableForWithdrawal: balance.RawAvailable,
                currency: balance.Currency,
                payoutWalletNumber: payoutWallet.Number,
                payoutWalletType: payoutWallet.Type,
                payoutWalletUpdatedAtUtc: payoutWallet.UpdatedAtUtc);

            _db.AffiliatePayoutRequests.Add(request);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            _logger.LogInformation(
                "[AF-38:PayoutRequest] Created | RequestId={RequestId} | Amount={Amount} | AffiliateId={AffiliateId}",
                request.Id, amount, affiliateAccountId);

            return request;
        });
    }

    private async Task<AffiliatePayoutRequest?> FindActiveDuplicatePayoutRequestAsync(
        Guid affiliateAccountId,
        Guid storeTenantId,
        decimal amount,
        CancellationToken ct)
    {
        return await _db.AffiliatePayoutRequests
            .IgnoreQueryFilters()
            .Where(request =>
                request.AffiliateAccountId == affiliateAccountId &&
                request.StoreTenantId == storeTenantId &&
                request.RequestedAmount == amount &&
                (request.Status == PayoutRequestStatus.Pending ||
                 request.Status == PayoutRequestStatus.Approved))
            .OrderByDescending(request => request.RequestedAtUtc)
            .FirstOrDefaultAsync(ct);
    }

    private async Task<Guid> ResolvePayoutStoreTenantIdAsync(
        Guid affiliateAccountId,
        Guid fallbackTenantId,
        Guid? requestedStoreTenantId,
        CancellationToken ct)
    {
        if (requestedStoreTenantId.HasValue && requestedStoreTenantId.Value != Guid.Empty)
        {
            return requestedStoreTenantId.Value;
        }

        var commissionStores = await _db.AffiliateCommissionLedger
            .IgnoreQueryFilters()
            .Where(l => l.AffiliateAccountId == affiliateAccountId && l.StoreTenantId != Guid.Empty)
            .Select(l => l.StoreTenantId)
            .Distinct()
            .Take(2)
            .ToListAsync(ct);

        if (commissionStores.Count == 1)
        {
            return commissionStores[0];
        }

        if (commissionStores.Count > 1)
        {
            throw new InvalidOperationException(
                "StoreTenantId is required when an affiliate has balances across multiple stores.");
        }

        if (fallbackTenantId != Guid.Empty)
        {
            return fallbackTenantId;
        }

        throw new InvalidOperationException("Cannot determine target store for payout.");
    }

    private async Task AcquireAffiliatePayoutBalanceLockAsync(
        Guid affiliateAccountId,
        Guid storeTenantId,
        CancellationToken ct)
    {
        if (IsNpgsqlProvider())
        {
            var lockId = BuildAffiliatePayoutLockId(affiliateAccountId, storeTenantId);
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({lockId})",
                ct);
            return;
        }

        if (IsSqlServerProvider())
        {
            var resource = $"affiliate-payout-balance:{affiliateAccountId:N}:{storeTenantId:N}";
            await _db.Database.ExecuteSqlInterpolatedAsync($@"
DECLARE @LockResult int;
EXEC @LockResult = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000;
IF @LockResult < 0 THROW 51000, 'Could not acquire affiliate payout balance lock.', 1;",
                ct);
        }
    }

    private static long BuildAffiliatePayoutLockId(Guid affiliateAccountId, Guid storeTenantId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"affiliate-payout-balance:{affiliateAccountId:N}:{storeTenantId:N}"));
        return BitConverter.ToInt64(bytes, 0);
    }

    public async Task<List<AffiliatePayoutRequest>> GetPayoutHistoryAsync(
        Guid affiliateAccountId,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        return await _db.AffiliatePayoutRequests
            .IgnoreQueryFilters()
            .Where(r => r.AffiliateAccountId == affiliateAccountId)
            .OrderByDescending(r => r.RequestedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task<List<AffiliatePayoutRequest>> GetPayoutHistoryAsync(
        Guid affiliateAccountId,
        Guid storeTenantId,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        if (storeTenantId == Guid.Empty)
        {
            throw new ArgumentException("StoreTenantId is required for store-scoped affiliate payout history.", nameof(storeTenantId));
        }

        return await _db.AffiliatePayoutRequests
            .IgnoreQueryFilters()
            .Where(r => r.AffiliateAccountId == affiliateAccountId && r.StoreTenantId == storeTenantId)
            .OrderByDescending(r => r.RequestedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    // ── Admin Operations ──────────────────────────────────────────────────────

    public async Task<(List<AffiliatePayoutRequest> Items, int TotalCount)> ListPayoutRequestsAsync(
        Guid storeTenantId,
        PayoutRequestStatus? status = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        var query = _db.AffiliatePayoutRequests
            .IgnoreQueryFilters()
            .Where(r => r.StoreTenantId == storeTenantId);

        if (status.HasValue)
            query = query.Where(r => r.Status == status.Value);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(r => r.RequestedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    // ── AF-38C: DATA ISOLATION FIX ──────────────────────────────────────

    public async Task<(List<AffiliatePayoutRequest> Items, int TotalCount)> ListPayoutRequestsForAffiliateAsync(
        Guid storeTenantId,
        Guid affiliateAccountId,
        PayoutRequestStatus? status = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        // AF-38C: CRITICAL - Filter by BOTH StoreTenantId AND AffiliateAccountId
        var query = _db.AffiliatePayoutRequests
            .IgnoreQueryFilters()
            .Where(r => 
                r.StoreTenantId == storeTenantId && 
                r.AffiliateAccountId == affiliateAccountId);

        if (status.HasValue)
            query = query.Where(r => r.Status == status.Value);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(r => r.RequestedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        
        // AF-38C: Defensive guard - verify all results match affiliate
        if (items.Any(r => r.AffiliateAccountId != affiliateAccountId))
        {
            _logger.LogError(
                "[DATA-ISOLATION-BREACH] Cross-affiliate data detected | " +
                "Expected: {ExpectedAffiliateId} | Query returned mixed affiliates",
                affiliateAccountId);
            throw new SecurityException("Cross-affiliate data access detected");
        }

        return (items, total);
    }

    /// <summary>
    /// Helper method to execute payout request state transitions with row-level locking and execution strategy.
    /// </summary>
    private async Task<AffiliatePayoutRequest> ExecuteWithRowLockAsync(
        Guid payoutRequestId,
        Guid storeTenantId,
        Func<AffiliatePayoutRequest, string?, CancellationToken, Task> stateTransition,
        string? adminNotes,
        CancellationToken ct)
    {
        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                // PRODUCTION HARDENING: Use provider-specific checks instead of IsRelational()
                // to ensure correct SQL syntax for each database provider.
                var providerName = _db.Database.ProviderName ?? string.Empty;
                IQueryable<AffiliatePayoutRequest> query;

                if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
                    providerName.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase))
                {
                    // PostgreSQL: Use FOR UPDATE with double-quoted identifiers
                    query = _db.AffiliatePayoutRequests.FromSqlInterpolated($@"
                        SELECT * FROM ""AffiliatePayoutRequests""
                        WHERE ""Id"" = {payoutRequestId}
                          AND ""StoreTenantId"" = {storeTenantId}
                        FOR UPDATE").IgnoreQueryFilters();
                }
                else if (providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) ||
                         providerName.Contains("MicrosoftSqlServer", StringComparison.OrdinalIgnoreCase))
                {
                    // SQL Server: Use UPDLOCK with square-bracketed identifiers
                    query = _db.AffiliatePayoutRequests.FromSqlInterpolated($@"
                        SELECT * FROM [AffiliatePayoutRequests] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                        WHERE [Id] = {payoutRequestId}
                          AND [StoreTenantId] = {storeTenantId}").IgnoreQueryFilters();
                }
                else
                {
                    // Non-production provider (e.g., in-memory, SQLite): Plain query, no lock needed
                    query = _db.AffiliatePayoutRequests.IgnoreQueryFilters()
                        .Where(r => r.Id == payoutRequestId && r.StoreTenantId == storeTenantId);
                }

                var request = await query.FirstOrDefaultAsync(ct)
                    ?? throw new InvalidOperationException($"Payout request {payoutRequestId} not found.");

                await stateTransition(request, adminNotes, ct);

                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);

                return request;
            }
            catch (Exception ex)
            {
                // Best-effort rollback: Use CancellationToken.None to avoid masking the original exception
                // if the cancellation token is already cancelled. Wrap in try/catch to log rollback failures
                // without suppressing the original exception.
                try
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                }
                catch (Exception rollbackEx)
                {
                    _logger.LogError(rollbackEx,
                        "Transaction rollback failed during payout request state transition | PayoutRequestId: {PayoutRequestId} | StoreTenantId: {StoreTenantId} | OriginalException: {OriginalException}",
                        payoutRequestId, storeTenantId, ex.Message);
                }
                
                throw; // Rethrow the original exception
            }
        });
    }

    public async Task<AffiliatePayoutRequest> ApprovePayoutAsync(
        Guid payoutRequestId,
        Guid storeTenantId,
        Guid approvedByUserId,
        string? adminNotes = null,
        CancellationToken ct = default)
    {
        if (approvedByUserId == Guid.Empty)
        {
            throw new ArgumentException("ApprovedBy user id is required.", nameof(approvedByUserId));
        }

        var request = await ExecuteWithRowLockAsync(
            payoutRequestId,
            storeTenantId,
            async (r, notes, token) =>
            {
                var account = await _db.AffiliateAccounts
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(a => a.Id == r.AffiliateAccountId, token)
                    ?? throw new InvalidOperationException($"Affiliate account {r.AffiliateAccountId} not found.");

                var payoutWallet = RequireCurrentPayoutWallet(account);
                r.EnsurePayoutWalletSnapshotMatches(
                    payoutWallet.Number,
                    payoutWallet.Type,
                    payoutWallet.UpdatedAtUtc);
                r.Approve(approvedByUserId, notes);
            },
            adminNotes,
            ct);

        _logger.LogInformation(
            "[AF-38:Approve] RequestId={RequestId} | Amount={Amount} | AffiliateId={AffiliateId}",
            request.Id, request.ApprovedAmount, request.AffiliateAccountId);

        return request;
    }

    public async Task<AffiliatePayoutRequest> RejectPayoutAsync(
        Guid payoutRequestId,
        Guid storeTenantId,
        Guid rejectedByUserId,
        string? adminNotes = null,
        CancellationToken ct = default)
    {
        if (rejectedByUserId == Guid.Empty)
        {
            throw new ArgumentException("RejectedBy user id is required.", nameof(rejectedByUserId));
        }

        var request = await ExecuteWithRowLockAsync(
            payoutRequestId,
            storeTenantId,
            (r, notes, _) =>
            {
                r.Reject(rejectedByUserId, notes);
                return Task.CompletedTask;
            },
            adminNotes,
            ct);

        _logger.LogInformation(
            "[AF-38:Reject] RequestId={RequestId} | AffiliateId={AffiliateId} | Notes={Notes}",
            request.Id, request.AffiliateAccountId, adminNotes);

        return request;
    }

    /// <summary>
    /// AF-38Y V-5: DEPRECATED — Use IAffiliateSettlementOrchestrator.ExecuteAffiliatePayoutAsync.
    /// This method bypasses the unified orchestrator and has inconsistent lock ordering.
    /// Throws NotSupportedException to prevent accidental use.
    /// </summary>
    [Obsolete("AF-38H: Use IAffiliateSettlementOrchestrator.ExecuteAffiliatePayoutAsync. This method is no longer supported.")]
    public Task<AffiliatePayoutRequest> MarkPayoutPaidAsync(
        Guid payoutRequestId,
        Guid storeTenantId,
        string? paymentReference = null,
        string? adminNotes = null,
        CancellationToken ct = default)
    {
        throw new NotSupportedException(
            "[AF-38Y V-5] MarkPayoutPaidAsync is DEPRECATED. " +
            "All payout execution MUST go through IAffiliateSettlementOrchestrator.ExecuteAffiliatePayoutAsync. " +
            "This method has been removed to eliminate inconsistent lock ordering.");
    }

    // ── AF-38F: Auto-Synchronization Layer ────────────────────────────────────

    /// <summary>
    /// AF-38F: Automatically reconciles open withdrawal requests when money leaves the wallet.
    /// AF-38G: Hardened with row-level locking for high-concurrency safety.
    /// 
    /// ARCHITECTURAL PRINCIPLES:
    /// - Orchestration-only (NO financial calculations)
    /// - NEVER modifies CommissionLedger
    /// - NEVER creates BalanceLedger entries
    /// - ONLY updates PayoutRequest state
    /// - FIFO-based reconciliation
    /// - Idempotent and transaction-safe
    /// 
    /// WORKFLOW:
    /// 1. Fetch open requests (Pending OR Approved) ordered by RequestedAtUtc ASC WITH ROW LOCK
    /// 2. Apply payment amount to requests in FIFO order
    /// 3. Mark requests as Paid when RemainingAmount reaches 0
    /// 4. Log all reconciliation actions
    /// 
    /// SAFETY:
    /// - Must run inside same transaction as BalanceLedger insert
    /// - Row-level locking prevents concurrent reconciliation of same requests
    /// - Never affects Available balance calculation
    /// - Deterministic and repeatable
    /// </summary>
    public async Task AutoReconcileWithdrawalRequestsAsync(
        Guid affiliateAccountId,
        Guid storeTenantId,
        decimal amountPaid,
        string paymentReference,
        CancellationToken ct = default)
    {
        if (storeTenantId == Guid.Empty)
        {
            throw new ArgumentException("StoreTenantId is required for store-scoped affiliate payout reconciliation.", nameof(storeTenantId));
        }

        if (amountPaid <= 0)
        {
            _logger.LogWarning(
                "[AF-38F:Reconcile] Invalid amount | AffiliateId={AffiliateId} | StoreTenantId={StoreTenantId} | Amount={Amount}",
                affiliateAccountId, storeTenantId, amountPaid);
            return;
        }

        // AF-38G: ROW-LEVEL LOCKING FOR HIGH-CONCURRENCY SAFETY
        // Use provider-specific row locking to prevent concurrent reconciliation.
        // This ensures only ONE transaction can reconcile these requests at a time.
        var providerName = _db.Database.ProviderName ?? string.Empty;
        IQueryable<AffiliatePayoutRequest> query;

        if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            // PostgreSQL: Use FOR UPDATE with double-quoted identifiers
            query = _db.AffiliatePayoutRequests.FromSqlInterpolated($@"
                SELECT * FROM ""AffiliatePayoutRequests""
                WHERE ""AffiliateAccountId"" = {affiliateAccountId}
                  AND ""StoreTenantId"" = {storeTenantId}
                  AND ""Status"" IN (1, 2)
                  AND ""RemainingAmount"" > 0
                ORDER BY ""RequestedAtUtc"" ASC
                FOR UPDATE");
        }
        else if (providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) ||
                 providerName.Contains("MicrosoftSqlServer", StringComparison.OrdinalIgnoreCase))
        {
            // SQL Server: Use UPDLOCK with square-bracketed identifiers
            query = _db.AffiliatePayoutRequests.FromSqlInterpolated($@"
                SELECT * FROM [AffiliatePayoutRequests] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                WHERE [AffiliateAccountId] = {affiliateAccountId}
                  AND [StoreTenantId] = {storeTenantId}
                  AND [Status] IN (1, 2)
                  AND [RemainingAmount] > 0
                ORDER BY [RequestedAtUtc] ASC");
        }
        else
        {
            // Non-production provider (e.g., in-memory, SQLite): Plain query, no lock needed
            // PHASE AF-38F FIX: Include both Pending and Approved statuses to match PostgreSQL/SQL Server branches
            // Previously only filtered Approved, causing inconsistency with production providers
            query = _db.AffiliatePayoutRequests
                .Where(r => r.AffiliateAccountId == affiliateAccountId
                         && r.StoreTenantId == storeTenantId
                         && (r.Status == PayoutRequestStatus.Pending || r.Status == PayoutRequestStatus.Approved)
                         && r.RemainingAmount > 0)
                .OrderBy(r => r.RequestedAtUtc);
        }

        var openRequests = await query.ToListAsync(ct);

        if (openRequests.Count == 0)
        {
            _logger.LogInformation(
                "[AF-38F:Reconcile] No open requests found | AffiliateId={AffiliateId} | StoreTenantId={StoreTenantId} | Amount={Amount}",
                affiliateAccountId, storeTenantId, amountPaid);
            return;
        }

        _logger.LogInformation(
            "[AF-38F:Reconcile] Starting reconciliation | AffiliateId={AffiliateId} | StoreTenantId={StoreTenantId} | " +
            "Amount={Amount} | OpenRequests={Count}",
            affiliateAccountId, storeTenantId, amountPaid, openRequests.Count);

        var remainingAmount = amountPaid;
        var reconciledCount = 0;
        var fullyPaidCount = 0;

        foreach (var request in openRequests)
        {
            if (remainingAmount <= 0)
                break;

            var beforeRemaining = request.RemainingAmount;
            var appliedAmount = request.Reconcile(remainingAmount, paymentReference);
            
            if (appliedAmount > 0)
            {
                reconciledCount++;
                remainingAmount -= appliedAmount;

                if (request.Status == PayoutRequestStatus.Paid)
                {
                    fullyPaidCount++;

                    _logger.LogInformation(
                        "[AF-38F:Reconcile] Request fully paid | RequestId={RequestId} | " +
                        "StoreTenantId={StoreTenantId} | Applied={Applied} | PreviousRemaining={Before} | Status=Paid",
                        request.Id, request.StoreTenantId, appliedAmount, beforeRemaining);
                }
                else
                {
                    _logger.LogInformation(
                        "[AF-38F:Reconcile] Partial reconciliation | RequestId={RequestId} | " +
                        "StoreTenantId={StoreTenantId} | Applied={Applied} | PreviousRemaining={Before} | NewRemaining={After}",
                        request.Id, request.StoreTenantId, appliedAmount, beforeRemaining, request.RemainingAmount);
                }
            }
        }

        // Save all changes (within existing transaction)
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "[AF-38F:Reconcile] Reconciliation complete | AffiliateId={AffiliateId} | StoreTenantId={StoreTenantId} | " +
            "TotalApplied={Applied} | Remaining={Remaining} | " +
            "RequestsReconciled={Reconciled} | RequestsFullyPaid={FullyPaid}",
            affiliateAccountId,
            storeTenantId,
            amountPaid - remainingAmount, remainingAmount,
            reconciledCount, fullyPaidCount);

        // Warn if there's leftover amount (settlement exceeded all open requests)
        if (remainingAmount > 0)
        {
            _logger.LogWarning(
                "[AF-38F:Reconcile] Settlement amount exceeded open requests | " +
                "AffiliateId={AffiliateId} | StoreTenantId={StoreTenantId} | Leftover={Leftover}",
                affiliateAccountId, storeTenantId, remainingAmount);
        }
    }

    private bool IsNpgsqlProvider() =>
        _db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;

    private bool IsSqlServerProvider() =>
        _db.Database.ProviderName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) == true;
}
