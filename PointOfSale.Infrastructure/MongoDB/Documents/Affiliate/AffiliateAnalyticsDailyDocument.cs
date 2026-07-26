using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace PointOfSale.Infrastructure.MongoDB.Documents.Affiliate;

// ═══════════════════════════════════════════════════════════════════
// AF-24: Daily Analytics Projection Documents
//
// RULES:
// - One document per (AffiliateAccountId, TenantId, Date)
// - Updated by event-driven consumers ONLY
// - Never written from controllers
// - Idempotent via ProcessedEventIds
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// AF-24: Daily analytics aggregation per affiliate per store.
///
/// Collection: affiliate_analytics_daily
///
/// Indexes:
///   - (AffiliateAccountId, Date) — affiliate trend queries
///   - (AffiliateAccountId, TenantId, Date) — unique compound key
///
/// Updated by:
///   - AffiliateLinkClickedEvent → $inc Clicks
///   - AffiliateCommissionEarnedEvent → $inc Orders, Earnings, per-product earnings
/// </summary>
public class AffiliateAnalyticsDailyDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string Id { get; set; } = string.Empty; // Format: "{AffiliateAccountId}:{TenantId}:{yyyy-MM-dd}"

    [BsonElement("affiliateAccountId")]
    [BsonRepresentation(BsonType.String)]
    public Guid AffiliateAccountId { get; set; }

    [BsonElement("tenantId")]
    [BsonRepresentation(BsonType.String)]
    public Guid TenantId { get; set; }

    /// <summary>
    /// Date portion only (UTC), e.g. "2026-02-25"
    /// </summary>
    [BsonElement("date")]
    public string Date { get; set; } = string.Empty;

    [BsonElement("clicks")]
    public long Clicks { get; set; }

    [BsonElement("orders")]
    public long Orders { get; set; }

    [BsonElement("earnings")]
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal Earnings { get; set; }

    /// <summary>
    /// Per-product earnings breakdown for "top products" queries.
    /// Kept small: only products with earnings > 0.
    /// </summary>
    [BsonElement("productEarnings")]
    public List<DailyProductEarning> ProductEarnings { get; set; } = new();

    [BsonElement("processedEventIds")]
    public List<string> ProcessedEventIds { get; set; } = new();

    [BsonElement("updatedAtUtc")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime UpdatedAtUtc { get; set; }
}

public class DailyProductEarning
{
    [BsonElement("productId")]
    [BsonRepresentation(BsonType.String)]
    public Guid ProductId { get; set; }

    [BsonElement("productName")]
    public string ProductName { get; set; } = string.Empty;

    [BsonElement("earnings")]
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal Earnings { get; set; }

    [BsonElement("orders")]
    public int Orders { get; set; }

    /// <summary>
    /// Tracks event IDs that have been applied to this product to ensure idempotency.
    /// Prevents duplicate earnings if product updates fail after document-level event is marked processed.
    /// </summary>
    [BsonElement("processedEventIds")]
    [BsonIgnoreIfNull]
    public List<string>? ProcessedEventIds { get; set; }
}

public class AffiliateProjectionEventReceiptDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty;

    [BsonElement("consumerName")]
    public string ConsumerName { get; set; } = string.Empty;

    [BsonElement("documentId")]
    public string DocumentId { get; set; } = string.Empty;

    [BsonElement("eventId")]
    public string EventId { get; set; } = string.Empty;

    [BsonElement("claimedUntilUtc")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? ClaimedUntilUtc { get; set; }

    [BsonElement("processedAtUtc")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? ProcessedAtUtc { get; set; }

    [BsonElement("attemptCount")]
    public int AttemptCount { get; set; }

    [BsonElement("lastError")]
    [BsonIgnoreIfNull]
    public string? LastError { get; set; }

    [BsonElement("createdAtUtc")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAtUtc { get; set; }

    [BsonElement("updatedAtUtc")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime UpdatedAtUtc { get; set; }
}
