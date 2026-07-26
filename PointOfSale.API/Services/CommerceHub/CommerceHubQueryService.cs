using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MongoDB.Bson;
using MongoDB.Driver;
using PointOfSale.API.CommerceHub.Services;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.Services.CommerceHub.Finance;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Documents;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.API.Services.CommerceHub;

public interface ICommerceHubQueryService
{
    Task<CommerceHubProductListResponse> GetProductsAsync(CommerceHubProductsQuery query, CancellationToken cancellationToken = default);
    Task<CommerceHubSearchResponse> SearchAsync(
        CommerceHubSearchQuery query,
        IReadOnlyCollection<CommerceHubSearchFilter> filters,
        CancellationToken cancellationToken = default);
    Task<CommerceHubSearchSuggestionsResponse> GetSuggestionsAsync(
        CommerceHubSearchSuggestionsQuery query,
        CancellationToken cancellationToken = default);
    Task<CommerceHubAutocompleteResponse> GetAutocompleteAsync(
        CommerceHubSearchAutocompleteQuery query,
        CancellationToken cancellationToken = default);
    Task<CommerceHubPopularSearchesResponse> GetPopularQueriesAsync(
        CommerceHubPopularSearchesQuery query,
        CancellationToken cancellationToken = default);
    Task<CommerceHubProductDetailDto?> GetProductByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CommerceHubVariantResolutionResponse?> ResolveProductVariantAsync(
        Guid id,
        CommerceHubVariantResolutionRequest request,
        CancellationToken cancellationToken = default);
}

public class CommerceHubQueryService : ICommerceHubQueryService
{
    private const string AutoHealingReasonEsMissMongoHit = "ES_MISS_MONGO_HIT";
    private const string AutoHealingReasonEsPartialDrift = "ES_DRIFT_MONGO_MORE";
    private const string DriftLevelHealthy = "healthy";
    private const string DriftLevelComplete = "complete";
    private const string DriftLevelPartial = "partial";
    private const int MaxAutoHealingCandidatesPerRequest = 20;
    private static readonly TimeSpan PopularQueriesCacheTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan SuggestionsCacheTtl = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan AutocompleteCacheTtl = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan BrowseCountCacheTtl = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan CategoryMetadataCacheTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan SuppressedVendorCacheTtl = TimeSpan.FromMinutes(1);
    private const int DefaultFacetSortOrder = int.MaxValue;

    private readonly ICommerceHubReadRepository _repository;
    private readonly ICommerceHubSearchReadService _searchReadService;
    private readonly ICommerceHubSearchIntelligenceService _searchIntelligenceService;
    private readonly ICommerceHubSearchIntentDetector _intentDetector;
    private readonly ICommerceHubAutoHealingQueue _autoHealingQueue;
    private readonly MongoDbContext _mongoDbContext;
    private readonly ApplicationDbContext _dbContext;
    private readonly ICommerceHubVendorFinancialControlService _vendorFinancialControlService;
    private readonly ICommerceHubGovernanceCacheInvalidationService _governanceCacheInvalidationService;
    private readonly ITenantContext _tenantContext;
    private readonly IMemoryCache _cache;
    private readonly ILogger<CommerceHubQueryService> _logger;

    public CommerceHubQueryService(
        ICommerceHubReadRepository repository,
        ICommerceHubSearchReadService searchReadService,
        ICommerceHubSearchIntelligenceService searchIntelligenceService,
        ICommerceHubSearchIntentDetector intentDetector,
        ICommerceHubAutoHealingQueue autoHealingQueue,
        MongoDbContext mongoDbContext,
        ApplicationDbContext dbContext,
        ICommerceHubVendorFinancialControlService vendorFinancialControlService,
        ICommerceHubGovernanceCacheInvalidationService governanceCacheInvalidationService,
        ITenantContext tenantContext,
        IMemoryCache cache,
        ILogger<CommerceHubQueryService> logger)
    {
        _repository = repository;
        _searchReadService = searchReadService;
        _searchIntelligenceService = searchIntelligenceService;
        _intentDetector = intentDetector;
        _autoHealingQueue = autoHealingQueue;
        _mongoDbContext = mongoDbContext;
        _dbContext = dbContext;
        _vendorFinancialControlService = vendorFinancialControlService;
        _governanceCacheInvalidationService = governanceCacheInvalidationService;
        _tenantContext = tenantContext;
        _cache = cache;
        _logger = logger;
    }

    public async Task<CommerceHubProductListResponse> GetProductsAsync(
        CommerceHubProductsQuery query,
        CancellationToken cancellationToken = default)
    {
        var tenantId = ResolveTenantScope(query.TenantId);
        var request = NormalizeRequest(
            query.Page,
            query.PageSize,
            query.CategoryId,
            tenantId,
            query.InStock,
            query.MinPrice,
            query.MaxPrice,
            query.Search,
            query.Sort,
            Array.Empty<CommerceHubSearchFilter>(),
            Array.Empty<CommerceHubSearchFacetDefinition>(),
            CommerceHubSearchIntentContext.Unknown());
        request.ExcludedTenantIds = await LoadPubliclySuppressedVendorIdsAsync(tenantId, cancellationToken);

        var result = await ExecuteSearchAsync(request, cancellationToken);

        var visibleItems = await RecomposeVisibleListingItemsAsync(result.Items, cancellationToken);

        return new CommerceHubProductListResponse
        {
            Items = visibleItems.Select(MapListItem).ToList(),
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = visibleItems.Count
        };
    }

    public async Task<CommerceHubSearchResponse> SearchAsync(
        CommerceHubSearchQuery query,
        IReadOnlyCollection<CommerceHubSearchFilter> filters,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = NormalizeUserQuery(query.Q);
        var intent = await _intentDetector.DetectAsync(normalizedQuery, cancellationToken);
        var facetDefinitions = await LoadFacetDefinitionsAsync(query.CategoryId, cancellationToken);
        if (HasUnsupportedAttributeFilters(filters, facetDefinitions))
        {
            _logger.LogWarning(
                "CommerceHub search rejected unsupported attribute filters | CategoryId: {CategoryId} | Keys: {FilterKeys}",
                query.CategoryId,
                string.Join(",", filters.Select(filter => filter.Key)));

            return new CommerceHubSearchResponse
            {
                Items = new List<CommerceHubProductDto>(),
                Total = 0,
                Page = query.Page <= 0 ? 1 : query.Page,
                PageSize = query.PageSize <= 0 ? 20 : Math.Min(query.PageSize, 100),
                Intent = ToIntentValue(intent.Type),
                Facets = new CommerceHubSearchFacetsDto(),
                Aggregations = new Dictionary<string, CommerceHubAggregationDto>(StringComparer.OrdinalIgnoreCase)
            };
        }

        var tenantId = ResolveTenantScope(query.TenantId);
        var request = NormalizeRequest(
            query.Page,
            query.PageSize,
            query.CategoryId,
            tenantId,
            query.InStock,
            query.MinPrice,
            query.MaxPrice,
            normalizedQuery,
            query.Sort,
            filters,
            facetDefinitions,
            intent);
        request.ExcludedTenantIds = await LoadPubliclySuppressedVendorIdsAsync(tenantId, cancellationToken);

        var result = await ExecuteSearchAsync(request, cancellationToken);
        var initialResultCount = result.TotalCount;
        var appliedExpansions = Array.Empty<string>();

        if (result.TotalCount == 0 && !string.IsNullOrWhiteSpace(request.Query))
        {
            var expansions = await _searchIntelligenceService.ExpandQueryAsync(request.Query, cancellationToken);
            if (expansions.Count > 0)
            {
                request.QueryExpansions = expansions;
                appliedExpansions = expansions.ToArray();
                var expandedResult = await ExecuteSearchAsync(request, cancellationToken);
                if (expandedResult.TotalCount > 0)
                {
                    result = expandedResult;
                }
            }
        }

        _searchIntelligenceService.TrackQuery(new CommerceHubTrackedSearchQuery(
            request.Query ?? string.Empty,
            initialResultCount,
            ToIntentValue(intent.Type),
            appliedExpansions,
            DateTime.UtcNow));

        var visibleItems = await RecomposeVisibleListingItemsAsync(result.Items, cancellationToken);

        return new CommerceHubSearchResponse
        {
            Items = visibleItems.Select(MapListItem).ToList(),
            Total = visibleItems.Count,
            Page = request.Page,
            PageSize = request.PageSize,
            Intent = ToIntentValue(intent.Type),
            Facets = await MapSqlVisibleFacetsAsync(visibleItems, cancellationToken),
            Aggregations = new Dictionary<string, CommerceHubAggregationDto>(StringComparer.OrdinalIgnoreCase),
            ZeroResultSuggestions = result.ZeroResultSuggestions == null
                ? null
                : await MapZeroResultSuggestionsAsync(result.ZeroResultSuggestions, cancellationToken)
        };
    }

    public async Task<CommerceHubSearchSuggestionsResponse> GetSuggestionsAsync(
        CommerceHubSearchSuggestionsQuery query,
        CancellationToken cancellationToken = default)
    {
        var tenantId = ResolveTenantScope(query.TenantId);
        var request = new CommerceHubSearchSuggestionsRequest
        {
            Query = NormalizeUserQuery(query.Q),
            CategoryId = query.CategoryId,
            TenantId = tenantId,
            Limit = query.Limit <= 0 ? 8 : Math.Min(query.Limit, 10)
        };
        request.ExcludedTenantIds = await LoadPubliclySuppressedVendorIdsAsync(tenantId, cancellationToken);

        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return new CommerceHubSearchSuggestionsResponse();
        }

        var visibilityVersion = await _governanceCacheInvalidationService.GetVisibilityVersionTokenAsync(cancellationToken);
        var cacheKey = BuildSuggestionsCacheKey(request, visibilityVersion);
        if (_cache.TryGetValue(cacheKey, out CommerceHubSearchSuggestionsResponse? cachedResponse) &&
            cachedResponse != null)
        {
            return await RevalidateSuggestionsResponseAsync(cachedResponse, tenantId, cancellationToken);
        }

