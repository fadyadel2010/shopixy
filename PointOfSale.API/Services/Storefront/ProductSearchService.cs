using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PointOfSale.Infrastructure.Data;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services;

namespace PointOfSale.API.Services.Storefront;

/// <summary>
/// Implementation of product search service.
///
/// Search flow:
/// 1. Fetch promotion ranking config
/// 2. Query Elasticsearch
/// 3. Hydrate Elasticsearch hits from MongoDB candidates
/// 4. Recompose current SQL product authority before mapping
/// 5. Map to ProductListItemDto
///
/// Failures are explicit and must not degrade to empty results.
/// </summary>
public class ProductSearchService : IProductSearchService
{
    private const int SqlAuthorityCandidateWindow = 10000;

    private readonly IProductSearchIndexService _elasticsearchService;
    private readonly IProductReadRepository _mongoRepository;
    private readonly ApplicationDbContext _dbContext;
    private readonly IPromotionRankingService _promotionRankingService;
    private readonly ILogger<ProductSearchService> _logger;

    public ProductSearchService(
        IProductSearchIndexService elasticsearchService,
        IProductReadRepository mongoRepository,
        ApplicationDbContext dbContext,
        IPromotionRankingService promotionRankingService,
        ILogger<ProductSearchService> logger)
    {
        _elasticsearchService = elasticsearchService;
        _mongoRepository = mongoRepository;
        _dbContext = dbContext;
        _promotionRankingService = promotionRankingService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<SearchResult<ProductListItemDto>> SearchProductsAsync(
        Guid tenantId,
        ProductSearchQuery query,
        string correlationId,
        bool affiliateModeOnly = false,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var normalizedQuery = query.GetNormalizedQuery();

        _logger.LogInformation(
            "Search starting | TenantId: {TenantId} | Query: {Query} | Page: {Page} | PageSize: {PageSize} | AffiliateMode: {AffiliateMode} | CorrelationId: {CorrelationId}",
            tenantId, normalizedQuery, query.Page, query.PageSize, affiliateModeOnly, correlationId);

        var promotionRankingConfig = await _promotionRankingService.GetConfigAsync(tenantId, cancellationToken);

        _logger.LogDebug(
            "Promotion ranking config | TenantId: {TenantId} | Enabled: {Enabled} | BaseBoost: {BaseBoost} | CorrelationId: {CorrelationId}",
            tenantId, promotionRankingConfig.Enabled, promotionRankingConfig.BaseBoost, correlationId);

        var esResult = await _elasticsearchService.SearchAsync(
            tenantId,
            normalizedQuery,
            page: 1,
            pageSize: SqlAuthorityCandidateWindow,
            query.CategoryId,
            query.MinPrice,
            query.MaxPrice,
            promotionRankingConfig,
            affiliateModeOnly,
            cancellationToken);

        List<ProductListItemDto> items;
        int totalCount;
        long searchTimeMs;

        if (!esResult.IsSuccess)
        {
            _logger.LogError(
                "Elasticsearch search failed | TenantId: {TenantId} | Query: {Query} | Error: {Error} | CorrelationId: {CorrelationId}",
                tenantId,
                normalizedQuery,
                esResult.ErrorMessage,
                correlationId);

            throw new StorefrontSearchUnavailableException(
                "product_search",
                esResult.ErrorMessage ?? "Search backend unavailable");
        }

        if (esResult.ProductIds.Count == 0)
        {
            _logger.LogInformation(
                "Elasticsearch search returned no results | TenantId: {TenantId} | Query: {Query} | TotalCount: {TotalCount} | CorrelationId: {CorrelationId}",
                tenantId,
                normalizedQuery,
                esResult.TotalCount,
                correlationId);

            items = new List<ProductListItemDto>();
            totalCount = 0;
            searchTimeMs = esResult.SearchTimeMs;
        }
        else
        {
            if (esResult.TotalCount > SqlAuthorityCandidateWindow)
            {
                throw new StorefrontSearchUnavailableException(
                    "product_search",
                    $"Search result set exceeds the SQL authority recomposition window of {SqlAuthorityCandidateWindow}.");
            }

            _logger.LogInformation(
                "Elasticsearch search success | TenantId: {TenantId} | Query: {Query} | ResultCount: {ResultCount} | SearchTimeMs: {SearchTimeMs} | CorrelationId: {CorrelationId}",
                tenantId, normalizedQuery, esResult.ProductIds.Count, esResult.SearchTimeMs, correlationId);

            try
            {
                items = await HydrateFromMongoDbAsync(
                    tenantId,
                    esResult.ProductIds,
                    affiliateModeOnly,
                    correlationId,
                    cancellationToken);

                totalCount = items.Count;
                items = items
                    .Skip((query.Page - 1) * query.PageSize)
                    .Take(query.PageSize)
                    .ToList();
                searchTimeMs = esResult.SearchTimeMs;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "MongoDB hydration failed after Elasticsearch success | TenantId: {TenantId} | Query: {Query} | CorrelationId: {CorrelationId}",
                    tenantId,
                    normalizedQuery,
                    correlationId);

                throw new StorefrontSearchUnavailableException(
                    "product_search",
                    "Search hydration failed",
                    ex);
            }
        }

        stopwatch.Stop();

        _logger.LogInformation(
            "Search completed | TenantId: {TenantId} | Query: {Query} | ResultCount: {ResultCount} | TotalCount: {TotalCount} | TotalSearchTimeMs: {SearchTimeMs} | CorrelationId: {CorrelationId}",
            tenantId, normalizedQuery, items.Count, totalCount, stopwatch.ElapsedMilliseconds, correlationId);

        return new SearchResult<ProductListItemDto>(
            items,
            query.Q?.Trim() ?? string.Empty,
            query.Page,
            query.PageSize,
            totalCount,
            searchTimeMs);
    }

