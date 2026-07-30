using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PointOfSale.API.Services.Catalog;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.Redis;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Models;
using PointOfSale.Infrastructure.Redis.Services;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.API.Services.Storefront;

/// <summary>
/// Implementation of storefront homepage section service.
/// 
/// ARCHITECTURAL BOUNDARY (Phase 2.5 - Homepage Sections):
/// - This service is READ-ONLY - no writes, no events, no sync logic
/// - Has NO dependency on Inventory, Pricing, or Stock
/// - Makes NO cross-service calls
/// - Is fully self-contained within the Read Side
/// - Uses ONLY read-only repository interfaces
/// 
/// Read Flow (strict order for all sections):
/// 1. Build Redis cache key
/// 2. Try Redis cache FIRST (cache-first mindset)
/// 3. Cache HIT → Return cached data
/// 4. Cache MISS → Query data source (MongoDB or Elasticsearch)
/// 5. Populate Redis cache (best-effort, fire-and-forget)
/// 6. Return mapped DTOs
/// 
/// Error Semantics:
/// - Redis failures → Log as WARNING, continue to data source (never return 503)
/// - MongoDB failures → Log as ERROR, throw exception (controller returns 503)
/// - Elasticsearch failures → Log as WARNING, use MongoDB degraded candidates for best sellers
/// - 503 SERVICE_UNAVAILABLE is returned ONLY when candidate retrieval or SQL revalidation fails
/// 
/// Design Principles:
/// - Cache-first mindset (Redis is the primary data source for performance)
/// - MongoDB is a projection candidate store; PostgreSQL remains business truth
/// - All operations are tenant-scoped
/// - Cache failures never break the request
/// - Homepage sections are READ-HEAVY and LOW-VOLATILITY
/// </summary>
public class StorefrontHomeService : IStorefrontHomeService
{
    private readonly IProductReadRepository _productRepository;
    private readonly ICategoryReadRepository _categoryRepository;
    private readonly IProductSearchIndexService _elasticsearchService;
    private readonly IRedisJsonCacheService _cacheService;
    private readonly ApplicationDbContext _dbContext;
    private readonly RedisSettings _redisSettings;
    private readonly ILogger<StorefrontHomeService> _logger;

    // =============================================================================
    // HOMEPAGE SECTION CONFIGURATION
    // These constants define the behavior of homepage sections.
    // =============================================================================
    
    /// <summary>
    /// Number of products to return for Featured Products section.
    /// </summary>
    private const int FeaturedProductsLimit = 12;
    
    /// <summary>
    /// Number of products to return for New Arrivals section.
    /// </summary>
    private const int NewArrivalsLimit = 12;
    
    /// <summary>
    /// Number of products to return for Best Sellers section.
    /// </summary>
    private const int BestSellersLimit = 12;
    
    /// <summary>
    /// TTL for Featured Products cache (30 minutes).
    /// Featured products are curated and change infrequently.
    /// </summary>
    private const int FeaturedProductsCacheTtlMinutes = 30;
    
    /// <summary>
    /// TTL for New Arrivals cache (15 minutes).
    /// New arrivals need more frequent updates.
    /// </summary>
    private const int NewArrivalsCacheTtlMinutes = 15;
    
    /// <summary>
    /// TTL for Best Sellers cache (15 minutes).
    /// Best sellers are based on sales data which changes frequently.
    /// </summary>
    private const int BestSellersCacheTtlMinutes = 15;
    
    /// <summary>
    /// TTL for Homepage Categories cache (60 minutes).
    /// Categories are structural and change very infrequently.
    /// </summary>
    private const int HomepageCategoriesCacheTtlMinutes = 60;

    public StorefrontHomeService(
        IProductReadRepository productRepository,
        ICategoryReadRepository categoryRepository,
        IProductSearchIndexService elasticsearchService,
        IRedisJsonCacheService cacheService,
        ApplicationDbContext dbContext,
        IOptions<RedisSettings> redisSettings,
        ILogger<StorefrontHomeService> logger)
    {
        _productRepository = productRepository;
        _categoryRepository = categoryRepository;
        _elasticsearchService = elasticsearchService;
        _cacheService = cacheService;
        _dbContext = dbContext;
        _redisSettings = redisSettings.Value;
        _logger = logger;
    }

    #region Featured Products