        var result = await _searchReadService.GetSuggestionsAsync(request, cancellationToken);
        if (result == null)
        {
            _logger.LogWarning(
                "CommerceHub suggestions falling back to MongoDB | Query: {Query} | CategoryId: {CategoryId} | TenantId: {TenantId}",
                request.Query,
                request.CategoryId,
                request.TenantId);

            result = await _repository.GetSuggestionsAsync(request, cancellationToken);
        }

        var visibleProducts = await RecomposeVisibleSuggestionProductsAsync(result.Products, cancellationToken);
        var filteredPopularTerms = await FilterLiveSuggestionTermsAsync(
            result.PopularTerms,
            tenantId,
            request.Limit,
            cancellationToken);
        var filteredSimilarTerms = await FilterLiveSuggestionTermsAsync(
            result.SimilarTerms,
            tenantId,
            request.Limit,
            cancellationToken);

        var response = new CommerceHubSearchSuggestionsResponse
        {
            Query = string.IsNullOrWhiteSpace(query.Q) ? result.Query : query.Q.Trim(),
            Products = visibleProducts.Select(MapSuggestionProduct).ToList(),
            PopularTerms = filteredPopularTerms,
            SimilarTerms = filteredSimilarTerms
        };

        _cache.Set(cacheKey, response, SuggestionsCacheTtl);
        return response;
    }

    public async Task<CommerceHubAutocompleteResponse> GetAutocompleteAsync(
        CommerceHubSearchAutocompleteQuery query,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = NormalizeUserQuery(query.Q);
        var normalizedProductLimit = query.ProductLimit <= 0 ? 5 : Math.Min(query.ProductLimit, 10);
        var normalizedCategoryLimit = query.CategoryLimit <= 0 ? 3 : Math.Min(query.CategoryLimit, 8);
        var normalizedSuggestionLimit = query.SuggestionLimit <= 0 ? 5 : Math.Min(query.SuggestionLimit, 10);
        var normalizedPopularLimit = query.PopularLimit <= 0 ? 5 : Math.Min(query.PopularLimit, 10);
        var tenantId = ResolveTenantScope(query.TenantId);

        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            var popularTerms = tenantId.HasValue
                ? new List<string>()
                : await GetPopularTermsAsync(
                    normalizedPopularLimit,
                    null,
                    cancellationToken);

            return new CommerceHubAutocompleteResponse
            {
                Query = string.Empty,
                Intent = ToIntentValue(CommerceHubSearchIntentType.Unknown),
                Popular = popularTerms
            };
        }

        if (!CommerceHubSearchTextUtility.HasAutocompleteQuery(normalizedQuery))
        {
            return new CommerceHubAutocompleteResponse
            {
                Query = normalizedQuery,
                Intent = ToIntentValue(CommerceHubSearchIntentType.Unknown)
            };
        }

        var intent = await _intentDetector.DetectAsync(normalizedQuery, cancellationToken);

        var request = new CommerceHubSearchAutocompleteRequest
        {
            Query = normalizedQuery,
            QueryExpansions = await _searchIntelligenceService.ExpandQueryAsync(normalizedQuery, cancellationToken),
            Intent = intent,
            CategoryId = query.CategoryId,
            TenantId = tenantId,
            ProductLimit = normalizedProductLimit,
            CategoryLimit = normalizedCategoryLimit,
            SuggestionLimit = normalizedSuggestionLimit
        };
        request.ExcludedTenantIds = await LoadPubliclySuppressedVendorIdsAsync(tenantId, cancellationToken);

        var visibilityVersion = await _governanceCacheInvalidationService.GetVisibilityVersionTokenAsync(cancellationToken);
        var cacheKey = BuildAutocompleteCacheKey(request, normalizedPopularLimit, visibilityVersion);
        if (_cache.TryGetValue(cacheKey, out CommerceHubAutocompleteResponse? cachedResponse) &&
            cachedResponse != null)
        {
            return await RevalidateAutocompleteResponseAsync(cachedResponse, tenantId, normalizedPopularLimit, cancellationToken);
        }

        var result = await _searchReadService.GetAutocompleteAsync(request, cancellationToken);
        if (result == null)
        {
            _logger.LogWarning(
                "CommerceHub autocomplete falling back to MongoDB | Query: {Query} | CategoryId: {CategoryId} | TenantId: {TenantId}",
                request.Query,
                request.CategoryId,
                request.TenantId);

            result = await _repository.GetAutocompleteAsync(request, cancellationToken);
        }

        var popular = tenantId.HasValue
            ? new List<string>()
            : await GetPopularTermsAsync(
                normalizedPopularLimit,
                normalizedQuery,
                cancellationToken);

        var visibleProducts = await RecomposeVisibleAutocompleteProductsAsync(result.Products, cancellationToken);
        var categories = await MapAutocompleteCategoriesAsync(result.Categories, cancellationToken);
        var excludedText = visibleProducts
            .Select(product => product.ProductName)
            .Concat(categories.Select(category => category.CategoryName))
            .ToList();
        var suggestions = CommerceHubSearchTextUtility.DeduplicateTerms(
            result.SuggestedTerms.Concat(request.QueryExpansions),
            excludedText,
            normalizedSuggestionLimit);
        suggestions = await FilterLiveSuggestionTermsAsync(
            suggestions,
            tenantId,
            normalizedSuggestionLimit,
            cancellationToken);
        var dedupedPopular = CommerceHubSearchTextUtility.DeduplicateTerms(
            popular,
            excludedText.Concat(suggestions),
            normalizedPopularLimit);
        dedupedPopular = await FilterLiveSuggestionTermsAsync(
            dedupedPopular,
            tenantId,
            normalizedPopularLimit,
            cancellationToken);

        var response = new CommerceHubAutocompleteResponse
        {
            Query = string.IsNullOrWhiteSpace(query.Q) ? normalizedQuery : query.Q.Trim(),
            Intent = ToIntentValue(intent.Type),
            Products = visibleProducts.Select(MapAutocompleteProduct).ToList(),
            Categories = categories,
            Suggestions = suggestions,
            Popular = dedupedPopular
        };

        _cache.Set(cacheKey, response, AutocompleteCacheTtl);
        return response;
    }

    public async Task<CommerceHubPopularSearchesResponse> GetPopularQueriesAsync(
        CommerceHubPopularSearchesQuery query,
        CancellationToken cancellationToken = default)
    {
        var normalizedDays = query.Days <= 0 ? 7 : Math.Min(query.Days, 90);
        var normalizedLimit = query.Limit <= 0 ? 10 : Math.Min(query.Limit, 50);
        var visibilityVersion = await _governanceCacheInvalidationService.GetVisibilityVersionTokenAsync(cancellationToken);
        var cacheKey = $"commercehub:popular:{visibilityVersion}:{normalizedDays}:{normalizedLimit}";

        if (_cache.TryGetValue(cacheKey, out CommerceHubPopularSearchesResponse? cachedResponse) &&
            cachedResponse != null)
        {
            return await RevalidatePopularQueriesResponseAsync(cachedResponse, normalizedLimit, cancellationToken);
        }

        var cutoff = DateTime.UtcNow.AddDays(-normalizedDays);
        var topSearches = await _dbContext.CommerceHubSearchQueries
            .AsNoTracking()
            .Where(x => !string.IsNullOrWhiteSpace(x.QueryText) && x.SearchedAt >= cutoff)
            .GroupBy(x => x.QueryText)
            .Select(group => new
            {
                QueryText = group.Key,
                SearchCount = group.LongCount(),
                ZeroResultCount = group.Sum(item => item.ResultCount == 0 ? 1L : 0L)
            })
            .OrderByDescending(group => group.SearchCount)
            .ThenBy(group => group.QueryText)
            .Take(normalizedLimit * 4)
            .ToListAsync(cancellationToken);

        var queryTexts = topSearches
            .Select(item => item.QueryText)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();

        var clickCounts = queryTexts.Count == 0
            ? new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
            : (await _dbContext.CommerceHubSearchClicks
                .AsNoTracking()
                .Where(x => x.ClickedAt >= cutoff && queryTexts.Contains(x.QueryText))
                .GroupBy(x => x.QueryText)
                .Select(group => new
                {
                    QueryText = group.Key,
                    ClickCount = group.LongCount()
                })
                .ToListAsync(cancellationToken))
                .ToDictionary(
                    item => item.QueryText,
                    item => item.ClickCount,
                    StringComparer.OrdinalIgnoreCase);

        var liveQueryTexts = await FilterLiveSuggestionTermsAsync(
            queryTexts,
            tenantId: null,
            normalizedLimit,
            cancellationToken);
        var liveQuerySet = liveQueryTexts.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var response = new CommerceHubPopularSearchesResponse
        {
            Days = normalizedDays,
            Items = topSearches
                .Where(item => liveQuerySet.Contains(item.QueryText))
                .Select(item =>
                {
                    clickCounts.TryGetValue(item.QueryText, out var clickCount);
                    var ctr = item.SearchCount <= 0 ? 0 : (double)clickCount / item.SearchCount;

                    return new CommerceHubPopularSearchQueryDto
                    {
                        Query = item.QueryText,
                        SearchCount = item.SearchCount,
                        ZeroResultCount = item.ZeroResultCount,
                        ClickCount = clickCount,
                        ClickThroughRate = Math.Round(ctr, 4)
                    };
                })
                .Take(normalizedLimit)
                .ToList()
        };

        _cache.Set(cacheKey, response, PopularQueriesCacheTtl);
        return response;
    }

    public async Task<CommerceHubProductDetailDto?> GetProductByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tenantId = ResolveTenantScope(null);
        var listing = await _repository.GetPublishedProductByIdAsync(id, tenantId, cancellationToken);
        if (listing == null)
        {
            return null;
        }

        if (await IsVendorSuppressedAsync(listing.TenantId, cancellationToken))
        {
            return null;
        }

        EnsureTenantOwnedEntity(listing.TenantId, "CommerceHubListing", listing.Id);

        var sqlAuthority = await CommerceHubSearchSqlAuthorityOverlay.ApplyAsync(
            _dbContext,
            new CommerceHubProductDocument
            {
                Id = listing.Id,
                TenantId = listing.TenantId
            },
            listing.TenantId,
            cancellationToken);
        if (sqlAuthority == null)
        {
            return null;
        }

        var product = await _mongoDbContext.Products
            .Find(x => x.TenantId == sqlAuthority.TenantId && x.ProductId == sqlAuthority.ProductId)
            .FirstOrDefaultAsync(cancellationToken);

        return MapDetail(sqlAuthority, product);
    }

    public async Task<CommerceHubVariantResolutionResponse?> ResolveProductVariantAsync(
        Guid id,
        CommerceHubVariantResolutionRequest request,
        CancellationToken cancellationToken = default)
    {
        var product = await GetProductByIdAsync(id, cancellationToken);
        if (product == null || product.Variants.Count == 0)
        {
            return null;
        }

        var selectedOptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var option in request.SelectedOptions ?? new Dictionary<string, string>())
        {
            if (string.IsNullOrWhiteSpace(option.Key) || string.IsNullOrWhiteSpace(option.Value))
            {
                continue;
            }

            var key = option.Key.Trim();
            if (selectedOptions.ContainsKey(key))
            {
                return null;
            }

            selectedOptions[key] = option.Value.Trim();
        }

        var resolvedVariant = selectedOptions.Count == 0
            ? ResolveDefaultVariant(product)
            : ResolveExactOptionCombination(product.Variants, selectedOptions);

        if (resolvedVariant == null)
        {
            return null;
        }

        return new CommerceHubVariantResolutionResponse
        {
            ProductId = product.ProductId,
            ListingId = product.Id,
            ResolvedVariantId = resolvedVariant.Id,
            Variant = resolvedVariant
        };
    }

    private async Task<CommerceHubSearchReadResult> ExecuteSearchAsync(
        CommerceHubSearchRequest request,
        CancellationToken cancellationToken)
    {
        var searchResult = await _searchReadService.SearchPublishedProductsAsync(request, cancellationToken);
        if (searchResult != null)
        {
            var mongoFallback = await TryUseMongoFallbackAsync(request, searchResult, cancellationToken);
            var effectiveResult = mongoFallback ?? searchResult;
            effectiveResult.ZeroResultSuggestions ??= await BuildZeroResultSuggestionsAsync(effectiveResult, request, cancellationToken);
            return effectiveResult;
        }

        _logger.LogWarning(
            "CommerceHub search falling back to MongoDB | Query: {Query} | CategoryId: {CategoryId} | TenantId: {TenantId}",
            request.Query,
            request.CategoryId,
            request.TenantId);

        var fallbackResult = await _repository.SearchPublishedProductsAsync(request, cancellationToken);
        fallbackResult.ZeroResultSuggestions ??= await BuildZeroResultSuggestionsAsync(fallbackResult, request, cancellationToken);
        return fallbackResult;
    }

    private async Task<List<CommerceHubProductDocument>> RecomposeVisibleListingItemsAsync(
        IReadOnlyCollection<CommerceHubProductDocument> items,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return new List<CommerceHubProductDocument>();
        }

        var hiddenVendorIds = await LoadGovernanceHiddenVendorIdsAsync(
            items.Select(item => item.TenantId),
            cancellationToken);
        var suspendedVendorIds = await LoadSuspendedVendorIdsAsync(
            items.Select(item => item.TenantId),
            cancellationToken);

        var visibleItems = new List<CommerceHubProductDocument>(items.Count);
        foreach (var item in items)
        {
            if (hiddenVendorIds.Contains(item.TenantId) ||
                suspendedVendorIds.Contains(item.TenantId))
            {
                continue;
            }

            var sqlAuthority = await CommerceHubSearchSqlAuthorityOverlay.ApplyAsync(
                _dbContext,
                item,
                item.TenantId,
                cancellationToken);
            if (sqlAuthority != null)
            {
                visibleItems.Add(sqlAuthority);
            }
        }

        return visibleItems;
    }

    private async Task<CommerceHubSearchSuggestionsResponse> RevalidateSuggestionsResponseAsync(
        CommerceHubSearchSuggestionsResponse response,
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        var products = response.Products
            .Select(product => new CommerceHubSearchSuggestionProduct
            {
                Id = product.Id,
                ProductId = product.ProductId,
                VariantId = product.VariantId,
                ProductName = product.ProductName,
                Image = product.Image,
                Price = product.Price,
                Currency = product.Currency,
                CategoryId = product.CategoryId,
                CategoryName = product.CategoryName,
                StoreName = product.StoreName
            })
            .ToList();

        var visibleProducts = await RecomposeVisibleSuggestionProductsAsync(products, cancellationToken);

        return new CommerceHubSearchSuggestionsResponse
        {
            Query = response.Query,
            Products = visibleProducts.Select(MapSuggestionProduct).ToList(),
            PopularTerms = await FilterLiveSuggestionTermsAsync(
                response.PopularTerms,
                tenantId,
                response.PopularTerms.Count,
                cancellationToken),
            SimilarTerms = await FilterLiveSuggestionTermsAsync(
                response.SimilarTerms,
                tenantId,
                response.SimilarTerms.Count,
                cancellationToken)
        };
    }

    private async Task<CommerceHubAutocompleteResponse> RevalidateAutocompleteResponseAsync(
        CommerceHubAutocompleteResponse response,
        Guid? tenantId,
        int popularLimit,
        CancellationToken cancellationToken)
    {
        var products = response.Products
            .Select(product => new CommerceHubSearchAutocompleteProduct
            {
                Id = product.Id,
                ProductId = product.ProductId,
                VariantId = product.VariantId,
                ProductName = product.ProductName,
                Image = product.Image,
                Price = product.Price,
                Currency = product.Currency,
                StoreName = product.StoreName,
                CategoryId = product.CategoryId,
                CategoryName = product.CategoryName
            })
            .ToList();

        var visibleProducts = await RecomposeVisibleAutocompleteProductsAsync(products, cancellationToken);
        var categories = await FilterAutocompleteCategoryDtosAsync(response.Categories, cancellationToken);
        var excludedText = visibleProducts
            .Select(product => product.ProductName)
            .Concat(categories.Select(category => category.CategoryName))
            .ToList();
        var suggestions = CommerceHubSearchTextUtility.DeduplicateTerms(
            response.Suggestions,
            excludedText,
            response.Suggestions.Count);
        suggestions = await FilterLiveSuggestionTermsAsync(
            suggestions,
            tenantId,
            response.Suggestions.Count,
            cancellationToken);
        var popular = CommerceHubSearchTextUtility.DeduplicateTerms(
            response.Popular,
            excludedText.Concat(suggestions),
            popularLimit);
        popular = await FilterLiveSuggestionTermsAsync(
            popular,
            tenantId,
            popularLimit,
            cancellationToken);

        return new CommerceHubAutocompleteResponse
        {
            Query = response.Query,
            Intent = response.Intent,
            Products = visibleProducts.Select(MapAutocompleteProduct).ToList(),
            Categories = categories,
            Suggestions = suggestions,
            Popular = popular
        };
    }

    private async Task<CommerceHubPopularSearchesResponse> RevalidatePopularQueriesResponseAsync(
        CommerceHubPopularSearchesResponse response,
        int limit,
        CancellationToken cancellationToken)
    {
        var liveQueryTexts = await FilterLiveSuggestionTermsAsync(
            response.Items.Select(item => item.Query),
            tenantId: null,
            limit,
            cancellationToken);
        var liveQuerySet = liveQueryTexts.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new CommerceHubPopularSearchesResponse
        {
            Days = response.Days,
            Items = response.Items
                .Where(item => liveQuerySet.Contains(item.Query))
                .Take(limit)
                .ToList()
        };
    }

    private async Task<List<CommerceHubSearchSuggestionProduct>> RecomposeVisibleSuggestionProductsAsync(
        IReadOnlyCollection<CommerceHubSearchSuggestionProduct> products,
        CancellationToken cancellationToken)
    {
        if (products.Count == 0)
        {
            return new List<CommerceHubSearchSuggestionProduct>();
        }

        var tenantIdsByListingId = await LoadSqlTenantIdsByListingIdAsync(
            products.Select(product => product.Id),
            cancellationToken);
        var suppressedVendorIds = await LoadPubliclySuppressedVendorIdsAsync(
            tenantIdsByListingId.Values,
            cancellationToken);

        var visibleProducts = new List<CommerceHubSearchSuggestionProduct>();
        foreach (var product in products)
        {
            if (!tenantIdsByListingId.TryGetValue(product.Id, out var tenantId) ||
                suppressedVendorIds.Contains(tenantId))
            {
                continue;
            }

            var sqlAuthority = await CommerceHubSearchSqlAuthorityOverlay.ApplyAsync(
                _dbContext,
                new CommerceHubProductDocument
                {
                    Id = product.Id,
                    TenantId = tenantId
                },
                tenantId,
                cancellationToken);

            if (sqlAuthority == null)
            {
                continue;
            }

            visibleProducts.Add(new CommerceHubSearchSuggestionProduct
            {
                Id = sqlAuthority.Id,
                ProductId = sqlAuthority.ProductId,
                VariantId = sqlAuthority.VariantId,
                ProductName = sqlAuthority.ProductName,
                Image = sqlAuthority.Image,
                Price = sqlAuthority.Price,
                Currency = sqlAuthority.Currency,
                CategoryId = sqlAuthority.CategoryId,
                CategoryName = sqlAuthority.CategoryName,
                StoreName = sqlAuthority.StoreName
            });
        }

        return visibleProducts;
    }

    private async Task<List<CommerceHubSearchAutocompleteProduct>> RecomposeVisibleAutocompleteProductsAsync(
        IReadOnlyCollection<CommerceHubSearchAutocompleteProduct> products,
        CancellationToken cancellationToken)
    {
        if (products.Count == 0)
        {
            return new List<CommerceHubSearchAutocompleteProduct>();
        }

        var tenantIdsByListingId = await LoadSqlTenantIdsByListingIdAsync(
            products.Select(product => product.Id),
            cancellationToken);
        var suppressedVendorIds = await LoadPubliclySuppressedVendorIdsAsync(
            tenantIdsByListingId.Values,
            cancellationToken);

        var visibleProducts = new List<CommerceHubSearchAutocompleteProduct>();
        foreach (var product in products)
        {
            if (!tenantIdsByListingId.TryGetValue(product.Id, out var tenantId) ||
                suppressedVendorIds.Contains(tenantId))
            {
                continue;
            }

            var sqlAuthority = await CommerceHubSearchSqlAuthorityOverlay.ApplyAsync(
                _dbContext,
                new CommerceHubProductDocument
                {
                    Id = product.Id,
                    TenantId = tenantId
                },
                tenantId,
                cancellationToken);

            if (sqlAuthority == null)
            {
                continue;
            }

            visibleProducts.Add(new CommerceHubSearchAutocompleteProduct
            {
                Id = sqlAuthority.Id,
                ProductId = sqlAuthority.ProductId,
                VariantId = sqlAuthority.VariantId,
                ProductName = sqlAuthority.ProductName,
                Image = sqlAuthority.Image,
                Price = sqlAuthority.Price,
                Currency = sqlAuthority.Currency,
                StoreName = sqlAuthority.StoreName,
                CategoryId = sqlAuthority.CategoryId,
                CategoryName = sqlAuthority.CategoryName
            });
        }

        return visibleProducts;
    }

    private async Task<Dictionary<Guid, Guid>> LoadSqlTenantIdsByListingIdAsync(
        IEnumerable<Guid> listingIds,
        CancellationToken cancellationToken)
    {
        var normalizedListingIds = listingIds
            .Where(listingId => listingId != Guid.Empty)
            .Distinct()
            .ToList();

        if (normalizedListingIds.Count == 0)
        {
            return new Dictionary<Guid, Guid>();
        }

        var documents = await _dbContext.CommerceHubListings
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(listing => normalizedListingIds.Contains(listing.Id))
            .Select(listing => new { listing.Id, listing.TenantId })
            .ToListAsync(cancellationToken);

        return documents.ToDictionary(document => document.Id, document => document.TenantId);
    }

    private async Task<HashSet<Guid>> LoadSuspendedVendorIdsAsync(
        IEnumerable<Guid> vendorIds,
        CancellationToken cancellationToken)
    {
        var normalizedVendorIds = vendorIds
            .Where(vendorId => vendorId != Guid.Empty)
            .Distinct()
            .ToList();

        if (normalizedVendorIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var suspendedVendorIds = await _vendorFinancialControlService.GetVendorsSuspendedFromNewOrdersAsync(
            normalizedVendorIds,
            cancellationToken);

        return suspendedVendorIds.ToHashSet();
    }

    private async Task<HashSet<Guid>> LoadPubliclySuppressedVendorIdsAsync(
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        if (tenantId.HasValue)
        {
            return await LoadPubliclySuppressedVendorIdsAsync(
                new[] { tenantId.Value },
                cancellationToken);
        }

        var visibilityVersion = await _governanceCacheInvalidationService.GetVisibilityVersionTokenAsync(cancellationToken);
        var cacheKey = $"commercehub:suppressed-vendors:{visibilityVersion}";
        if (_cache.TryGetValue(cacheKey, out HashSet<Guid>? cachedSuppressedVendorIds) &&
            cachedSuppressedVendorIds != null)
        {
            return cachedSuppressedVendorIds;
        }

        var currentListingVendorIds = await _dbContext.CommerceHubListings
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(listing => !listing.IsDeleted && listing.IsPublished)
            .Select(listing => listing.TenantId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var suppressedVendorIds = await LoadPubliclySuppressedVendorIdsAsync(
            currentListingVendorIds,
            cancellationToken);

        _cache.Set(cacheKey, suppressedVendorIds, SuppressedVendorCacheTtl);
        return suppressedVendorIds;
    }

    private async Task<HashSet<Guid>> LoadPubliclySuppressedVendorIdsAsync(
        IEnumerable<Guid> vendorIds,
        CancellationToken cancellationToken)
    {
        var governanceHiddenVendorIds = await LoadGovernanceHiddenVendorIdsAsync(
            vendorIds,
            cancellationToken);
        var suspendedVendorIds = await LoadSuspendedVendorIdsAsync(
            vendorIds,
            cancellationToken);

        governanceHiddenVendorIds.UnionWith(suspendedVendorIds);
        return governanceHiddenVendorIds;
    }

    private async Task<HashSet<Guid>> LoadGovernanceHiddenVendorIdsAsync(
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        var vendorIds = tenantId.HasValue
            ? new List<Guid> { tenantId.Value }
            : await _dbContext.CommerceHubListings
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(listing => !listing.IsDeleted && listing.IsPublished)
                .Select(listing => listing.TenantId)
                .Distinct()
                .ToListAsync(cancellationToken);

        var eligibleVendorIds = await CommerceHubVendorAuthority.LoadEligibleVendorIdsAsync(
            _dbContext,
            vendorIds,
            cancellationToken);

        return vendorIds
            .Where(vendorId => vendorId != Guid.Empty && !eligibleVendorIds.Contains(vendorId))
            .ToHashSet();
    }

    private async Task<HashSet<Guid>> LoadGovernanceHiddenVendorIdsAsync(
        IEnumerable<Guid> vendorIds,
        CancellationToken cancellationToken)
    {
        var normalizedVendorIds = vendorIds
            .Where(vendorId => vendorId != Guid.Empty)
            .Distinct()
            .ToList();

        if (normalizedVendorIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var eligibleVendorIds = await CommerceHubVendorAuthority.LoadEligibleVendorIdsAsync(
            _dbContext,
            normalizedVendorIds,
            cancellationToken);

        return normalizedVendorIds
            .Where(vendorId => !eligibleVendorIds.Contains(vendorId))
            .ToHashSet();
    }

    private async Task<bool> IsVendorSuppressedAsync(Guid vendorId, CancellationToken cancellationToken)
    {
        if (vendorId == Guid.Empty)
        {
            return false;
        }

        var suppressedVendorIds = await LoadPubliclySuppressedVendorIdsAsync(
            new[] { vendorId },
            cancellationToken);
        return suppressedVendorIds.Contains(vendorId);
    }

    private async Task<CommerceHubSearchReadResult?> TryUseMongoFallbackAsync(
        CommerceHubSearchRequest request,
        CommerceHubSearchReadResult elasticsearchResult,
        CancellationToken cancellationToken)
    {
        if (elasticsearchResult.TotalCount == 0)
        {
            var mongoResult = await _repository.SearchPublishedProductsAsync(request, cancellationToken);
            if (mongoResult.TotalCount > elasticsearchResult.TotalCount)
            {
                TriggerAutoHealing(
                    elasticsearchResult,
                    mongoResult,
                    request,
                    AutoHealingReasonEsMissMongoHit,
                    DriftLevelComplete);
                _logger.LogWarning(
                    "CommerceHub search fell back to MongoDB because Elasticsearch is completely out of sync with the read model | DriftLevel: {DriftLevel} | Query: {Query} | CategoryId: {CategoryId} | TenantId: {TenantId} | ElasticsearchTotal: {ElasticsearchTotal} | MongoTotal: {MongoTotal}",
                    DriftLevelComplete,
                    request.Query,
                    request.CategoryId,
                    request.TenantId,
                    elasticsearchResult.TotalCount,
                    mongoResult.TotalCount);

                return mongoResult;
            }

            return null;
        }

        if (!IsBrowseDiscoveryRequest(request))
        {
            return null;
        }

        var mongoPublishedCount = await CountPublishedProductsAsync(request, cancellationToken);
        if (mongoPublishedCount == elasticsearchResult.TotalCount)
        {
            return null;
        }

        var fallbackResult = await _repository.SearchPublishedProductsAsync(request, cancellationToken);
        if (fallbackResult.TotalCount > elasticsearchResult.TotalCount)
        {
            TriggerAutoHealing(
                elasticsearchResult,
                fallbackResult,
                request,
                AutoHealingReasonEsPartialDrift,
                DriftLevelPartial);
        }

        _logger.LogWarning(
            "CommerceHub search fell back to MongoDB because Elasticsearch is partially out of sync with the read model | DriftLevel: {DriftLevel} | Query: {Query} | CategoryId: {CategoryId} | TenantId: {TenantId} | ElasticsearchTotal: {ElasticsearchTotal} | MongoTotal: {MongoTotal}",
            GetDriftLevel(elasticsearchResult.TotalCount, fallbackResult.TotalCount),
            request.Query,
            request.CategoryId,
            request.TenantId,
            elasticsearchResult.TotalCount,
            fallbackResult.TotalCount);

        return fallbackResult;
    }

    private void TriggerAutoHealing(
        CommerceHubSearchReadResult elasticsearchResult,
        CommerceHubSearchReadResult mongoResult,
        CommerceHubSearchRequest request,
        string reason,
        string driftLevel)
    {
        if (mongoResult.TotalCount <= elasticsearchResult.TotalCount)
        {
            return;
        }

        var elasticsearchIds = elasticsearchResult.Items
            .Select(item => item.Id)
            .Where(id => id != Guid.Empty)
            .ToHashSet();

        var missingListings = mongoResult.Items
            .Where(item => item.Id != Guid.Empty)
            .Where(item => !elasticsearchIds.Contains(item.Id))
            .OrderByDescending(item => item.UpdatedAtUtc)
            .Take(MaxAutoHealingCandidatesPerRequest)
            .ToList();

        if (missingListings.Count == 0)
        {
            return;
        }

        var listingIds = missingListings
            .Select(item => item.Id)
            .Distinct()
            .ToList();

        if (listingIds.Count == 0)
        {
            return;
        }

        var enqueuedCount = _autoHealingQueue.Enqueue(new CommerceHubAutoHealingTask
        {
            ListingIds = listingIds,
            Reason = reason,
            TriggeredAtUtc = DateTime.UtcNow,
            Query = request.Query,
            CategoryId = request.CategoryId,
            TenantId = request.TenantId,
            IsHighPriority = true
        });

        _logger.LogInformation(
            "[AutoHealing] {DriftLevel} drift detected -> enqueued {CandidateCount} listings | Enqueued: {EnqueuedCount} | Reason: {Reason} | Query: {Query} | CategoryId: {CategoryId} | TenantId: {TenantId} | ElasticsearchTotal: {ElasticsearchTotal} | MongoTotal: {MongoTotal}",
            driftLevel,
            missingListings.Count,
            enqueuedCount,
            reason,
            request.Query,
            request.CategoryId,
            request.TenantId,
            elasticsearchResult.TotalCount,
            mongoResult.TotalCount);
    }

    private static string GetDriftLevel(long elasticsearchTotal, long mongoTotal) =>
        mongoTotal == elasticsearchTotal
            ? DriftLevelHealthy
            : elasticsearchTotal == 0 && mongoTotal > 0
                ? DriftLevelComplete
                : mongoTotal > elasticsearchTotal
                    ? DriftLevelPartial
                    : DriftLevelHealthy;

    private async Task<long> CountPublishedProductsAsync(
        CommerceHubSearchRequest request,
        CancellationToken cancellationToken)
    {
        var visibilityVersion = await _governanceCacheInvalidationService.GetVisibilityVersionTokenAsync(cancellationToken);
        var cacheKey = BuildBrowseCountCacheKey(request, visibilityVersion);
        if (_cache.TryGetValue(cacheKey, out long? cachedCount) && cachedCount.HasValue)
        {
            return cachedCount.Value;
        }

        var filters = new List<FilterDefinition<CommerceHubProductDocument>>
        {
            Builders<CommerceHubProductDocument>.Filter.Eq(x => x.IsPublished, true)
        };

        if (request.TenantId.HasValue)
        {
            filters.Add(Builders<CommerceHubProductDocument>.Filter.Eq(x => x.TenantId, request.TenantId.Value));
        }

        if (request.ExcludedTenantIds.Count > 0)
        {
            filters.Add(Builders<CommerceHubProductDocument>.Filter.Nin(x => x.TenantId, request.ExcludedTenantIds));
        }

        if (request.CategoryId.HasValue)
        {
            filters.Add(Builders<CommerceHubProductDocument>.Filter.Eq(x => x.CategoryId, request.CategoryId.Value));
        }

        if (request.InStock.HasValue)
        {
            filters.Add(Builders<CommerceHubProductDocument>.Filter.Eq(x => x.InStock, request.InStock.Value));
        }

        if (request.MinPrice.HasValue)
        {
            filters.Add(Builders<CommerceHubProductDocument>.Filter.Gte(x => x.Price, request.MinPrice.Value));
        }

        if (request.MaxPrice.HasValue)
        {
            filters.Add(Builders<CommerceHubProductDocument>.Filter.Lte(x => x.Price, request.MaxPrice.Value));
        }

        var filter = filters.Count == 1
            ? filters[0]
            : Builders<CommerceHubProductDocument>.Filter.And(filters);

        var count = await _mongoDbContext.CommerceHubProducts
            .CountDocumentsAsync(filter, cancellationToken: cancellationToken);

        _cache.Set(cacheKey, count, BrowseCountCacheTtl);
        return count;
    }

    private static bool IsBrowseDiscoveryRequest(CommerceHubSearchRequest request) =>
        string.IsNullOrWhiteSpace(request.Query) && request.Filters.Count == 0;

    private static string BuildBrowseCountCacheKey(
        CommerceHubSearchRequest request,
        string visibilityVersion) =>
        $"commercehub:browse-count:{visibilityVersion}:{request.TenantId?.ToString("N") ?? "all"}:{request.CategoryId?.ToString("N") ?? "all"}:{request.InStock?.ToString() ?? "all"}:{request.MinPrice?.ToString(CultureInfo.InvariantCulture) ?? "none"}:{request.MaxPrice?.ToString(CultureInfo.InvariantCulture) ?? "none"}:{BuildVendorExclusionFingerprint(request.ExcludedTenantIds)}";

    private async Task<IReadOnlyCollection<CommerceHubSearchFacetDefinition>> LoadFacetDefinitionsAsync(
        Guid? categoryId,
        CancellationToken cancellationToken)
    {
        IReadOnlySet<Guid> liveCategoryIds;
        if (categoryId.HasValue)
        {
            liveCategoryIds = await CommerceHubCategoryLineageAuthority.LoadLiveIdsAsync(
                _dbContext,
                new[] { categoryId.Value },
                cancellationToken);

            if (!liveCategoryIds.Contains(categoryId.Value))
            {
                return Array.Empty<CommerceHubSearchFacetDefinition>();
            }
        }
        else
        {
            var candidateIds = await _dbContext.CommerceHubCategories
                .AsNoTracking()
                .Where(category => category.IsActive)
                .Select(category => category.Id)
                .ToListAsync(cancellationToken);

            liveCategoryIds = await CommerceHubCategoryLineageAuthority.LoadLiveIdsAsync(
                _dbContext,
                candidateIds,
                cancellationToken);
        }

        var query = _dbContext.CommerceHubCategoryAttributes
            .AsNoTracking()
            .Where(x => x.IsFilterable && liveCategoryIds.Contains(x.CategoryId));

        var attributes = await query
            .Select(x => new
            {
                x.Name,
                x.Type,
                x.SortOrder
            })
            .ToListAsync(cancellationToken);

        return attributes
            .GroupBy(x => CommerceHubAttributeService.NormalizeKey(x.Name), StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .Select(group => new CommerceHubSearchFacetDefinition
            {
                Key = group.Key,
                DisplayName = group.First().Name,
                Type = ResolveFacetType(group.Select(x => x.Type)),
                SortOrder = NormalizeFacetSortOrder(group.Min(x => x.SortOrder))
            })
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool HasUnsupportedAttributeFilters(
        IReadOnlyCollection<CommerceHubSearchFilter> filters,
        IReadOnlyCollection<CommerceHubSearchFacetDefinition> facetDefinitions)
    {
        if (filters.Count == 0)
        {
            return false;
        }

        var allowedKeys = facetDefinitions
            .Select(definition => definition.Key)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return filters.Any(filter =>
            string.IsNullOrWhiteSpace(filter.Key) ||
            !allowedKeys.Contains(CommerceHubAttributeService.NormalizeKey(filter.Key)));
    }

    private static string ResolveFacetType(IEnumerable<string> types)
    {
        var normalized = types
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .Select(type => type.Trim().ToLowerInvariant())
            .ToList();

        if (normalized.Contains("number"))
        {
            return "number";
        }

        if (normalized.Contains("select"))
        {
            return "select";
        }

        return "text";
    }

    private static CommerceHubSearchRequest NormalizeRequest(
        int page,
        int pageSize,
        Guid? categoryId,
        Guid? tenantId,
        bool? inStock,
        decimal? minPrice,
        decimal? maxPrice,
        string? query,
        string? sort,
        IReadOnlyCollection<CommerceHubSearchFilter> filters,
        IReadOnlyCollection<CommerceHubSearchFacetDefinition> facetDefinitions,
        CommerceHubSearchIntentContext intent)
    {
        var normalizedMinPrice = minPrice < 0 ? 0 : minPrice;
        var normalizedMaxPrice = maxPrice < 0 ? null : maxPrice;

        if (!normalizedMinPrice.HasValue && intent.MinPrice.HasValue)
        {
            normalizedMinPrice = intent.MinPrice.Value;
        }

        if (!normalizedMaxPrice.HasValue && intent.MaxPrice.HasValue)
        {
            normalizedMaxPrice = intent.MaxPrice.Value;
        }

        if (normalizedMinPrice.HasValue && normalizedMaxPrice.HasValue && normalizedMinPrice > normalizedMaxPrice)
        {
            (normalizedMinPrice, normalizedMaxPrice) = (normalizedMaxPrice, normalizedMinPrice);
        }

        return new CommerceHubSearchRequest
        {
            Page = page <= 0 ? 1 : page,
            PageSize = pageSize <= 0 ? 20 : Math.Min(pageSize, 100),
            CategoryId = categoryId,
            TenantId = tenantId,
            InStock = inStock,
            MinPrice = normalizedMinPrice,
            MaxPrice = normalizedMaxPrice,
            Query = NormalizeUserQuery(query),
            QueryExpansions = Array.Empty<string>(),
            Intent = intent ?? CommerceHubSearchIntentContext.Unknown(),
            Sort = NormalizeSort(sort),
            Filters = filters,
            FacetDefinitions = facetDefinitions
        };
    }

    private static CommerceHubProductDto MapListItem(CommerceHubProductDocument document) =>
        new()
        {
            Id = document.Id,
            TenantId = document.TenantId,
            StoreId = document.TenantId,
            StoreName = document.StoreName,
            StoreShippingFee = document.StoreShippingFee,
            ProductId = document.ProductId,
            VariantId = document.VariantId,
            ProductName = document.ProductName,
            Description = document.Description,
            Image = document.Image,
            Price = document.Price,
            CategoryId = document.CategoryId,
            CategoryName = document.CategoryName,
            CategoryPath = document.CategoryPath,
            Currency = document.Currency,
            InStock = document.InStock,
            AvailableStock = document.AvailableStock,
            TotalStock = document.AvailableStock,
            IsPublished = document.IsPublished,
            Attributes = CommerceHubSearchDocumentMapper.MapAttributes(document.Attributes)
                .ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.OrdinalIgnoreCase)
        };

    private async Task<CommerceHubZeroResultSuggestionsResult?> BuildZeroResultSuggestionsAsync(
        CommerceHubSearchReadResult result,
        CommerceHubSearchRequest request,
        CancellationToken cancellationToken)
    {
        if (result.TotalCount > 0)
        {
            return result.ZeroResultSuggestions;
        }

        return await _repository.GetZeroResultSuggestionsAsync(request, cancellationToken);
    }

    private async Task<CommerceHubSearchFacetsDto> MapFacetsAsync(
        CommerceHubSearchReadResult result,
        IReadOnlyCollection<CommerceHubSearchFacetDefinition> facetDefinitions,
        CancellationToken cancellationToken)
    {
        var orderedAttributes = facetDefinitions
            .Select(definition =>
            {
                if (!result.Aggregations.TryGetValue(definition.Key, out var aggregation) || !HasFacetContent(aggregation))
                {
                    return null;
                }

                var values = aggregation.Buckets
                    .Where(bucket => bucket.Count > 0 && !string.IsNullOrWhiteSpace(bucket.Value))
                    .OrderByDescending(bucket => bucket.Count)
                    .ThenBy(bucket => bucket.Value, StringComparer.OrdinalIgnoreCase)
                    .Select(bucket => new CommerceHubAggregationBucketDto
                    {
                        Value = bucket.Value,
                        Count = bucket.Count
                    })
                    .ToList();

                var valueCount = aggregation.Stats?.Count
                    ?? values.Sum(bucket => bucket.Count);

                return new CommerceHubAttributeFacetDto
                {
                    Key = definition.Key,
                    Label = definition.DisplayName,
                    Type = definition.Type,
                    SortOrder = definition.SortOrder,
                    ValueCount = valueCount,
                    Values = values,
                    Range = aggregation.Stats == null || aggregation.Stats.Count <= 0
                        ? null
                        : new CommerceHubFacetRangeDto
                        {
                            Min = aggregation.Stats.Min,
                            Max = aggregation.Stats.Max
                        }
                };
            })
            .Where(attribute => attribute != null)
            .Select(attribute => attribute!)
            .OrderBy(attribute => attribute.SortOrder)
            .ThenByDescending(attribute => attribute.ValueCount)
            .ThenBy(attribute => attribute.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var attributes = orderedAttributes.ToDictionary(
            attribute => attribute.Key,
            attribute => attribute,
            StringComparer.OrdinalIgnoreCase);

        return new CommerceHubSearchFacetsDto
        {
            Categories = await MapFacetCategoriesAsync(result.CategoryBuckets, cancellationToken),
            Price = result.PriceStats == null || result.PriceStats.Count <= 0
                ? new CommerceHubFacetRangeDto()
                : new CommerceHubFacetRangeDto
                {
                    Min = result.PriceStats.Min,
                    Max = result.PriceStats.Max
                },
            Attributes = attributes
        };
    }

    private async Task<CommerceHubSearchFacetsDto> MapSqlVisibleFacetsAsync(
        IReadOnlyCollection<CommerceHubProductDocument> visibleItems,
        CancellationToken cancellationToken)
    {
        if (visibleItems.Count == 0)
        {
            return new CommerceHubSearchFacetsDto();
        }

        var metadata = await LoadCategoryFacetMetadataAsync(cancellationToken);
        var metadataById = metadata.ToDictionary(item => item.Id);
        var categories = visibleItems
            .Where(item => metadataById.ContainsKey(item.CategoryId))
            .GroupBy(item => item.CategoryId)
            .Select(group =>
            {
                var item = metadataById[group.Key];
                return new CommerceHubSearchFacetCategoryDto
                {
                    CategoryId = item.Id,
                    Name = item.Name,
                    Slug = item.Slug,
                    Count = group.LongCount()
                };
            })
            .OrderBy(category => category.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new CommerceHubSearchFacetsDto
        {
            Categories = categories,
            Price = new CommerceHubFacetRangeDto
            {
                Min = visibleItems.Min(item => (double?)Convert.ToDouble(item.Price, CultureInfo.InvariantCulture)),
                Max = visibleItems.Max(item => (double?)Convert.ToDouble(item.Price, CultureInfo.InvariantCulture))
            },
            Attributes = new Dictionary<string, CommerceHubAttributeFacetDto>(StringComparer.OrdinalIgnoreCase)
        };
    }

    private async Task<List<CommerceHubSearchFacetCategoryDto>> MapFacetCategoriesAsync(
        IReadOnlyCollection<CommerceHubSearchCategoryBucket> categoryBuckets,
        CancellationToken cancellationToken)
    {
        if (categoryBuckets.Count == 0)
        {
            return new List<CommerceHubSearchFacetCategoryDto>();
        }

        var metadata = await LoadCategoryFacetMetadataAsync(cancellationToken);
        if (metadata.Count == 0)
        {
            return new List<CommerceHubSearchFacetCategoryDto>();
        }

        var directCounts = categoryBuckets
            .GroupBy(bucket => bucket.CategoryId)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(bucket => bucket.Count));

        var childrenByParentId = metadata
            .Where(item => item.ParentId.HasValue)
            .GroupBy(item => item.ParentId!.Value)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(item => item.SortOrder)
                    .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList());

        CommerceHubSearchFacetCategoryDto? BuildNode(CategoryFacetMetadata item)
        {
            var children = childrenByParentId.TryGetValue(item.Id, out var directChildren)
                ? directChildren
                    .Select(BuildNode)
                    .Where(child => child != null)
                    .Select(child => child!)
                    .ToList()
                : new List<CommerceHubSearchFacetCategoryDto>();

            directCounts.TryGetValue(item.Id, out var directCount);
            var totalCount = directCount + children.Sum(child => child.Count);
            if (totalCount <= 0)
            {
                return null;
            }

            return new CommerceHubSearchFacetCategoryDto
            {
                CategoryId = item.Id,
                Name = item.Name,
                Slug = item.Slug,
                Count = totalCount,
                Children = children
            };
        }

        var roots = metadata
            .Where(item => item.ParentId == null)
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return roots
            .Select(BuildNode)
            .Where(category => category != null)
            .Select(category => category!)
            .ToList();
    }

    private async Task<List<CategoryFacetMetadata>> LoadCategoryFacetMetadataAsync(CancellationToken cancellationToken)
    {
        const string cacheKey = "commercehub:facet-category-metadata";
        List<CategoryFacetMetadata> metadata;
        if (_cache.TryGetValue(cacheKey, out List<CategoryFacetMetadata>? cachedMetadata) &&
            cachedMetadata != null)
        {
            metadata = cachedMetadata;
        }
        else
        {
            metadata = await _dbContext.CommerceHubCategories
                .AsNoTracking()
                .Where(x => x.IsActive)
                .Select(x => new CategoryFacetMetadata
                {
                    Id = x.Id,
                    Name = x.Name,
                    Slug = x.Slug,
                    ParentId = x.ParentId,
                    SortOrder = x.SortOrder
                })
                .ToListAsync(cancellationToken);

            _cache.Set(cacheKey, metadata, CategoryMetadataCacheTtl);
        }

        var liveCategoryIds = await CommerceHubCategoryLineageAuthority.LoadLiveIdsAsync(
            _dbContext,
            metadata.Select(item => item.Id),
            cancellationToken);

        return metadata
            .Where(item => liveCategoryIds.Contains(item.Id))
            .ToList();
    }

    private static bool HasFacetContent(CommerceHubSearchAggregationResult aggregation) =>
        aggregation.Buckets.Any(bucket => bucket.Count > 0 && !string.IsNullOrWhiteSpace(bucket.Value)) ||
        aggregation.Stats?.Count > 0;

    private static int NormalizeFacetSortOrder(int sortOrder) =>
        sortOrder > 0 ? sortOrder : DefaultFacetSortOrder;

    private static Dictionary<string, CommerceHubAggregationDto> MapAggregations(
        Dictionary<string, CommerceHubSearchAggregationResult> aggregations)
    {
        return aggregations.ToDictionary(
            pair => pair.Key,
            pair => new CommerceHubAggregationDto
            {
                Type = pair.Value.Type,
                Values = pair.Value.Buckets.Select(bucket => new CommerceHubAggregationBucketDto
                {
                    Value = bucket.Value,
                    Count = bucket.Count
                }).ToList(),
                Stats = pair.Value.Stats == null
                    ? null
                    : new CommerceHubAggregationStatsDto
                    {
                        Min = pair.Value.Stats.Min,
                        Max = pair.Value.Stats.Max,
                        Avg = pair.Value.Stats.Avg,
                        Count = pair.Value.Stats.Count
                    }
            },
            StringComparer.OrdinalIgnoreCase);
    }

    private async Task<CommerceHubZeroResultSuggestionsDto> MapZeroResultSuggestionsAsync(
        CommerceHubZeroResultSuggestionsResult suggestions,
        CancellationToken cancellationToken)
    {
        var visibleProducts = await RecomposeVisibleSuggestionProductsAsync(suggestions.Products, cancellationToken);
        var visibleCategories = new List<CommerceHubSearchCategorySuggestionDto>();
        foreach (var category in suggestions.Categories)
        {
            if (await IsLiveCommerceHubCategoryAsync(category.CategoryId, cancellationToken))
            {
                visibleCategories.Add(new CommerceHubSearchCategorySuggestionDto
                {
                    CategoryId = category.CategoryId,
                    CategoryName = category.CategoryName,
                    Count = category.Count
                });
            }
        }

        return new CommerceHubZeroResultSuggestionsDto
        {
            Products = visibleProducts.Select(MapSuggestionProduct).ToList(),
            PopularTerms = await FilterLiveSuggestionTermsAsync(suggestions.PopularTerms, null, suggestions.PopularTerms.Count, cancellationToken),
            SimilarTerms = await FilterLiveSuggestionTermsAsync(suggestions.SimilarTerms, null, suggestions.SimilarTerms.Count, cancellationToken),
            Categories = visibleCategories,
            Filters = MapAggregations(suggestions.Filters)
        };
    }

    private static CommerceHubSearchSuggestionProductDto MapSuggestionProduct(
        CommerceHubSearchSuggestionProduct product)
    {
        return new CommerceHubSearchSuggestionProductDto
        {
            Id = product.Id,
            ProductId = product.ProductId,
            VariantId = product.VariantId,
            ProductName = product.ProductName,
            Image = product.Image,
            Price = product.Price,
            Currency = product.Currency,
            CategoryId = product.CategoryId,
            CategoryName = product.CategoryName,
            StoreName = product.StoreName
        };
    }

    private static CommerceHubAutocompleteProductDto MapAutocompleteProduct(
        CommerceHubSearchAutocompleteProduct product)
    {
        return new CommerceHubAutocompleteProductDto
        {
            Id = product.Id,
            ProductId = product.ProductId,
            VariantId = product.VariantId,
            ProductName = product.ProductName,
            ProductNameHighlight = product.ProductNameHighlight,
            Image = product.Image,
            Price = product.Price,
            Currency = product.Currency,
            StoreName = product.StoreName,
            CategoryId = product.CategoryId,
            CategoryName = product.CategoryName,
            CategoryNameHighlight = product.CategoryNameHighlight
        };
    }

    private async Task<List<CommerceHubAutocompleteCategoryDto>> MapAutocompleteCategoriesAsync(
        IReadOnlyCollection<CommerceHubSearchAutocompleteCategoryBucket> categories,
        CancellationToken cancellationToken)
    {
        if (categories.Count == 0)
        {
            return new List<CommerceHubAutocompleteCategoryDto>();
        }

        var categoryIds = categories
            .Select(category => category.CategoryId)
            .Distinct()
            .ToList();

        var metadata = await _dbContext.CommerceHubCategories
            .AsNoTracking()
            .Where(x => categoryIds.Contains(x.Id) && x.IsActive)
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.Slug,
                x.ParentId
            })
            .ToListAsync(cancellationToken);

        var metadataLookup = metadata.ToDictionary(x => x.Id);
        var liveCategoryIds = new HashSet<Guid>();
        foreach (var categoryId in categoryIds)
        {
            if (await IsLiveCommerceHubCategoryAsync(categoryId, cancellationToken))
            {
                liveCategoryIds.Add(categoryId);
            }
        }

        return categories
            .Where(category => metadataLookup.ContainsKey(category.CategoryId) && liveCategoryIds.Contains(category.CategoryId))
            .Select(category =>
            {
                var item = metadataLookup[category.CategoryId];
                return new CommerceHubAutocompleteCategoryDto
                {
                    CategoryId = item.Id,
                    CategoryName = item.Name,
                    Slug = item.Slug,
                    Count = category.Count
                };
            })
            .ToList();
    }

    private async Task<List<CommerceHubAutocompleteCategoryDto>> FilterAutocompleteCategoryDtosAsync(
        IReadOnlyCollection<CommerceHubAutocompleteCategoryDto> categories,
        CancellationToken cancellationToken)
    {
        if (categories.Count == 0)
        {
            return new List<CommerceHubAutocompleteCategoryDto>();
        }

        var visible = new List<CommerceHubAutocompleteCategoryDto>();
        foreach (var category in categories)
        {
            if (await IsLiveCommerceHubCategoryAsync(category.CategoryId, cancellationToken))
            {
                visible.Add(category);
            }
        }

        return visible;
    }

    private async Task<List<string>> FilterLiveSuggestionTermsAsync(
        IEnumerable<string> terms,
        Guid? tenantId,
        int limit,
        CancellationToken cancellationToken)
    {
        var normalizedLimit = limit <= 0 ? 5 : Math.Min(limit, 10);
        var liveTerms = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var term in terms)
        {
            var collapsed = CommerceHubSearchTextUtility.CollapseWhitespace(term);
            if (string.IsNullOrWhiteSpace(collapsed) || !seen.Add(collapsed))
            {
                continue;
            }

            if (await HasLiveCommerceHubSuggestionTermAsync(collapsed, tenantId, cancellationToken))
            {
                liveTerms.Add(collapsed);
            }

            if (liveTerms.Count >= normalizedLimit)
            {
                break;
            }
        }

        return liveTerms;
    }

    private async Task<bool> HasLiveCommerceHubSuggestionTermAsync(
        string term,
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        var normalized = NormalizeUserQuery(term);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        var pattern = $"%{normalized.ToLowerInvariant()}%";
        var candidateListings = await (
                from listing in _dbContext.CommerceHubListings.AsNoTracking().IgnoreQueryFilters()
                join product in _dbContext.Products.AsNoTracking().IgnoreQueryFilters()
                    on new { listing.TenantId, listing.ProductId }
                    equals new { product.TenantId, ProductId = product.Id }
                join tenant in _dbContext.Tenants.AsNoTracking().IgnoreQueryFilters()
                    on listing.TenantId equals tenant.Id
                join category in _dbContext.CommerceHubCategories.AsNoTracking()
                    on listing.CommerceHubCategoryId equals category.Id
                where !listing.IsDeleted &&
                      listing.IsPublished &&
                      (!tenantId.HasValue || listing.TenantId == tenantId.Value) &&
                      !product.IsDeleted &&
                      product.IsActive &&
                      product.IsOnlineVisible &&
                      product.PublishStatus == PublishStatus.Published &&
                      tenant.Status == TenantStatus.Active &&
                      tenant.DeletedAtUtc == null &&
                      category.IsActive &&
                      (EF.Functions.Like(product.Name.ToLower(), pattern) ||
                       EF.Functions.Like(category.Name.ToLower(), pattern))
                select new
                {
                    listing.Id,
                    listing.TenantId
                })
            .Distinct()
            .Take(20)
            .ToListAsync(cancellationToken);

        if (candidateListings.Count == 0)
        {
            return false;
        }

        var governanceHiddenVendorIds = await LoadGovernanceHiddenVendorIdsAsync(
            candidateListings.Select(item => item.TenantId),
            cancellationToken);
        var suspendedVendorIds = await LoadSuspendedVendorIdsAsync(
            candidateListings.Select(item => item.TenantId),
            cancellationToken);

        foreach (var candidate in candidateListings)
        {
            if (governanceHiddenVendorIds.Contains(candidate.TenantId) ||
                suspendedVendorIds.Contains(candidate.TenantId))
            {
                continue;
            }

            var sqlAuthority = await CommerceHubSearchSqlAuthorityOverlay.ApplyAsync(
                _dbContext,
                new CommerceHubProductDocument
                {
                    Id = candidate.Id,
                    TenantId = candidate.TenantId
                },
                candidate.TenantId,
                cancellationToken);

            if (sqlAuthority != null)
            {
                return true;
            }
        }

        return false;
    }

    private async Task<bool> IsLiveCommerceHubCategoryAsync(
        Guid categoryId,
        CancellationToken cancellationToken)
    {
        return await CommerceHubCategoryLineageAuthority.IsLiveAsync(
            _dbContext,
            categoryId,
            cancellationToken);
    }

    private async Task<List<string>> GetPopularTermsAsync(
        int limit,
        string? query,
        CancellationToken cancellationToken)
    {
        var normalizedLimit = limit <= 0 ? 5 : Math.Min(limit, 10);
        var normalizedQuery = NormalizeUserQuery(query);

        var popularQuery = _dbContext.CommerceHubSearchQueries
            .AsNoTracking()
            .Where(x => !string.IsNullOrWhiteSpace(x.QueryText));

        if (!string.IsNullOrWhiteSpace(normalizedQuery))
        {
            popularQuery = popularQuery.Where(x => x.QueryText.Contains(normalizedQuery));
        }

        var popularTerms = await popularQuery
            .GroupBy(x => x.QueryText)
            .Select(group => new
            {
                QueryText = group.Key,
                SearchCount = group.LongCount()
            })
            .OrderByDescending(group => group.SearchCount)
            .ThenBy(group => group.QueryText)
            .Take(normalizedLimit * 4)
            .Select(group => group.QueryText)
            .ToListAsync(cancellationToken);

        return await FilterLiveSuggestionTermsAsync(
            popularTerms,
            tenantId: null,
            normalizedLimit,
            cancellationToken);
    }

    private static string BuildSuggestionsCacheKey(
        CommerceHubSearchSuggestionsRequest request,
        string visibilityVersion) =>
        $"commercehub:suggest:{visibilityVersion}:{request.TenantId?.ToString("N") ?? "all"}:{request.CategoryId?.ToString("N") ?? "all"}:{request.Limit}:{BuildVendorExclusionFingerprint(request.ExcludedTenantIds)}:{CommerceHubSearchQueryNormalizer.Normalize(request.Query)}";

    private static string BuildAutocompleteCacheKey(
        CommerceHubSearchAutocompleteRequest request,
        int popularLimit,
        string visibilityVersion) =>
        $"commercehub:autocomplete:{visibilityVersion}:{request.TenantId?.ToString("N") ?? "all"}:{request.CategoryId?.ToString("N") ?? "all"}:{request.ProductLimit}:{request.CategoryLimit}:{request.SuggestionLimit}:{popularLimit}:{BuildVendorExclusionFingerprint(request.ExcludedTenantIds)}:{CommerceHubSearchQueryNormalizer.Normalize(request.Query)}";

    private static string BuildVendorExclusionFingerprint(IReadOnlyCollection<Guid> vendorIds)
    {
        if (vendorIds.Count == 0)
        {
            return "none";
        }

        var normalizedVendorIds = vendorIds
            .Where(vendorId => vendorId != Guid.Empty)
            .Distinct()
            .OrderBy(vendorId => vendorId)
            .Select(vendorId => vendorId.ToString("N"))
            .ToArray();

        if (normalizedVendorIds.Length == 0)
        {
            return "none";
        }

        var payload = string.Join(",", normalizedVendorIds);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return $"{normalizedVendorIds.Length}:{Convert.ToHexString(hash)}";
    }

    private Guid? ResolveTenantScope(Guid? requestedTenantId)
    {
        if (!_tenantContext.HasTenant)
        {
            return requestedTenantId;
        }

        var currentTenantId = _tenantContext.RequiredTenantId;
        if (!requestedTenantId.HasValue)
        {
            return currentTenantId;
        }

        if (requestedTenantId.Value == currentTenantId)
        {
            return currentTenantId;
        }

        _logger.LogWarning(
            "[SECURITY] Cross-tenant access attempt detected | TenantId: {TenantId} | EntityTenantId: {EntityTenantId} | Entity: {Entity}",
            currentTenantId,
            requestedTenantId.Value,
            "CommerceHubQuery");

        throw new UnauthorizedAccessException("Tenant isolation violation");
    }

    private void EnsureTenantOwnedEntity(Guid entityTenantId, string entityName, Guid entityId)
    {
        if (!_tenantContext.HasTenant)
        {
            return;
        }

        var currentTenantId = _tenantContext.RequiredTenantId;
        if (entityTenantId == currentTenantId)
        {
            return;
        }

        _logger.LogWarning(
            "[SECURITY] Cross-tenant access attempt detected | TenantId: {TenantId} | EntityTenantId: {EntityTenantId} | Entity: {Entity} | EntityId: {EntityId}",
            currentTenantId,
            entityTenantId,
            entityName,
            entityId);

        throw new UnauthorizedAccessException("Tenant isolation violation");
    }

    private static string? NormalizeUserQuery(string? query)
    {
        var collapsed = CommerceHubSearchTextUtility.CollapseWhitespace(query);
        if (string.IsNullOrWhiteSpace(collapsed))
        {
            return null;
        }

        var normalized = CommerceHubSearchQueryNormalizer.Normalize(collapsed);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            return normalized;
        }

        return collapsed.Length == 0 ? null : collapsed;
    }

    private sealed class CategoryFacetMetadata
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Slug { get; init; } = string.Empty;
        public Guid? ParentId { get; init; }
        public int SortOrder { get; init; }
    }

    private static string ToIntentValue(CommerceHubSearchIntentType intentType) =>
        intentType.ToString().ToLowerInvariant();

    private static CommerceHubProductDetailDto MapDetail(
        CommerceHubProductDocument listing,
        ProductDocument? product)
    {
        if (product == null)
        {
            return new CommerceHubProductDetailDto
            {
                Id = listing.Id,
                TenantId = listing.TenantId,
                StoreName = listing.StoreName,
                StoreShippingFee = listing.StoreShippingFee,
                ProductId = listing.ProductId,
                InitialVariantId = listing.VariantId,
                ProductName = listing.ProductName,
                Description = listing.Description,
                Image = listing.Image,
                Price = listing.Price,
                CategoryId = listing.CategoryId,
                CategoryName = listing.CategoryName,
                CategoryPath = listing.CategoryPath,
                Currency = listing.Currency,
                InStock = listing.InStock,
                StockQuantity = (int)Math.Max(0, decimal.ToInt32(decimal.Truncate(listing.AvailableStock))),
                IsPublished = listing.IsPublished,
                IsAvailable = listing.IsPublished && listing.Price > 0,
                HasVariants = false,
                DefaultVariantId = listing.VariantId,
                Seo = new CommerceHubProductSeoDto(),
                Images = string.IsNullOrWhiteSpace(listing.Image) ? new List<string>() : new List<string> { listing.Image },
                Variants = new List<CommerceHubProductVariantDto>
                {
                    new()
                    {
                        Id = listing.VariantId,
                        Price = listing.Price,
                        OnlinePrice = listing.Price,
                        ImageUrl = listing.Image,
                        TotalStock = Math.Max(0, decimal.ToInt32(decimal.Truncate(listing.AvailableStock))),
                        IsDefault = true,
                        IsActive = true
                    }
                }
            };
        }

        var media = GetOrderedProductMedia(product.Media);
        var images = media
            .Where(item => string.Equals(item.MediaType, "image", StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Url)
            .ToList();

        if (images.Count == 0 && !string.IsNullOrWhiteSpace(listing.Image))
        {
            images.Add(listing.Image);
        }

        var variants = MapVariants(product.Variants, product.Media, images);
        if (variants.Count == 0)
        {
            variants.Add(new CommerceHubProductVariantDto
            {
                Id = listing.VariantId,
                SKU = product.SKU,
                Price = listing.Price,
                OnlinePrice = listing.Price,
                ImageUrl = listing.Image ?? images.FirstOrDefault(),
                TotalStock = Math.Max(0, product.StockQuantity),
                IsDefault = true,
                IsActive = true
            });
        }

        var initialVariant = variants.FirstOrDefault(v => v.Id == listing.VariantId)
            ?? variants.FirstOrDefault(v => v.IsDefault)
            ?? variants.FirstOrDefault();

        var displayPrice = initialVariant?.EffectivePrice
            ?? product.StorefrontEffectivePrice;

        var totalStock = variants.Count > 0
            ? variants.Sum(v => Math.Max(0, v.TotalStock))
            : Math.Max(0, product.StockQuantity);

        var inStock = variants.Count > 0
            ? variants.Any(v => v.InStock)
            : product.InStock;

        var isAvailable = product.IsActive && product.IsOnlineVisible && displayPrice > 0;

        return new CommerceHubProductDetailDto
        {
            Id = listing.Id,
            TenantId = listing.TenantId,
            StoreName = listing.StoreName,
            StoreShippingFee = listing.StoreShippingFee,
            ProductId = listing.ProductId,
            InitialVariantId = initialVariant?.Id ?? listing.VariantId,
            ProductName = string.IsNullOrWhiteSpace(product.Name) ? listing.ProductName : product.Name,
            Description = product.Description ?? listing.Description,
            Image = listing.Image ?? images.FirstOrDefault(),
            Price = displayPrice,
            CategoryId = listing.CategoryId,
            CategoryName = listing.CategoryName,
            CategoryPath = listing.CategoryPath,
            Currency = listing.Currency,
            InStock = inStock,
            StockQuantity = totalStock,
            IsPublished = listing.IsPublished,
            IsAvailable = isAvailable,
            HasVariants = product.HasVariants || variants.Count > 1,
            DefaultVariantId = variants.FirstOrDefault(v => v.IsDefault)?.Id ?? initialVariant?.Id,
            Features = (product.Features ?? new List<string>()).ToList(),
            Faqs = (product.Faqs ?? new List<ProductFaqDocument>())
                .Select(item => new CommerceHubProductFaqDto
                {
                    Question = item.Question,
                    Answer = item.Answer
                })
                .ToList(),
            Seo = new CommerceHubProductSeoDto
            {
                MetaTitle = product.MetaTitle,
                MetaDescription = product.MetaDescription
            },
            Tags = (product.Tags ?? new List<string>()).ToList(),
            AiGenerated = product.AiGenerated,
            AiMetadata = CloneAiMetadata(product.AiMetadata),
            Media = media,
            Images = images,
            Variants = variants
        };
    }

    private static CommerceHubProductVariantDto? ResolveDefaultVariant(CommerceHubProductDetailDto product) =>
        product.Variants.FirstOrDefault(variant => variant.Id == product.DefaultVariantId && variant.IsActive)
        ?? product.Variants.FirstOrDefault(variant => variant.Id == product.InitialVariantId && variant.IsActive)
        ?? product.Variants.FirstOrDefault(variant => variant.IsDefault && variant.IsActive)
        ?? product.Variants.FirstOrDefault(variant => variant.IsActive);

    private static CommerceHubProductVariantDto? ResolveExactOptionCombination(
        IReadOnlyCollection<CommerceHubProductVariantDto> variants,
        IReadOnlyDictionary<string, string> selectedOptions)
    {
        if (selectedOptions.Count == 0)
        {
            return null;
        }

        var matches = variants
            .Where(variant => variant.IsActive)
            .Where(variant => HasExactOptionCombination(variant, selectedOptions))
            .Take(2)
            .ToList();

        return matches.Count == 1 ? matches[0] : null;
    }

    private static bool HasExactOptionCombination(
        CommerceHubProductVariantDto variant,
        IReadOnlyDictionary<string, string> selectedOptions)
    {
        if (variant.Attributes.Count != selectedOptions.Count)
        {
            return false;
        }

        foreach (var attribute in variant.Attributes)
        {
            if (!selectedOptions.TryGetValue(attribute.Name, out var selectedValue) ||
                !string.Equals(attribute.Value, selectedValue, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static JsonElement? CloneAiMetadata(BsonDocument? aiMetadata)
    {
        if (aiMetadata == null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(aiMetadata.ToJson());
        return document.RootElement.Clone();
    }

    private static List<CommerceHubProductVariantDto> MapVariants(
        List<ProductVariantDocument>? variantDocs,
        List<ProductMediaDocument>? productMedia,
        List<string> productImages)
    {
        if (variantDocs == null || variantDocs.Count == 0)
        {
            return new List<CommerceHubProductVariantDto>();
        }

        return variantDocs
            .Where(v => v.IsActive)
            .OrderBy(v => v.SortOrder)
            .ThenBy(v => v.IsDefault ? 0 : 1)
            .Select(v => new CommerceHubProductVariantDto
            {
                Id = v.VariantId,
                SKU = v.SKU,
                Price = v.Price,
                OnlinePrice = v.OnlinePrice,
                MediaId = v.MediaId,
                ImageUrl = ResolveVariantImageUrl(v, productMedia, productImages),
                TotalStock = Math.Max(0, (int)Math.Truncate(v.TotalStock)),
                IsDefault = v.IsDefault,
                IsActive = v.IsActive,
                Attributes = (v.Attributes ?? new List<VariantAttributeDocument>())
                    .OrderBy(a => a.SortOrder)
                    .Select(a => new CommerceHubProductVariantAttributeDto
                    {
                        Name = a.Name,
                        Value = a.Value
                    })
                    .ToList()
            })
            .ToList();
    }

    private static string? ResolveVariantImageUrl(
        ProductVariantDocument variant,
        List<ProductMediaDocument>? productMedia,
        List<string> productImages)
    {
        if (variant.MediaId.HasValue && productMedia != null)
        {
            var mediaMatch = productMedia
                .Where(media => string.Equals(media.Type, "image", StringComparison.OrdinalIgnoreCase))
                .OrderBy(media => media.Position)
                .FirstOrDefault(media => media.Id == variant.MediaId.Value);

            if (!string.IsNullOrWhiteSpace(mediaMatch?.Url))
            {
                return mediaMatch.Url;
            }
        }

        return productImages.FirstOrDefault();
    }

    private static List<CommerceHubProductMediaDto> GetOrderedProductMedia(List<ProductMediaDocument>? media)
    {
        return (media ?? new List<ProductMediaDocument>())
            .Where(item => !string.IsNullOrWhiteSpace(item.Url))
            .OrderBy(item => item.Position)
            .ThenBy(item => item.Id)
            .Select(item => new CommerceHubProductMediaDto
            {
                Id = item.Id,
                MediaType = item.Type,
                Url = item.Url,
                Provider = item.Provider,
                Position = item.Position
            })
            .ToList();
    }

    private static string? NormalizeSort(string? sort) =>
        sort?.Trim().ToLowerInvariant() switch
        {
            "price_asc" => "price_asc",
            "price_desc" => "price_desc",
            "newest" => "newest",
            "relevance" => null,
            _ => null
        };
}
