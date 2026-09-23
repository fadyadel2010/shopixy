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
using PointOfSale.Infrastructure.Redis.Models;
using StackExchange.Redis;
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
/// Cache contract:
/// - Metadata, breadcrumbs, and sitemap are recomposed from current authority.
/// - Slug resolution and robots.txt retain live read-through Redis entries.
/// 
/// Error Semantics:
/// - Redis failures → Log as WARNING, continue to data source (never return 503)
/// - MongoDB failures → Log as ERROR, throw exception (controller returns 503)
/// - 503 SERVICE_UNAVAILABLE is returned ONLY when MongoDB read fails
/// 
/// Design Principles:
/// - SQL remains public-return authority; MongoDB supplies product candidates.
/// - All operations are tenant-scoped
/// - Cache failures never break the request
/// - Only caches with active return readers are retained
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
    private readonly IRedisJsonCacheService? _ownedSlugCache;
    private readonly SlugDiscoveryFlights? _slugFlights;
    private sealed class NegativeSlugResult
    {
        public bool Missing { get; set; } = true;
    }

    // =============================================================================
    // SEO CACHE TTL CONFIGURATION
    // =============================================================================
    
    /// <summary>
    /// TTL for slug resolution cache (60 minutes).
    /// </summary>
    private const int SlugResolutionCacheTtlMinutes = 60;
    
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
        ILogger<SeoService> logger,
        IRedisJsonCacheService? ownedSlugCache = null,
        SlugDiscoveryFlights? slugFlights = null)
    {
        _dbContext = dbContext;
        _productRepository = productRepository;
        _categoryRepository = categoryRepository;
        _redisFactory = redisFactory;
        _redisSettings = redisSettings.Value;
        _storefrontUrlResolver = storefrontUrlResolver;
        _logger = logger;
        _ownedSlugCache = ownedSlugCache;
        _slugFlights = slugFlights;
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

        _logger.LogDebug(
            "{OperationName} starting | TenantId: {TenantId} | Type: {Type} | Slug: {Slug} | CorrelationId: {CorrelationId}",
            operationName, tenantId, type, slug, correlationId);

        var normalizedType = type.ToLowerInvariant();
        if (normalizedType == "cms")
        {
            return await BuildCmsSeoMetadataAsync(tenantId, slug, baseUrl, cancellationToken);
        }

        _logger.LogDebug(
            "Querying authoritative read models | Operation: {OperationName} | TenantId: {TenantId} | Type: {Type} | Slug: {Slug} | CorrelationId: {CorrelationId}",
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

        _logger.LogDebug(
            "{OperationName} starting | TenantId: {TenantId} | Type: {Type} | Slug: {Slug} | CorrelationId: {CorrelationId}",
            operationName, tenantId, type, slug, correlationId);

        _logger.LogDebug(
            "Querying authoritative read models | Operation: {OperationName} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
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
        var cacheKey = CacheKeyBuilder.SeoSlugResolutionKey(_redisSettings.InstanceName, tenantId, slug)
            + (_ownedSlugCache == null ? "" : ":owned:v2");

        _logger.LogDebug(
            "{OperationName} starting | TenantId: {TenantId} | Slug: {Slug} | CorrelationId: {CorrelationId}",
            operationName, tenantId, slug, correlationId);

        // Step 1: Try Redis cache FIRST
        var cachedData = _ownedSlugCache == null
            ? await TryGetFromCacheAsync<SlugResolutionDto>(tenantId, operationName, cacheKey, correlationId)
            : await _ownedSlugCache.GetOwnedAsync<SlugResolutionDto>(
                cacheKey, "seo-slug-candidate",
                SeoSlugAuthorityKeys.Generation(_redisSettings.InstanceName),
                cancellationToken: cancellationToken);

        if (cachedData != null)
        {
            List<CategoryLineageRow>? categorySnapshot = null;
            var isCurrent = true;
            if (string.Equals(cachedData.Type, "cms", StringComparison.OrdinalIgnoreCase))
            {
                isCurrent = await IsCurrentPublishedCmsPageAsync(
                    tenantId, cachedData.Id, cachedData.CanonicalSlug, cancellationToken);
            }
            else if (string.Equals(cachedData.Type, "category", StringComparison.OrdinalIgnoreCase))
            {
                categorySnapshot = await LoadCurrentCategoriesAsync(tenantId, cancellationToken);
                isCurrent = await IsCurrentCategorySlugAsync(
                    tenantId, cachedData.Id, cachedData.CanonicalSlug,
                    cancellationToken, categorySnapshot);
            }
            else if (string.Equals(cachedData.Type, "product", StringComparison.OrdinalIgnoreCase))
            {
                isCurrent = await IsCurrentProductSlugAsync(
                    tenantId, cachedData.Id, cachedData.CanonicalSlug, cancellationToken);
            }
            else
            {
                isCurrent = false;
            }

            if (isCurrent)
            {
                _logger.LogInformation(
                    "Cache HIT | Operation: {OperationName} | TenantId: {TenantId} | Slug: {Slug} | CorrelationId: {CorrelationId}",
                    operationName, tenantId, slug, correlationId);
                return cachedData;
            }

            _logger.LogInformation(
                "Cached slug candidate rejected by SQL authority; retrying current discovery | TenantId: {TenantId} | Slug: {Slug} | Type: {Type} | CorrelationId: {CorrelationId}",
                tenantId, slug, cachedData.Type, correlationId);
            await TryRemoveSlugCandidateAsync(cacheKey, tenantId, correlationId);
            return await DiscoverOrShareMissingSlugAsync(tenantId, slug, cacheKey, correlationId, categorySnapshot, cancellationToken);
        }

        return await DiscoverOrShareMissingSlugAsync(tenantId, slug, cacheKey, correlationId, null, cancellationToken);
    }

    private async Task<SlugResolutionDto?> DiscoverOrShareMissingSlugAsync(
        Guid tenantId, string slug, string cacheKey, string correlationId,
        List<CategoryLineageRow>? categorySnapshot, CancellationToken cancellationToken)
    {
        async Task<SlugResolutionDto?> Discover(CancellationToken ct) =>
            await DiscoverSlugAsync(tenantId, slug, cacheKey, correlationId, categorySnapshot, ct);

        if (_ownedSlugCache == null)
            return _slugFlights == null
                ? await Discover(cancellationToken)
                : await _slugFlights.RunAsync(
                    SeoSlugAuthorityKeys.Negative(_redisSettings.InstanceName, tenantId, slug),
                    Discover, cancellationToken);

        SlugResolutionDto? discovered = null;
        try
        {
        await _ownedSlugCache.GetOrCreateOwnedAsync(
            SeoSlugAuthorityKeys.Negative(_redisSettings.InstanceName, tenantId, slug),
            "seo-slug-negative",
            SeoSlugAuthorityKeys.Generation(_redisSettings.InstanceName),
            TimeSpan.FromSeconds(3),
            async ct =>
            {
                discovered = _slugFlights == null
                    ? await Discover(ct)
                    : await _slugFlights.RunAsync(
                        SeoSlugAuthorityKeys.Negative(_redisSettings.InstanceName, tenantId, slug),
                        Discover, ct);
                // Only a SQL-confirmed miss may be shared. A positive result still
                // requires SQL authority on every subsequent request.
                return new CachePopulationResult<NegativeSlugResult>
                {
                    Value = new NegativeSlugResult(),
                    SkipCache = discovered != null
                };
            },
            cancellationToken: cancellationToken);

        return discovered;
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Slug negative coordination unavailable; using local single-flight");
            return discovered ?? (_slugFlights == null
                ? await Discover(cancellationToken)
                : await _slugFlights.RunAsync(
                    SeoSlugAuthorityKeys.Negative(_redisSettings.InstanceName, tenantId, slug),
                    Discover, cancellationToken));
        }
    }

    private async Task<SlugResolutionDto?> DiscoverSlugAsync(
        Guid tenantId,
        string slug,
        string cacheKey,
        string correlationId,
        List<CategoryLineageRow>? categorySnapshot,
        CancellationToken cancellationToken)
    {
        const string operationName = "ResolveSlug";

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
                var category = categorySnapshot != null
                    ? categorySnapshot.FirstOrDefault(item =>
                        string.Equals(GenerateSlug(item.Name), slug, StringComparison.OrdinalIgnoreCase))
                    : await LoadCurrentCategoryBySlugAsync(tenantId, slug, cancellationToken);
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

        // A stale candidate is still SQL-revalidated on every return; await the
        // best-effort cache write so failures cannot outlive the request scope.
        if (_ownedSlugCache == null)
            await PopulateCacheAsync(
                tenantId, operationName, cacheKey, resolution,
                TimeSpan.FromMinutes(SlugResolutionCacheTtlMinutes), correlationId);
        else
        {
            try
            {
                await _ownedSlugCache.GetOrCreateOwnedAsync(
                cacheKey, "seo-slug-candidate",
                SeoSlugAuthorityKeys.Generation(_redisSettings.InstanceName),
                TimeSpan.FromMinutes(SlugResolutionCacheTtlMinutes),
                _ => Task.FromResult(CachePopulationResult<SlugResolutionDto>.FromValue(resolution)),
                cancellationToken: cancellationToken);
            }
            catch (RedisException ex)
            {
                _logger.LogWarning(ex, "Slug candidate cache unavailable; SQL resolution remains authoritative");
            }
        }

        return resolution;
    }

    private async Task TryRemoveSlugCandidateAsync(string cacheKey, Guid tenantId, string correlationId)
    {
        try
        {
            if (_ownedSlugCache != null)
            {
                await _ownedSlugCache.RemoveAsync(cacheKey);
                return;
            }
            var database = _redisFactory.GetDatabase();
            if (database != null)
            {
                await database.KeyDeleteAsync(cacheKey);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to evict stale slug candidate; SQL discovery continues | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                tenantId, correlationId);
        }
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

                var recomposedProducts = await ProductSearchSqlAuthorityOverlay.ApplyBatchAsync(
                    _dbContext,
                    products,
                    tenantId,
                    cancellationToken);
                foreach (var product in recomposedProducts)
                {
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
        return await _dbContext.CmsPages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(page => page.TenantId == tenantId)
            .Where(page => _dbContext.Tenants.IgnoreQueryFilters().Any(tenant =>
                tenant.Id == page.TenantId && tenant.IsActive &&
                tenant.Status == TenantStatus.Active && tenant.DeletedAtUtc == null))
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
        return await _dbContext.CmsPages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .AnyAsync(
                page => page.TenantId == tenantId &&
                        _dbContext.Tenants.IgnoreQueryFilters().Any(tenant =>
                            tenant.Id == page.TenantId && tenant.IsActive &&
                            tenant.Status == TenantStatus.Active && tenant.DeletedAtUtc == null) &&
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
        CancellationToken cancellationToken,
        List<CategoryLineageRow>? preloadedCategories = null)
    {
        var categories = preloadedCategories ?? await LoadCurrentCategoriesAsync(tenantId, cancellationToken);
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
        var current = await ApplyCurrentSqlProductAuthorityAsync(tenantId, candidate, cancellationToken);
        if (current != null && string.Equals(current.Slug, slug, StringComparison.OrdinalIgnoreCase))
            return current;

        // Mongo is a projection and may not yet contain a freshly committed
        // slug. Negative caching must never hide a SQL-visible product claim.
        var currentProductId = await _dbContext.Products.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(product => product.TenantId == tenantId && product.Slug == slug &&
                              !product.IsDeleted && product.IsActive && product.IsOnlineVisible)
            .Select(product => (Guid?)product.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (!currentProductId.HasValue)
            return null;

        return await ApplyCurrentSqlProductAuthorityAsync(
            tenantId,
            new ProductDocument { TenantId = tenantId, ProductId = currentProductId.Value },
            cancellationToken);
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
