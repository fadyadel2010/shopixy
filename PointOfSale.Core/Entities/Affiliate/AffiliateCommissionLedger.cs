using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities.Affiliate;

/// <summary>
/// Aggregate root: Append-only commission ledger entry at OrderItem (SKU) level.
///
/// INVARIANTS:
/// - APPEND ONLY — no updates, no deletes (enforced by DB trigger)
/// - NO IsDeleted field — this entity is genuinely immutable
/// - One entry per (OrderItemId, EntryType) — unique constraint (AF-19B: no TenantId)
/// - CommissionAmount > 0 always (CHECK constraint + domain)
/// - CommissionAmount = min(BaseAmount × CommissionRate, MaxPerItem)
/// - BaseAmount = OrderItem.PaidAmount at calculation time (snapshot)
/// - Reversal BaseAmount = original entry's BaseAmount (not commission)
/// - EntryType: Earned, Reversed, Approved, or Paid
///
/// SETTLEMENT LIFECYCLE (AF-7):
///   Earned → Approved → Paid
///   Reversal cancels Earned and blocks Approved/Paid
///
/// AF-19B GLOBAL MARKETPLACE:
/// - StoreTenantId tracks which store owes the commission
/// - Affiliate can earn from multiple stores
/// - TenantId is ALWAYS NULL (affiliates are global)
/// - Does NOT implement ITenantOwned (not tenant-scoped)
/// </summary>
public class AffiliateCommissionLedger
{
    // ═══════════════════════════════════════════════════════════════════
    // NOTE: This entity does NOT inherit from BaseEntity.
    // It has NO IsDeleted, NO UpdatedAt, NO UpdatedBy.
    // It is genuinely append-only — the DB trigger enforces this.
    // ═══════════════════════════════════════════════════════════════════

    public Guid Id { get; set; }
    
