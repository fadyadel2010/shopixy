using PointOfSale.API.DTOs.Storefront;

namespace PointOfSale.API.Services.Storefront;

/// <summary>
/// Service interface for SEO &amp; Metadata operations.
/// 
/// ARCHITECTURAL BOUNDARY (Phase 3.0 - SEO APIs):
/// - This service is READ-ONLY - no writes, no events, no sync logic
/// - Has NO dependency on Inventory, Pricing, or Stock
/// - Makes NO cross-service calls
/// - Is fully self-contained within the Read Side
/// - Uses ONLY read-only repository interfaces
/// 
/// Read Flow (strict order for all operations):
/// 1. Build Redis cache key
/// 2. Try Redis cache FIRST (cache-first mindset)
/// 3. Cache HIT → Return cached data
/// 4. Cache MISS → Query MongoDB
/// 5. Build SEO data with fallback logic
/// 6. Populate Redis cache (best-effort, fire-and-forget)
/// 7. Return mapped DTOs
/// </summary>
public interface ISeoService
{
    /// <summary>
    /// Gets SEO metadata for a product or category.
    /// Uses fallback logic: product → category → tenant defaults
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="type">Resource type (product, category)</param>
    /// <param name="slug">SEO-friendly URL slug</param>
    /// <param name="baseUrl">Base URL for canonical URL generation</param>
    /// <param name="correlationId">Correlation ID for logging</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>SEO metadata DTO or null if resource not found</returns>
    Task<SeoMetadataDto?> GetSeoMetadataAsync(
        Guid tenantId,
        string type,
        string slug,
        string? baseUrl,
        string correlationId,
        CancellationToken cancellationToken = default,
        string requestedLanguage = "en");

    /// <summary>
    /// Gets breadcrumb trail for a product or category.
    /// Builds navigation path from root to current resource.
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="type">Resource type (product, category)</param>
    /// <param name="slug">SEO-friendly URL slug</param>
    /// <param name="baseUrl">Base URL for breadcrumb URL generation</param>
    /// <param name="correlationId">Correlation ID for logging</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Breadcrumbs DTO or null if resource not found</returns>
    Task<BreadcrumbsDto?> GetBreadcrumbsAsync(
        Guid tenantId,
        string type,
        string slug,
        string? baseUrl,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a slug to its resource type and ID.
    /// Tries product first, then category.
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="slug">SEO-friendly URL slug</param>
    /// <param name="correlationId">Correlation ID for logging</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Slug resolution DTO or null if not found</returns>
    Task<SlugResolutionDto?> ResolveSlugAsync(
        Guid tenantId,
        string slug,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates sitemap data for a tenant.
    /// Includes all visible products and categories.
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="baseUrl">Base URL for sitemap URLs</param>
    /// <param name="correlationId">Correlation ID for logging</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Sitemap DTO</returns>
    Task<SitemapDto> GetSitemapAsync(
        Guid tenantId,
        string baseUrl,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates robots.txt content for a tenant.
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="baseUrl">Base URL for sitemap URL</param>
    /// <param name="correlationId">Correlation ID for logging</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Robots.txt content as string</returns>
    Task<string> GetRobotsTxtAsync(
        Guid tenantId,
        string baseUrl,
        string correlationId,
        CancellationToken cancellationToken = default);
}
