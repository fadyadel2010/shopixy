namespace PointOfSale.API.DTOs.Storefront;

/// <summary>
/// SEO Metadata DTO for product/category pages.
/// Contains all meta tags needed for search engine optimization.
/// 
/// Design:
/// - Frontend-ready (camelCase serialization)
/// - Supports fallback logic (product → category → tenant defaults)
/// - Includes Open Graph and Twitter meta tags
/// </summary>
public class SeoMetadataDto
{
    /// <summary>
    /// Page title for <title> tag and og:title
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Meta description for <meta name="description"> and og:description
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Canonical URL for <link rel="canonical"> and og:url
    /// </summary>
    public string CanonicalUrl { get; set; } = string.Empty;

    /// <summary>
    /// Open Graph image URL for og:image
    /// </summary>
    public string? OgImage { get; set; }

    /// <summary>
    /// Open Graph type (product, category, website)
    /// </summary>
    public string OgType { get; set; } = "website";

    /// <summary>
    /// Keywords for <meta name="keywords"> (optional, less important for modern SEO)
    /// </summary>
    public string? Keywords { get; set; }

    /// <summary>
    /// Whether search engines should index this page
    /// </summary>
    public bool Index { get; set; } = true;

    /// <summary>
    /// Whether search engines should follow links on this page
    /// </summary>
    public bool Follow { get; set; } = true;

    /// <summary>
    /// The type of resource (product, category, cms)
    /// </summary>
    public string ResourceType { get; set; } = string.Empty;

    /// <summary>
    /// The resource ID (ProductId or CategoryId)
    /// </summary>
    public Guid? ResourceId { get; set; }

    /// <summary>
    /// Source of the metadata (cached, product, category, fallback)
    /// For debugging purposes
    /// </summary>
    public string? Source { get; set; }
    public string RequestedLanguage { get; set; } = "en";
    public string Language { get; set; } = "en";
    public bool FallbackApplied { get; set; }
    public Dictionary<string, string> AlternateUrls { get; set; } = new();
}

/// <summary>
/// Breadcrumb item for navigation trail.
/// </summary>
public class BreadcrumbItemDto
{
    /// <summary>
    /// Display name for the breadcrumb
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Arabic display name (optional)
    /// </summary>
    public string? NameAr { get; set; }

    /// <summary>
    /// URL slug for the breadcrumb link
    /// </summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Full URL path for the breadcrumb
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Type of resource (home, category, product)
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Whether this is the current/active page
    /// </summary>
    public bool IsCurrent { get; set; }
}

/// <summary>
/// Breadcrumb trail response.
/// </summary>
public class BreadcrumbsDto
{
    /// <summary>
    /// List of breadcrumb items from root to current page
    /// </summary>
    public List<BreadcrumbItemDto> Items { get; set; } = new();

    /// <summary>
    /// JSON-LD structured data for breadcrumbs (SEO)
    /// </summary>
    public string? JsonLd { get; set; }
}

/// <summary>
/// Slug resolution result.
/// Returns the type and ID of the resolved resource.
/// </summary>
public class SlugResolutionDto
{
    /// <summary>
    /// Type of the resolved resource (product, category)
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// ID of the resolved resource
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The canonical slug for the resource (may differ from input if redirected)
    /// </summary>
    public string CanonicalSlug { get; set; } = string.Empty;

    /// <summary>
    /// Display name of the resource
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Arabic display name (optional)
    /// </summary>
    public string? NameAr { get; set; }

    /// <summary>
    /// Whether a redirect is needed (input slug != canonical slug)
    /// </summary>
    public bool RequiresRedirect { get; set; }
}

/// <summary>
/// Sitemap URL entry.
/// </summary>
public class SitemapUrlDto
{
    /// <summary>
    /// Full URL of the page
    /// </summary>
    public string Loc { get; set; } = string.Empty;

    /// <summary>
    /// Last modification date (ISO 8601)
    /// </summary>
    public string? Lastmod { get; set; }

    /// <summary>
    /// Change frequency (always, hourly, daily, weekly, monthly, yearly, never)
    /// </summary>
    public string ChangeFreq { get; set; } = "weekly";

    /// <summary>
    /// Priority (0.0 to 1.0)
    /// </summary>
    public decimal Priority { get; set; } = 0.5m;
    public Dictionary<string, string> Alternates { get; set; } = new();
}

/// <summary>
/// Sitemap response containing all URLs for a tenant.
/// </summary>
public class SitemapDto
{
    /// <summary>
    /// List of sitemap URLs
    /// </summary>
    public List<SitemapUrlDto> Urls { get; set; } = new();

    /// <summary>
    /// Total number of URLs in the sitemap
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// When the sitemap was generated (ISO 8601)
    /// </summary>
    public string GeneratedAtUtc { get; set; } = string.Empty;
}

/// <summary>
/// Query parameters for SEO metadata endpoint.
/// </summary>
public class SeoMetadataQuery
{
    /// <summary>
    /// Type of resource (product, category, cms). Required.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// SEO-friendly URL slug. Required.
    /// </summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Base URL for canonical URL generation (optional).
    /// </summary>
    public string? BaseUrl { get; set; }
    public string? Lang { get; set; }

    /// <summary>
    /// Validates the query parameters.
    /// Returns error code and message if invalid, null if valid.
    /// </summary>
    public (string? ErrorCode, string? ErrorMessage) Validate()
    {
        var validTypes = new[] { "product", "category", "cms" };
        
        if (string.IsNullOrWhiteSpace(Type))
        {
            return ("INVALID_SEO_QUERY", "Type parameter is required");
        }

        if (!validTypes.Contains(Type.ToLowerInvariant()))
        {
            return ("INVALID_SEO_QUERY", "Type must be 'product', 'category', or 'cms'");
        }

        if (string.IsNullOrWhiteSpace(Slug))
        {
            return ("INVALID_SEO_QUERY", "Slug parameter is required");
        }

        return (null, null);
    }
}

/// <summary>
/// Query parameters for breadcrumbs endpoint.
/// </summary>
public class BreadcrumbsQuery
{
    /// <summary>
    /// Type of resource (product, category). Required.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// SEO-friendly URL slug. Required.
    /// </summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Base URL for breadcrumb URL generation (optional).
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Validates the query parameters.
    /// </summary>
    public (string? ErrorCode, string? ErrorMessage) Validate()
    {
        var validTypes = new[] { "product", "category" };
        
        if (string.IsNullOrWhiteSpace(Type))
        {
            return ("INVALID_BREADCRUMBS_QUERY", "Type parameter is required");
        }

        if (!validTypes.Contains(Type.ToLowerInvariant()))
        {
            return ("INVALID_BREADCRUMBS_QUERY", "Type must be 'product' or 'category'");
        }

        if (string.IsNullOrWhiteSpace(Slug))
        {
            return ("INVALID_BREADCRUMBS_QUERY", "Slug parameter is required");
        }

        return (null, null);
    }
}