    /// <inheritdoc />
    public async Task<List<ProductListItemDto>> GetFeaturedProductsAsync(
        Guid tenantId,
        string correlationId,
        bool affiliateModeOnly = false,
        CancellationToken cancellationToken = default)
    {
        const string sectionName = "FeaturedProducts";
        var cacheKey = CacheKeyBuilder.HomeFeaturedProductsKey(_redisSettings.InstanceName, tenantId);

        _logger.LogDebug(
            "{SectionName} starting | TenantId: {TenantId} | AffiliateMode: {AffiliateMode} | CacheKey: {CacheKey} | CorrelationId: {CorrelationId}",
            sectionName, tenantId, affiliateModeOnly, cacheKey, correlationId);

        var cached = await _cacheService.GetOrCreateAsync(
            cacheKey,
            "storefront-home-featured",
            TimeSpan.FromMinutes(FeaturedProductsCacheTtlMinutes),
            async ct =>
            {
                List<ProductDocument> documents;
                try
                {
                    documents = await _productRepository.GetFeaturedProductsAsync(
                        tenantId,
                        FeaturedProductsLimit,
                        affiliateModeOnly,
                        ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "MongoDB query failed | Section: {SectionName} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                        sectionName,
                        tenantId,
                        correlationId);
                    throw;
                }

                var dtos = documents.Select(MapToListItemDto).ToList();
                var lastSyncedAtUtc = documents.Count == 0
                    ? (DateTime?)null
                    : documents.Max(document => document.SyncedAtUtc);

                return CachePopulationResult<List<ProductListItemDto>>.FromValue(dtos, lastSyncedAtUtc);
            },
            cancellationToken: cancellationToken)
            ?? new List<ProductListItemDto>();

        return await RecomposeProductCardsFromSqlAuthorityAsync(
            tenantId,
            cached,
            sectionName,
            correlationId,
            affiliateModeOnly,
            cancellationToken);
    }

    #endregion

    #region New Arrivals

    /// <inheritdoc />
    public async Task<List<ProductListItemDto>> GetNewArrivalsAsync(
        Guid tenantId,
        string correlationId,
        bool affiliateModeOnly = false,
        CancellationToken cancellationToken = default)
    {
        const string sectionName = "NewArrivals";
        var cacheKey = CacheKeyBuilder.HomeNewArrivalsKey(_redisSettings.InstanceName, tenantId);

        _logger.LogDebug(
            "{SectionName} starting | TenantId: {TenantId} | AffiliateMode: {AffiliateMode} | CacheKey: {CacheKey} | CorrelationId: {CorrelationId}",
            sectionName, tenantId, affiliateModeOnly, cacheKey, correlationId);

        var cached = await _cacheService.GetOrCreateAsync(
            cacheKey,
            "storefront-home-new-arrivals",
            TimeSpan.FromMinutes(NewArrivalsCacheTtlMinutes),
            async ct =>
            {
                List<ProductDocument> documents;
                try
                {
                    documents = await _productRepository.GetNewArrivalsAsync(
                        tenantId,
                        NewArrivalsLimit,
                        affiliateModeOnly,
                        ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "MongoDB query failed | Section: {SectionName} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                        sectionName,
                        tenantId,
                        correlationId);
                    throw;
                }

                var dtos = documents.Select(MapToListItemDto).ToList();
                var lastSyncedAtUtc = documents.Count == 0
                    ? (DateTime?)null
                    : documents.Max(document => document.SyncedAtUtc);

                return CachePopulationResult<List<ProductListItemDto>>.FromValue(dtos, lastSyncedAtUtc);
            },
            cancellationToken: cancellationToken)
            ?? new List<ProductListItemDto>();

        return await RecomposeProductCardsFromSqlAuthorityAsync(
            tenantId,
            cached,
            sectionName,
            correlationId,
            affiliateModeOnly,
            cancellationToken);
    }

    #endregion

    #region Best Sellers

