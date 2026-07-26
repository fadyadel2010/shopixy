namespace PointOfSale.Core.Entities.Affiliate;

/// <summary>
/// AF-38: Affiliate-initiated withdrawal request (Hybrid Withdrawal Engine).
///
/// ARCHITECTURAL POSITION:
/// This entity sits ABOVE the CommissionLedger lifecycle.
/// Commission lifecycle: Earned → Approved → Paid (unchanged)
/// Withdrawal lifecycle: Pending → Approved → Paid (this entity)
///
/// RULES:
/// - Minimum threshold: 100 (hard rule, no exceptions)
/// - Amount must not exceed AvailableForWithdrawal at time of request
/// - AvailableForWithdrawal = SUM(Approved commissions) - SUM(Paid commissions) - SUM(Pending/Approved payout requests)
/// - Marking as Paid creates a CommissionLedger entry (EntryType.Payout)
/// - Rejected requests restore the reserved amount to Available
/// - Append-only audit: no soft-delete, no mutation of ledger
///
/// Does NOT inherit BaseEntity — no soft-delete, no IsDeleted.
/// Immutable after status transitions (except by designated methods).
/// </summary>
public class AffiliatePayoutRequest
{
    public Guid Id { get; private set; }

    /// <summary>
    /// The StoreTenantId where commissions were earned.
    /// Matches AffiliateCommissionLedger.StoreTenantId.
    /// </summary>
    public Guid StoreTenantId { get; private set; }

    /// <summary>
    /// The affiliate account making the request.
    /// </summary>
    public Guid AffiliateAccountId { get; private set; }

    /// <summary>
    /// Amount requested by the affiliate.
    /// Must be >= 100 (MinimumThreshold) and <= AvailableForWithdrawal.
    /// </summary>
    public decimal RequestedAmount { get; private set; }

    /// <summary>
    /// Admin-set approved amount. Set when Status transitions to Approved.
    /// Typically equals RequestedAmount.
    /// </summary>
    public decimal? ApprovedAmount { get; private set; }

    /// <summary>
    /// AF-38B: Remaining amount left to be settled. 
    /// Used for automatic reconciliation with legacy settlement.
    /// Initialize to RequestedAmount.
    /// </summary>
    public decimal RemainingAmount { get; private set; }

    public PayoutRequestStatus Status { get; private set; }

    public DateTime RequestedAtUtc { get; private set; }
    public DateTime? ApprovedAtUtc { get; private set; }
    public DateTime? RejectedAtUtc { get; private set; }
    public DateTime? PaidAtUtc { get; private set; }
    public Guid? ApprovedBy { get; private set; }
    public Guid? RejectedBy { get; private set; }
    public Guid? PaidBy { get; private set; }

    /// <summary>
    /// Optional admin note (rejection reason, approval notes, etc.)
    /// </summary>
    public string? AdminNotes { get; private set; }

    /// <summary>
    /// External payment reference (bank transfer ID, Vodafone Cash ref, etc.)
    /// Set when marking as Paid.
    /// </summary>
    public string? PaymentReference { get; private set; }

    /// <summary>
    /// Currency snapshot from the commissions.
    /// </summary>
    public string Currency { get; private set; } = string.Empty;

    /// <summary>
    /// Immutable payout destination captured when the affiliate reserves money.
    /// This prevents approved payouts from silently following later wallet edits.
    /// </summary>
    public string? PayoutWalletNumberSnapshot { get; private set; }
    public string? PayoutWalletTypeSnapshot { get; private set; }
    public DateTime? PayoutWalletUpdatedAtUtcSnapshot { get; private set; }
    public DateTime? PayoutWalletSnapshotCapturedAtUtc { get; private set; }

    /// <summary>
    /// Minimum withdrawal threshold (hard rule, config-driven).
    /// </summary>
    public const decimal MinimumThreshold = 100m;

    private AffiliatePayoutRequest() { }

    // ── Factory ──────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a new payout request.
    /// Validates threshold and available balance BEFORE creating.
    /// </summary>
    public static AffiliatePayoutRequest Create(
        Guid storeTenantId,
        Guid affiliateAccountId,
        decimal requestedAmount,
        decimal availableForWithdrawal,
        string currency,
        string payoutWalletNumber,
        string payoutWalletType,
        DateTime? payoutWalletUpdatedAtUtc)
    {
        if (storeTenantId == Guid.Empty)
            throw new ArgumentException("StoreTenantId is required", nameof(storeTenantId));
        if (affiliateAccountId == Guid.Empty)
            throw new ArgumentException("AffiliateAccountId is required", nameof(affiliateAccountId));
        if (string.IsNullOrWhiteSpace(payoutWalletNumber))
            throw new InvalidOperationException("AFFILIATE_PAYOUT_WALLET_REQUIRED: A payout wallet must be registered before requesting affiliate payout.");
        if (string.IsNullOrWhiteSpace(payoutWalletType))
            throw new InvalidOperationException("AFFILIATE_PAYOUT_WALLET_REQUIRED: A payout wallet type must be registered before requesting affiliate payout.");

        if (requestedAmount < MinimumThreshold)
            throw new InvalidOperationException(
                $"Requested amount {requestedAmount:F2} is below the minimum threshold of {MinimumThreshold:F2}.");

        if (requestedAmount > availableForWithdrawal)
            throw new InvalidOperationException(
                $"Requested amount {requestedAmount:F2} exceeds available balance of {availableForWithdrawal:F2}.");

        return new AffiliatePayoutRequest
        {
            Id = Guid.NewGuid(),
            StoreTenantId = storeTenantId,
            AffiliateAccountId = affiliateAccountId,
            RequestedAmount = requestedAmount,
            RemainingAmount = requestedAmount, // AF-38B initialization
            ApprovedAmount = null,
            Status = PayoutRequestStatus.Pending,
            Currency = currency,
            RequestedAtUtc = DateTime.UtcNow,
            PayoutWalletNumberSnapshot = payoutWalletNumber.Trim(),
            PayoutWalletTypeSnapshot = payoutWalletType.Trim(),
            PayoutWalletUpdatedAtUtcSnapshot = payoutWalletUpdatedAtUtc,
            PayoutWalletSnapshotCapturedAtUtc = DateTime.UtcNow
        };
    }

