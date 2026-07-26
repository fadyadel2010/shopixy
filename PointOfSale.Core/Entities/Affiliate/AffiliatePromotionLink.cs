using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities.Affiliate;

/// <summary>
/// AF-15: Store-Scoped Promotion Link.
///
/// Decouples affiliate identity from store/tenant binding.
/// An affiliate (AffiliateUser) creates PromotionLinks to promote specific
/// products within specific stores. The ReferralCode on the link is the
/// URL-facing token used in ?ref=CODE.
///
/// INVARIANTS:
/// - One PromotionLink per (AffiliateUserId, StoreId, ProductId) — enforced DB UNIQUE
/// - ReferralCode globally unique — different from AffiliateAccount.ReferralCode
/// - StoreId == TenantId of the store being promoted
/// - AffiliateUser must exist (FK enforced)
/// - Immutable after creation (append-only)
///
/// SECURITY:
/// - ReferralCode lookup MUST validate StoreId == CurrentTenant
/// - Cross-tenant promotion link usage is rejected at middleware level
///
/// KILL LIST:
/// - ❌ No commission logic here
/// - ❌ No monetary fields
/// - ❌ No mutable state
/// </summary>
public class AffiliatePromotionLink : BaseEntity, ITenantOwned
{
    /// <summary>
    /// StoreId == TenantId of the store this link promotes.
    /// Maps to ITenantOwned.TenantId for query filter compatibility.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// FK → AffiliateUsers.Id.
    /// The global affiliate identity that owns this link.
    /// </summary>
    public Guid AffiliateUserId { get; private set; }

    /// <summary>
    /// Navigation property to the owning AffiliateUser.
    /// </summary>
    public AffiliateUser AffiliateUser { get; private set; } = null!;

    /// <summary>
    /// FK → Products.Id.
    /// The product being promoted in this store.
    /// </summary>
    public Guid ProductId { get; private set; }

    /// <summary>
    /// Unique referral code for this promotion link.
    /// Used in ?ref=CODE on storefront URLs.
    /// Format: alphanumeric, 8-16 chars, stored UPPERCASE.
    /// GLOBALLY UNIQUE (not per-tenant).
    /// </summary>
    public string ReferralCode { get; private set; } = string.Empty;

    /// <summary>
    /// FK → AffiliateAccounts.Id.
    /// The store-scoped affiliate account (legacy bridge — populated during dual-write).
    /// This allows commission pipeline to continue using AffiliateAccountId.
    /// </summary>
    public Guid? AffiliateAccountId { get; private set; }

    /// <summary>
    /// Whether this link is currently active and accepting clicks.
    /// </summary>
    public bool IsActive { get; private set; } = true;

    // Private ctor for EF Core
    private AffiliatePromotionLink() { }

    // ─── Factory ────────────────────────────────────────────────────

    public static AffiliatePromotionLink Create(
        Guid storeId, Guid affiliateUserId, Guid productId,
        string referralCode, Guid? affiliateAccountId = null)
    {
        if (storeId == Guid.Empty)
            throw new ArgumentException("StoreId is required", nameof(storeId));
        if (affiliateUserId == Guid.Empty)
            throw new ArgumentException("AffiliateUserId is required", nameof(affiliateUserId));
        if (productId == Guid.Empty)
            throw new ArgumentException("ProductId is required", nameof(productId));
        if (string.IsNullOrWhiteSpace(referralCode))
            throw new ArgumentException("ReferralCode is required", nameof(referralCode));
        if (referralCode.Length < 8 || referralCode.Length > 16)
            throw new ArgumentException("ReferralCode must be 8-16 characters", nameof(referralCode));

        return new AffiliatePromotionLink
        {
            TenantId = storeId,
            AffiliateUserId = affiliateUserId,
            ProductId = productId,
            ReferralCode = referralCode.ToUpperInvariant(),
            AffiliateAccountId = affiliateAccountId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
    }

    // ─── Mutations ──────────────────────────────────────────────────

    public void Deactivate()
    {
        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Activate()
    {
        IsActive = true;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// AF-15 dual-write: set the legacy AffiliateAccountId bridge.
    /// </summary>
    public void SetLegacyAffiliateAccountId(Guid accountId)
    {
        AffiliateAccountId = accountId;
        UpdatedAt = DateTime.UtcNow;
    }

    // ─── EF Core setters ────────────────────────────────────────────

    public void SetAffiliateUserId(Guid id) => AffiliateUserId = id;
    public void SetProductId(Guid id) => ProductId = id;
    public void SetReferralCode(string code) => ReferralCode = code;
    public void SetIsActive(bool active) => IsActive = active;
    public void SetAffiliateAccountId(Guid? id) => AffiliateAccountId = id;
}