    /// <inheritdoc />
    public async Task<List<ProductListItemDto>> GetBestSellersAsync(
        Guid tenantId,
        string correlationId,
        bool affiliateModeOnly = false,
        CancellationToken cancellationToken = default)
    {
        const string sectionName = "BestSellers";
        var cacheKey = CacheKeyBuilder.HomeBestSellersKey(_redisSettings.InstanceName, tenantId);

        _logger.LogDebug(
            "{SectionName} starting | TenantId: {TenantId} | AffiliateMode: {AffiliateMode} | CacheKey: {CacheKey} | CorrelationId: {CorrelationId}",
            sectionName, tenantId, affiliateModeOnly, cacheKey, correlationId);

        var cached = await _cacheService.GetOrCreateAsync(
            cacheKey,
            "storefront-home-best-sellers",
            TimeSpan.FromMinutes(BestSellersCacheTtlMinutes),
            async ct =>
            {
                List<ProductDocument> documents;

                try
                {
                    var esResult = await _elasticsearchService.GetBestSellersAsync(
                        tenantId,
                        BestSellersLimit,
                        affiliateModeOnly,
                        ct);

                    if (esResult.IsSuccess && esResult.ProductIds.Count > 0)
                    {
                        documents = await _productRepository.GetByProductIdsAsync(
                            tenantId,
                            esResult.ProductIds,
                            affiliateModeOnly,
                            ct);
                    }
                    else
                    {
                        documents = await _productRepository.GetNewArrivalsAsync(
                            tenantId,
                            BestSellersLimit,
                            affiliateModeOnly,
                            ct);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Elasticsearch query failed | Section: {SectionName} - using MongoDB degraded candidates with SQL revalidation | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                        sectionName,
                        tenantId,
                        correlationId);

                    documents = await _productRepository.GetNewArrivalsAsync(
                        tenantId,
                        BestSellersLimit,
                        affiliateModeOnly,
                        ct);
                }

                var filteredDocuments = documents
                    .Where(document => document.TenantId == tenantId && document.IsActive && document.IsOnlineVisible)
                    .ToList();

                var dtos = filteredDocuments
                    .Select(MapToListItemDto)
                    .ToList();

                var lastSyncedAtUtc = filteredDocuments.Count == 0
                    ? (DateTime?)null
                    : filteredDocuments.Max(document => document.SyncedAtUtc);

                return CachePopulationResult<List<ProductListItemDto>>.FromValue(dtos, lastSyncedAtUtc);
            },
            cancellationToken: cancellationToken)
            ?? new List<ProductListItemDto>();

        return await RecomposeProductCardsFromSqlAuthorityAsync(
            tenantId,
            cached,
            sectionName,
            correlationId,
            affiliateModeOnly,
            cancellationToken);
    }

    #endregion

    #region Curated Homepage Products

    /// <inheritdoc />
    public async Task<List<ProductListItemDto>> GetProductsByIdsAsync(
        Guid tenantId,
        IReadOnlyList<Guid> productIds,
        string correlationId,
        bool affiliateModeOnly = false,
        CancellationToken cancellationToken = default)
    {
        const string sectionName = "CuratedProducts";
        var requestedIds = productIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        if (requestedIds.Count == 0)
        {
            _logger.LogInformation(
                "Curated products request skipped because no product IDs were provided | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                tenantId,
                correlationId);

            return [];
        }

        _logger.LogDebug(
            "{SectionName} starting | TenantId: {TenantId} | ProductCount: {ProductCount} | AffiliateMode: {AffiliateMode} | CorrelationId: {CorrelationId}",
            sectionName,
            tenantId,
            requestedIds.Count,
            affiliateModeOnly,
            correlationId);

        List<ProductDocument> documents;
        try
        {
            documents = await _productRepository.GetByProductIdsAsync(
                tenantId,
                requestedIds,
                affiliateModeOnly,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "MongoDB query failed | Section: {SectionName} | TenantId: {TenantId} | ProductCount: {ProductCount} | CorrelationId: {CorrelationId}",
                sectionName,
                tenantId,
                requestedIds.Count,
                correlationId);
            throw;
        }

        var dtos = documents
            .Select(MapToListItemDto)
            .ToList();

        var recomposedDtos = await RecomposeProductCardsFromSqlAuthorityAsync(
            tenantId,
            dtos,
            sectionName,
            correlationId,
            affiliateModeOnly,
            cancellationToken);

        _logger.LogInformation(
            "Curated products query success | TenantId: {TenantId} | Requested: {RequestedCount} | Returned: {ReturnedCount} | CorrelationId: {CorrelationId}",
            tenantId,
            requestedIds.Count,
            recomposedDtos.Count,
            correlationId);

        return recomposedDtos;
    }

    #endregion

    #region Homepage Categories