    // ── Transitions ───────────────────────────────────────────────────────

    public void Approve(Guid approvedBy, string? adminNotes = null)
    {
        if (approvedBy == Guid.Empty)
            throw new ArgumentException("ApprovedBy is required.", nameof(approvedBy));

        if (Status != PayoutRequestStatus.Pending)
            throw new InvalidOperationException(
                $"Cannot approve a payout request in status {Status}. Only Pending requests can be approved.");

        Status = PayoutRequestStatus.Approved;
        ApprovedAmount = RequestedAmount;
        ApprovedAtUtc = DateTime.UtcNow;
        ApprovedBy = approvedBy;
        AdminNotes = adminNotes;
    }

    public void EnsurePayoutWalletSnapshotMatches(
        string? currentWalletNumber,
        string? currentWalletType,
        DateTime? currentWalletUpdatedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(PayoutWalletNumberSnapshot) ||
            string.IsNullOrWhiteSpace(PayoutWalletTypeSnapshot) ||
            PayoutWalletSnapshotCapturedAtUtc == null)
        {
            throw new InvalidOperationException(
                "AFFILIATE_PAYOUT_WALLET_SNAPSHOT_REQUIRED: This payout request has no immutable payout wallet snapshot and must be rejected and recreated before payment.");
        }

        var walletNumberMatches = string.Equals(
            PayoutWalletNumberSnapshot,
            currentWalletNumber?.Trim(),
            StringComparison.Ordinal);
        var walletTypeMatches = string.Equals(
            PayoutWalletTypeSnapshot,
            currentWalletType?.Trim(),
            StringComparison.Ordinal);
        var walletTimestampMatches = PayoutWalletUpdatedAtUtcSnapshot == currentWalletUpdatedAtUtc;

        if (!walletNumberMatches || !walletTypeMatches || !walletTimestampMatches)
        {
            throw new InvalidOperationException(
                "AFFILIATE_PAYOUT_WALLET_CHANGED_AFTER_REQUEST: The affiliate payout wallet changed after this payout request was created. Reject and recreate the payout request so the destination is re-approved.");
        }
    }

    public void Reject(Guid rejectedBy, string? adminNotes = null)
    {
        if (rejectedBy == Guid.Empty)
            throw new ArgumentException("RejectedBy is required.", nameof(rejectedBy));

        if (Status != PayoutRequestStatus.Pending && Status != PayoutRequestStatus.Approved)
            throw new InvalidOperationException(
                $"Cannot reject a payout request in status {Status}. Only Pending or Approved requests can be rejected.");

        Status = PayoutRequestStatus.Rejected;
        RejectedAtUtc = DateTime.UtcNow;
        RejectedBy = rejectedBy;
        AdminNotes = adminNotes;
    }

    /// <summary>
    /// AF-38F: Automatic Reconciliation (Enhanced).
    /// AF-38G: Hardened with decimal precision tolerance.
    /// Reduces RemainingAmount by the applied settlement amount.
    /// If RemainingAmount drops to 0 (or within tolerance), automatically marks as Paid.
    /// </summary>
    public decimal Reconcile(decimal appliedAmount, string? paymentReference = null)
    {
        if (Status != PayoutRequestStatus.Pending && Status != PayoutRequestStatus.Approved)
            return 0m; // Cannot reconcile if already Paid or Rejected

        var amountToApply = Math.Min(RemainingAmount, appliedAmount);
        
        RemainingAmount -= amountToApply;

        // AF-38G: DECIMAL PRECISION HARDENING
        // Prevent rounding drift from blocking request closure.
        // If RemainingAmount is within tolerance (0.0001), snap to zero and mark as Paid.
        // This handles cases like: 100.00 - 33.33 - 33.33 - 33.34 = 0.00 (but might be 0.0001 due to rounding)
        const decimal TOLERANCE = 0.0001m;
        
        if (RemainingAmount <= TOLERANCE)
        {
            RemainingAmount = 0m; // Snap to zero
            Status = PayoutRequestStatus.Paid;
            PaidAtUtc = DateTime.UtcNow;
            PaymentReference = paymentReference ?? "Legacy Settlement Reconciled";
        }

        return amountToApply;
    }

    public void MarkPaid(Guid paidBy, string? paymentReference = null, string? adminNotes = null)
    {
        if (paidBy == Guid.Empty)
            throw new ArgumentException("PaidBy is required.", nameof(paidBy));

        if (Status != PayoutRequestStatus.Approved)
            throw new InvalidOperationException(
                $"Cannot mark as Paid a payout request in status {Status}. Only Approved requests can be paid.");
        if (string.IsNullOrWhiteSpace(paymentReference))
            throw new InvalidOperationException(
                "AFFILIATE_PAYOUT_EXTERNAL_CONFIRMATION_REQUIRED: Payment reference is required.");

        Status = PayoutRequestStatus.Paid;
        RemainingAmount = 0m;
        PaidAtUtc = DateTime.UtcNow;
        PaidBy = paidBy;
        PaymentReference = paymentReference.Trim();
        if (adminNotes != null)
            AdminNotes = adminNotes;
    }
}

public enum PayoutRequestStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Paid = 4
}
