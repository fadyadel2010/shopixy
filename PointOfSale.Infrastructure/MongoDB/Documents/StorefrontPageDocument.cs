using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using PointOfSale.Core.Enums;

namespace PointOfSale.Infrastructure.MongoDB.Documents;

/// <summary>
/// MongoDB Read Model for Static CMS Pages.
/// Maps to 'storefront_pages' collection.
/// 
/// Phase 9.11: Static CMS Pages Engine
/// 
/// This document stores LIVE content only.
/// Draft content is NEVER projected to MongoDB.
/// 
/// Indexes (MANDATORY):
/// - (tenantId, slug) → UNIQUE
/// - (tenantId)
/// </summary>
public class StorefrontPageDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    /// <summary>
    /// Tenant ID for multi-tenant isolation.
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid TenantId { get; set; }

    [BsonRepresentation(BsonType.String)]
    public Guid PageGroupId { get; set; }

    /// <summary>
    /// Page slug (URL identifier).
    /// Examples: "contact", "about", "privacy", "terms"
    /// </summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Page title for display.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    public CmsPageContentType ContentType { get; set; } = CmsPageContentType.RichText;

    public string Content { get; set; } = string.Empty;

    public Dictionary<string, StorefrontPageTranslationDocument> Translations { get; set; } = new();

    /// <summary>
    /// SEO metadata for the page.
    /// </summary>
    public CmsPageSeo Seo { get; set; } = new();

    /// <summary>
    /// Page sections (content blocks).
    /// Same structure as storefront config sections.
    /// </summary>
    public List<CmsPageSection> Sections { get; set; } = new();

    /// <summary>
    /// Whether the page is published and visible.
    /// </summary>
    public bool IsPublished { get; set; } = true;

    /// <summary>
    /// Display order for navigation menus.
    /// </summary>
    public int DisplayOrder { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? PublishedAt { get; set; }

    /// <summary>
    /// When the page was last updated (published).
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
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

public sealed class StorefrontPageTranslationDocument
{
    public string Title { get; set; } = string.Empty;
    public CmsPageContentType ContentType { get; set; } = CmsPageContentType.RichText;
    public string Content { get; set; } = string.Empty;
    public int SchemaVersion { get; set; } = 1;
    public CmsPageSeo Seo { get; set; } = new();
}

/// <summary>
/// SEO metadata for CMS pages.
/// </summary>
public class CmsPageSeo
{
    /// <summary>
    /// Page meta title (for browser tab and search results).
    /// </summary>
    public string MetaTitle { get; set; } = string.Empty;

    /// <summary>
    /// Page meta description (for search results).
    /// </summary>
    public string MetaDescription { get; set; } = string.Empty;

    public string? MetaKeywords { get; set; }

    /// <summary>
    /// Optional Open Graph image URL.
    /// </summary>
    public string? OpenGraphImageUrl { get; set; }

    /// <summary>
    /// Whether search engines should index this page.
    /// </summary>
    public bool NoIndex { get; set; } = false;
}

/// <summary>
/// CMS page content section.
/// </summary>
public class CmsPageSection
{
    /// <summary>
    /// Unique section identifier.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Section type (e.g., "text-block", "contact-form", "image", "rich-text").
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Whether the section is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Display order within the page.
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Section-specific settings (varies by type).
    /// </summary>
    public Dictionary<string, object> Settings { get; set; } = new();
}