    /// <summary>
    /// Hydrates product DTOs from MongoDB using Elasticsearch product IDs.
    /// Preserves order from Elasticsearch results and applies a defensive tenant check.
    /// AF-12: Filters by IsAffiliateEnabled when Affiliate Mode is active.
    /// </summary>
    private async Task<List<ProductListItemDto>> HydrateFromMongoDbAsync(
        Guid tenantId,
        List<Guid> productIds,
        bool affiliateModeOnly,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var hydrateStopwatch = Stopwatch.StartNew();

        _logger.LogDebug(
            "Hydrating from MongoDB | TenantId: {TenantId} | ProductCount: {ProductCount} | AffiliateMode: {AffiliateMode} | CorrelationId: {CorrelationId}",
            tenantId, productIds.Count, affiliateModeOnly, correlationId);

        var documents = await _mongoRepository.GetByProductIdsAsync(
            tenantId,
            productIds,
            affiliateModeOnly,
            cancellationToken);

        var filteredDocuments = new List<ProductDocument>(documents.Count);
        foreach (var document in documents.Where(d => d.TenantId == tenantId))
        {
            var sqlProjection = await ProductSearchSqlAuthorityOverlay.ApplyAsync(
                _dbContext,
                document,
                tenantId,
                cancellationToken);

            if (sqlProjection is { IsActive: true, IsOnlineVisible: true, IsSearchable: true })
            {
                filteredDocuments.Add(sqlProjection);
            }
        }

        var dtos = filteredDocuments
            .Select(MapToListItemDto)
            .ToList();

        hydrateStopwatch.Stop();

        _logger.LogInformation(
            "MongoDB hydration completed | TenantId: {TenantId} | RequestedCount: {RequestedCount} | RetrievedCount: {RetrievedCount} | HydrateTimeMs: {HydrateTimeMs} | CorrelationId: {CorrelationId}",
            tenantId, productIds.Count, dtos.Count, hydrateStopwatch.ElapsedMilliseconds, correlationId);

        return dtos;
    }

    /// <summary>
    /// Maps MongoDB ProductDocument to ProductListItemDto.
    ///
    /// Variant-centric architecture:
    /// - Products with variants use default variant pricing
    /// - Products without variants use legacy product pricing
    /// </summary>
    private static ProductListItemDto MapToListItemDto(ProductDocument document)
    {
        var images = GetOrderedProductImages(document);
        var thumbnailImage = images.FirstOrDefault();

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
            HasActivePromotion = document.HasActivePromotion,
            PromotionBadge = document.PromotionBadge,
            PromotionDiscountType = document.PromotionDiscountType,
            PromotionDiscountValue = document.PromotionDiscountValue
        };
    }

    private static List<string> GetOrderedProductImages(ProductDocument document)
    {
        return (document.Media ?? new List<ProductMediaDocument>())
            .Where(media => string.Equals(media.Type, "image", StringComparison.OrdinalIgnoreCase))
            .OrderBy(media => media.Position)
            .Select(media => media.Url)
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .ToList();
    }
}
