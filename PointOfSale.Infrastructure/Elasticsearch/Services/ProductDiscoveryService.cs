using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nest;
using PointOfSale.Core.DTOs;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Configuration;
using PointOfSale.Infrastructure.Elasticsearch.Documents;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.ReadModels.Evolution;

namespace PointOfSale.Infrastructure.Elasticsearch.Services;

/// <summary>
/// Service interface for product discovery operations.
/// 
/// CRITICAL ARCHITECTURE PRINCIPLES:
/// - Elasticsearch is the engine for search, filtering, sorting, ranking, pagination
/// - Current SQL product authority is reapplied before public response shaping
/// - NO MongoDB queries for product listing
/// - NO frontend filtering or sorting
/// - Faceted navigation with dynamic counts
/// - Promotion-aware ranking (additive, never overrides text relevance)
/// - AF-12: Supports Affiliate Mode filtering
/// </summary>
public interface IProductDiscoveryService
{
    /// <summary>
    /// Performs product discovery search with full filtering and faceting.
    /// AF-12: Supports Affiliate Mode filtering.
    /// </summary>
    /// <param name="tenantId">Tenant ID (REQUIRED)</param>
    /// <param name="query">Discovery query parameters</param>
    /// <param name="affiliateModeOnly">If true, only returns IsAffiliateEnabled=true products</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Product discovery result with items, pagination, and facets</returns>
    Task<ProductDiscoveryResult> DiscoverAsync(
        Guid tenantId,
        ProductDiscoveryQuery query,
        bool affiliateModeOnly = false,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Elasticsearch-powered product discovery service.
/// 
/// SHOPIFY-GRADE IMPLEMENTATION:
/// - Browse without search (empty q = discovery mode)
/// - Search + filter combinations
/// - Dynamic facet counts
/// - Promotion-aware ranking with function_score
/// - Variant-aware price handling
/// - Bilingual support (EN/AR)
/// - SQL-authoritative response recomposition before returning public items
/// </summary>
public class ProductDiscoveryService : IProductDiscoveryService
{
    private const int DiscoveryCandidateWindow = 10000;
    private const string SearchableFieldName = "isSearchable";

    private readonly IElasticClient _client;
    private readonly ApplicationDbContext _dbContext;
    private readonly ElasticsearchSettings _settings;
    private readonly IReadModelEvolutionConvergenceService _readModelEvolution;
    private readonly ILogger<ProductDiscoveryService> _logger;

    // Price range buckets for faceting
    private static readonly (decimal? From, decimal? To, string Label)[] PriceRanges = new (decimal?, decimal?, string)[]
    {
        ((decimal?)null, 50m, "Under 50"),
        (50m, 100m, "50 - 100"),
        (100m, 200m, "100 - 200"),
        (200m, 500m, "200 - 500"),
        (500m, 1000m, "500 - 1000"),
        (1000m, (decimal?)null, "Over 1000")
    };

    public ProductDiscoveryService(
        IElasticClient client,
        ApplicationDbContext dbContext,
        IOptions<ElasticsearchSettings> settings,
        IReadModelEvolutionConvergenceService readModelEvolution,
        ILogger<ProductDiscoveryService> logger)
    {
        _client = client;
        _dbContext = dbContext;
        _settings = settings.Value;
        _readModelEvolution = readModelEvolution;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ProductDiscoveryResult> DiscoverAsync(
        Guid tenantId,
        ProductDiscoveryQuery query,
        bool affiliateModeOnly = false,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        if (!_settings.Enabled)
        {
            _logger.LogWarning("Elasticsearch disabled - failing closed for product discovery");
            throw new InvalidOperationException("Product discovery search backend is disabled.");
        }

        try
        {
            _logger.LogInformation(
                "Product discovery starting | TenantId: {TenantId} | Query: {Query} | Sort: {Sort} | Page: {Page}/{PageSize} | IsSearch: {IsSearch} | AffiliateMode: {AffiliateMode}",
                tenantId, query.Q ?? "(browse)", query.Sort, query.Page, query.PageSize, query.IsSearchMode, affiliateModeOnly);

            var productSearchCompatibility = await _readModelEvolution.GetCompatibilityDecisionAsync(
                ReadModelEvolutionRegistry.StorefrontProduct,
                ReadModelStore.Elasticsearch,
                cancellationToken);

            // Build the Elasticsearch query
            var searchDescriptor = BuildSearchDescriptor(
                tenantId,
                query,
                affiliateModeOnly,
                productSearchCompatibility);

            // Execute search
            var searchResponse = await _client.SearchAsync<ProductSearchDocument>(
                searchDescriptor, cancellationToken);

            stopwatch.Stop();

            if (!searchResponse.IsValid)
            {
                // Extract detailed root cause from shard failures
                var rootCause = searchResponse.ServerError?.Error?.RootCause?.FirstOrDefault();
                var topLevelReason = searchResponse.ServerError?.Error?.Reason ?? "Unknown";

                _logger.LogError(
                    "Product discovery failed | TenantId: {TenantId} | Error: {Error} | RootCause: {RootCause} | ErrorType: {ErrorType} | Index: {Index}",
                    tenantId, topLevelReason,
                    rootCause?.Reason ?? "N/A",
                    rootCause?.Type ?? "N/A",
                    _settings.ProductsIndexName);

                throw new InvalidOperationException($"Product discovery search failed: {topLevelReason}");
            }

            // Map results
            if (searchResponse.Total > DiscoveryCandidateWindow)
            {
                throw new InvalidOperationException(
                    $"Product discovery result set exceeds the SQL recomposition window of {DiscoveryCandidateWindow}. Refine filters before serving public pagination metadata.");
            }

            var result = await MapSearchResponseAsync(
                tenantId,
                searchResponse,
                query,
                affiliateModeOnly,
                stopwatch.ElapsedMilliseconds,
                cancellationToken);

            _logger.LogInformation(
                "Product discovery success | TenantId: {TenantId} | Query: {Query} | Results: {Count}/{Total} | TimeMs: {TimeMs}",
                tenantId, query.Q ?? "(browse)", result.Items.Count, result.TotalCount, result.SearchTimeMs);

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex,
                "Product discovery exception | TenantId: {TenantId} | Query: {Query}",
                tenantId, query.Q ?? "(browse)");

            throw new InvalidOperationException("Product discovery search backend is unavailable.", ex);
        }
    }

    /// <summary>
    /// Builds the Elasticsearch search descriptor with all filters, sorting, and aggregations.
    /// </summary>
    private Func<SearchDescriptor<ProductSearchDocument>, ISearchRequest> BuildSearchDescriptor(
        Guid tenantId,
        ProductDiscoveryQuery query,
        bool affiliateModeOnly,
        ReadModelCompatibilityDecision productSearchCompatibility)
    {
        return s => s
            .Index(_settings.ProductsIndexName)
            .From(0)
            .Size(DiscoveryCandidateWindow)
            .TrackTotalHits(true)
            .Query(q => BuildQuery(q, tenantId, query, affiliateModeOnly, productSearchCompatibility))
            .Sort(sort => BuildSort(sort, query))
            .Aggregations(agg => BuildAggregations(agg, tenantId, query));
    }

    /// <summary>
    /// Builds the bool query with function_score for promotion-aware ranking.
    /// </summary>
    private QueryContainer BuildQuery(
        QueryContainerDescriptor<ProductSearchDocument> q,
        Guid tenantId,
        ProductDiscoveryQuery query,
        bool affiliateModeOnly,
        ReadModelCompatibilityDecision productSearchCompatibility)
    {
        return q.FunctionScore(fs => fs
            .Query(innerQ => innerQ
                .Bool(b => b
                    .Must(BuildMustQueries(tenantId, query))
                    .Filter(BuildFilterQueries(tenantId, query, affiliateModeOnly, productSearchCompatibility))
                )
            )
            .Functions(funcs => funcs
                // Base promotion boost: +3 for any active promotion
                .Weight(w => w
                    .Weight(3.0)
                    .Filter(f => f
                        .Term(t => t.Field(p => p.HasActivePromotion).Value(true))
                    )
                )
                // High discount boost: +2 for discounts >= 20%
                .Weight(w => w
                    .Weight(2.0)
                    .Filter(f => f
                        .Bool(b => b
                            .Must(
                                m => m.Term(t => t.Field(p => p.HasActivePromotion).Value(true)),
                                m => m.Range(r => r.Field(p => p.PromotionDiscountValue).GreaterThanOrEquals(20))
                            )
                        )
                    )
                )
                // Manual boost override: add SearchBoostOverride value
                .FieldValueFactor(fvf => fvf
                    .Field(p => p.SearchBoostOverride)
                    .Factor(1.0)
                    .Modifier(FieldValueFactorModifier.None)
                    .Missing(0)
                )
            )
            .ScoreMode(FunctionScoreMode.Sum)
            .BoostMode(FunctionBoostMode.Sum)
        );
    }

    /// <summary>
    /// Builds must queries (text search).
    /// </summary>
    private Func<QueryContainerDescriptor<ProductSearchDocument>, QueryContainer>[] BuildMustQueries(
        Guid tenantId,
        ProductDiscoveryQuery query)
    {
        var must = new List<Func<QueryContainerDescriptor<ProductSearchDocument>, QueryContainer>>();

        // Text search (if query provided)
        if (query.IsSearchMode)
        {
            var searchFields = query.Lang == "ar"
                ? new[] { "searchText^6", "searchText.arabic^5", "name^4", "name.arabic^4", "nameAr^3" }
                : new[] { "searchText^6", "name^5", "searchText.autocomplete^4", "nameAr^2" };

            must.Add(m => m.MultiMatch(mm => mm
                .Query(query.Q)
                .Fields(searchFields)
                .Type(TextQueryType.BestFields)
                .Fuzziness(Fuzziness.Auto)
            ));
        }
        else
        {
            // Browse mode - match all
            must.Add(m => m.MatchAll());
        }

        return must.ToArray();
    }

    /// <summary>
    /// Builds filter queries (no scoring, just filtering).
    /// </summary>
    private Func<QueryContainerDescriptor<ProductSearchDocument>, QueryContainer>[] BuildFilterQueries(
        Guid tenantId,
        ProductDiscoveryQuery query,
        bool affiliateModeOnly,
        ReadModelCompatibilityDecision productSearchCompatibility)
    {
        var filters = new List<Func<QueryContainerDescriptor<ProductSearchDocument>, QueryContainer>>
        {
            // ALWAYS filter by tenant
            f => f.Term(t => t.Field(p => p.TenantId).Value(tenantId)),
            // ALWAYS filter by active
            f => f.Term(t => t.Field(p => p.IsActive).Value(true)),
            // ALWAYS filter by online visible
            f => f.Term(t => t.Field(p => p.IsOnlineVisible).Value(true)),
            // Searchability is SQL-owned and runtime-required only after search index convergence.
            f => ReadModelEvolutionQueryFilters.RequiredBoolean(
                f,
                SearchableFieldName,
                requiredValue: true,
                productSearchCompatibility)
        };

        // AF-12: Affiliate Mode filter (CRITICAL SECURITY FIX)
        if (affiliateModeOnly)
        {
            filters.Add(f => f.Term(t => t
                .Field(p => p.IsAffiliateEnabled)
                .Value(true)));
        }

        // Category filter
        if (query.Category?.Count > 0)
        {
            // Handle both GUID and slug - for now assume GUIDs are passed
            var categoryIds = query.Category
                .Where(c => Guid.TryParse(c, out _))
                .Select(c => Guid.Parse(c))
                .ToList();

            if (categoryIds.Count > 0)
            {
                filters.Add(f => f.Terms(t => t
                    .Field(p => p.CategoryId)
                    .Terms(categoryIds)));
            }
        }

        // Brand filter
        if (query.Brand?.Count > 0)
        {
            filters.Add(f => f.Terms(t => t
                .Field(p => p.Brand)
                .Terms(query.Brand)));
        }

        // Price range filter. ProductSearchDocument.Price is SQL-recomposed visible storefront price
        // for both simple and variant products, so variant products must not be excluded here.
        if (query.PriceFrom.HasValue || query.PriceTo.HasValue)
        {
            filters.Add(f => f.Range(r =>
            {
                var range = r.Field(p => p.Price);
                if (query.PriceFrom.HasValue)
                    range = range.GreaterThanOrEquals((double)query.PriceFrom.Value);
                if (query.PriceTo.HasValue)
                    range = range.LessThanOrEquals((double)query.PriceTo.Value);
                return range;
            }));
        }

        // Promotion filter
        if (query.HasPromotion.HasValue)
        {
            filters.Add(f => f.Term(t => t
                .Field(p => p.HasActivePromotion)
                .Value(query.HasPromotion.Value)));
        }

        // Stock filter
        if (query.InStock.HasValue)
        {
            filters.Add(f => f.Term(t => t
                .Field(p => p.IsInStock)
                .Value(query.InStock.Value)));
        }

        // Variants filter
        if (query.HasVariants.HasValue)
        {
            filters.Add(f => f.Term(t => t
                .Field(p => p.HasVariants)
                .Value(query.HasVariants.Value)));
        }

        return filters.ToArray();
    }

    /// <summary>
    /// Builds sort descriptor based on sort mode.
    /// </summary>
    private SortDescriptor<ProductSearchDocument> BuildSort(
        SortDescriptor<ProductSearchDocument> sort,
        ProductDiscoveryQuery query)
    {
        return query.Sort switch
        {
            ProductSortMode.PriceAsc => sort.Ascending(p => p.Price),
            
            ProductSortMode.PriceDesc => sort.Descending(p => p.Price),
            
            ProductSortMode.Newest => sort
                .Descending(p => p.CreatedAtUtc),
            
            ProductSortMode.HighestDiscount => sort
                .Descending(p => p.PromotionDiscountValue)
                .Descending(SortSpecialField.Score),
            
            // Relevance: _score for search, createdAt for browse
            _ => query.IsSearchMode
                ? sort.Descending(SortSpecialField.Score)
                : sort.Descending(p => p.CreatedAtUtc)
        };
    }

    /// <summary>
    /// Builds aggregations for faceted navigation.
    /// </summary>
    private AggregationContainerDescriptor<ProductSearchDocument> BuildAggregations(
        AggregationContainerDescriptor<ProductSearchDocument> agg,
        Guid tenantId,
        ProductDiscoveryQuery query)
    {
        return agg
            // Brand aggregation
            .Terms("brands", t => t
                .Field(p => p.Brand)
                .Size(50)
                .MinimumDocumentCount(1)
            )
            // Category aggregation
            .Terms("categories", t => t
                .Field(p => p.CategoryId)
                .Size(50)
                .MinimumDocumentCount(1)
            )
            // Promotion aggregation
            .Terms("promotions", t => t
                .Field(p => p.HasActivePromotion)
            )
            // Stock aggregation
            .Terms("stock", t => t
                .Field(p => p.IsInStock)
            )
            .Range("price_ranges", r => r
                .Field(p => p.Price)
                .Ranges(
                    rng => rng.To(50).Key("under_50"),
                    rng => rng.From(50).To(100).Key("50_100"),
                    rng => rng.From(100).To(200).Key("100_200"),
                    rng => rng.From(200).To(500).Key("200_500"),
                    rng => rng.From(500).To(1000).Key("500_1000"),
                    rng => rng.From(1000).Key("over_1000")
                )
            );
    }

    /// <summary>
    /// Maps Elasticsearch response to ProductDiscoveryResult.
    /// </summary>
    private async Task<ProductDiscoveryResult> MapSearchResponseAsync(
        Guid tenantId,
        ISearchResponse<ProductSearchDocument> response,
        ProductDiscoveryQuery query,
        bool affiliateModeOnly,
        long searchTimeMs,
        CancellationToken cancellationToken)
    {
        var isRtl = query.Lang == "ar";

        var items = response.Hits.Select(hit =>
        {
            var doc = hit.Source;
            return new ProductDiscoveryItemDto
            {
                Id = doc.ProductId,
                Name = doc.Name,
                NameAr = doc.NameAr,
                Slug = doc.Slug,
                StorefrontLayout = doc.StorefrontLayout,
                IsFeatured = doc.IsFeatured,
                Brand = doc.Brand,
                CategoryName = doc.CategoryName,
                CategoryId = doc.CategoryId,
                ImageUrl = doc.ImageUrl,
                HasVariants = doc.HasVariants,
                InStock = doc.IsInStock,
                HasActivePromotion = doc.HasActivePromotion,
                PromotionBadge = doc.HasActivePromotion
                    ? BuildPromotionBadge(doc.PromotionDiscountValue, doc.PromotionDiscountType)
                    : null,
                PromotionDiscountType = doc.PromotionDiscountType,
                PromotionDiscountValue = (decimal)doc.PromotionDiscountValue,
                Score = hit.Score ?? 0,
                // CRITICAL: Variant-aware price handling
                Price = doc.HasVariants ? null : (decimal?)doc.Price,
                Pricing = BuildPricingPresentation(
                    (decimal)doc.Price,
                    doc.HasActivePromotion,
                    doc.HasActivePromotion
                        ? BuildPromotionBadge(doc.PromotionDiscountValue, doc.PromotionDiscountType)
                        : null,
                    doc.PromotionDiscountType,
                    (decimal)doc.PromotionDiscountValue),
                DisplayPriceText = doc.HasVariants
                    ? (isRtl ? "متعدد الاختيارات" : "Multiple options")
                    : null
            };
        }).ToList();

        items = await RecomposeDiscoveryItemsFromSqlAuthorityAsync(
            tenantId,
            items,
            query,
            affiliateModeOnly,
            isRtl,
            cancellationToken);

        items = SortSqlRecomposedItems(items, query).ToList();

        var totalCount = items.Count;
        var pageItems = items
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToList();

        return new ProductDiscoveryResult
        {
            Items = pageItems,
            TotalCount = totalCount,
            Page = query.Page,
            PageSize = query.PageSize,
            Query = query.Q,
            SearchTimeMs = searchTimeMs,
            Facets = items.Count > 0
                ? BuildFacetsFromVisibleItems(items)
                : new ProductDiscoveryFacets() // Facets = new ProductDiscoveryFacets()
        };
    }

    private async Task<List<ProductDiscoveryItemDto>> RecomposeDiscoveryItemsFromSqlAuthorityAsync(
        Guid tenantId,
        IReadOnlyCollection<ProductDiscoveryItemDto> candidates,
        ProductDiscoveryQuery query,
        bool affiliateModeOnly,
        bool isRtl,
        CancellationToken cancellationToken)
    {
        var items = new List<ProductDiscoveryItemDto>(candidates.Count);

        foreach (var candidate in candidates)
        {
            var product = await ProductSearchSqlAuthorityOverlay.ApplyAsync(
                _dbContext,
                new ProductDocument
                {
                    TenantId = tenantId,
                    ProductId = candidate.Id
                },
                tenantId,
                cancellationToken);

            if (product is not { IsActive: true, IsOnlineVisible: true, IsSearchable: true } ||
                !MatchesSqlAuthoritativeDiscoveryFilters(product, query, affiliateModeOnly))
            {
                continue;
            }

            var purchasability = ProductStorefrontVisibilityAuthority.Evaluate(product);
            var hasVariants = purchasability.RequiresVariantSelection;
            var price = purchasability.Price;
            var promotionBadge = product.HasActivePromotion
                ? BuildPromotionBadge((double)(product.PromotionDiscountValue ?? 0m), product.PromotionDiscountType)
                : null;
            var imageUrl = (product.Media ?? new List<ProductMediaDocument>())
                .Where(media => string.Equals(media.Type, "image", StringComparison.OrdinalIgnoreCase))
                .OrderBy(media => media.Position)
                .Select(media => media.Url)
                .FirstOrDefault(url => !string.IsNullOrWhiteSpace(url));

            items.Add(new ProductDiscoveryItemDto
            {
                Id = product.ProductId,
                Name = product.Name,
                NameAr = product.NameAr,
                Slug = product.Slug,
                StorefrontLayout = product.ProductTemplate,
                IsFeatured = product.IsFeatured,
                Brand = product.Brand,
                CategoryName = product.CategoryName,
                CategoryId = product.CategoryId,
                ImageUrl = imageUrl,
                HasVariants = hasVariants,
                InStock = purchasability.InStock,
                HasActivePromotion = product.HasActivePromotion,
                PromotionBadge = promotionBadge,
                PromotionDiscountType = product.PromotionDiscountType,
                PromotionDiscountValue = product.PromotionDiscountValue ?? 0m,
                Score = candidate.Score,
                Price = hasVariants ? null : price,
                Pricing = BuildPricingPresentation(
                    price,
                    product.HasActivePromotion,
                    promotionBadge,
                    product.PromotionDiscountType,
                    product.PromotionDiscountValue),
                Availability = BuildAvailabilityPresentation(purchasability),
                DisplayPriceText = hasVariants
                    ? (isRtl ? "متعدد الاختيارات" : "Multiple options")
                    : null
            });
        }

        return items;
    }

    private static bool MatchesSqlAuthoritativeDiscoveryFilters(
        ProductDocument product,
        ProductDiscoveryQuery query,
        bool affiliateModeOnly)
    {
        if (affiliateModeOnly && !product.IsAffiliateEnabled)
        {
            return false;
        }

        if (query.Category?.Count > 0)
        {
            var categoryIds = query.Category
                .Where(value => Guid.TryParse(value, out _))
                .Select(Guid.Parse)
                .ToHashSet();

            if (categoryIds.Count > 0 && !categoryIds.Contains(product.CategoryId))
            {
                return false;
            }
        }

        if (query.Brand?.Count > 0 &&
            !query.Brand.Any(brand => string.Equals(brand, product.Brand, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var purchasability = ProductStorefrontVisibilityAuthority.Evaluate(product);
        var visiblePrice = purchasability.Price;
        if ((query.PriceFrom.HasValue && visiblePrice < query.PriceFrom.Value) ||
            (query.PriceTo.HasValue && visiblePrice > query.PriceTo.Value))
        {
            return false;
        }

        if (query.HasPromotion.HasValue && product.HasActivePromotion != query.HasPromotion.Value)
        {
            return false;
        }

        if (query.InStock.HasValue && purchasability.InStock != query.InStock.Value)
        {
            return false;
        }

        if (query.HasVariants.HasValue && purchasability.RequiresVariantSelection != query.HasVariants.Value)
        {
            return false;
        }

        return true;
    }

    private static IEnumerable<ProductDiscoveryItemDto> SortSqlRecomposedItems(
        IEnumerable<ProductDiscoveryItemDto> items,
        ProductDiscoveryQuery query)
    {
        return query.Sort switch
        {
            ProductSortMode.PriceAsc => items.OrderBy(item => item.Pricing.CurrentPrice).ThenBy(item => item.Id),
            ProductSortMode.PriceDesc => items.OrderByDescending(item => item.Pricing.CurrentPrice).ThenBy(item => item.Id),
            ProductSortMode.HighestDiscount => items
                .OrderByDescending(item => item.PromotionDiscountValue ?? 0m)
                .ThenByDescending(item => item.Score),
            _ => items
        };
    }

    private static ProductDiscoveryPricingDto BuildPricingPresentation(
        decimal basePrice,
        bool hasActivePromotion,
        string? promotionBadge,
        string? promotionDiscountType,
        decimal? promotionDiscountValue)
    {
        var currentPrice = RoundCurrency(basePrice);
        if (currentPrice <= 0m || !hasActivePromotion || !promotionDiscountValue.HasValue || promotionDiscountValue.Value <= 0m)
        {
            return new ProductDiscoveryPricingDto
            {
                CurrentPrice = currentPrice
            };
        }

        var discountedPrice = promotionDiscountType?.Equals("percentage", StringComparison.OrdinalIgnoreCase) == true
            ? currentPrice * (1m - (promotionDiscountValue.Value / 100m))
            : promotionDiscountType?.Equals("fixed", StringComparison.OrdinalIgnoreCase) == true
                ? currentPrice - promotionDiscountValue.Value
                : currentPrice;

        discountedPrice = RoundCurrency(Math.Max(0m, discountedPrice));
        if (discountedPrice >= currentPrice)
        {
            return new ProductDiscoveryPricingDto
            {
                CurrentPrice = currentPrice,
                DiscountBadge = string.IsNullOrWhiteSpace(promotionBadge) ? null : promotionBadge.Trim()
            };
        }

        return new ProductDiscoveryPricingDto
        {
            CurrentPrice = discountedPrice,
            CompareAtPrice = currentPrice,
            DiscountedPrice = discountedPrice,
            DiscountBadge = string.IsNullOrWhiteSpace(promotionBadge) ? null : promotionBadge.Trim()
        };
    }

    private static decimal RoundCurrency(decimal amount) =>
        Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    private static ProductDiscoveryAvailabilityDto BuildAvailabilityPresentation(
        StorefrontPurchasabilityResult purchasability)
    {
        var canPurchase = purchasability.CanPurchase;
        return new ProductDiscoveryAvailabilityDto
        {
            IsAvailable = purchasability.IsAvailable,
            CanPurchase = canPurchase,
            CanAddToCart = canPurchase,
            CanBuyNow = canPurchase,
            StockLabel = BuildStockLabel(purchasability),
            MaximumQuantity = canPurchase && purchasability.StockQuantity > 0
                ? purchasability.StockQuantity
                : null,
            UnavailableReason = purchasability.UnavailableReason
        };
    }

    private static string BuildStockLabel(StorefrontPurchasabilityResult purchasability)
    {
        if (purchasability.RequiresVariantSelection)
        {
            return "Select options";
        }

        if (!purchasability.IsAvailable)
        {
            return "Unavailable";
        }

        if (!purchasability.InStock)
        {
            return "Out of stock";
        }

        return purchasability.StockQuantity > 0
            ? $"In Stock ({purchasability.StockQuantity})"
            : "In Stock";
    }

    /// <summary>
    /// Builds facets from SQL-authority-recomposed visible items.
    /// Ensures facet counts only reflect products the customer can actually purchase.
    /// </summary>
    private static ProductDiscoveryFacets BuildFacetsFromVisibleItems(
        IReadOnlyCollection<ProductDiscoveryItemDto> items)
    {
        var facets = new ProductDiscoveryFacets
        {
            Brands = items
                .Where(item => !string.IsNullOrWhiteSpace(item.Brand))
                .GroupBy(item => item.Brand!, StringComparer.OrdinalIgnoreCase)
                .Select(group => new FacetBucket
                {
                    Key = group.Key,
                    Count = group.LongCount()
                })
                .OrderByDescending(bucket => bucket.Count)
                .ThenBy(bucket => bucket.Key, StringComparer.OrdinalIgnoreCase)
                .ToList(),

            Categories = items
                .Where(item => item.CategoryId.HasValue && item.CategoryId.Value != Guid.Empty)
                .GroupBy(item => item.CategoryId!.Value)
                .Select(group => new CategoryFacetBucket
                {
                    Id = group.Key,
                    Name = group.First().CategoryName ?? group.Key.ToString(),
                    Count = group.LongCount()
                })
                .OrderByDescending(bucket => bucket.Count)
                .ThenBy(bucket => bucket.Name, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };

        var inStockCount = items.LongCount(item => item.InStock);
        var outOfStockCount = items.LongCount() - inStockCount;
        facets.Stock.InStockCount = inStockCount;
        facets.Stock.OutOfStockCount = outOfStockCount;

        var promotedCount = items.LongCount(item => item.HasActivePromotion);
        var nonPromotedCount = items.LongCount() - promotedCount;
        facets.Promotions.PromotedCount = promotedCount;
        facets.Promotions.NonPromotedCount = nonPromotedCount;

        var visiblePrices = items
            .Select(item => item.Pricing.CurrentPrice)
            .Where(price => price > 0)
            .ToList();

        if (visiblePrices.Count > 0)
        {
            facets.PriceRanges = new List<PriceRangeBucket>();
            foreach (var (from, to, label) in PriceRanges)
            {
                var count = visiblePrices.LongCount(price =>
                    (!from.HasValue || price >= from.Value) &&
                    (!to.HasValue || price < to.Value));
                if (count > 0)
                {
                    facets.PriceRanges.Add(new PriceRangeBucket
                    {
                        Label = label,
                        From = from,
                        To = to,
                        Count = count
                    });
                }
            }
        }

        return facets;
    }

    /// <summary>
    /// Maps Elasticsearch aggregations to ProductDiscoveryFacets.
    /// </summary>
    private ProductDiscoveryFacets MapFacets(
        AggregateDictionary aggregations,
        ProductDiscoveryQuery query)
    {
        var facets = new ProductDiscoveryFacets();

        // Brand facet
        if (aggregations.TryGetValue("brands", out var brandsAgg) && brandsAgg is BucketAggregate brandBuckets)
        {
            facets.Brands = brandBuckets.Items
                .OfType<KeyedBucket<object>>()
                .Where(b => b.Key != null)
                .Select(b => new FacetBucket
                {
                    Key = b.Key?.ToString() ?? string.Empty,
                    Count = b.DocCount.GetValueOrDefault()
                })
                .ToList();
        }

        // Category facet (IDs only - need to enrich with names)
        if (aggregations.TryGetValue("categories", out var categoriesAgg) && categoriesAgg is BucketAggregate categoryBuckets)
        {
            facets.Categories = categoryBuckets.Items
                .OfType<KeyedBucket<object>>()
                .Where(b => b.Key != null && Guid.TryParse(b.Key.ToString(), out _))
                .Select(b => new CategoryFacetBucket
                {
                    Id = Guid.TryParse(b.Key?.ToString(), out var id) ? id : Guid.Empty,
                    Name = b.Key?.ToString() ?? string.Empty, // Will need enrichment
                    Count = b.DocCount.GetValueOrDefault()
                })
                .Where(c => c.Id != Guid.Empty)
                .ToList();
        }

        // Promotion facet
        if (aggregations.TryGetValue("promotions", out var promotionsAgg) && promotionsAgg is BucketAggregate promotionBuckets)
        {
            foreach (var bucket in promotionBuckets.Items.OfType<KeyedBucket<object>>())
            {
                var keyStr = bucket.Key?.ToString() ?? "";
                if (bool.TryParse(keyStr, out var isPromoted) && isPromoted)
                    facets.Promotions.PromotedCount = bucket.DocCount.GetValueOrDefault();
                else
                    facets.Promotions.NonPromotedCount = bucket.DocCount.GetValueOrDefault();
            }
        }

        // Stock facet
        if (aggregations.TryGetValue("stock", out var stockAgg) && stockAgg is BucketAggregate stockBuckets)
        {
            foreach (var bucket in stockBuckets.Items.OfType<KeyedBucket<object>>())
            {
                var keyStr = bucket.Key?.ToString() ?? "";
                if (bool.TryParse(keyStr, out var inStock) && inStock)
                    facets.Stock.InStockCount = bucket.DocCount.GetValueOrDefault();
                else
                    facets.Stock.OutOfStockCount = bucket.DocCount.GetValueOrDefault();
            }
        }

        // Price range facet - nested inside filter aggregation
        if (aggregations.TryGetValue("price_ranges_filtered", out var priceFilteredAgg) && priceFilteredAgg is SingleBucketAggregate priceFilterBucket)
        {
            // Get the nested price_ranges aggregation from inside the filter
            if (priceFilterBucket.TryGetValue("price_ranges", out var priceAgg) && priceAgg is BucketAggregate priceBuckets)
            {
                facets.PriceRanges = priceBuckets.Items
                    .OfType<RangeBucket>()
                    .Select(b => new PriceRangeBucket
                    {
                        Label = MapPriceRangeLabel(b.Key ?? string.Empty),
                        From = b.From.HasValue ? (decimal)b.From.Value : null,
                        To = b.To.HasValue ? (decimal)b.To.Value : null,
                        Count = b.DocCount
                    })
                    .Where(r => r.Count > 0)
                    .ToList();
            }
        }

        return facets;
    }

    /// <summary>
    /// Maps price range key to display label.
    /// </summary>
    private static string MapPriceRangeLabel(string key)
    {
        return key switch
        {
            "under_50" => "Under 50",
            "50_100" => "50 - 100",
            "100_200" => "100 - 200",
            "200_500" => "200 - 500",
            "500_1000" => "500 - 1000",
            "over_1000" => "Over 1000",
            _ => key
        };
    }

    /// <summary>
    /// Builds promotion badge text.
    /// </summary>
    private static string? BuildPromotionBadge(double discountValue, string? discountType)
    {
        if (discountValue <= 0) return null;

        return discountType?.ToLowerInvariant() == "percentage"
            ? $"{discountValue:0}% OFF"
            : "SALE";
    }
}
