using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Services.Catalog;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.Redis;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Services;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.API.Services.Storefront;

/// <summary>
/// Implementation of SEO &amp; Metadata service.
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
/// 
/// Error Semantics:
/// - Redis failures → Log as WARNING, continue to data source (never return 503)
/// - MongoDB failures → Log as ERROR, throw exception (controller returns 503)
/// - 503 SERVICE_UNAVAILABLE is returned ONLY when MongoDB read fails
/// 
/// Design Principles:
/// - Cache-first mindset (Redis is the primary data source for performance)
/// - MongoDB is the read-model fallback
/// - All operations are tenant-scoped
/// - Cache failures never break the request
/// - SEO data is READ-HEAVY and LOW-VOLATILITY
/// </summary>
public class SeoService : ISeoService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IProductReadRepository _productRepository;
    private readonly ICategoryReadRepository _categoryRepository;
    private readonly RedisConnectionFactory _redisFactory;
    private readonly RedisSettings _redisSettings;
    private readonly StorefrontUrlResolver _storefrontUrlResolver;
    private readonly ILogger<SeoService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    // =============================================================================
    // SEO CACHE TTL CONFIGURATION
    // =============================================================================
    
    /// <summary>
    /// TTL for SEO metadata cache (30 minutes).
    /// </summary>
    private const int SeoMetadataCacheTtlMinutes = 30;
    
    /// <summary>
    /// TTL for breadcrumbs cache (60 minutes).
    /// </summary>
    private const int BreadcrumbsCacheTtlMinutes = 60;
    
    /// <summary>
    /// TTL for slug resolution cache (60 minutes).
    /// </summary>
    private const int SlugResolutionCacheTtlMinutes = 60;
    
    /// <summary>
    /// TTL for sitemap cache (6 hours).
    /// </summary>
    private const int SitemapCacheTtlMinutes = 360;
    
    /// <summary>
    /// TTL for robots.txt cache (12 hours).
    /// </summary>
    private const int RobotsTxtCacheTtlMinutes = 720;

    public SeoService(
        ApplicationDbContext dbContext,
        IProductReadRepository productRepository,
        ICategoryReadRepository categoryRepository,
        RedisConnectionFactory redisFactory,
        IOptions<RedisSettings> redisSettings,
        StorefrontUrlResolver storefrontUrlResolver,
        ILogger<SeoService> logger)
    {
        _dbContext = dbContext;
        _productRepository = productRepository;
        _categoryRepository = categoryRepository;
        _redisFactory = redisFactory;
        _redisSettings = redisSettings.Value;
        _storefrontUrlResolver = storefrontUrlResolver;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    #region SEO Metadata

    /// <inheritdoc />
    public async Task<SeoMetadataDto?> GetSeoMetadataAsync(
        Guid tenantId,
        string type,
        string slug,
        string? baseUrl,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        const string operationName = "GetSeoMetadata";
        baseUrl = await _storefrontUrlResolver.ResolveByTenantIdAsync(tenantId);
        var cacheKey = CacheKeyBuilder.SeoMetadataKey(_redisSettings.InstanceName, tenantId, type, slug);

        _logger.LogDebug(
            "{OperationName} starting | TenantId: {TenantId} | Type: {Type} | Slug: {Slug} | CacheKey: {CacheKey} | CorrelationId: {CorrelationId}",
            operationName, tenantId, type, slug, cacheKey, correlationId);

        var normalizedType = type.ToLowerInvariant();
        if (normalizedType == "cms")
        {
            return await BuildCmsSeoMetadataAsync(tenantId, slug, baseUrl, cancellationToken);
        }

        // Step 1: Redis is only a hint for SEO. Product/category SEO must still prove SQL authority.
        var cachedData = await TryGetFromCacheAsync<SeoMetadataDto>(
            tenantId, operationName, cacheKey, correlationId);

        if (cachedData != null)
        {
            _logger.LogInformation(
                "Cache HIT ignored until SQL reauthorization | Operation: {OperationName} | TenantId: {TenantId} | Type: {Type} | Slug: {Slug} | CorrelationId: {CorrelationId}",
                operationName, tenantId, type, slug, correlationId);
        }

        // Step 2: Query MongoDB based on type
        _logger.LogDebug(
            "Cache MISS | Operation: {OperationName} - querying MongoDB | TenantId: {TenantId} | Type: {Type} | Slug: {Slug} | CorrelationId: {CorrelationId}",
            operationName, tenantId, type, slug, correlationId);

        SeoMetadataDto? metadata = null;

        try
        {
            if (normalizedType == "product")
            {
                metadata = await BuildProductSeoMetadataAsync(tenantId, slug, baseUrl, correlationId, cancellationToken);
            }
            else if (normalizedType == "category")
            {
                metadata = await BuildCategorySeoMetadataAsync(tenantId, slug, baseUrl, correlationId, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "MongoDB query failed | Operation: {OperationName} | TenantId: {TenantId} | Type: {Type} | Slug: {Slug} | CorrelationId: {CorrelationId}",
                operationName, tenantId, type, slug, correlationId);
            throw;
        }

        if (metadata == null)
        {
            _logger.LogInformation(
                "Resource not found | Operation: {OperationName} | TenantId: {TenantId} | Type: {Type} | Slug: {Slug} | CorrelationId: {CorrelationId}",
                operationName, tenantId, type, slug, correlationId);
            return null;
        }

        _logger.LogInformation(
            "MongoDB query success | Operation: {OperationName} | TenantId: {TenantId} | Type: {Type} | Slug: {Slug} | CorrelationId: {CorrelationId}",
            operationName, tenantId, type, slug, correlationId);

        // Step 3: Populate cache (fire-and-forget)
        _ = PopulateCacheAsync(
            tenantId, operationName, cacheKey, metadata,
            TimeSpan.FromMinutes(SeoMetadataCacheTtlMinutes), correlationId);

        return metadata;
    }

    private async Task<SeoMetadataDto?> BuildProductSeoMetadataAsync(
        Guid tenantId,
        string slug,
        string? baseUrl,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var product = await LoadCurrentVisibleProductBySlugAsync(tenantId, slug, cancellationToken);
        if (product == null)
        {
            return null;
        }

        // Get category for fallback
        CategoryDocument? category = null;
        if (product.CategoryId != Guid.Empty)
        {
            var allCategories = await _categoryRepository.GetAllCategoriesAsync(tenantId, cancellationToken);
            category = allCategories.FirstOrDefault(c => c.CategoryId == product.CategoryId);
        }

        var canonicalUrl = BuildCanonicalUrl(baseUrl, "product", product.Slug ?? slug);
        var ogImage = GetPrimaryProductImage(product);

        return new SeoMetadataDto
        {
            Title = BuildProductTitle(product),
            Description = BuildProductDescription(product, category),
            CanonicalUrl = canonicalUrl,
            OgImage = ogImage,
            OgType = "product",
            Keywords = BuildProductKeywords(product, category),
            Index = product.IsActive && product.IsOnlineVisible,
            Follow = true,
            ResourceType = "product",
            ResourceId = product.ProductId,
            Source = "product"
        };
    }

    private async Task<SeoMetadataDto?> BuildCategorySeoMetadataAsync(
        Guid tenantId,
        string slug,
        string? baseUrl,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var category = await LoadCurrentCategoryBySlugAsync(tenantId, slug, cancellationToken);
        if (category == null)
        {
            return null;
        }

        var canonicalUrl = BuildCanonicalUrl(baseUrl, "category", GenerateSlug(category.Name));

        return new SeoMetadataDto
        {
            Title = BuildCategoryTitle(category),
            Description = BuildCategoryDescription(category),
            CanonicalUrl = canonicalUrl,
            OgImage = null, // Categories don't have images in current schema
            OgType = "category",
            Keywords = BuildCategoryKeywords(category),
            Index = true,
            Follow = true,
            ResourceType = "category",
            ResourceId = category.CategoryId,
            Source = "category"
        };
    }

    #endregion

    #region Breadcrumbs

    /// <inheritdoc />
    public async Task<BreadcrumbsDto?> GetBreadcrumbsAsync(
        Guid tenantId,
        string type,
        string slug,
        string? baseUrl,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        const string operationName = "GetBreadcrumbs";
        baseUrl = await _storefrontUrlResolver.ResolveByTenantIdAsync(tenantId);
        var cacheKey = CacheKeyBuilder.SeoBreadcrumbsKey(_redisSettings.InstanceName, tenantId, type, slug);

        _logger.LogDebug(
            "{OperationName} starting | TenantId: {TenantId} | Type: {Type} | Slug: {Slug} | CorrelationId: {CorrelationId}",
            operationName, tenantId, type, slug, correlationId);

        // Step 1: Redis is only a hint for breadcrumbs. Product/category breadcrumbs must still prove SQL authority.
        var cachedData = await TryGetFromCacheAsync<BreadcrumbsDto>(
            tenantId, operationName, cacheKey, correlationId);

        if (cachedData != null)
        {
            _logger.LogInformation(
                "Cache HIT ignored until SQL reauthorization | Operation: {OperationName} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                operationName, tenantId, correlationId);
        }

        // Step 2: Query MongoDB
        _logger.LogDebug(
            "Cache MISS | Operation: {OperationName} - querying MongoDB | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
            operationName, tenantId, correlationId);

        BreadcrumbsDto? breadcrumbs = null;
        var normalizedType = type.ToLowerInvariant();

        try
        {
            var allCategories = await LoadCurrentCategoriesAsync(tenantId, cancellationToken);

            if (normalizedType == "product")
            {
                breadcrumbs = await BuildProductBreadcrumbsAsync(
                    tenantId, slug, baseUrl, allCategories, correlationId, cancellationToken);
            }
            else if (normalizedType == "category")
            {
                breadcrumbs = BuildCategoryBreadcrumbs(slug, baseUrl, allCategories);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "MongoDB query failed | Operation: {OperationName} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                operationName, tenantId, correlationId);
            throw;
        }

        if (breadcrumbs == null)
        {
            return null;
        }

        _logger.LogInformation(
            "MongoDB query success | Operation: {OperationName} | TenantId: {TenantId} | ItemCount: {ItemCount} | CorrelationId: {CorrelationId}",
            operationName, tenantId, breadcrumbs.Items.Count, correlationId);

        // Step 3: Populate cache (fire-and-forget)
        _ = PopulateCacheAsync(
            tenantId, operationName, cacheKey, breadcrumbs,
            TimeSpan.FromMinutes(BreadcrumbsCacheTtlMinutes), correlationId);

        return breadcrumbs;
    }

    private async Task<BreadcrumbsDto?> BuildProductBreadcrumbsAsync(
        Guid tenantId,
        string slug,
        string? baseUrl,
        List<CategoryLineageRow> allCategories,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var product = await LoadCurrentVisibleProductBySlugAsync(tenantId, slug, cancellationToken);
        if (product == null)
        {
            return null;
        }

        var items = new List<BreadcrumbItemDto>
        {
            // Home breadcrumb
            new BreadcrumbItemDto
            {
                Name = "Home",
                NameAr = "الرئيسية",
                Slug = "",
                Url = baseUrl ?? "/",
                Type = "home",
                IsCurrent = false
            }
        };

        // Build category trail
        if (product.CategoryId != Guid.Empty)
        {
            var categoryTrail = BuildCategoryTrail(product.CategoryId, allCategories);
            foreach (var category in categoryTrail)
            {
                items.Add(new BreadcrumbItemDto
                {
                    Name = category.Name,
                    NameAr = null,
                    Slug = GenerateSlug(category.Name),
                    Url = BuildBreadcrumbUrl(baseUrl, "category", GenerateSlug(category.Name)),
                    Type = "category",
                    IsCurrent = false
                });
            }
        }

        // Add product as current
        items.Add(new BreadcrumbItemDto
        {
            Name = product.Name,
            NameAr = product.NameAr,
            Slug = product.Slug ?? slug,
            Url = BuildBreadcrumbUrl(baseUrl, "product", product.Slug ?? slug),
            Type = "product",
            IsCurrent = true
        });

        return new BreadcrumbsDto
        {
            Items = items,
            JsonLd = BuildBreadcrumbJsonLd(items, baseUrl)
        };
    }

    private BreadcrumbsDto? BuildCategoryBreadcrumbs(
        string slug,
        string? baseUrl,
        List<CategoryLineageRow> allCategories)
    {
        var category = allCategories.FirstOrDefault(c => 
            GenerateSlug(c.Name).Equals(slug, StringComparison.OrdinalIgnoreCase));
        
        if (category == null)
        {
            return null;
        }

        var items = new List<BreadcrumbItemDto>
        {
            // Home breadcrumb
            new BreadcrumbItemDto
            {
                Name = "Home",
                NameAr = "الرئيسية",
                Slug = "",
                Url = baseUrl ?? "/",
                Type = "home",
                IsCurrent = false
            }
        };

        // Build category trail (excluding current)
        var categoryTrail = BuildCategoryTrail(category.CategoryId, allCategories);
        for (int i = 0; i < categoryTrail.Count - 1; i++)
        {
            var cat = categoryTrail[i];
            items.Add(new BreadcrumbItemDto
            {
                Name = cat.Name,
                NameAr = null,
                Slug = GenerateSlug(cat.Name),
                Url = BuildBreadcrumbUrl(baseUrl, "category", GenerateSlug(cat.Name)),
                Type = "category",
                IsCurrent = false
            });
        }

        // Add current category
        items.Add(new BreadcrumbItemDto
        {
            Name = category.Name,
            NameAr = null,
            Slug = GenerateSlug(category.Name),
            Url = BuildBreadcrumbUrl(baseUrl, "category", GenerateSlug(category.Name)),
            Type = "category",
            IsCurrent = true
        });

        return new BreadcrumbsDto
        {
            Items = items,
            JsonLd = BuildBreadcrumbJsonLd(items, baseUrl)
        };
    }

    private List<CategoryLineageRow> BuildCategoryTrail(Guid categoryId, List<CategoryLineageRow> allCategories)
    {
        var trail = new List<CategoryLineageRow>();
        var current = allCategories.FirstOrDefault(c => c.CategoryId == categoryId);

        while (current != null)
        {
            trail.Insert(0, current); // Insert at beginning to maintain order
            
            // Root category detection: ParentId is null OR Guid.Empty
            if (current.ParentCategoryId == null || current.ParentCategoryId == Guid.Empty)
                break;
            
            current = allCategories.FirstOrDefault(c => c.CategoryId == current.ParentCategoryId);
        }

        return trail;
    }

    #endregion

    #region Slug Resolution

    /// <inheritdoc />
    public async Task<SlugResolutionDto?> ResolveSlugAsync(
        Guid tenantId,
        string slug,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        const string operationName = "ResolveSlug";
        var cacheKey = CacheKeyBuilder.SeoSlugResolutionKey(_redisSettings.InstanceName, tenantId, slug);

        _logger.LogDebug(
            "{OperationName} starting | TenantId: {TenantId} | Slug: {Slug} | CorrelationId: {CorrelationId}",
            operationName, tenantId, slug, correlationId);

        // Step 1: Try Redis cache FIRST
        var cachedData = await TryGetFromCacheAsync<SlugResolutionDto>(
            tenantId, operationName, cacheKey, correlationId);

        if (cachedData != null)
        {
            if (string.Equals(cachedData.Type, "cms", StringComparison.OrdinalIgnoreCase) &&
                !await IsCurrentPublishedCmsPageAsync(tenantId, cachedData.Id, cachedData.CanonicalSlug, cancellationToken))
            {
                _logger.LogInformation(
                    "Cached CMS slug resolution rejected by current SQL authority | TenantId: {TenantId} | Slug: {Slug} | CorrelationId: {CorrelationId}",
                    tenantId, slug, correlationId);
                return null;
            }

            if (string.Equals(cachedData.Type, "category", StringComparison.OrdinalIgnoreCase) &&
                !await IsCurrentCategorySlugAsync(tenantId, cachedData.Id, cachedData.CanonicalSlug, cancellationToken))
            {
                _logger.LogInformation(
                    "Cached category slug resolution rejected by current SQL lineage authority | TenantId: {TenantId} | Slug: {Slug} | CorrelationId: {CorrelationId}",
                    tenantId, slug, correlationId);
                return null;
            }

            if (string.Equals(cachedData.Type, "product", StringComparison.OrdinalIgnoreCase) &&
                !await IsCurrentProductSlugAsync(tenantId, cachedData.Id, cachedData.CanonicalSlug, cancellationToken))
            {
                _logger.LogInformation(
                    "Cached product slug resolution rejected by current SQL product authority | TenantId: {TenantId} | Slug: {Slug} | CorrelationId: {CorrelationId}",
                    tenantId, slug, correlationId);
                return null;
            }

            _logger.LogInformation(
                "Cache HIT | Operation: {OperationName} | TenantId: {TenantId} | Slug: {Slug} | CorrelationId: {CorrelationId}",
                operationName, tenantId, slug, correlationId);
            return cachedData;
        }

        // Step 2: Try product first, then category
        _logger.LogDebug(
            "Cache MISS | Operation: {OperationName} - querying MongoDB | TenantId: {TenantId} | Slug: {Slug} | CorrelationId: {CorrelationId}",
            operationName, tenantId, slug, correlationId);

        SlugResolutionDto? resolution = null;

        try
        {
            // Try product first
            var product = await LoadCurrentVisibleProductBySlugAsync(tenantId, slug, cancellationToken);
            if (product != null)
            {
                resolution = new SlugResolutionDto
                {
                    Type = "product",
                    Id = product.ProductId,
                    CanonicalSlug = product.Slug ?? slug,
                    Name = product.Name,
                    NameAr = product.NameAr,
                    RequiresRedirect = !string.Equals(slug, product.Slug, StringComparison.OrdinalIgnoreCase)
                };
            }
            else
            {
                // Try category
                var category = await LoadCurrentCategoryBySlugAsync(tenantId, slug, cancellationToken);
                if (category != null)
                {
                    var categorySlug = GenerateSlug(category.Name);
                    resolution = new SlugResolutionDto
                    {
                        Type = "category",
                        Id = category.CategoryId,
                        CanonicalSlug = categorySlug,
                        Name = category.Name,
                        NameAr = null,
                        RequiresRedirect = !string.Equals(slug, categorySlug, StringComparison.OrdinalIgnoreCase)
                    };
                }
                else
                {
                    var cmsPage = await LoadCurrentPublishedCmsPageBySlugAsync(
                        tenantId,
                        slug,
                        cancellationToken);

                    if (cmsPage != null)
                    {
                        resolution = new SlugResolutionDto
                        {
                            Type = "cms",
                            Id = cmsPage.PageGroupId,
                            CanonicalSlug = cmsPage.Slug,
                            Name = cmsPage.Title,
                            NameAr = null,
                            RequiresRedirect = !string.Equals(slug, cmsPage.Slug, StringComparison.OrdinalIgnoreCase)
                        };
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "MongoDB query failed | Operation: {OperationName} | TenantId: {TenantId} | Slug: {Slug} | CorrelationId: {CorrelationId}",
                operationName, tenantId, slug, correlationId);
            throw;
        }

        if (resolution == null)
        {
            _logger.LogInformation(
                "Slug not found | Operation: {OperationName} | TenantId: {TenantId} | Slug: {Slug} | CorrelationId: {CorrelationId}",
                operationName, tenantId, slug, correlationId);
            return null;
        }

        _logger.LogInformation(
            "Slug resolved | Operation: {OperationName} | TenantId: {TenantId} | Slug: {Slug} | Type: {Type} | CorrelationId: {CorrelationId}",
            operationName, tenantId, slug, resolution.Type, correlationId);

        // Step 3: Populate cache (fire-and-forget)
        _ = PopulateCacheAsync(
            tenantId, operationName, cacheKey, resolution,
            TimeSpan.FromMinutes(SlugResolutionCacheTtlMinutes), correlationId);

        return resolution;
    }

    #endregion

    #region Sitemap

    /// <inheritdoc />
    public async Task<SitemapDto> GetSitemapAsync(
        Guid tenantId,
        string baseUrl,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        const string operationName = "GetSitemap";
        baseUrl = await _storefrontUrlResolver.ResolveByTenantIdAsync(tenantId);
        var cacheKey = CacheKeyBuilder.SeoSitemapKey(_redisSettings.InstanceName, tenantId);

        _logger.LogDebug(
            "{OperationName} starting | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
            operationName, tenantId, correlationId);

        // Sitemaps are SEO visibility surfaces. Recompute instead of allowing a
        // stale Redis sitemap to preserve deleted or unpublished CMS URLs.
        _logger.LogDebug(
            "Sitemap recomposition starting | Operation: {OperationName} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
            operationName, tenantId, correlationId);

        var urls = new List<SitemapUrlDto>();

        try
        {
            // Get all SQL-visible categories. Category sitemap entries are SEO
            // visibility, so Mongo category projections are not return authority.
            var categories = await LoadCurrentCategoriesAsync(tenantId, cancellationToken);
            foreach (var category in categories)
            {
                urls.Add(new SitemapUrlDto
                {
                    Loc = BuildCanonicalUrl(baseUrl, "category", GenerateSlug(category.Name)),
                    Lastmod = category.AuthorityTimestampUtc.ToString("yyyy-MM-dd"),
                    ChangeFreq = "weekly",
                    Priority = 0.8m
                });
            }

            // Get all visible products (paginated in batches)
            var page = 1;
            const int pageSize = 100;
            bool hasMore = true;

            while (hasMore)
            {
                var (products, totalCount) = await _productRepository.GetProductListAsync(
                    tenantId,
                    page,
                    pageSize,
                    categoryId: null,
                    isActive: null,
                    onlineVisibleOnly: true,
                    cancellationToken: cancellationToken);

                foreach (var candidate in products)
                {
                    var product = await ApplyCurrentSqlProductAuthorityAsync(
                        tenantId,
                        candidate,
                        cancellationToken);

                    if (product is not { IsActive: true, IsOnlineVisible: true })
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(product.Slug))
                    {
                        urls.Add(new SitemapUrlDto
                        {
                            Loc = BuildCanonicalUrl(baseUrl, "product", product.Slug),
                            Lastmod = product.SyncedAtUtc.ToString("yyyy-MM-dd"),
                            ChangeFreq = "daily",
                            Priority = 0.6m
                        });
                    }
                }

                hasMore = page * pageSize < totalCount;
                page++;

                // Safety limit
                if (page > 100)
                {
                    _logger.LogWarning(
                        "Sitemap generation truncated at 10000 products | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                        tenantId, correlationId);
                    break;
                }
            }

            var cmsPages = await LoadCurrentPublishedCmsPagesAsync(tenantId, cancellationToken);
            foreach (var cmsPage in cmsPages)
            {
                urls.Add(new SitemapUrlDto
                {
                    Loc = BuildCanonicalUrl(baseUrl, "cms", cmsPage.Slug),
                    Lastmod = cmsPage.UpdatedAt.ToString("yyyy-MM-dd"),
                    ChangeFreq = "monthly",
                    Priority = 0.4m
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "MongoDB query failed | Operation: {OperationName} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                operationName, tenantId, correlationId);
            throw;
        }

        var sitemap = new SitemapDto
        {
            Urls = urls,
            TotalCount = urls.Count,
            GeneratedAtUtc = DateTime.UtcNow.ToString("O")
        };

        _logger.LogInformation(
            "Sitemap generated | Operation: {OperationName} | TenantId: {TenantId} | UrlCount: {UrlCount} | CorrelationId: {CorrelationId}",
            operationName, tenantId, urls.Count, correlationId);

        // Step 3: Populate cache (fire-and-forget). Future reads still recompose
        // before returning, so Redis remains an optimization artifact only.
        _ = PopulateCacheAsync(
            tenantId, operationName, cacheKey, sitemap,
            TimeSpan.FromMinutes(SitemapCacheTtlMinutes), correlationId);

        return sitemap;
    }

    #endregion

    #region Robots.txt

    /// <inheritdoc />
    public async Task<string> GetRobotsTxtAsync(
        Guid tenantId,
        string baseUrl,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        const string operationName = "GetRobotsTxt";
        baseUrl = await _storefrontUrlResolver.ResolveByTenantIdAsync(tenantId);
        var cacheKey = CacheKeyBuilder.SeoRobotsKey(_redisSettings.InstanceName, tenantId);

        _logger.LogDebug(
            "{OperationName} starting | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
            operationName, tenantId, correlationId);

        // Step 1: Try Redis cache FIRST
        var cachedData = await TryGetFromCacheAsync<string>(
            tenantId, operationName, cacheKey, correlationId);

        if (cachedData != null)
        {
            _logger.LogInformation(
                "Cache HIT | Operation: {OperationName} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                operationName, tenantId, correlationId);
            return cachedData;
        }

        // Step 2: Build robots.txt content
        _logger.LogDebug(
            "Cache MISS | Operation: {OperationName} - building robots.txt | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
            operationName, tenantId, correlationId);

        var sitemapUrl = $"{baseUrl.TrimEnd('/')}/api/storefront/sitemap";
        
        var robotsTxt = new StringBuilder();
        robotsTxt.AppendLine("User-agent: *");
        robotsTxt.AppendLine("Allow: /");
        robotsTxt.AppendLine();
        robotsTxt.AppendLine("# Disallow admin and API paths");
        robotsTxt.AppendLine("Disallow: /admin/");
        robotsTxt.AppendLine("Disallow: /api/");
        robotsTxt.AppendLine();
        robotsTxt.AppendLine("# Allow storefront API for prerendering");
        robotsTxt.AppendLine("Allow: /api/storefront/");
        robotsTxt.AppendLine();
        robotsTxt.AppendLine($"Sitemap: {sitemapUrl}");

        var content = robotsTxt.ToString();

        _logger.LogInformation(
            "Robots.txt generated | Operation: {OperationName} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
            operationName, tenantId, correlationId);

        // Step 3: Populate cache (fire-and-forget)
        _ = PopulateCacheAsync(
            tenantId, operationName, cacheKey, content,
            TimeSpan.FromMinutes(RobotsTxtCacheTtlMinutes), correlationId);

        return content;
    }

    #endregion

    #region Private Helpers

    private async Task<T?> TryGetFromCacheAsync<T>(
        Guid tenantId,
        string operationName,
        string cacheKey,
        string correlationId) where T : class
    {
        try
        {
            var database = _redisFactory.GetDatabase();
            if (database == null)
            {
                _logger.LogDebug(
                    "Cache SKIP (no connection) | Operation: {OperationName} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                    operationName, tenantId, correlationId);
                return null;
            }

            var value = await database.StringGetAsync(cacheKey);
            if (value.IsNullOrEmpty)
            {
                return null;
            }

            // Handle string type specially
            if (typeof(T) == typeof(string))
            {
                return value.ToString() as T;
            }

            var data = JsonSerializer.Deserialize<T>(value!, _jsonOptions);
            return data;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Cache GET failed | Operation: {OperationName} | TenantId: {TenantId} | Key: {Key} | CorrelationId: {CorrelationId}",
                operationName, tenantId, cacheKey, correlationId);
            return null;
        }
    }

    private async Task PopulateCacheAsync<T>(
        Guid tenantId,
        string operationName,
        string cacheKey,
        T data,
        TimeSpan ttl,
        string correlationId)
    {
        try
        {
            var database = _redisFactory.GetDatabase();
            if (database == null)
            {
                return;
            }

            string json;
            if (typeof(T) == typeof(string))
            {
                json = data as string ?? string.Empty;
            }
            else
            {
                json = JsonSerializer.Serialize(data, _jsonOptions);
            }
            
            await database.StringSetAsync(cacheKey, json, ttl);

            _logger.LogDebug(
                "Cache SET | Operation: {OperationName} | TenantId: {TenantId} | TTL: {TtlMinutes}min | CorrelationId: {CorrelationId}",
                operationName, tenantId, ttl.TotalMinutes, correlationId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Cache SET failed | Operation: {OperationName} | TenantId: {TenantId} | Key: {Key} | CorrelationId: {CorrelationId}",
                operationName, tenantId, cacheKey, correlationId);
            // Swallow exception - cache failure should not fail the request
        }
    }

    private static string BuildProductTitle(ProductDocument product)
    {
        return $"{product.Name} | Buy Online";
    }

    private static string BuildProductDescription(ProductDocument product, CategoryDocument? category)
    {
        if (!string.IsNullOrEmpty(product.Description))
        {
            // Truncate description to ~150 chars for meta description
            var description = product.Description.Length > 150 
                ? product.Description.Substring(0, 147) + "..." 
                : product.Description;
            return description;
        }

        // Fallback description
        var categoryName = category?.Name ?? "our store";
        return $"Buy {product.Name} from {categoryName}. Best prices and fast delivery.";
    }

    private static string BuildCategoryTitle(CategoryLineageRow category)
    {
        return $"{category.Name} | Shop Online";
    }

    private static string BuildCategoryDescription(CategoryLineageRow category)
    {
        if (!string.IsNullOrEmpty(category.Description))
        {
            var description = category.Description.Length > 150 
                ? category.Description.Substring(0, 147) + "..." 
                : category.Description;
            return description;
        }

        return $"Shop our {category.Name} collection. Find the best products at great prices.";
    }

    private static string? BuildProductKeywords(ProductDocument product, CategoryDocument? category)
    {
        var keywords = new List<string> { product.Name };
        
        if (!string.IsNullOrEmpty(product.NameAr))
            keywords.Add(product.NameAr);
        
        if (category != null)
        {
            keywords.Add(category.Name);
            if (!string.IsNullOrEmpty(category.NameAr))
                keywords.Add(category.NameAr);
        }

        if (!string.IsNullOrEmpty(product.SKU))
            keywords.Add(product.SKU);

        return string.Join(", ", keywords);
    }

    private static string? BuildCategoryKeywords(CategoryLineageRow category)
    {
        var keywords = new List<string> { category.Name };

        return string.Join(", ", keywords);
    }

    private static string BuildCanonicalUrl(string? baseUrl, string type, string slug)
    {
        var basePath = baseUrl?.TrimEnd('/') ?? "";
        return type switch
        {
            "product" => $"{basePath}/product/{slug}",
            "cms" => $"{basePath}/pages/{slug}",
            _ => $"{basePath}/category/{slug}"
        };
    }

    private static string BuildBreadcrumbUrl(string? baseUrl, string type, string slug)
    {
        return BuildCanonicalUrl(baseUrl, type, slug);
    }

    private static string BuildBreadcrumbJsonLd(List<BreadcrumbItemDto> items, string? baseUrl)
    {
        var jsonLd = new
        {
            @context = "https://schema.org",
            @type = "BreadcrumbList",
            itemListElement = items.Select((item, index) => new
            {
                @type = "ListItem",
                position = index + 1,
                name = item.Name,
                item = item.Url
            }).ToArray()
        };

        return JsonSerializer.Serialize(jsonLd, new JsonSerializerOptions { WriteIndented = false });
    }

    private static string? GetPrimaryProductImage(ProductDocument product)
    {
        return (product.Media ?? new List<ProductMediaDocument>())
            .Where(media => string.Equals(media.Type, "image", StringComparison.OrdinalIgnoreCase))
            .OrderBy(media => media.Position)
            .Select(media => media.Url)
            .FirstOrDefault(url => !string.IsNullOrWhiteSpace(url));
    }

    private async Task<SeoMetadataDto?> BuildCmsSeoMetadataAsync(
        Guid tenantId,
        string slug,
        string? baseUrl,
        CancellationToken cancellationToken)
    {
        var page = await LoadCurrentPublishedCmsPageBySlugAsync(tenantId, slug, cancellationToken);
        if (page == null)
        {
            return null;
        }

        return new SeoMetadataDto
        {
            Title = page.MetaTitle ?? page.Title,
            Description = page.MetaDescription ?? string.Empty,
            CanonicalUrl = BuildCanonicalUrl(baseUrl, "cms", page.Slug),
            OgImage = page.OpenGraphImageUrl,
            OgType = "article",
            Keywords = page.MetaKeywords,
            Index = true,
            Follow = true,
            ResourceType = "cms",
            ResourceId = page.PageGroupId,
            Source = "cms-sql"
        };
    }

    private async Task<CmsPage?> LoadCurrentPublishedCmsPageBySlugAsync(
        Guid tenantId,
        string slug,
        CancellationToken cancellationToken)
    {
        if (!await IsTenantPubliclyActiveAsync(tenantId, cancellationToken))
        {
            return null;
        }

        return await _dbContext.CmsPages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(page => page.TenantId == tenantId)
            .Where(page => !page.IsDeleted)
            .Where(page => page.IsPublished && !page.IsDraft)
            .Where(page => page.PublishedAt != null)
            .Where(page => page.Slug == slug)
            .OrderByDescending(page => page.Version)
            .ThenByDescending(page => page.PublishedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<CmsPage>> LoadCurrentPublishedCmsPagesAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        if (!await IsTenantPubliclyActiveAsync(tenantId, cancellationToken))
        {
            return Array.Empty<CmsPage>();
        }

        var pages = await _dbContext.CmsPages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(page => page.TenantId == tenantId)
            .Where(page => !page.IsDeleted)
            .Where(page => page.IsPublished && !page.IsDraft)
            .Where(page => page.PublishedAt != null)
            .OrderBy(page => page.Slug)
            .ThenByDescending(page => page.Version)
            .ThenByDescending(page => page.PublishedAt)
            .ToListAsync(cancellationToken);

        return pages
            .GroupBy(page => page.PageGroupId)
            .Select(group => group
                .OrderByDescending(page => page.Version)
                .ThenByDescending(page => page.PublishedAt)
                .First())
            .ToList();
    }

    private async Task<bool> IsCurrentPublishedCmsPageAsync(
        Guid tenantId,
        Guid pageGroupId,
        string slug,
        CancellationToken cancellationToken)
    {
        if (!await IsTenantPubliclyActiveAsync(tenantId, cancellationToken))
        {
            return false;
        }

        return await _dbContext.CmsPages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .AnyAsync(
                page => page.TenantId == tenantId &&
                        page.PageGroupId == pageGroupId &&
                        page.Slug == slug &&
                        !page.IsDeleted &&
                        page.IsPublished &&
                        !page.IsDraft &&
                        page.PublishedAt != null,
                cancellationToken);
    }

    private async Task<List<CategoryLineageRow>> LoadCurrentCategoriesAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        if (!await TenantLineageAuthority.IsActiveTenantAsync(_dbContext, tenantId, cancellationToken))
        {
            return [];
        }

        var lineage = await CategoryLineageAuthority.LoadTenantSnapshotAsync(
            _dbContext,
            tenantId,
            cancellationToken);

        return lineage.Rows
            .Where(row => lineage.IsLiveInLineage(row.CategoryId))
            .OrderBy(row => row.Name)
            .ToList();
    }

    private async Task<CategoryLineageRow?> LoadCurrentCategoryBySlugAsync(
        Guid tenantId,
        string slug,
        CancellationToken cancellationToken)
    {
        var categories = await LoadCurrentCategoriesAsync(tenantId, cancellationToken);
        return categories.FirstOrDefault(category =>
            string.Equals(GenerateSlug(category.Name), slug, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<bool> IsCurrentCategorySlugAsync(
        Guid tenantId,
        Guid categoryId,
        string slug,
        CancellationToken cancellationToken)
    {
        var categories = await LoadCurrentCategoriesAsync(tenantId, cancellationToken);
        return categories.Any(category =>
            category.CategoryId == categoryId &&
            string.Equals(GenerateSlug(category.Name), slug, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<ProductDocument?> LoadCurrentVisibleProductBySlugAsync(
        Guid tenantId,
        string slug,
        CancellationToken cancellationToken)
    {
        var candidate = await _productRepository.FindBySlugAsync(tenantId, slug, true, cancellationToken);
        return await ApplyCurrentSqlProductAuthorityAsync(tenantId, candidate, cancellationToken);
    }

    private async Task<ProductDocument?> ApplyCurrentSqlProductAuthorityAsync(
        Guid tenantId,
        ProductDocument? candidate,
        CancellationToken cancellationToken)
    {
        if (candidate == null)
        {
            return null;
        }

        var product = await ProductSearchSqlAuthorityOverlay.ApplyAsync(
            _dbContext,
            candidate,
            tenantId,
            cancellationToken);

        return product is { IsActive: true, IsOnlineVisible: true }
            ? product
            : null;
    }

    private async Task<bool> IsCurrentProductSlugAsync(
        Guid tenantId,
        Guid productId,
        string slug,
        CancellationToken cancellationToken)
    {
        var product = await ApplyCurrentSqlProductAuthorityAsync(
            tenantId,
            new ProductDocument { TenantId = tenantId, ProductId = productId },
            cancellationToken);

        return product is { IsActive: true, IsOnlineVisible: true } &&
               string.Equals(product.Slug, slug, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> IsTenantPubliclyActiveAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        return await _dbContext.Tenants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .AnyAsync(
                tenant => tenant.Id == tenantId &&
                          tenant.IsActive &&
                          tenant.Status == TenantStatus.Active &&
                          tenant.DeletedAtUtc == null,
                cancellationToken);
    }

    private static string GenerateSlug(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "category";
        }

        var slug = name.ToLowerInvariant()
            .Replace(" ", "-")
            .Replace("&", "and");

        return System.Text.RegularExpressions.Regex.Replace(slug, @"[^a-z0-9\-]", "");
    }

    #endregion
}
