using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Services.Catalog;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MultiTenancy;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Storefront Unified Search Controller - READ-ONLY search across all entity types.
/// 
/// Route: /api/storefront/search
/// 
/// CRITICAL RULES:
/// - READ-ONLY: This controller NEVER writes to Elasticsearch
/// - Reads Elasticsearch candidates, then reapplies current SQL public visibility
/// - NO MongoDB $regex queries (uses proper Elasticsearch full-text search)
/// - All queries are tenant-scoped
/// 
/// Search Behavior:
/// - Tenant isolation enforced on EVERY query
/// - Only returns isActive=true AND isOnlineVisible=true items
/// - Supports partial matching, fuzzy search, and multi-language (en/ar)
/// - Ranking priority: Products > Categories > CMS Pages
/// </summary>
[ApiController]
[Route("api/storefront/search")]
[AllowAnonymous]
public class StorefrontSearchController : ControllerBase
{
    private readonly IUnifiedSearchIndexService _searchService;
    private readonly ApplicationDbContext _dbContext;
    private readonly CommerceReliabilityTelemetry _reliabilityTelemetry;
    private readonly ILogger<StorefrontSearchController> _logger;
    private const string TenantIdHeader = "X-Tenant-Id";
    private const int SqlAuthorityCandidateWindow = 10000;

    public StorefrontSearchController(
        IUnifiedSearchIndexService searchService,
        ApplicationDbContext dbContext,
        CommerceReliabilityTelemetry reliabilityTelemetry,
        ILogger<StorefrontSearchController> logger)
    {
        _searchService = searchService;
        _dbContext = dbContext;
        _reliabilityTelemetry = reliabilityTelemetry;
        _logger = logger;
    }

