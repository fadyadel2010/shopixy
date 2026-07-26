namespace PointOfSale.Core.Entities.Affiliate;

/// <summary>
/// AF-38C: Account-level financial movements ledger.
/// Tracks money movement at affiliate account level (withdrawals, adjustments, bonuses, penalties).
/// 
/// ARCHITECTURAL SEPARATION:
/// - AffiliateCommissionLedger: Commerce-derived earnings (per order item)
/// - AffiliateBalanceLedger: Account-level movements (withdrawals, adjustments)
/// 
/// INVARIANTS:
/// - APPEND ONLY — no updates, no deletes (enforced by DB trigger)
/// - Amount cannot be zero
/// - Withdrawal entries must have negative amount
/// - No FK to Attribution, Order, or OrderItem (account-level, not transaction-level)
/// - Unique IdempotencyKey per entry
/// 
/// ENTRY TYPES:
/// - Withdrawal (1): Payout to affiliate (negative amount)
/// - Adjustment (2): Manual correction (positive or negative)
/// - Bonus (3): Promotional bonus (positive)
/// - Penalty (4): Violation penalty (negative)
/// </summary>
public class AffiliateBalanceLedger
{
    public Guid Id { get; private set; }
    
    /// <summary>
    /// AF-19B: DEPRECATED - Always NULL for global affiliates.
    /// Kept for rollback safety only.
    /// </summary>
    public Guid? TenantId { get; private set; }
    
    /// <summary>
    /// The TenantId of the STORE where the transaction occurred.
    /// For withdrawals, this is the store that processed the payout.
    /// </summary>
    public Guid StoreTenantId { get; private set; }
    
    /// <summary>
    /// FK → AffiliateAccounts.Id
    /// </summary>
    public Guid AffiliateAccountId { get; private set; }
    
    /// <summary>
    /// Type of balance movement.
    /// </summary>
    public BalanceEntryType EntryType { get; private set; }
    
    /// <summary>
    /// Amount of the movement.
    /// Negative for withdrawals and penalties.
    /// Positive for bonuses and positive adjustments.
    /// Cannot be zero.
    /// </summary>
    public decimal Amount { get; private set; }
    
    /// <summary>
    /// Optional reference to related entity (e.g., WithdrawalRequestId).
    /// </summary>
    public Guid? ReferenceId { get; private set; }
    
    /// <summary>
    /// Human-readable description of the entry.
    /// </summary>
    public string? Description { get; private set; }
    
    /// <summary>
    /// Idempotency key. UNIQUE globally.
    /// Format: AFFILIATE_BALANCE:{EntryType}:{ReferenceId or Guid}
    /// </summary>
    public string IdempotencyKey { get; private set; } = string.Empty;
    
    /// <summary>
    /// Currency code.
    /// </summary>
    public string Currency { get; private set; } = string.Empty;
    
