using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace PointOfSale.Infrastructure.MongoDB.Documents.Affiliate;

/// <summary>
/// AF-10B: Public affiliate store directory — MongoDB read model.
///
/// ARCHITECTURE:
/// - Cross-tenant document: allows public, unauthenticated browsing
/// - TenantId is stored for internal resolution ONLY — never exposed in API responses
/// - Updated via AffiliateProductPolicyChangedEvent projection consumer
/// - When affiliateProductCount drops to 0, the document is removed
///
/// INDEXES:
/// - (storeSlug) UNIQUE
/// - (tenantId) for internal resolution
///
/// KILL LIST:
/// - ❌ TenantId never in API responses
/// - ❌ No commission data
/// - ❌ No customer data
/// </summary>
public class PublicAffiliateStoreDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; }

    /// <summary>
    /// Internal only — used to resolve storeSlug → tenantId during registration.
    /// NEVER returned in public API responses.
    /// </summary>
    [BsonElement("tenantId")]
    [BsonRepresentation(BsonType.String)]
    public Guid TenantId { get; set; }

    /// <summary>
    /// Public-facing store identifier. Derived from Tenant.Slug.
    /// Used in URLs: /affiliate/register/nike-egypt
    /// </summary>
    [BsonElement("storeSlug")]
    public string StoreSlug { get; set; } = string.Empty;

    /// <summary>
    /// Store display name. Derived from Tenant.Name.
    /// </summary>
    [BsonElement("storeName")]
    public string StoreName { get; set; } = string.Empty;

    /// <summary>
    /// Optional store logo URL.
    /// </summary>
    [BsonElement("logoUrl")]
    public string? LogoUrl { get; set; }

    /// <summary>
    /// Number of affiliate-enabled products in this store.
    /// When this reaches 0, the document is removed.
    /// </summary>
    [BsonElement("affiliateProductCount")]
    public int AffiliateProductCount { get; set; }

    /// <summary>
    /// Last time this document was updated.
    /// </summary>
    [BsonElement("updatedAtUtc")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime UpdatedAtUtc { get; set; }
}
