using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Helpers;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.DTOs;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Storefront Products Controller - Read-only e-commerce product endpoints.
/// 
/// Route: /api/storefront/products
/// 
/// All endpoints require X-Tenant-Id header for multi-tenant isolation.
/// All endpoints are READ-ONLY.
/// </summary>
[ApiController]
[Route("api/storefront/products")]
[AllowAnonymous]
public class StorefrontProductsController : ControllerBase
{
    private readonly IStorefrontProductService _productService;
    private readonly IProductSearchService _searchService;
    private readonly IProductDiscoveryService _discoveryService;
    private readonly ICategoryReadRepository _categoryRepository;
    private readonly ApplicationDbContext _dbContext;
    private readonly CommerceReliabilityTelemetry _reliabilityTelemetry;
    private readonly ILogger<StorefrontProductsController> _logger;
    private const string TenantIdHeader = "X-Tenant-Id";

    public StorefrontProductsController(
        IStorefrontProductService productService,
        IProductSearchService searchService,
        IProductDiscoveryService discoveryService,
        ICategoryReadRepository categoryRepository,
        ApplicationDbContext dbContext,
        CommerceReliabilityTelemetry reliabilityTelemetry,
        ILogger<StorefrontProductsController> logger)
    {
        _productService = productService;
        _searchService = searchService;
        _discoveryService = discoveryService;
        _categoryRepository = categoryRepository;
        _dbContext = dbContext;
        _reliabilityTelemetry = reliabilityTelemetry;
        _logger = logger;
    }

