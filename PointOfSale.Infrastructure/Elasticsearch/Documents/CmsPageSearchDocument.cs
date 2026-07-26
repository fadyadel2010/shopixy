using Nest;

namespace PointOfSale.Infrastructure.Elasticsearch.Documents;

/// <summary>
/// Elasticsearch document for CMS page search.
/// 
/// Design Principles:
/// - Flat structure (no nested objects)
/// - Tenant-isolated by TenantId
/// - Optimized for search operations
/// - NOT the source of truth (PostgreSQL is)
/// 
/// Document ID Strategy:
/// - Uses composite key: {TenantId}_{Slug}
/// - Ensures tenant isolation and idempotent indexing
/// </summary>
[ElasticsearchType(RelationName = "cmspage")]
public class CmsPageSearchDocument
{
    /// <summary>
    /// Tenant that owns this CMS page.
    /// CRITICAL: All queries MUST filter by TenantId.
    /// </summary>
    [Keyword]
    public Guid TenantId { get; set; }

    /// <summary>
    /// Page ID (StoreConfigId + Slug combination makes unique).
    /// </summary>
    [Keyword]
    public Guid PageId { get; set; }

    /// <summary>
    /// Entity type for unified search.
    /// </summary>
    [Keyword]
    public string EntityType { get; set; } = "cms";

    /// <summary>
    /// Page slug (e.g., "contact", "about", "privacy").
    /// </summary>
    [Keyword]
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Page title for display and search.
    /// </summary>
    [Text(Analyzer = "standard")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Page title in Arabic (optional).
    /// </summary>
    [Text(Analyzer = "standard")]
    public string? NameAr { get; set; }

    /// <summary>
    /// Page content/description for search.
    /// </summary>
    [Text(Analyzer = "standard")]
    public string? Description { get; set; }

    /// <summary>
    /// Meta description for SEO.
    /// </summary>
    [Text(Analyzer = "standard")]
    public string? MetaDescription { get; set; }

    /// <summary>
    /// Whether the page is active/published.
    /// </summary>
    [Boolean]
    public bool IsActive { get; set; }

    /// <summary>
    /// Whether the page is visible on storefront.
    /// </summary>
    [Boolean]
    public bool IsOnlineVisible { get; set; }

    /// <summary>
    /// Aggregated searchable text.
    /// </summary>
    [Text(Analyzer = "standard")]
    public string? SearchText { get; set; }

    /// <summary>
    /// Published version number.
    /// </summary>
    [Number(NumberType.Integer)]
    public int Version { get; set; }

    /// <summary>
    /// When the page was published.
    /// </summary>
    [Date(Format = "strict_date_optional_time||epoch_millis")]
    public DateTime? PublishedAtUtc { get; set; }

    /// <summary>
    /// When this document was last indexed.
    /// </summary>
    [Date(Format = "strict_date_optional_time||epoch_millis")]
    public DateTime IndexedAtUtc { get; set; }

    /// <summary>
    /// The EventId that triggered this indexing (for debugging/tracing).
    /// </summary>
    [Keyword]
    public Guid? LastEventId { get; set; }

    /// <summary>
    /// Generates the document ID for Elasticsearch.
    /// Uses composite key: {TenantId}_{Slug} for tenant isolation and idempotency.
    /// </summary>
    public string GetDocumentId() => $"{TenantId}_{Slug}";

    /// <summary>
    /// Builds aggregated search text from all searchable fields.
    /// </summary>
    public void BuildSearchText()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(Name)) parts.Add(Name);
        if (!string.IsNullOrWhiteSpace(NameAr)) parts.Add(NameAr);
        if (!string.IsNullOrWhiteSpace(Description)) parts.Add(Description);
        if (!string.IsNullOrWhiteSpace(MetaDescription)) parts.Add(MetaDescription);
        SearchText = string.Join(" ", parts);
    }
}
