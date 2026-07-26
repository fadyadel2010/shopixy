using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace PointOfSale.Infrastructure.MongoDB.Documents.Affiliate;

// ═══════════════════════════════════════════════════════════════════
// AF-8: Click Tracking & Analytics Documents
//
// RULES:
// - Append-only: clicks are never updated (except conversion bridge)
// - No PII: ipHash is SHA-256, not raw IP
// - No commission data: analytics only
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// AF-8: Individual affiliate link click record.
///
/// Collection: affiliate_clicks
///
/// Indexes:
/// - (TenantId, AffiliateAccountId, ClickedAtUtc) — affiliate click timeline
/// - (TenantId, ProductId) — product click aggregation
/// - (TenantId, SessionId) — session lookup for conversion bridge
///
/// Idempotency: (TenantId, SessionId, ProductId) unique compound index
/// prevents duplicate clicks in same session for same product.
/// </summary>
public class AffiliateClickDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; }

    [BsonElement("tenantId")]
    [BsonRepresentation(BsonType.String)]
    public Guid TenantId { get; set; }

    [BsonElement("affiliateAccountId")]
    [BsonRepresentation(BsonType.String)]
    public Guid AffiliateAccountId { get; set; }

    [BsonElement("referralCode")]
    public string ReferralCode { get; set; } = string.Empty;

    [BsonElement("productId")]
    [BsonRepresentation(BsonType.String)]
    public Guid? ProductId { get; set; }

    [BsonElement("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [BsonElement("userAgent")]
    public string? UserAgent { get; set; }

    /// <summary>
    /// SHA-256 hash of client IP — never store raw IP (PII compliance).
    /// </summary>
    [BsonElement("ipHash")]
    public string? IpHash { get; set; }

    [BsonElement("clickedAtUtc")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime ClickedAtUtc { get; set; }

    // ════════════ Conversion Bridge Fields ════════════

    /// <summary>
    /// Set by AffiliateClickConversionConsumer when an order is attributed.
    /// Null = not converted.
    /// </summary>
    [BsonElement("orderId")]
    [BsonRepresentation(BsonType.String)]
    public Guid? OrderId { get; set; }

    [BsonElement("convertedAtUtc")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? ConvertedAtUtc { get; set; }
}

/// <summary>
/// AF-8: Aggregated analytics summary per affiliate.
///
/// Collection: affiliate_analytics_summaries
///
/// Key: (TenantId, AffiliateAccountId) — unique compound index
///
/// Updated by:
/// - AffiliateLinkClickedEvent → increments TotalClicks, updates TopProducts
/// - AffiliateAttributedEvent  → increments TotalConversions, recalculates rate
/// </summary>
public class AffiliateAnalyticsSummaryDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; }

    [BsonElement("tenantId")]
    [BsonRepresentation(BsonType.String)]
    public Guid TenantId { get; set; }

    [BsonElement("affiliateAccountId")]
    [BsonRepresentation(BsonType.String)]
    public Guid AffiliateAccountId { get; set; }

    [BsonElement("totalClicks")]
    public long TotalClicks { get; set; }

    [BsonElement("totalConversions")]
    public long TotalConversions { get; set; }

    /// <summary>
    /// Computed: TotalConversions / TotalClicks (0 if no clicks).
    /// </summary>
    [BsonElement("conversionRate")]
    public double ConversionRate { get; set; }

    [BsonElement("topProducts")]
    public List<TopProductSubDocument> TopProducts { get; set; } = new();

    [BsonElement("lastClickAtUtc")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? LastClickAtUtc { get; set; }

    [BsonElement("updatedAtUtc")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime UpdatedAtUtc { get; set; }
}

public class TopProductSubDocument
{
    [BsonElement("productId")]
    [BsonRepresentation(BsonType.String)]
    public Guid ProductId { get; set; }

    [BsonElement("clicks")]
    public long Clicks { get; set; }

    [BsonElement("conversions")]
    public long Conversions { get; set; }
}
