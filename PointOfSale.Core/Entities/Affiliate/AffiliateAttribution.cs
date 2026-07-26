using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities.Affiliate;

/// <summary>
/// Aggregate root: Immutable record that an order was attributed to an affiliate.
///
/// INVARIANTS:
/// - Exactly ONE attribution per (TenantId, OrderId) — enforced DB UNIQUE
/// - Immutable after creation (append-only)
/// - No attribution → No commission. This is the gateway entity.
/// - AffiliateAccount must be Active at attribution time (enforced by service)
/// </summary>
public class AffiliateAttribution : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }

    /// <summary>
    /// FK → Orders.Id. UNIQUE per tenant.
    /// </summary>
    public Guid OrderId { get; private set; }

    /// <summary>
    /// FK → AffiliateAccounts.Id.
    /// </summary>
    public Guid AffiliateAccountId { get; private set; }

    /// <summary>
    /// Referral code snapshot at attribution time.
    /// </summary>
    public string ReferralCode { get; private set; } = string.Empty;

    /// <summary>
    /// How the attribution was established.
    /// </summary>
    public AttributionSource Source { get; private set; }

    /// <summary>
    /// UTC timestamp of attribution.
    /// </summary>
    public DateTime AttributedAtUtc { get; private set; }

    /// <summary>
    /// AF-15: The promotion link that generated this attribution.
    /// NULL for legacy attributions using v1 (AffiliateAccountId-only) flow.
    /// </summary>
    public Guid? PromotionLinkId { get; private set; }

    private AffiliateAttribution() { }

    public static AffiliateAttribution Create(
        Guid tenantId, Guid orderId, Guid affiliateAccountId,
        string referralCode, AttributionSource source)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("TenantId required", nameof(tenantId));
        if (orderId == Guid.Empty) throw new ArgumentException("OrderId required", nameof(orderId));
        if (affiliateAccountId == Guid.Empty) throw new ArgumentException("AffiliateAccountId required", nameof(affiliateAccountId));
        if (string.IsNullOrWhiteSpace(referralCode)) throw new ArgumentException("ReferralCode required", nameof(referralCode));

        return new AffiliateAttribution
        {
            TenantId = tenantId,
            OrderId = orderId,
            AffiliateAccountId = affiliateAccountId,
            ReferralCode = referralCode,
            Source = source,
            PromotionLinkId = null,
            AttributedAtUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// AF-15: Creates attribution with PromotionLinkId for v2 flow.
    /// </summary>
    public static AffiliateAttribution CreateV2(
        Guid tenantId, Guid orderId, Guid affiliateAccountId,
        string referralCode, AttributionSource source,
        Guid promotionLinkId)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("TenantId required", nameof(tenantId));
        if (orderId == Guid.Empty) throw new ArgumentException("OrderId required", nameof(orderId));
        if (affiliateAccountId == Guid.Empty) throw new ArgumentException("AffiliateAccountId required", nameof(affiliateAccountId));
        if (string.IsNullOrWhiteSpace(referralCode)) throw new ArgumentException("ReferralCode required", nameof(referralCode));
        if (promotionLinkId == Guid.Empty) throw new ArgumentException("PromotionLinkId required", nameof(promotionLinkId));

        return new AffiliateAttribution
        {
            TenantId = tenantId,
            OrderId = orderId,
            AffiliateAccountId = affiliateAccountId,
            ReferralCode = referralCode,
            Source = source,
            PromotionLinkId = promotionLinkId,
            AttributedAtUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
    }

    // AF-30A FIX D-1: EF Core setter methods REMOVED.
    // AffiliateAttribution is IMMUTABLE after creation (append-only).
    // Public setter methods directly violate the immutability invariant.
    // EF Core materializes entities via reflection on `private set` properties.
}

public enum AttributionSource
{
    ReferralLink = 0,
    CouponCode = 1,
    ManualAdmin = 2
}