    /// <summary>
    /// Gets a single product by slug for storefront display.
    /// </summary>
    /// <param name="slug">SEO-friendly product URL slug</param>
    /// <returns>ProductReadDto if found, 404 if not found</returns>
    /// <response code="200">Product found and returned</response>
    /// <response code="400">Missing or invalid X-Tenant-Id header</response>
    /// <response code="404">Product not found</response>
    /// <response code="503">Service temporarily unavailable</response>
    [HttpGet("{slug}")]
    [ProducesResponseType(typeof(ProductReadDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetProductBySlug(string slug, CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();

        // Step 1: Validate X-Tenant-Id header
        var tenantValidation = ValidateTenantHeader(traceId);
        if (tenantValidation.Error != null)
        {
            return tenantValidation.Error;
        }
        var tenantId = tenantValidation.TenantId!.Value;

        // Step 2: Validate slug
        if (string.IsNullOrWhiteSpace(slug))
        {
            _logger.LogWarning(
                "Invalid slug | TenantId: {TenantId} | Slug: {Slug} | TraceId: {TraceId}",
                tenantId, slug, traceId);
            return BadRequest(new ApiErrorResponse("INVALID_SLUG", "Product slug is required", traceId));
        }

        // Step 3: Detect Affiliate Mode
        var affiliateMode = AffiliateModeResolver.IsAffiliateMode(HttpContext);

        // Step 3a: Record Affiliate Mode request metric
        HttpContext.RecordAffiliateModeRequest(tenantId);

        // Step 3b: Add response headers for frontend
        HttpContext.AddAffiliateModeHeaders();

        _logger.LogInformation(
            "GetProductBySlug request | TenantId: {TenantId} | Slug: {Slug} | AffiliateMode: {AffiliateMode} | TraceId: {TraceId}",
            tenantId, slug, affiliateMode, traceId);

        try
        {
            // Step 4: Get product from service (handles Redis + MongoDB)
            var product = await _productService.GetProductBySlugAsync(tenantId, slug, affiliateMode, cancellationToken);

            if (product == null)
            {
                // Record blocked access if Affiliate Mode filtered the product
                if (affiliateMode)
                {
                    // We don't have the productId here, but log the attempt
                    _logger.LogInformation(
                        "[AffiliateMode] Product not found or blocked | TenantId: {TenantId} | Slug: {Slug} | TraceId: {TraceId}",
                        tenantId, slug, traceId);
                }

                _logger.LogInformation(
                    "Product not found | TenantId: {TenantId} | Slug: {Slug} | TraceId: {TraceId}",
                    tenantId, slug, traceId);
                return NotFound(ApiErrorResponse.ProductNotFound(traceId));
            }

            _logger.LogInformation(
                "GetProductBySlug success | TenantId: {TenantId} | Slug: {Slug} | ProductId: {ProductId} | TraceId: {TraceId}",
                tenantId, slug, product.Id, traceId);

            return Ok(product);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "GetProductBySlug failed | TenantId: {TenantId} | Slug: {Slug} | TraceId: {TraceId}",
                tenantId, slug, traceId);

            return StatusCode(StatusCodes.Status503ServiceUnavailable, 
                ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    /// <summary>
    /// Gets a single product by ID for storefront display.
    /// Used by wishlist and other features that reference products by ID.
    /// </summary>
    /// <param name="id">Product GUID</param>
    /// <returns>ProductReadDto if found, 404 if not found</returns>
    /// <response code="200">Product found and returned</response>
    /// <response code="400">Missing or invalid X-Tenant-Id header</response>
    /// <response code="404">Product not found</response>
    /// <response code="503">Service temporarily unavailable</response>
    [HttpGet("by-id/{id:guid}")]
    [ProducesResponseType(typeof(ProductReadDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetProductById(Guid id, CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();

        // Step 1: Validate X-Tenant-Id header
        var tenantValidation = ValidateTenantHeader(traceId);
        if (tenantValidation.Error != null)
        {
            return tenantValidation.Error;
        }
        var tenantId = tenantValidation.TenantId!.Value;

        // Step 2: Detect Affiliate Mode
        var affiliateMode = AffiliateModeResolver.IsAffiliateMode(HttpContext);

        // Step 2a: Record Affiliate Mode request metric
        HttpContext.RecordAffiliateModeRequest(tenantId);

        // Step 2b: Add response headers for frontend
        HttpContext.AddAffiliateModeHeaders();

        _logger.LogInformation(
            "GetProductById request | TenantId: {TenantId} | ProductId: {ProductId} | AffiliateMode: {AffiliateMode} | TraceId: {TraceId}",
            tenantId, id, affiliateMode, traceId);

        try
        {
            // Get product from service by ID
            var product = await _productService.GetProductByIdAsync(tenantId, id, affiliateMode, cancellationToken);

            if (product == null)
            {
                // Record blocked access if Affiliate Mode filtered the product
                if (affiliateMode)
                {
                    HttpContext.RecordBlockedProductAccess(tenantId, id, $"GET /api/storefront/products/by-id/{id}");
                }

                _logger.LogInformation(
                    "Product not found | TenantId: {TenantId} | ProductId: {ProductId} | TraceId: {TraceId}",
                    tenantId, id, traceId);
                return NotFound(ApiErrorResponse.ProductNotFound(traceId));
            }

            _logger.LogInformation(
                "GetProductById success | TenantId: {TenantId} | ProductId: {ProductId} | Slug: {Slug} | TraceId: {TraceId}",
                tenantId, id, product.Slug, traceId);

            return Ok(product);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "GetProductById failed | TenantId: {TenantId} | ProductId: {ProductId} | TraceId: {TraceId}",
                tenantId, id, traceId);

            return StatusCode(StatusCodes.Status503ServiceUnavailable, 
                ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    [HttpPost("by-id/{id:guid}/variant-resolution")]
    [ProducesResponseType(typeof(StorefrontVariantResolutionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ResolveProductVariant(
        Guid id,
        [FromBody] StorefrontVariantResolutionRequest? request,
        CancellationToken cancellationToken)
    {
        ApplyNoStoreHeaders();
        var traceId = GetTraceId();

        var tenantValidation = ValidateTenantHeader(traceId);
        if (tenantValidation.Error != null)
        {
            return tenantValidation.Error;
        }
        var tenantId = tenantValidation.TenantId!.Value;
        var affiliateMode = AffiliateModeResolver.IsAffiliateMode(HttpContext);

        try
        {
            var result = await _productService.ResolveProductVariantAsync(
                tenantId,
                id,
                request ?? new StorefrontVariantResolutionRequest(),
                affiliateMode,
                cancellationToken);

            return result == null ? Conflict() : Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "ResolveProductVariant failed | TenantId: {TenantId} | ProductId: {ProductId} | TraceId: {TraceId}",
                tenantId, id, traceId);

            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    /// <summary>
    /// [DEPRECATED] Gets a paginated list of products for storefront display.
    /// When filtering by category, includes products from all subcategories.
    /// 
    /// DEPRECATION NOTICE:
    /// This endpoint uses MongoDB and will be deprecated.
    /// Use GET /api/storefront/products/discover instead for:
    /// - Full-text search
    /// - Advanced filtering (brand, price, promotion, stock)
    /// - Faceted navigation
    /// - Promotion-aware ranking
    /// - Better performance
    /// </summary>
    /// <param name="page">Page number (1-indexed, default: 1)</param>
    /// <param name="pageSize">Items per page (default: 20, max: 50)</param>
    /// <param name="categoryId">Optional category filter (GUID or slug). Includes subcategories.</param>
    /// <param name="isActive">Filter by active status (default: true)</param>
    /// <returns>PagedResult with ProductListItemDto items</returns>
    /// <response code="200">Products returned successfully</response>
    /// <response code="400">Missing/invalid X-Tenant-Id or invalid pagination</response>
    /// <response code="503">Service temporarily unavailable</response>
    [Obsolete("Use DiscoverProducts endpoint (GET /discover) instead. This endpoint will be removed in a future version.")]
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ProductListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetProductList(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? categoryId = null,
        [FromQuery] bool isActive = true,
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

        // Step 2: Resolve categoryId and get category tree (includes subcategories)
        List<Guid>? categoryTreeIds = null;
        if (!string.IsNullOrWhiteSpace(categoryId))
        {
            var resolvedCategoryId = await ResolveCategoryIdAsync(tenantId, categoryId, traceId, cancellationToken);
            
            // If categoryId was provided but couldn't be resolved, return empty results
            if (resolvedCategoryId == null)
            {
                _logger.LogInformation(
                    "Category not found, returning empty results | TenantId: {TenantId} | CategoryId: {CategoryId} | TraceId: {TraceId}",
                    tenantId, categoryId, traceId);
                
                return Ok(new DTOs.Storefront.PagedResult<ProductListItemDto>(
                    new List<ProductListItemDto>(),
                    page,
                    pageSize,
                    0
                ));
            }

            // Get all category IDs in the tree (parent + all subcategories)
            categoryTreeIds = await _categoryRepository.GetCategoryTreeIdsAsync(
                tenantId, resolvedCategoryId.Value, cancellationToken);
            
            _logger.LogDebug(
                "Category tree resolved | TenantId: {TenantId} | CategoryId: {CategoryId} | TreeIds: {TreeIds} | TraceId: {TraceId}",
                tenantId, resolvedCategoryId, string.Join(",", categoryTreeIds), traceId);
        }

        // Step 3: Detect Affiliate Mode
        var affiliateMode = AffiliateModeResolver.IsAffiliateMode(HttpContext);

        // Step 3a: Record Affiliate Mode request metric
        HttpContext.RecordAffiliateModeRequest(tenantId);

        // Step 3b: Add response headers for frontend
        HttpContext.AddAffiliateModeHeaders();

        // Step 4: Validate pagination
        var query = new ProductListQuery
        {
            Page = page,
            PageSize = pageSize,
            CategoryId = categoryTreeIds?.FirstOrDefault(), // For cache key generation
            IsActive = isActive
        };

        var validationError = query.Validate();
        if (validationError != null)
        {
            _logger.LogWarning(
                "Invalid pagination | TenantId: {TenantId} | Page: {Page} | PageSize: {PageSize} | Error: {Error} | TraceId: {TraceId}",
                tenantId, page, pageSize, validationError, traceId);
            return BadRequest(ApiErrorResponse.InvalidPagination(traceId));
        }

        _logger.LogInformation(
            "GetProductList request | TenantId: {TenantId} | Page: {Page} | PageSize: {PageSize} | CategoryId: {CategoryId} | CategoryTreeCount: {TreeCount} | AffiliateMode: {AffiliateMode} | TraceId: {TraceId}",
            tenantId, page, pageSize, categoryId, categoryTreeIds?.Count ?? 0, affiliateMode, traceId);

        try
        {
            // Step 5: Get products - use category tree if available
            DTOs.Storefront.PagedResult<ProductListItemDto> result;
            
            if (categoryTreeIds != null && categoryTreeIds.Count > 0)
            {
                // Use the new method that filters by multiple category IDs
                result = await GetProductListByCategoryTreeAsync(
                    tenantId, page, pageSize, categoryTreeIds, isActive, affiliateMode, cancellationToken);
            }
            else
            {
                // No category filter - use standard method
                result = await _productService.GetProductListAsync(tenantId, query, affiliateMode, cancellationToken);
            }

            _logger.LogInformation(
                "GetProductList success | TenantId: {TenantId} | Page: {Page} | PageSize: {PageSize} | TotalCount: {TotalCount} | ItemsReturned: {ItemsReturned} | TraceId: {TraceId}",
                tenantId, page, pageSize, result.TotalCount, result.Items.Count, traceId);

            return Ok(result);
        }
        catch (Exception ex)
        {
            // PHASE 11.5 AUDIT: Enhanced exception logging to identify variant-related issues
            _logger.LogError(ex,
                "GetProductList failed | TenantId: {TenantId} | Page: {Page} | PageSize: {PageSize} | TraceId: {TraceId} | ExceptionType: {ExceptionType} | Message: {ExceptionMessage} | StackTrace: {StackTrace}",
                tenantId, page, pageSize, traceId, ex.GetType().FullName, ex.Message, ex.StackTrace);

            // Log inner exception if present
            if (ex.InnerException != null)
            {
                _logger.LogError(ex.InnerException,
                    "GetProductList inner exception | TenantId: {TenantId} | TraceId: {TraceId} | InnerType: {InnerType} | InnerMessage: {InnerMessage}",
                    tenantId, traceId, ex.InnerException.GetType().FullName, ex.InnerException.Message);
            }

            return StatusCode(StatusCodes.Status503ServiceUnavailable, 
                ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    /// <summary>
    /// Gets products filtered by multiple category IDs (category tree).
    /// </summary>
    private async Task<DTOs.Storefront.PagedResult<ProductListItemDto>> GetProductListByCategoryTreeAsync(
        Guid tenantId,
        int page,
        int pageSize,
        List<Guid> categoryIds,
        bool isActive,
        bool affiliateMode,
        CancellationToken cancellationToken)
    {
        // Query MongoDB directly with category tree filter
        var productRepository = HttpContext.RequestServices.GetRequiredService<IProductReadRepository>();
        
        var (items, totalCount) = await productRepository.GetProductListByCategoryTreeAsync(
            tenantId,
            page,
            pageSize,
            categoryIds,
            isActive,
            onlineVisibleOnly: true,
            affiliateMode,
            cancellationToken);

        var visibleItems = new List<ProductDocument>(items.Count);
        foreach (var item in items)
        {
            var sqlProjection = await ProductSearchSqlAuthorityOverlay.ApplyAsync(
                _dbContext,
                item,
                tenantId,
                cancellationToken);

            if (sqlProjection is { IsActive: true, IsOnlineVisible: true })
            {
                visibleItems.Add(sqlProjection);
            }
        }

        // Map to DTOs - using variant-centric architecture (PHASE 11.5)
        var dtoItems = visibleItems.Select(doc => MapToCategoryTreeListItemDto(doc)).ToList();

        return new DTOs.Storefront.PagedResult<ProductListItemDto>(dtoItems, page, pageSize, (int)totalCount);
    }

    /// <summary>
    /// Maps a ProductDocument to ProductListItemDto with variant-centric pricing.
    /// PHASE 11.5 — Products with variants use DEFAULT VARIANT price.
    /// </summary>
    private ProductListItemDto MapToCategoryTreeListItemDto(
        PointOfSale.Infrastructure.MongoDB.Documents.ProductDocument doc)
    {
        // Check if product has active variants
        var activeVariants = (doc.Variants ?? new List<PointOfSale.Infrastructure.MongoDB.Documents.ProductVariantDocument>())
            .Where(v => v.IsActive)
            .ToList();
        var defaultVariant = activeVariants.FirstOrDefault(v => v.IsDefault) ?? activeVariants.FirstOrDefault();
        
        decimal effectivePrice;
        bool inStock;
        int stockQuantity;
        
        if (activeVariants.Count > 0 && defaultVariant != null)
        {
            // VARIANT SOURCE OF TRUTH: Use default variant's effective price
            effectivePrice = defaultVariant.EffectivePrice;
            inStock = activeVariants.Any(v => v.TotalStock > 0);
            stockQuantity = (int)activeVariants.Sum(v => v.TotalStock);
        }
        else
        {
            // LEGACY FALLBACK: Use product-level price
            // Use EffectivePrice computed property which handles null OnlinePrice correctly
            effectivePrice = doc.StorefrontEffectivePrice;
            inStock = doc.InStock;
            stockQuantity = doc.StockQuantity;
        }
        
        var isAvailable = doc.IsActive && doc.IsOnlineVisible && effectivePrice > 0;

        return new ProductListItemDto
        {
            Id = doc.ProductId,
            Name = doc.Name,
            NameAr = doc.NameAr,
            Price = effectivePrice,
            Pricing = StorefrontPricingPresentationMapper.Build(
                effectivePrice,
                doc.HasActivePromotion,
                doc.PromotionBadge,
                doc.PromotionDiscountType,
                doc.PromotionDiscountValue),
            CategoryName = doc.CategoryName,
            Slug = doc.Slug,
            ThumbnailImage = GetPrimaryProductImage(doc),
            ProductTemplate = doc.ProductTemplate,
            StorefrontLayout = doc.ProductTemplate,
            IsFeatured = doc.IsFeatured,
            IsAvailable = isAvailable,
            InStock = inStock,
            StockQuantity = stockQuantity,
            // PHASE: Promotions Engine - Badge Display Fields (CRITICAL FIX)
            // These fields were MISSING, causing promotion badges to not appear on category pages
            HasActivePromotion = doc.HasActivePromotion,
            PromotionBadge = doc.PromotionBadge,
            PromotionDiscountType = doc.PromotionDiscountType,
            PromotionDiscountValue = doc.PromotionDiscountValue
        };
    }

    private static string? GetPrimaryProductImage(ProductDocument document)
    {
        return (document.Media ?? new List<ProductMediaDocument>())
            .Where(media => string.Equals(media.Type, "image", StringComparison.OrdinalIgnoreCase))
            .OrderBy(media => media.Position)
            .Select(media => media.Url)
            .FirstOrDefault(url => !string.IsNullOrWhiteSpace(url));
    }

    /// <summary>
    /// Searches products using Elasticsearch candidates, MongoDB hydration, and SQL authority recomposition.
    /// </summary>
    /// <param name="q">Search query (required, min 2 characters)</param>
    /// <param name="page">Page number (1-indexed, default: 1)</param>
    /// <param name="pageSize">Items per page (default: 20, max: 50)</param>
    /// <param name="categoryId">Optional category filter</param>
    /// <param name="minPrice">Optional minimum price filter</param>
    /// <param name="maxPrice">Optional maximum price filter</param>
    /// <returns>SearchResult with ProductListItemDto items</returns>
    /// <response code="200">Search results returned successfully</response>
    /// <response code="400">Missing/invalid X-Tenant-Id, invalid search query, or invalid pagination</response>
    /// <response code="503">Service temporarily unavailable</response>
    [HttpGet("search")]
    [ProducesResponseType(typeof(SearchResult<ProductListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> SearchProducts(
        [FromQuery] string q,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] Guid? categoryId = null,
        [FromQuery] decimal? minPrice = null,
        [FromQuery] decimal? maxPrice = null,
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

        // Step 2: Detect Affiliate Mode
        var affiliateMode = AffiliateModeResolver.IsAffiliateMode(HttpContext);

        // Step 2a: Record Affiliate Mode request metric
        HttpContext.RecordAffiliateModeRequest(tenantId);

        // Step 2b: Add response headers for frontend
        HttpContext.AddAffiliateModeHeaders();

        // Step 3: Build and validate query
        var query = new ProductSearchQuery
        {
            Q = q ?? string.Empty,
            Page = page,
            PageSize = pageSize,
            CategoryId = categoryId,
            MinPrice = minPrice,
            MaxPrice = maxPrice
        };

        var (errorCode, errorMessage) = query.Validate();
        if (errorCode != null)
        {
            _logger.LogWarning(
                "Invalid search request | TenantId: {TenantId} | Query: {Query} | Page: {Page} | PageSize: {PageSize} | Error: {ErrorCode} - {ErrorMessage} | TraceId: {TraceId}",
                tenantId, q, page, pageSize, errorCode, errorMessage, traceId);

            return BadRequest(new ApiErrorResponse(errorCode, errorMessage!, traceId));
        }

        _logger.LogInformation(
            "SearchProducts request | TenantId: {TenantId} | Query: {Query} | Page: {Page} | PageSize: {PageSize} | CategoryId: {CategoryId} | MinPrice: {MinPrice} | MaxPrice: {MaxPrice} | AffiliateMode: {AffiliateMode} | TraceId: {TraceId}",
            tenantId, q, page, pageSize, categoryId, minPrice, maxPrice, affiliateMode, traceId);

        try
        {
            // Step 4: Execute search candidate retrieval and SQL authority recomposition
            var result = await _searchService.SearchProductsAsync(
                tenantId,
                query,
                traceId,
                affiliateMode,
                cancellationToken);

            _logger.LogInformation(
                "SearchProducts success | TenantId: {TenantId} | Query: {Query} | ResultCount: {ResultCount} | TotalCount: {TotalCount} | SearchTimeMs: {SearchTimeMs} | TraceId: {TraceId}",
                tenantId, q, result.Items.Count, result.TotalCount, result.SearchTimeMs, traceId);

            return Ok(result);
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation from AbortController - don't log as error or record telemetry
            _logger.LogDebug(
                "SearchProducts canceled | TenantId: {TenantId} | Query: {Query} | TraceId: {TraceId}",
                tenantId, q, traceId);

            throw; // Let ASP.NET Core handle the cancellation
        }
        catch (StorefrontSearchUnavailableException ex)
        {
            _logger.LogError(ex,
                "SearchProducts unavailable | TenantId: {TenantId} | Query: {Query} | TraceId: {TraceId}",
                tenantId, q, traceId);

            return SearchUnavailableResult(traceId, ex.Surface, "exception");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "SearchProducts failed | TenantId: {TenantId} | Query: {Query} | TraceId: {TraceId}",
                tenantId, q, traceId);

            return SearchUnavailableResult(traceId, "product_search", "exception");
        }
    }

    /// <summary>
    /// ELASTICSEARCH-POWERED PRODUCT DISCOVERY
    /// 
    /// Shopify-grade product browsing with full filtering and faceted navigation.
    /// Works with or without search query (browse mode vs search mode).
    /// 
    /// CRITICAL: Elasticsearch supplies candidates only; current SQL authority is reapplied before returning.
    /// </summary>
    /// <param name="q">Search query (optional - empty for browse mode)</param>
    /// <param name="category">Category filter (ID or slug, multiple values supported)</param>
    /// <param name="brand">Brand filter (multiple values supported)</param>
    /// <param name="priceFrom">Minimum price filter</param>
    /// <param name="priceTo">Maximum price filter</param>
    /// <param name="hasPromotion">Filter by active promotion</param>
    /// <param name="inStock">Filter by stock availability</param>
    /// <param name="hasVariants">Filter by variant products</param>
    /// <param name="sort">Sort mode: relevance, price_asc, price_desc, newest, highest_discount</param>
    /// <param name="page">Page number (1-indexed, default: 1)</param>
    /// <param name="pageSize">Items per page (default: 12, max: 100)</param>
    /// <param name="lang">Language: en or ar</param>
    /// <returns>ProductDiscoveryResult with items, facets, and pagination</returns>
    /// <response code="200">Discovery results returned successfully</response>
    /// <response code="400">Missing/invalid X-Tenant-Id or invalid parameters</response>
    /// <response code="503">Service temporarily unavailable</response>
    [HttpGet("discover")]
    [ProducesResponseType(typeof(ProductDiscoveryResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> DiscoverProducts(
        [FromQuery] string? q = null,
        [FromQuery] List<string>? category = null,
        [FromQuery] List<string>? brand = null,
        [FromQuery] decimal? priceFrom = null,
        [FromQuery] decimal? priceTo = null,
        [FromQuery] bool? hasPromotion = null,
        [FromQuery] bool? inStock = null,
        [FromQuery] bool? hasVariants = null,
        [FromQuery] string sort = "relevance",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 12,
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

        // Step 2: Detect Affiliate Mode
        var affiliateMode = AffiliateModeResolver.IsAffiliateMode(HttpContext);

        // Step 2a: Record Affiliate Mode request metric
        HttpContext.RecordAffiliateModeRequest(tenantId);

        // Step 2b: Add response headers for frontend
        HttpContext.AddAffiliateModeHeaders();

        // Step 3: Resolve category slugs to IDs and include subcategories
        var resolvedCategories = new List<string>();
        if (category?.Count > 0)
        {
            foreach (var cat in category)
            {
                Guid? categoryGuid = null;
                
                if (Guid.TryParse(cat, out var parsedGuid))
                {
                    // Already a GUID
                    categoryGuid = parsedGuid;
                }
                else
                {
                    // Try to resolve slug to ID
                    categoryGuid = await ResolveCategoryIdAsync(tenantId, cat, traceId, cancellationToken);
                }
                
                if (categoryGuid.HasValue)
                {
                    // Get the full category tree (parent + all subcategories)
                    // This ensures clicking "Electronics" shows products in "Electronics" AND "Electronics > Phones", etc.
                    var categoryTreeIds = await _categoryRepository.GetCategoryTreeIdsAsync(
                        tenantId, categoryGuid.Value, cancellationToken);
                    
                    if (categoryTreeIds?.Count > 0)
                    {
                        // Add all category IDs from the tree
                        resolvedCategories.AddRange(categoryTreeIds.Select(id => id.ToString()));
                        
                        _logger.LogDebug(
                            "Category tree resolved for discovery | TenantId: {TenantId} | Category: {Category} | TreeIds: {TreeIds} | TraceId: {TraceId}",
                            tenantId, cat, string.Join(",", categoryTreeIds), traceId);
                    }
                    else
                    {
                        // Fallback: just use the single category ID
                        resolvedCategories.Add(categoryGuid.Value.ToString());
                    }
                }
            }
        }

        // Step 4: Build query object
        var query = new ProductDiscoveryQuery
        {
            Q = q,
            Category = resolvedCategories.Count > 0 ? resolvedCategories : null,
            Brand = brand?.Where(b => !string.IsNullOrWhiteSpace(b)).ToList(),
            PriceFrom = priceFrom,
            PriceTo = priceTo,
            HasPromotion = hasPromotion,
            InStock = inStock,
            HasVariants = hasVariants,
            Sort = ParseSortMode(sort),
            Page = page,
            PageSize = pageSize,
            Lang = lang?.ToLowerInvariant() ?? "en"
        };

        // Step 5: Validate query
        var (errorCode, errorMessage) = query.Validate();
        if (errorCode != null)
        {
            _logger.LogWarning(
                "Invalid discovery request | TenantId: {TenantId} | Error: {ErrorCode} - {ErrorMessage} | TraceId: {TraceId}",
                tenantId, errorCode, errorMessage, traceId);

            return BadRequest(new ApiErrorResponse(errorCode, errorMessage!, traceId));
        }

        _logger.LogInformation(
            "DiscoverProducts request | TenantId: {TenantId} | Query: {Query} | Categories: {Categories} | Brands: {Brands} | Sort: {Sort} | Page: {Page}/{PageSize} | AffiliateMode: {AffiliateMode} | TraceId: {TraceId}",
            tenantId, q ?? "(browse)", string.Join(",", resolvedCategories), 
            brand != null ? string.Join(",", brand) : "(all)", sort, page, pageSize, affiliateMode, traceId);

        try
        {
            // Step 6: Execute discovery search
            var result = await _discoveryService.DiscoverAsync(tenantId, query, affiliateMode, cancellationToken);

            _logger.LogInformation(
                "DiscoverProducts success | TenantId: {TenantId} | Query: {Query} | ResultCount: {ResultCount} | TotalCount: {TotalCount} | SearchTimeMs: {SearchTimeMs} | TraceId: {TraceId}",
                tenantId, q ?? "(browse)", result.Items.Count, result.TotalCount, result.SearchTimeMs, traceId);

            return Ok(result);
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation from AbortController - don't log as error or record telemetry
            _logger.LogDebug(
                "DiscoverProducts canceled | TenantId: {TenantId} | Query: {Query} | TraceId: {TraceId}",
                tenantId, q ?? "(browse)", traceId);

            throw; // Let ASP.NET Core handle the cancellation
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "DiscoverProducts failed | TenantId: {TenantId} | Query: {Query} | TraceId: {TraceId}",
                tenantId, q ?? "(browse)", traceId);

            return SearchUnavailableResult(traceId, "product_discovery", "exception");
        }
    }

    /// <summary>
    /// Parses sort mode string to enum.
    /// </summary>
    private static ProductSortMode ParseSortMode(string? sort)
    {
        return sort?.ToLowerInvariant() switch
        {
            "price_asc" => ProductSortMode.PriceAsc,
            "priceasc" => ProductSortMode.PriceAsc,
            "price_desc" => ProductSortMode.PriceDesc,
            "pricedesc" => ProductSortMode.PriceDesc,
            "newest" => ProductSortMode.Newest,
            "highest_discount" => ProductSortMode.HighestDiscount,
            "highestdiscount" => ProductSortMode.HighestDiscount,
            "discount" => ProductSortMode.HighestDiscount,
            _ => ProductSortMode.Relevance
        };
    }

    #region Helper Methods

    /// <summary>
    /// Validates the X-Tenant-Id header.
    /// Accepts both UUID and slug formats.
    /// Returns parsed TenantId or error response.
    /// </summary>
    private async Task<(Guid? TenantId, IActionResult? Error)> ValidateTenantHeaderAsync(string traceId, CancellationToken cancellationToken = default)
    {
        return await Task.FromResult(PointOfSale.API.Helpers.StorefrontTenantContextResolver.ValidateTenant(this, traceId));
    }
    
    /// <summary>
    /// Validates the X-Tenant-Id header (sync version for backward compatibility).
    /// </summary>
    private (Guid? TenantId, IActionResult? Error) ValidateTenantHeader(string traceId)
    {
        return PointOfSale.API.Helpers.StorefrontTenantContextResolver.ValidateTenant(this, traceId);
    }

    /// <summary>
    /// Gets the trace ID for the current request.
    /// </summary>
    private string GetTraceId()
    {
        return Activity.Current?.Id ?? HttpContext.TraceIdentifier;
    }

    private void ApplyNoStoreHeaders()
    {
        Response.Headers.CacheControl = "no-store, no-cache, max-age=0, must-revalidate";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.Expires = "0";
        Response.Headers["Surrogate-Control"] = "no-store";
    }

    private IActionResult SearchUnavailableResult(string traceId, string surface, string reason)
    {
        _reliabilityTelemetry.RecordSearchFailure(surface, reason);
        return StatusCode(
            StatusCodes.Status503ServiceUnavailable,
            ApiErrorResponse.SearchUnavailable(traceId));
    }

    /// <summary>
    /// Resolves a category identifier to a GUID.
    /// Accepts both GUID strings and category slugs.
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="categoryIdOrSlug">Category ID (GUID) or slug</param>
    /// <param name="traceId">Trace ID for logging</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Resolved category GUID, or null if not found</returns>
    private async Task<Guid?> ResolveCategoryIdAsync(
        Guid tenantId,
        string categoryIdOrSlug,
        string traceId,
        CancellationToken cancellationToken)
    {
        // Try to parse as GUID first
        if (Guid.TryParse(categoryIdOrSlug, out var categoryGuid))
        {
            _logger.LogDebug(
                "CategoryId is GUID | TenantId: {TenantId} | CategoryId: {CategoryId} | TraceId: {TraceId}",
                tenantId, categoryGuid, traceId);
            return categoryGuid;
        }

        // Not a GUID - treat as slug and look up in MongoDB
        _logger.LogDebug(
            "CategoryId is slug, resolving | TenantId: {TenantId} | Slug: {Slug} | TraceId: {TraceId}",
            tenantId, categoryIdOrSlug, traceId);

        try
        {
            var category = await _categoryRepository.FindBySlugAsync(tenantId, categoryIdOrSlug, cancellationToken);
            if (category != null)
            {
                _logger.LogDebug(
                    "Category slug resolved | TenantId: {TenantId} | Slug: {Slug} | CategoryId: {CategoryId} | TraceId: {TraceId}",
                    tenantId, categoryIdOrSlug, category.CategoryId, traceId);
                return category.CategoryId;
            }

            _logger.LogWarning(
                "Category slug not found | TenantId: {TenantId} | Slug: {Slug} | TraceId: {TraceId}",
                tenantId, categoryIdOrSlug, traceId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to resolve category slug | TenantId: {TenantId} | Slug: {Slug} | TraceId: {TraceId}",
                tenantId, categoryIdOrSlug, traceId);
            return null;
        }
    }

    #endregion
}