    /// <summary>
    /// AF-19B: DEPRECATED - Always NULL. Affiliates are global.
    /// Column kept for rollback safety only. Use StoreTenantId instead.
    /// </summary>
    public Guid? TenantId { get; set; }
    
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }

    /// <summary>
    /// AF-19: The TenantId of the STORE where the sale happened.
    /// This is the store that owes the commission, NOT the affiliate's home tenant.
    /// This is the ONLY tenant field that matters.
    /// </summary>
    public Guid StoreTenantId { get; private set; }

    /// <summary>
    /// FK → AffiliateAttributions.Id
    /// AF-38C: NOT NULL - commission always tied to specific attribution.
    /// </summary>
    public Guid AttributionId { get; private set; }

    /// <summary>
    /// FK → AffiliateAccounts.Id (denormalized for fast queries)
    /// </summary>
    public Guid AffiliateAccountId { get; private set; }

    /// <summary>
    /// FK → Orders.Id (denormalized)
    /// AF-38C: NOT NULL - commission always tied to specific order.
    /// </summary>
    public Guid OrderId { get; private set; }

    /// <summary>
    /// FK → OrderItems.Id — commission is per-SKU.
    /// AF-38C: NOT NULL - commission always tied to specific order item.
    /// </summary>
    public Guid OrderItemId { get; private set; }

    /// <summary>
    /// The OrderItem.PaidAmount at calculation time (snapshot).
    /// For reversals, this equals the ORIGINAL entry's BaseAmount.
    /// </summary>
    public decimal BaseAmount { get; private set; }

    /// <summary>
    /// Commission rate snapshot from AffiliateProductPolicy.
    /// </summary>
    public decimal CommissionRate { get; private set; }

    /// <summary>
    /// Calculated commission. Always > 0.
    /// </summary>
    public decimal CommissionAmount { get; private set; }

    /// <summary>
    /// Currency code snapshot from Order.
    /// </summary>
    public string Currency { get; private set; } = string.Empty;

    public CommissionEntryType EntryType { get; private set; }

    /// <summary>
    /// For Reversed entries: the original Earned entry.
    /// Null for Earned entries.
    /// </summary>
    public Guid? OriginalEntryId { get; private set; }

    /// <summary>
    /// Idempotency key. UNIQUE per (TenantId, IdempotencyKey).
    /// Format: AFFILIATE_COMM:{EntryType}:{TenantId}:{OrderItemId}
    /// Ensures one entry per (TenantId, OrderItemId, EntryType).
    /// </summary>
    public string IdempotencyKey { get; private set; } = string.Empty;

    public DateTime RecordedAtUtc { get; private set; }

    private AffiliateCommissionLedger() { }

    // --- Factory: Earned ---
    /// <summary>
    /// AF-19B: Creates Earned entry with StoreTenantId ONLY (truly global).
    /// TenantId is always NULL.
    /// </summary>
    public static AffiliateCommissionLedger CreateEarned(
        Guid storeTenantId, Guid attributionId, Guid affiliateAccountId,
        Guid orderId, Guid orderItemId,
        decimal paidAmount, decimal commissionRate, decimal? maxPerItem,
        string currency)
    {
        if (storeTenantId == Guid.Empty) throw new ArgumentException("StoreTenantId is required", nameof(storeTenantId));
        if (paidAmount <= 0) throw new ArgumentException("PaidAmount must be positive", nameof(paidAmount));
        if (commissionRate <= 0 || commissionRate > 0.50m)
            throw new ArgumentException("CommissionRate must be in (0, 0.50]", nameof(commissionRate));

        var raw = paidAmount * commissionRate;
        var commission = maxPerItem.HasValue ? Math.Min(raw, maxPerItem.Value) : raw;
        commission = Math.Round(commission, 4, MidpointRounding.ToZero);

        if (commission <= 0)
            throw new InvalidOperationException("Calculated commission must be positive");

        return new AffiliateCommissionLedger
        {
            Id = Guid.NewGuid(),
            TenantId = null, // AF-19B: Always NULL - affiliates are global
            StoreTenantId = storeTenantId, // AF-19: Where sale happened
            AttributionId = attributionId,
            AffiliateAccountId = affiliateAccountId,
            OrderId = orderId,
            OrderItemId = orderItemId,
            BaseAmount = paidAmount,
            CommissionRate = commissionRate,
            CommissionAmount = commission,
            Currency = currency,
            EntryType = CommissionEntryType.Earned,
            OriginalEntryId = null,
            IdempotencyKey = $"AFFILIATE_COMM:EARNED:{orderItemId}", // AF-19B: No TenantId
            RecordedAtUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
    }

    // --- Factory: Reversed ---
    /// <summary>
    /// AF-19B: Creates a reversal entry with StoreTenantId ONLY.
    /// BaseAmount carries the ORIGINAL entry's BaseAmount (not the original commission).
    /// TenantId is always NULL.
    /// </summary>
    public static AffiliateCommissionLedger CreateReversal(
        Guid storeTenantId, Guid originalEntryId,
        Guid attributionId, Guid affiliateAccountId,
        Guid orderId, Guid orderItemId,
        decimal originalBaseAmount, decimal originalCommission, decimal originalRate,
        string currency)
    {
        if (storeTenantId == Guid.Empty) throw new ArgumentException("StoreTenantId is required", nameof(storeTenantId));
        if (originalCommission <= 0)
            throw new ArgumentException("Original commission must be positive", nameof(originalCommission));
        if (originalBaseAmount <= 0)
            throw new ArgumentException("Original base amount must be positive", nameof(originalBaseAmount));

        return new AffiliateCommissionLedger
        {
            Id = Guid.NewGuid(),
            TenantId = null, // AF-19B: Always NULL
            StoreTenantId = storeTenantId,
            AttributionId = attributionId,
            AffiliateAccountId = affiliateAccountId,
            OrderId = orderId,
            OrderItemId = orderItemId,
            BaseAmount = originalBaseAmount,
            CommissionRate = originalRate,
            CommissionAmount = originalCommission,
            Currency = currency,
            EntryType = CommissionEntryType.Reversed,
            OriginalEntryId = originalEntryId,
            IdempotencyKey = $"AFFILIATE_COMM:REVERSED:{orderItemId}", // AF-19B: No TenantId
            RecordedAtUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// AF-19B: Creates a reversal entry with custom idempotency key for partial reversals.
    /// Supports multiple partial reversals of the same earned entry.
    /// </summary>
    public static AffiliateCommissionLedger CreateReversalWithCustomKey(
        Guid storeTenantId, Guid originalEntryId,
        Guid attributionId, Guid affiliateAccountId,
        Guid orderId, Guid orderItemId,
        decimal reversalBaseAmount, decimal reversalCommission, decimal originalRate,
        string currency,
        string idempotencyKey)
    {
        if (storeTenantId == Guid.Empty) throw new ArgumentException("StoreTenantId is required", nameof(storeTenantId));
        if (reversalCommission <= 0)
            throw new ArgumentException("Reversal commission must be positive", nameof(reversalCommission));
        if (reversalBaseAmount <= 0)
            throw new ArgumentException("Reversal base amount must be positive", nameof(reversalBaseAmount));
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new ArgumentException("Idempotency key is required", nameof(idempotencyKey));

        return new AffiliateCommissionLedger
        {
            Id = Guid.NewGuid(),
            TenantId = null, // AF-19B: Always NULL
            StoreTenantId = storeTenantId,
            AttributionId = attributionId,
            AffiliateAccountId = affiliateAccountId,
            OrderId = orderId,
            OrderItemId = orderItemId,
            BaseAmount = reversalBaseAmount,
            CommissionRate = originalRate,
            CommissionAmount = reversalCommission,
            Currency = currency,
            EntryType = CommissionEntryType.Reversed,
            OriginalEntryId = originalEntryId,
            IdempotencyKey = idempotencyKey,
            RecordedAtUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
    }

    public static AffiliateCommissionLedger CreateEarnedFromSnapshot(
        Guid storeTenantId, Guid attributionId, Guid affiliateAccountId,
        Guid orderId, Guid orderItemId,
        decimal baseAmount, decimal commissionRate, decimal commissionAmount,
        string currency)
    {
        if (storeTenantId == Guid.Empty) throw new ArgumentException("StoreTenantId is required", nameof(storeTenantId));
        if (baseAmount <= 0) throw new ArgumentException("BaseAmount must be positive", nameof(baseAmount));
        if (commissionRate <= 0 || commissionRate > 0.50m)
            throw new ArgumentException("CommissionRate must be in (0, 0.50]", nameof(commissionRate));
        if (commissionAmount <= 0)
            throw new ArgumentException("CommissionAmount must be positive", nameof(commissionAmount));

        // Round first, then validate the rounded value to ensure CommissionAmount > 0 invariant
        var roundedCommission = Math.Round(commissionAmount, 4, MidpointRounding.ToZero);
        if (roundedCommission <= 0)
            throw new ArgumentException(
                $"CommissionAmount rounds to zero or negative ({commissionAmount} → {roundedCommission}). " +
                "Commission too small to record.", nameof(commissionAmount));

        return new AffiliateCommissionLedger
        {
            Id = Guid.NewGuid(),
            TenantId = null,
            StoreTenantId = storeTenantId,
            AttributionId = attributionId,
            AffiliateAccountId = affiliateAccountId,
            OrderId = orderId,
            OrderItemId = orderItemId,
            BaseAmount = baseAmount,
            CommissionRate = commissionRate,
            CommissionAmount = roundedCommission,
            Currency = currency,
            EntryType = CommissionEntryType.Earned,
            OriginalEntryId = null,
            IdempotencyKey = $"AFFILIATE_COMM:EARNED:{orderItemId}",
            RecordedAtUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
    }

    // --- Factory: Approved (AF-7) ---
    /// <summary>
    /// AF-19B: Creates an Approved entry with StoreTenantId from Earned entry.
    /// INVARIANT: Cannot approve without a prior Earned entry.
    /// No recalculation — copies financial data from Earned.
    /// TenantId is always NULL.
    /// </summary>
    public static AffiliateCommissionLedger CreateApproved(
        AffiliateCommissionLedger earnedEntry)
    {
        if (earnedEntry.EntryType != CommissionEntryType.Earned)
            throw new InvalidOperationException(
                $"Cannot approve non-Earned entry. EntryType={earnedEntry.EntryType}");

        return new AffiliateCommissionLedger
        {
            Id = Guid.NewGuid(),
            TenantId = null, // AF-19B: Always NULL
            StoreTenantId = earnedEntry.StoreTenantId,
            AttributionId = earnedEntry.AttributionId,
            AffiliateAccountId = earnedEntry.AffiliateAccountId,
            OrderId = earnedEntry.OrderId,
            OrderItemId = earnedEntry.OrderItemId,
            BaseAmount = earnedEntry.BaseAmount,
            CommissionRate = earnedEntry.CommissionRate,
            CommissionAmount = earnedEntry.CommissionAmount,
            Currency = earnedEntry.Currency,
            EntryType = CommissionEntryType.Approved,
            OriginalEntryId = earnedEntry.Id,
            IdempotencyKey = $"AFFILIATE_COMM:APPROVED:{earnedEntry.OrderItemId}", // AF-19B: No TenantId
            RecordedAtUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
    }

    // --- Factory: Paid (AF-7) ---
    /// <summary>
    /// AF-38C: Creates a Paid entry with StoreTenantId from Approved entry.
    /// INVARIANT: Cannot pay without a prior Approved entry.
    /// No recalculation — copies financial data from Approved.
    /// TenantId is always NULL.
    /// </summary>
    public static AffiliateCommissionLedger CreatePaid(
        AffiliateCommissionLedger approvedEntry,
        decimal? approvedMaxPerItem)
    {
        return CreatePaidNetOfReversals(
            approvedEntry,
            approvedEntry.BaseAmount,
            approvedEntry.CommissionAmount,
            approvedMaxPerItem);
    }

    /// <summary>
    /// Creates a Paid entry for the unreversed remainder of an Approved entry.
    /// Used when partial refunds have reduced the affiliate liability after approval.
    /// </summary>
    public static AffiliateCommissionLedger CreatePaidNetOfReversals(
        AffiliateCommissionLedger approvedEntry,
        decimal payableBaseAmount,
        decimal payableCommissionAmount,
        decimal? approvedMaxPerItem)
    {
        if (approvedEntry.EntryType != CommissionEntryType.Approved)
            throw new InvalidOperationException(
                $"Cannot pay non-Approved entry. EntryType={approvedEntry.EntryType}");
        if (payableBaseAmount <= 0)
            throw new ArgumentException("Payable base amount must be positive.", nameof(payableBaseAmount));
        if (payableCommissionAmount <= 0)
            throw new ArgumentException("Payable commission amount must be positive.", nameof(payableCommissionAmount));
        if (payableBaseAmount > approvedEntry.BaseAmount)
            throw new ArgumentException("Payable base amount cannot exceed the approved base amount.", nameof(payableBaseAmount));
        if (payableCommissionAmount > approvedEntry.CommissionAmount)
            throw new ArgumentException("Payable commission amount cannot exceed the approved commission amount.", nameof(payableCommissionAmount));

        // Validate a full payment against the immutable approved authority. For a
        // partial remainder the caller supplies exact append-only ledger subtraction
        // (Approved - Reversed). Recalculating it from the remaining base can reject a
        // valid history after multiple independently rounded reversals, and reapplying
        // the cap can recreate value for capped commissions. The upper-bound checks
        // above keep the partial result inside the approved authority.
        var isPartialRemainder = payableBaseAmount < approvedEntry.BaseAmount ||
                                  payableCommissionAmount < approvedEntry.CommissionAmount;
        var expectedCommission = approvedMaxPerItem.HasValue
                ? Math.Round(
                    Math.Min(
                        approvedEntry.BaseAmount * approvedEntry.CommissionRate,
                        approvedMaxPerItem.Value),
                    4,
                    MidpointRounding.ToZero)
                : Math.Round(
                    approvedEntry.BaseAmount * approvedEntry.CommissionRate,
                    4,
                    MidpointRounding.ToZero);

        // Allow small rounding tolerance (0.0001) for currency precision
        var tolerance = 0.0001m;
        if (!isPartialRemainder && Math.Abs(payableCommissionAmount - expectedCommission) > tolerance)
        {
            throw new InvalidOperationException(
                $"Payable commission amount does not match approved calculation. " +
                $"AffiliateCommissionLedger.CreatePaidNetOfReversals validation failed: " +
                $"Expected {expectedCommission:F4} (BaseAmount {payableBaseAmount:F4} × Rate {approvedEntry.CommissionRate:F6}" +
                (approvedMaxPerItem.HasValue ? $", capped at {approvedMaxPerItem.Value:F4}" : "") + 
                $"), but received {payableCommissionAmount:F4}. " +
                $"OriginalEntryId={approvedEntry.Id}, OrderItemId={approvedEntry.OrderItemId}, EntryType=Paid");
        }

        return new AffiliateCommissionLedger
        {
            Id = Guid.NewGuid(),
            TenantId = null, // AF-19B: Always NULL
            StoreTenantId = approvedEntry.StoreTenantId,
            AttributionId = approvedEntry.AttributionId,
            AffiliateAccountId = approvedEntry.AffiliateAccountId,
            OrderId = approvedEntry.OrderId,
            OrderItemId = approvedEntry.OrderItemId,
            BaseAmount = payableBaseAmount,
            CommissionRate = approvedEntry.CommissionRate,
            CommissionAmount = payableCommissionAmount,
            Currency = approvedEntry.Currency,
            EntryType = CommissionEntryType.Paid,
            OriginalEntryId = approvedEntry.Id,
            IdempotencyKey = $"AFFILIATE_COMM:PAID:{approvedEntry.OrderItemId}", // AF-19B: No TenantId
            RecordedAtUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

    }

    // Y2 Audit Fix: Public setter methods REMOVED.
    // EF Core materializes entities via private setters directly — no public mutators needed.
    // This enforces the append-only immutability contract at the API surface level.
}

public enum CommissionEntryType
{
    Earned = 0,
    Reversed = 1,
    Approved = 2,
    Paid = 3
    // AF-38C: Payout (4) REMOVED - moved to AffiliateBalanceLedger.BalanceEntryType.Withdrawal
}