    /// <inheritdoc />
    public async Task<List<CategoryReadDto>> GetHomepageCategoriesAsync(
        Guid tenantId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        const string sectionName = "HomepageCategories";
        var cacheKey = CacheKeyBuilder.HomeCategoriesKey(_redisSettings.InstanceName, tenantId);

        _logger.LogDebug(
            "{SectionName} starting | TenantId: {TenantId} | CacheKey: {CacheKey} | CorrelationId: {CorrelationId}",
            sectionName, tenantId, cacheKey, correlationId);

        var cached = await _cacheService.GetOrCreateAsync(
            cacheKey,
            "storefront-home-categories",
            TimeSpan.FromMinutes(HomepageCategoriesCacheTtlMinutes),
            async ct =>
            {
                List<CategoryDocument> documents;
                try
                {
                    documents = await _categoryRepository.GetTopLevelCategoriesAsync(tenantId, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "MongoDB query failed | Section: {SectionName} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                        sectionName,
                        tenantId,
                        correlationId);
                    throw;
                }

                var dtos = documents.Select(MapToCategoryDto).ToList();
                var lastSyncedAtUtc = documents.Count == 0
                    ? (DateTime?)null
                    : documents.Max(document => document.SyncedAtUtc);

                return CachePopulationResult<List<CategoryReadDto>>.FromValue(dtos, lastSyncedAtUtc);
            },
            cancellationToken: cancellationToken)
            ?? new List<CategoryReadDto>();

        return await RecomposeHomepageCategoriesFromSqlAuthorityAsync(
            tenantId,
            cached,
            sectionName,
            correlationId,
            cancellationToken);
    }

    #endregion

    #region Mapping Helpers

    /// <summary>
    /// Maps MongoDB ProductDocument to ProductListItemDto.
    /// 
    /// PHASE 11.5 — VARIANT-CENTRIC ARCHITECTURE:
    /// - For products WITH variants: Price comes from DEFAULT VARIANT
    /// - For products WITHOUT variants: Legacy price fallback
    /// - InStock is computed from variant data if variants exist
    /// </summary>
    private static ProductListItemDto MapToListItemDto(ProductDocument document)
    {
        // Get first image as thumbnail
        var thumbnailImage = GetPrimaryProductImage(document);

        var purchasability = ProductStorefrontVisibilityAuthority.Evaluate(document);
        var effectivePrice = purchasability.Price;

        return new ProductListItemDto
        {
            Id = document.ProductId,
            Name = document.Name,
            NameAr = document.NameAr,
            Price = effectivePrice,
            Pricing = StorefrontPricingPresentationMapper.Build(
                effectivePrice,
                document.HasActivePromotion,
                document.PromotionBadge,
                document.PromotionDiscountType,
                document.PromotionDiscountValue),
            CategoryName = document.CategoryName,
            Slug = document.Slug,
            ThumbnailImage = thumbnailImage,
            ProductTemplate = document.ProductTemplate,
            StorefrontLayout = document.ProductTemplate,
            IsFeatured = document.IsFeatured,
            Availability = StorefrontAvailabilityPresentationMapper.Build(
                purchasability.IsAvailable,
                purchasability.InStock,
                purchasability.StockQuantity,
                purchasability.RequiresVariantSelection,
                purchasability.CanPurchase,
                purchasability.UnavailableReason),
            IsAvailable = purchasability.IsAvailable,
            InStock = purchasability.InStock,
            StockQuantity = purchasability.StockQuantity,
            // PHASE: Promotions Engine - Badge Display Fields
            HasActivePromotion = document.HasActivePromotion,
            PromotionBadge = document.PromotionBadge,
            PromotionDiscountType = document.PromotionDiscountType,
            PromotionDiscountValue = document.PromotionDiscountValue
        };
    }