    public DateTime RecordedAtUtc { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public Guid? CreatedBy { get; private set; }
    
    private AffiliateBalanceLedger() { }
    
    // ═══════════════════════════════════════════════════════════════════
    // Factory: Withdrawal
    // ═══════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Creates a withdrawal entry when admin marks a payout request as Paid.
    /// This is the authoritative deduction record.
    /// </summary>
    public static AffiliateBalanceLedger CreateWithdrawal(
        Guid storeTenantId,
        Guid affiliateAccountId,
        Guid payoutRequestId,
        decimal amount,
        string currency,
        string? description = null)
    {
        if (storeTenantId == Guid.Empty)
            throw new ArgumentException("StoreTenantId is required", nameof(storeTenantId));
        if (affiliateAccountId == Guid.Empty)
            throw new ArgumentException("AffiliateAccountId is required", nameof(affiliateAccountId));
        if (payoutRequestId == Guid.Empty)
            throw new ArgumentException("PayoutRequestId is required", nameof(payoutRequestId));
        if (amount <= 0)
            throw new ArgumentException("Withdrawal amount must be positive", nameof(amount));
        
        return new AffiliateBalanceLedger
        {
            Id = Guid.NewGuid(),
            TenantId = null, // AF-19B: Always NULL for global affiliates
            StoreTenantId = storeTenantId,
            AffiliateAccountId = affiliateAccountId,
            EntryType = BalanceEntryType.Withdrawal,
            Amount = -amount, // Negative for withdrawal
            ReferenceId = payoutRequestId,
            Description = description ?? "Affiliate payout withdrawal",
            IdempotencyKey = $"AFFILIATE_BALANCE:WITHDRAWAL:{payoutRequestId}",
            Currency = currency,
            RecordedAtUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
    }
    
    // ═══════════════════════════════════════════════════════════════════
    // Factory: Adjustment
    // ═══════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Creates a manual adjustment entry (admin correction).
    /// Can be positive or negative.
    /// </summary>
    public static AffiliateBalanceLedger CreateAdjustment(
        Guid storeTenantId,
        Guid affiliateAccountId,
        decimal amount,
        string currency,
        string description,
        Guid? referenceId = null)
    {
        if (storeTenantId == Guid.Empty)
            throw new ArgumentException("StoreTenantId is required", nameof(storeTenantId));
        if (affiliateAccountId == Guid.Empty)
            throw new ArgumentException("AffiliateAccountId is required", nameof(affiliateAccountId));
        if (amount == 0)
            throw new ArgumentException("Adjustment amount cannot be zero", nameof(amount));
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Description is required for adjustments", nameof(description));
        
        var idempotencyKey = referenceId.HasValue
            ? $"AFFILIATE_BALANCE:ADJUSTMENT:{referenceId.Value}"
            : $"AFFILIATE_BALANCE:ADJUSTMENT:{Guid.NewGuid()}";
        
        return new AffiliateBalanceLedger
        {
            Id = Guid.NewGuid(),
            TenantId = null,
            StoreTenantId = storeTenantId,
            AffiliateAccountId = affiliateAccountId,
            EntryType = BalanceEntryType.Adjustment,
            Amount = amount,
            ReferenceId = referenceId,
            Description = description,
            IdempotencyKey = idempotencyKey,
            Currency = currency,
            RecordedAtUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
    }
    
    // ═══════════════════════════════════════════════════════════════════
    // Factory: Bonus
    // ═══════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Creates a bonus entry (promotional reward, referral bonus, etc.).
    /// Always positive.
    /// </summary>
    public static AffiliateBalanceLedger CreateBonus(
        Guid storeTenantId,
        Guid affiliateAccountId,
        decimal amount,
        string currency,
        string description,
        Guid? referenceId = null)
    {
        if (storeTenantId == Guid.Empty)
            throw new ArgumentException("StoreTenantId is required", nameof(storeTenantId));
        if (affiliateAccountId == Guid.Empty)
            throw new ArgumentException("AffiliateAccountId is required", nameof(affiliateAccountId));
        if (amount <= 0)
            throw new ArgumentException("Bonus amount must be positive", nameof(amount));
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Description is required for bonuses", nameof(description));
        
        var idempotencyKey = referenceId.HasValue
            ? $"AFFILIATE_BALANCE:BONUS:{referenceId.Value}"
            : $"AFFILIATE_BALANCE:BONUS:{Guid.NewGuid()}";
        
        return new AffiliateBalanceLedger
        {
            Id = Guid.NewGuid(),
            TenantId = null,
            StoreTenantId = storeTenantId,
            AffiliateAccountId = affiliateAccountId,
            EntryType = BalanceEntryType.Bonus,
            Amount = amount,
            ReferenceId = referenceId,
            Description = description,
            IdempotencyKey = idempotencyKey,
            Currency = currency,
            RecordedAtUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
    }
    
    // ═══════════════════════════════════════════════════════════════════
    // Factory: Penalty
    // ═══════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Creates a penalty entry (policy violation, chargeback, etc.).
    /// Always negative.
    /// </summary>
    public static AffiliateBalanceLedger CreatePenalty(
        Guid storeTenantId,
        Guid affiliateAccountId,
        decimal amount,
        string currency,
        string description,
        Guid? referenceId = null)
    {
        if (storeTenantId == Guid.Empty)
            throw new ArgumentException("StoreTenantId is required", nameof(storeTenantId));
        if (affiliateAccountId == Guid.Empty)
            throw new ArgumentException("AffiliateAccountId is required", nameof(affiliateAccountId));
        if (amount <= 0)
            throw new ArgumentException("Penalty amount must be positive (will be stored as negative)", nameof(amount));
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Description is required for penalties", nameof(description));
        
        var idempotencyKey = referenceId.HasValue
            ? $"AFFILIATE_BALANCE:PENALTY:{referenceId.Value}"
            : $"AFFILIATE_BALANCE:PENALTY:{Guid.NewGuid()}";
        
        return new AffiliateBalanceLedger
        {
            Id = Guid.NewGuid(),
            TenantId = null,
            StoreTenantId = storeTenantId,
            AffiliateAccountId = affiliateAccountId,
            EntryType = BalanceEntryType.Penalty,
            Amount = -amount, // Negative for penalty
            ReferenceId = referenceId,
            Description = description,
            IdempotencyKey = idempotencyKey,
            Currency = currency,
            RecordedAtUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
    }
}

public enum BalanceEntryType
{
    Withdrawal = 1,
    Adjustment = 2,
    Bonus = 3,
    Penalty = 4
}
