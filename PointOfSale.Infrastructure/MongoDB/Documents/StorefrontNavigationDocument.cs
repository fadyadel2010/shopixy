using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace PointOfSale.Infrastructure.MongoDB.Documents;

/// <summary>
/// MongoDB Read Model for Storefront Navigation.
/// Maps to 'storefront_navigation' collection.
/// 
/// Phase 9.11: Footer & Navigation Engine
/// 
/// Single document per tenant containing all navigation links.
/// 
/// Indexes (MANDATORY):
/// - (tenantId) → UNIQUE
/// </summary>
public class StorefrontNavigationDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    /// <summary>
    /// Tenant ID for multi-tenant isolation.
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid TenantId { get; set; }

    /// <summary>
    /// Tenant slug for easier debugging/queries.
    /// </summary>
    public string TenantSlug { get; set; } = string.Empty;

    /// <summary>
    /// Header navigation links.
    /// </summary>
    public NavigationMenu Header { get; set; } = new();

    /// <summary>
    /// Footer navigation links.
    /// </summary>
    public NavigationMenu Footer { get; set; } = new();

    /// <summary>
    /// When the navigation was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Schema version for document structure migrations.
    /// </summary>
    public int SchemaVersion { get; set; } = 1;

    [BsonRepresentation(BsonType.String)]
    public Guid? LastEventId { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? LastEventOccurredAtUtc { get; set; }

    public int LastEventVersion { get; set; }
}

/// <summary>
/// Navigation menu containing links.
/// </summary>
public class NavigationMenu
{
    /// <summary>
    /// Menu links.
    /// </summary>
    public List<NavigationLink> Links { get; set; } = new();
}

/// <summary>
/// Navigation link item.
/// </summary>
public class NavigationLink
{
    /// <summary>
    /// Display label for the link.
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Arabic display label (optional).
    /// </summary>
    public string? LabelAr { get; set; }

    /// <summary>
    /// Link type: "internal" (storefront page), "category", "cms", "external".
    /// </summary>
    public string Type { get; set; } = "internal";

    /// <summary>
    /// Target path or slug.
    /// For "internal": absolute path (e.g., "/", "/products")
    /// For "category": category slug
    /// For "cms": CMS page slug (e.g., "contact", "about")
    /// For "external": full URL
    /// </summary>
    public string? Path { get; set; }

    /// <summary>
    /// Slug for category or CMS page links.
    /// </summary>
    public string? Slug { get; set; }

    /// <summary>
    /// Whether to open in new tab (for external links).
    /// </summary>
    public bool OpenInNewTab { get; set; } = false;

    /// <summary>
    /// Display order within the menu.
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Whether the link is enabled/visible.
    /// </summary>
    public bool Enabled { get; set; } = true;
}
