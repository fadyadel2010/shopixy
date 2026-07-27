using Nest;
using PointOfSale.Infrastructure.ReadModels.Evolution;

namespace PointOfSale.Infrastructure.Elasticsearch.Documents;

/// <summary>
/// Elasticsearch document for category search.
/// 
/// Design Principles:
/// - Flat structure (no nested objects)
/// - Tenant-isolated by TenantId
/// - Optimized for search operations
/// - NOT the source of truth (PostgreSQL is)
/// 
/// Document ID Strategy:
/// - Uses composite key: {TenantId}_{CategoryId}
/// - Ensures tenant isolation and idempotent indexing
/// </summary>
[ElasticsearchType(RelationName = "category")]
public class CategorySearchDocument : IReadModelEvolutionDocument
{
    public const int CurrentReadModelSchemaVersion = ReadModelEvolutionRegistry.StorefrontCategorySearchSchemaVersion;
    public const int CurrentProjectionVersion = ReadModelEvolutionRegistry.StorefrontCategorySearchProjectionVersion;
    public const int CurrentIndexVersion = ReadModelEvolutionRegistry.StorefrontCategorySearchIndexVersion;

    [Keyword]
    public string ReadModelName { get; set; } = ReadModelEvolutionRegistry.StorefrontCategory;

    [Number(NumberType.Integer)]
    public int ReadModelSchemaVersion { get; set; } = CurrentReadModelSchemaVersion;

    [Number(NumberType.Integer)]
    public int ProjectionVersion { get; set; } = CurrentProjectionVersion;

    [Date(Format = "strict_date_optional_time||epoch_millis")]
    public DateTime? ProjectionConvergedAtUtc { get; set; }

    /// <summary>
    /// Tenant that owns this category.
    /// CRITICAL: All queries MUST filter by TenantId.
    /// </summary>
    [Keyword]
    public Guid TenantId { get; set; }

    /// <summary>
    /// Category ID from PostgreSQL (source of truth).
    /// </summary>
    [Keyword]
    public Guid CategoryId { get; set; }

    /// <summary>
    /// Entity type for unified search.
    /// </summary>
    [Keyword]
    public string EntityType { get; set; } = "category";

    /// <summary>
    /// Category name for display and search.
    /// </summary>
    [Text(Analyzer = "standard")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Category name in Arabic (optional).
    /// </summary>
    [Text(Analyzer = "standard")]
    public string? NameAr { get; set; }

    /// <summary>
    /// Category description.
    /// </summary>
    [Text(Analyzer = "standard")]
    public string? Description { get; set; }

    /// <summary>
    /// SEO-friendly URL slug.
    /// </summary>
    [Keyword]
    public string? Slug { get; set; }

    /// <summary>
    /// Parent category ID (null for root categories).
    /// </summary>
    [Keyword]
    public Guid? ParentId { get; set; }

    /// <summary>
    /// Whether the category is active in the system.
    /// </summary>
    [Boolean]
    public bool IsActive { get; set; }

    /// <summary>
    /// Whether the category is visible on e-commerce storefront.
    /// </summary>
    [Boolean]
    public bool IsOnlineVisible { get; set; }

    /// <summary>
    /// Aggregated searchable text (name + nameAr + description).
    /// </summary>
    [Text(Analyzer = "standard")]
    public string? SearchText { get; set; }

    /// <summary>
    /// When the category was created in PostgreSQL.
    /// </summary>
    [Date(Format = "strict_date_optional_time||epoch_millis")]
    public DateTime CreatedAtUtc { get; set; }

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
    /// Uses composite key: {TenantId}_{CategoryId} for tenant isolation and idempotency.
    /// </summary>
    public string GetDocumentId() => $"{TenantId}_{CategoryId}";

    /// <summary>
    /// Builds aggregated search text from all searchable fields.
    /// </summary>
    public void BuildSearchText()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(Name)) parts.Add(Name);
        if (!string.IsNullOrWhiteSpace(NameAr)) parts.Add(NameAr);
        if (!string.IsNullOrWhiteSpace(Description)) parts.Add(Description);
        SearchText = string.Join(" ", parts);
    }
}