    /// <summary>
    /// Maps MongoDB CategoryDocument to CategoryReadDto.
    /// </summary>
    private static CategoryReadDto MapToCategoryDto(CategoryDocument document)
    {
        return new CategoryReadDto
        {
            Id = document.CategoryId,
            Name = document.Name,
            NameAr = document.NameAr,
            Slug = document.Slug,
            ParentId = document.ParentId
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

    private async Task<List<ProductListItemDto>> RecomposeProductCardsFromSqlAuthorityAsync(
        Guid tenantId,
        IReadOnlyList<ProductListItemDto> projectedCards,
        string sectionName,
        string correlationId,
        bool affiliateModeOnly,
        CancellationToken cancellationToken)
    {
        if (!await TenantLineageAuthority.IsActiveTenantAsync(_dbContext, tenantId, cancellationToken))
        {
            _logger.LogWarning(
                "Homepage product cards suppressed because tenant SQL lineage is inactive or missing | Section: {SectionName} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                sectionName,
                tenantId,
                correlationId);
            return [];
        }

        var requestedIds = projectedCards
            .Select(card => card.Id)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        if (requestedIds.Count == 0)
        {
            return [];
        }

        var affiliateEnabledProductIds = await _dbContext.AffiliateProductPolicies
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(policy => policy.TenantId == tenantId)
            .Where(policy => requestedIds.Contains(EF.Property<Guid>(policy, "ProductId")))
            .Where(policy => !policy.IsDeleted && policy.IsEnabled)
            .Select(policy => EF.Property<Guid>(policy, "ProductId"))
            .ToListAsync(cancellationToken);
        var affiliateEnabled = affiliateEnabledProductIds.ToHashSet();
        var recomposed = new List<ProductListItemDto>(projectedCards.Count);

        foreach (var projectedCard in projectedCards)
        {
            var isAffiliateEnabled = affiliateEnabled.Contains(projectedCard.Id);
            if (affiliateModeOnly && !isAffiliateEnabled)
            {
                continue;
            }

            var product = await ProductSearchSqlAuthorityOverlay.ApplyAsync(
                _dbContext,
                new ProductDocument
                {
                    TenantId = tenantId,
                    ProductId = projectedCard.Id,
                    IsAffiliateEnabled = isAffiliateEnabled
                },
                tenantId,
                cancellationToken);

            if (product is not { IsActive: true, IsOnlineVisible: true })
            {
                continue;
            }

            recomposed.Add(MapToListItemDto(product));
        }

        if (recomposed.Count != projectedCards.Count)
        {
            _logger.LogWarning(
                "Homepage product cards converged from SQL authority | Section: {SectionName} | TenantId: {TenantId} | ProjectedCount: {ProjectedCount} | SqlVisibleCount: {SqlVisibleCount} | CorrelationId: {CorrelationId}",
                sectionName,
                tenantId,
                projectedCards.Count,
                recomposed.Count,
                correlationId);
        }

        return recomposed;
    }

    private async Task<List<CategoryReadDto>> RecomposeHomepageCategoriesFromSqlAuthorityAsync(
        Guid tenantId,
        IReadOnlyList<CategoryReadDto> projectedCategories,
        string sectionName,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (!await TenantLineageAuthority.IsActiveTenantAsync(_dbContext, tenantId, cancellationToken))
        {
            _logger.LogWarning(
                "Homepage category cache suppressed because tenant SQL lineage is inactive or missing | Section: {SectionName} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                sectionName,
                tenantId,
                correlationId);
            return [];
        }

        var requestedIds = projectedCategories
            .Select(category => category.Id)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        if (requestedIds.Count == 0)
        {
            return [];
        }

        var lineage = await CategoryLineageAuthority.LoadTenantSnapshotAsync(
            _dbContext,
            tenantId,
            cancellationToken);

        var categories = await _dbContext.Categories
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(category => category.TenantId == tenantId)
            .Where(category => requestedIds.Contains(category.Id))
            .Select(category => category.Id)
            .ToListAsync(cancellationToken);

        var liveSqlIds = categories
            .Where(lineage.IsLiveInLineage)
            .ToHashSet();
        var recomposed = new List<CategoryReadDto>(projectedCategories.Count);
        foreach (var projectedCategory in projectedCategories)
        {
            if (!liveSqlIds.Contains(projectedCategory.Id))
            {
                continue;
            }

            recomposed.Add(projectedCategory);
        }

        if (recomposed.Count != projectedCategories.Count)
        {
            _logger.LogWarning(
                "Homepage category cache converged from SQL tenant/category authority | Section: {SectionName} | TenantId: {TenantId} | ProjectedCount: {ProjectedCount} | SqlVisibleCount: {SqlVisibleCount} | CorrelationId: {CorrelationId}",
                sectionName,
                tenantId,
                projectedCategories.Count,
                recomposed.Count,
                correlationId);
        }

        return recomposed;
    }

    #endregion
}
