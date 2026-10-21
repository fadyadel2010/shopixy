using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities.Affiliate;

/// <summary>
/// Aggregate root: the independent global Affiliate program profile.
///
/// INVARIANTS:
/// - One independent AffiliateUser owns the profile (legacy TenantId is not merchant ownership)
/// - Status transitions: Pending -> Active -> Suspended -> Active; Active -> Closed is terminal
/// - IsActive computed from Status == Active
/// - Cannot earn commission unless Status == Active
/// - ReferralCode is globally unique, immutable after creation
/// </summary>
public class AffiliateAccount : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }

    /// <summary>
    /// The independent AffiliateUser who owns this profile (not a POS User FK).
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// Unique referral code used in tracking URLs.
    /// Format: 6-12 alphanumeric, case-insensitive, stored uppercase.
    /// IMMUTABLE after creation.
    /// </summary>
    public string ReferralCode { get; private set; } = string.Empty;

    /// <summary>
    /// Current lifecycle status.
    /// </summary>
    public AffiliateStatus Status { get; private set; } = AffiliateStatus.Pending;

    /// <summary>
    /// Display name for admin/reporting.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Whether the affiliate can earn commission right now.
    /// </summary>
    public bool IsActive => Status == AffiliateStatus.Active;

    public DateTime? ActivatedAtUtc { get; private set; }
    public DateTime? SuspendedAtUtc { get; private set; }
    public string? SuspensionReason { get; private set; }
    public DateTime? ClosedAtUtc { get; private set; }
    public string? ClosureReason { get; private set; }

    // ═══════════════════════════════════════════════════════════════════
    // AF-16: PAYOUT WALLET REGISTRATION
    // Affiliate-provided payout information for commission payments
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Payout wallet number (Vodafone Cash, Bank Account, etc.)
    /// Nullable for backward compatibility.
    /// </summary>
    public string? PayoutWalletNumber { get; private set; }

    /// <summary>
    /// Wallet type: VodafoneCash, OrangeMoney, EtisalatCash, Bank, etc.
    /// </summary>
    public string? PayoutWalletType { get; private set; }

    /// <summary>
    /// Last time wallet was updated by affiliate.
    /// </summary>
    public DateTime? PayoutWalletUpdatedAtUtc { get; private set; }

    // Private ctor for EF Core
    private AffiliateAccount() { }

    // --- Factory ---
    // --- Factory ---
    /// <summary>
    /// AF-19: Creates a GLOBAL affiliate account (no tenant binding).
    /// TenantId parameter kept for backward compatibility but becomes audit-only.
    /// </summary>
    public static AffiliateAccount Create(
        Guid tenantId, Guid userId, string referralCode, string displayName)
    {
        // AF-19: TenantId validation removed - affiliates are now global
        // Keep parameter for backward compatibility during migration
        if (userId == Guid.Empty) throw new ArgumentException("UserId is required", nameof(userId));
        if (string.IsNullOrWhiteSpace(referralCode))
            throw new ArgumentException("ReferralCode is required", nameof(referralCode));
        if (referralCode.Length < 6 || referralCode.Length > 12)
            throw new ArgumentException("ReferralCode must be 6-12 characters", nameof(referralCode));
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("DisplayName is required", nameof(displayName));

        return new AffiliateAccount
        {
            TenantId = tenantId, // AF-19: Legacy field, kept for audit trail
            UserId = userId,
            ReferralCode = referralCode.ToUpperInvariant(),
            DisplayName = displayName,
            Status = AffiliateStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
    }


    public void Activate()
    {
        if (Status == AffiliateStatus.Active) return; // idempotent
        if (Status == AffiliateStatus.Closed)
            throw new InvalidOperationException("Closed affiliate accounts cannot be reactivated");

        Status = AffiliateStatus.Active;
        ActivatedAtUtc = DateTime.UtcNow;
        SuspensionReason = null;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Suspend(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Suspension reason is required", nameof(reason));
        if (Status == AffiliateStatus.Suspended) return; // idempotent
        if (Status != AffiliateStatus.Active)
            throw new InvalidOperationException("Only active affiliate accounts can be suspended");

        Status = AffiliateStatus.Suspended;
        SuspendedAtUtc = DateTime.UtcNow;
        SuspensionReason = reason;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Close(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Closure reason is required", nameof(reason));
        if (Status == AffiliateStatus.Closed) return; // idempotent
        if (Status != AffiliateStatus.Active)
            throw new InvalidOperationException("Only active affiliate accounts can be closed");

        Status = AffiliateStatus.Closed;
        ClosedAtUtc = DateTime.UtcNow;
        ClosureReason = reason;
        SuspensionReason = null;
        UpdatedAt = DateTime.UtcNow;
    }

    // ═══════════════════════════════════════════════════════════════════
    // AF-16B: PAYOUT WALLET MANAGEMENT (Hardened with Normalization & Validation)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Updates payout wallet information with type-aware validation and normalization.
    /// SECURITY: Only callable by affiliate owner, never from admin.
    /// AF-16B: Now uses PayoutWallet value object for normalization and validation.
    /// </summary>
    public void UpdatePayoutWallet(string walletNumber, string walletType)
    {
        // AF-16B: Use value object for validation and normalization
        var wallet = PayoutWallet.Create(walletNumber, walletType);

        PayoutWalletNumber = wallet.Number;
        PayoutWalletType = wallet.Type;
        PayoutWalletUpdatedAtUtc = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// AF-16B: Gets the masked wallet number for display in affiliate portal.
    /// </summary>
    public string? GetMaskedWalletNumber()
    {
        return PayoutWalletNumber != null 
            ? PayoutWallet.MaskWalletNumber(PayoutWalletNumber) 
            : null;
    }

    // AF-30A FIX D-1: Most EF Core setter methods REMOVED — bypass domain validation.
    // SetUserId is KEPT: required during registration (AffiliateAccount created before
    // AffiliateUser, so UserId is unknown at construction time).
    // All other setters use `private set` which EF Core accesses via reflection.
    public void SetUserId(Guid id) => UserId = id;
}

public enum AffiliateStatus
{
    Pending = 0,
    Active = 1,
    Suspended = 2,
    Closed = 3
}