    /// <summary>
    /// Performs unified search across Products, Categories, and CMS Pages.
    /// 
    /// Reads Elasticsearch candidates and reapplies current SQL authority before returning.
    /// </summary>
    /// <param name="q">Search query (required, min 2 characters)</param>
    /// <param name="page">Page number (1-indexed, default: 1)</param>
    /// <param name="pageSize">Items per page (default: 20, max: 50)</param>
    /// <param name="type">Optional entity type filter: product, category, cms (comma-separated for multiple)</param>
    /// <param name="lang">Language code for search: en (English), ar (Arabic). Defaults to en.</param>
    /// <returns>Unified search results</returns>
    /// <response code="200">Search results returned successfully</response>
    /// <response code="400">Missing X-Tenant-Id, invalid search query, or invalid pagination</response>
    /// <response code="503">Search service temporarily unavailable</response>
    [HttpGet]
    [ProducesResponseType(typeof(UnifiedSearchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Search(
        [FromQuery] string q,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? type = null,
        [FromQuery] string lang = "en",
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();

        // Step 1: Validate X-Tenant-Id header
        var tenantValidation = ValidateTenantHeader(traceId);
        if (tenantValidation.Error != null)
        {
            return tenantValidation.Error;
        }
        var tenantId = tenantValidation.TenantId!.Value;

        // Step 2: Validate query
        if (string.IsNullOrWhiteSpace(q))
        {
            _logger.LogWarning(
                "Search query is empty | TenantId: {TenantId} | TraceId: {TraceId}",
                tenantId, traceId);
            return BadRequest(new ApiErrorResponse("INVALID_QUERY", "Search query is required", traceId));
        }

        var trimmedQuery = q.Trim();
        if (trimmedQuery.Length < 2)
        {
            _logger.LogWarning(
                "Search query too short | TenantId: {TenantId} | Query: {Query} | TraceId: {TraceId}",
                tenantId, trimmedQuery, traceId);
            return BadRequest(new ApiErrorResponse(
                "QUERY_TOO_SHORT",
                "Search query must be at least 2 characters",
                traceId));
        }

        // Step 3: Validate pagination
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 50) pageSize = 50;

        // Step 4: Parse entity types
        string[]? entityTypes = null;
        if (!string.IsNullOrWhiteSpace(type))
        {
            entityTypes = type.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        // Step 5: Validate and normalize language
        var language = (lang?.ToLowerInvariant()) switch
        {
            "ar" => "ar",
            "arabic" => "ar",
            _ => "en" // Default to English
        };

        _logger.LogInformation(
            "Unified search request | TenantId: {TenantId} | Query: {Query} | Page: {Page} | PageSize: {PageSize} | Types: {Types} | Lang: {Lang} | TraceId: {TraceId}",
            tenantId, trimmedQuery, page, pageSize,
            entityTypes != null ? string.Join(",", entityTypes) : "all",
            language,
            traceId);

        try
        {
            // Step 6: Execute search candidate retrieval
            var result = await _searchService.SearchAsync(
                tenantId,
                trimmedQuery,
                entityTypes,
                page: 1,
                pageSize: SqlAuthorityCandidateWindow,
                language,
                cancellationToken);

            if (!result.IsSuccess)
            {
                _logger.LogError(
                    "Unified search failed | TenantId: {TenantId} | Query: {Query} | Error: {Error} | TraceId: {TraceId}",
                    tenantId, trimmedQuery, result.ErrorMessage, traceId);
                return SearchUnavailableResult(traceId, "unified", "result_failure");
            }

            if (result.TotalCount > SqlAuthorityCandidateWindow)
            {
                _logger.LogWarning(
                    "Unified search result set exceeds SQL authority recomposition window | TenantId: {TenantId} | Query: {Query} | TotalCount: {TotalCount} | Window: {Window} | TraceId: {TraceId}",
                    tenantId,
                    trimmedQuery,
                    result.TotalCount,
                    SqlAuthorityCandidateWindow,
                    traceId);
                return SearchUnavailableResult(traceId, "unified", "authority_window_exceeded");
            }

            var sqlVisibleItems = await FilterSearchItemsBySqlAuthorityAsync(
                tenantId,
                result.Items,
                cancellationToken);

            var totalCount = sqlVisibleItems.Count;
            var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);
            var pageItems = sqlVisibleItems
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            // Step 6: Map to response DTOs
            var items = pageItems.Select(item => new UnifiedSearchItemDto
            {
                EntityId = item.EntityId,
                EntityType = item.EntityType,
                Name = item.Name,
                NameAr = item.NameAr,
                Slug = item.Slug,
                Description = TruncateDescription(item.Description, 150),
                Price = item.EntityType == "product" ? item.Price : null,
                CategoryName = item.CategoryName,
                Score = item.Score
            }).ToList();

            _logger.LogInformation(
                "Unified search success | TenantId: {TenantId} | Query: {Query} | Results: {Count} | TotalCount: {TotalCount} | TimeMs: {TimeMs} | TraceId: {TraceId}",
                tenantId, trimmedQuery, items.Count, result.TotalCount, result.SearchTimeMs, traceId);

            return Ok(new UnifiedSearchResponse
            {
                Query = trimmedQuery,
                Items = items,
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = totalPages,
                SearchTimeMs = result.SearchTimeMs,
                HasNextPage = page < totalPages,
                HasPreviousPage = page > 1
            });
        }
        catch (OperationCanceledException)
        {
            // Client canceled the request (normal for typeahead/search as user types)
            // Do not log as error or record as search failure
            _logger.LogDebug(
                "Unified search canceled | TenantId: {TenantId} | Query: {Query} | TraceId: {TraceId}",
                tenantId, trimmedQuery, traceId);
            throw; // Let ASP.NET Core handle the cancellation
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unified search exception | TenantId: {TenantId} | Query: {Query} | TraceId: {TraceId}",
                tenantId, trimmedQuery, traceId);
            return SearchUnavailableResult(traceId, "unified", "exception");
        }
    }

    /// <summary>
    /// Performs SMART header search (autocomplete/suggestions) for products.
    /// 
    /// CRITICAL DIFFERENCES FROM UNIFIED SEARCH:
    /// - Works from 1 character (edge n-gram)
    /// - Typo tolerance (fuzziness)
    /// - Arabic input support
    /// - Promotion-aware (light boost)
    /// - Variant-aware price display
    /// - Products only (no categories or CMS pages)
    /// - Returns max 5-8 results
    /// </summary>
    /// <param name="q">Search query (min 1 character)</param>
    /// <param name="limit">Max results (default 5, max 8)</param>
    /// <param name="lang">Language code: en or ar (default: en)</param>
    /// <returns>Header search suggestions</returns>
    /// <response code="200">Suggestions returned successfully</response>
    /// <response code="400">Missing X-Tenant-Id or invalid query</response>
    /// <response code="503">Search service temporarily unavailable</response>
    [HttpGet("suggest")]
    [ProducesResponseType(typeof(HeaderSearchSuggestResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Suggest(
        [FromQuery] string q,
        [FromQuery] int limit = 5,
        [FromQuery] string lang = "en",
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();

        // Step 1: Validate X-Tenant-Id header
        var tenantValidation = ValidateTenantHeader(traceId);
        if (tenantValidation.Error != null)
        {
            return tenantValidation.Error;
        }
        var tenantId = tenantValidation.TenantId!.Value;

        // Step 2: Validate query (min 1 character for header search)
        if (string.IsNullOrWhiteSpace(q))
        {
            _logger.LogDebug(
                "Header search query is empty | TenantId: {TenantId} | TraceId: {TraceId}",
                tenantId, traceId);
            // Return empty results gracefully for empty query
            return Ok(new HeaderSearchSuggestResponse
            {
                Query = "",
                Items = new List<HeaderSearchItemDto>(),
                SearchTimeMs = 0
            });
        }

        var trimmedQuery = q.Trim();
        // Header search works from 1 character!
        if (trimmedQuery.Length < 1)
        {
            return Ok(new HeaderSearchSuggestResponse
            {
                Query = trimmedQuery,
                Items = new List<HeaderSearchItemDto>(),
                SearchTimeMs = 0
            });
        }

        // Step 3: Validate and normalize language
        var language = (lang?.ToLowerInvariant()) switch
        {
            "ar" => "ar",
            "arabic" => "ar",
            _ => "en"
        };

        // Clamp limit
        limit = Math.Clamp(limit, 1, 8);

        _logger.LogInformation(
            "Header search request | TenantId: {TenantId} | Query: {Query} | Limit: {Limit} | Lang: {Lang} | TraceId: {TraceId}",
            tenantId, trimmedQuery, limit, language, traceId);

        try
        {
            // Step 4: Execute header search candidate retrieval
            var result = await _searchService.HeaderSuggestAsync(
                tenantId,
                trimmedQuery,
                limit,
                language,
                cancellationToken);

            if (!result.IsSuccess)
            {
                _logger.LogError(
                    "Header search failed | TenantId: {TenantId} | Query: {Query} | Error: {Error} | TraceId: {TraceId}",
                    tenantId, trimmedQuery, result.ErrorMessage, traceId);
                return SearchUnavailableResult(traceId, "header", "result_failure");
            }

            var sqlVisibleItems = await RecomposeHeaderSuggestionsBySqlAuthorityAsync(
                tenantId,
                result.Items,
                cancellationToken);

            // Step 5: Map to response DTOs with variant-aware price display
            var isRtl = language == "ar";
            var items = sqlVisibleItems.Select(item => new HeaderSearchItemDto
            {
                ProductId = item.ProductId,
                Name = isRtl && !string.IsNullOrWhiteSpace(item.NameAr) ? item.NameAr : item.Name,
                Slug = item.Slug,
                HasVariants = item.HasVariants,
                // CRITICAL: Variant-aware price display
                DisplayPriceText = item.HasVariants 
                    ? (isRtl ? "متعدد الاختيارات" : "Multiple options")
                    : null, // When null, frontend uses formatted price
                Price = item.HasVariants ? null : item.Price, // Don't send price for variant products
                HasActivePromotion = item.HasActivePromotion,
                PromotionBadge = item.HasActivePromotion 
                    ? (isRtl ? GetArabicPromotionBadge(item.PromotionDiscountValue) : item.PromotionBadge)
                    : null,
                ImageUrl = item.ImageUrl,
                CategoryName = item.CategoryName
            }).ToList();

            _logger.LogInformation(
                "Header search success | TenantId: {TenantId} | Query: {Query} | Results: {Count} | TimeMs: {TimeMs} | TraceId: {TraceId}",
                tenantId, trimmedQuery, items.Count, result.SearchTimeMs, traceId);

            return Ok(new HeaderSearchSuggestResponse
            {
                Query = trimmedQuery,
                Items = items,
                SearchTimeMs = result.SearchTimeMs
            });
        }
        catch (OperationCanceledException)
        {
            // Client canceled the request (normal for typeahead/autocomplete as user types)
            // Do not log as error or record as search failure
            _logger.LogDebug(
                "Header search canceled | TenantId: {TenantId} | Query: {Query} | TraceId: {TraceId}",
                tenantId, trimmedQuery, traceId);
            throw; // Let ASP.NET Core handle the cancellation
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Header search exception | TenantId: {TenantId} | Query: {Query} | TraceId: {TraceId}",
                tenantId, trimmedQuery, traceId);
            return SearchUnavailableResult(traceId, "header", "exception");
        }
    }

    /// <summary>
    /// Builds Arabic promotion badge text.
    /// </summary>
    private static string? GetArabicPromotionBadge(double discountValue)
    {
        if (discountValue <= 0) return null;
        return $"خصم {discountValue:0}%";
    }

    #region Helper Methods

    private (Guid? TenantId, IActionResult? Error) ValidateTenantHeader(string traceId)
    {
        return PointOfSale.API.Helpers.StorefrontTenantContextResolver.ValidateTenant(this, traceId);
    }

    private async Task<List<UnifiedSearchItem>> FilterSearchItemsBySqlAuthorityAsync(
        Guid tenantId,
        IReadOnlyCollection<UnifiedSearchItem> items,
        CancellationToken cancellationToken)
    {
        var tenantIsActive = await _dbContext.Tenants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .AnyAsync(
                tenant => tenant.Id == tenantId &&
                          tenant.IsActive &&
                          tenant.Status == TenantStatus.Active,
                cancellationToken);

        if (!tenantIsActive)
        {
            return new List<UnifiedSearchItem>();
        }

        var cmsItems = items
            .Where(item => string.Equals(item.EntityType, "cms", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var visibleGroups = new HashSet<Guid>();
        var visibleSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var cmsPageGroups = cmsItems
            .Where(item => item.EntityId != Guid.Empty)
            .Select(item => item.EntityId)
            .Distinct()
            .ToList();

        var cmsSlugs = cmsItems
            .Select(item => item.Slug)
            .Where(slug => !string.IsNullOrWhiteSpace(slug))
            .Select(slug => slug!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (cmsPageGroups.Count > 0 || cmsSlugs.Count > 0)
        {
            var visiblePages = await _dbContext.CmsPages
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(page => page.TenantId == tenantId)
                .Where(page => !page.IsDeleted)
                .Where(page => page.IsPublished && !page.IsDraft)
                .Where(page => page.PublishedAt != null)
                .Where(page => cmsPageGroups.Contains(page.PageGroupId) || cmsSlugs.Contains(page.Slug))
                .Select(page => new { page.PageGroupId, page.Slug })
                .ToListAsync(cancellationToken);

            visibleGroups = visiblePages
                .Select(page => page.PageGroupId)
                .ToHashSet();
            visibleSlugs = visiblePages
                .Select(page => page.Slug)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        var visibleItems = new List<UnifiedSearchItem>(items.Count);
        foreach (var item in items)
        {
            if (string.Equals(item.EntityType, "product", StringComparison.OrdinalIgnoreCase))
            {
                var product = await ProductSearchSqlAuthorityOverlay.ApplyAsync(
                    _dbContext,
                    new ProductDocument
                    {
                        TenantId = tenantId,
                        ProductId = item.EntityId
                    },
                    tenantId,
                    cancellationToken);

                if (product is not { IsActive: true, IsOnlineVisible: true, IsSearchable: true })
                {
                    continue;
                }

                item.Name = product.Name;
                item.NameAr = product.NameAr;
                item.Slug = product.Slug;
                item.Description = product.Description;
                var purchasability = ProductStorefrontVisibilityAuthority.Evaluate(product);
                item.Price = product.HasVariants ? null : (double?)purchasability.Price;
                item.CategoryName = product.CategoryName;
                visibleItems.Add(item);
                continue;
            }

            if (string.Equals(item.EntityType, "category", StringComparison.OrdinalIgnoreCase))
            {
                if (await CategoryLineageAuthority.RequireLiveCategoryAsync(_dbContext, tenantId, item.EntityId, cancellationToken) != null)
                {
                    visibleItems.Add(item);
                }

                continue;
            }

            if (string.Equals(item.EntityType, "cms", StringComparison.OrdinalIgnoreCase))
            {
                if (visibleGroups.Contains(item.EntityId) ||
                    (!string.IsNullOrWhiteSpace(item.Slug) && visibleSlugs.Contains(item.Slug)))
                {
                    visibleItems.Add(item);
                }

                continue;
            }
        }

        return visibleItems;
    }

    private async Task<List<HeaderSearchItem>> RecomposeHeaderSuggestionsBySqlAuthorityAsync(
        Guid tenantId,
        IReadOnlyCollection<HeaderSearchItem> items,
        CancellationToken cancellationToken)
    {
        var visible = new List<HeaderSearchItem>(items.Count);
        foreach (var item in items)
        {
            var product = await ProductSearchSqlAuthorityOverlay.ApplyAsync(
                _dbContext,
                new ProductDocument
                {
                    TenantId = tenantId,
                    ProductId = item.ProductId
                },
                tenantId,
                cancellationToken);

            if (product is not { IsActive: true, IsOnlineVisible: true, IsSearchable: true })
            {
                continue;
            }

            var purchasability = ProductStorefrontVisibilityAuthority.Evaluate(product);
            var hasVariants = product.HasVariants;
            var primaryImage = product.Media
                .Where(media => string.Equals(media.Type, "image", StringComparison.OrdinalIgnoreCase))
                .OrderBy(media => media.Position)
                .Select(media => media.Url)
                .FirstOrDefault(url => !string.IsNullOrWhiteSpace(url));

            visible.Add(new HeaderSearchItem
            {
                ProductId = product.ProductId,
                Name = product.Name,
                NameAr = product.NameAr,
                Slug = product.Slug,
                HasVariants = hasVariants,
                Price = hasVariants ? 0 : (double)purchasability.Price,
                HasActivePromotion = product.HasActivePromotion,
                PromotionBadge = product.PromotionBadge,
                PromotionDiscountValue = (double)(product.PromotionDiscountValue ?? 0m),
                ImageUrl = primaryImage,
                CategoryName = product.CategoryName,
                Score = item.Score
            });
        }

        return visible;
    }

    private string GetTraceId() => Activity.Current?.Id ?? HttpContext.TraceIdentifier;

    private IActionResult SearchUnavailableResult(string traceId, string surface, string reason)
    {
        _reliabilityTelemetry.RecordSearchFailure(surface, reason);
        return StatusCode(
            StatusCodes.Status503ServiceUnavailable,
            ApiErrorResponse.SearchUnavailable(traceId));
    }

    private static string? TruncateDescription(string? description, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(description)) return null;
        if (description.Length <= maxLength) return description;
        return description[..(maxLength - 3)] + "...";
    }

    #endregion
}

/// <summary>
/// Response DTO for unified search.
/// </summary>
public class UnifiedSearchResponse
{
    public string Query { get; set; } = string.Empty;
    public List<UnifiedSearchItemDto> Items { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
    public long SearchTimeMs { get; set; }
    public bool HasNextPage { get; set; }
    public bool HasPreviousPage { get; set; }
}

/// <summary>
/// DTO for a single unified search result item.
/// </summary>
public class UnifiedSearchItemDto
{
    public Guid EntityId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public string? Slug { get; set; }
    public string? Description { get; set; }
    public double? Price { get; set; }
    public string? CategoryName { get; set; }
    public double Score { get; set; }
}

// =========================================================================
// HEADER SEARCH (AUTOCOMPLETE) DTOs
// Phase: Smart Promotion-Aware Header Search
// =========================================================================

/// <summary>
/// Response DTO for header search suggestions.
/// </summary>
public class HeaderSearchSuggestResponse
{
    public string Query { get; set; } = string.Empty;
    public List<HeaderSearchItemDto> Items { get; set; } = new();
    public long SearchTimeMs { get; set; }
}

/// <summary>
/// DTO for a single header search suggestion item.
/// 
/// API CONTRACT:
/// - productId: Product identifier
/// - name: Display name (language-aware, already localized by API)
/// - slug: URL-friendly slug for navigation
/// - hasVariants: If true, use displayPriceText instead of price
/// - displayPriceText: Pre-formatted text like "Multiple options" or "متعدد الاختيارات"
/// - price: Raw price (null if hasVariants=true)
/// - hasActivePromotion: Whether product has promotion
/// - promotionBadge: Localized badge text (e.g., "20% OFF" or "خصم 20%")
/// - imageUrl: Product thumbnail URL
/// - categoryName: Category for context
/// </summary>
public class HeaderSearchItemDto
{
    public Guid ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Slug { get; set; }
    public bool HasVariants { get; set; }
    
    /// <summary>
    /// Pre-formatted price text for variant products.
    /// When populated, frontend should display this instead of formatting Price.
    /// Examples: "Multiple options", "متعدد الاختيارات"
    /// </summary>
    public string? DisplayPriceText { get; set; }
    
    /// <summary>
    /// Raw price value. Null if HasVariants=true.
    /// Frontend formats this using tenant currency.
    /// </summary>
    public double? Price { get; set; }
    
    public bool HasActivePromotion { get; set; }
    
    /// <summary>
    /// Localized promotion badge text.
    /// Examples: "20% OFF", "خصم 20%", "SALE"
    /// </summary>
    public string? PromotionBadge { get; set; }
    
    public string? ImageUrl { get; set; }
    public string? CategoryName { get; set; }
}
