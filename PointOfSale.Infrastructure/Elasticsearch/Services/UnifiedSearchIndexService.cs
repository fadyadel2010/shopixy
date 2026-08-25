using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nest;
using PointOfSale.Infrastructure.Elasticsearch.Configuration;
using PointOfSale.Infrastructure.Elasticsearch.Documents;

namespace PointOfSale.Infrastructure.Elasticsearch.Services;

/// <summary>
/// Implementation of unified search index service.
/// 
/// Design Decisions:
/// - Uses NEST high-level client for type safety
/// - Single index with entity type field for unified search
/// - Graceful failure handling - never breaks the caller
/// - Comprehensive logging for observability
/// 
/// Index Strategy:
/// - products index for ProductSearchDocument
/// - categories index for CategorySearchDocument  
/// - cmspages index for CmsPageSearchDocument
/// </summary>
public class UnifiedSearchIndexService : IUnifiedSearchIndexService
{
    private readonly IElasticClient _client;
    private readonly IProductSearchIndexService _productIndexService;
    private readonly ElasticsearchSettings _settings;
    private readonly ILogger<UnifiedSearchIndexService> _logger;

    private const string CategoriesIndexName = "categories";
    private const string CmsPagesIndexName = "cmspages";
    private const int BulkRequestBatchSize = 250;
    private const int MaxReportedBulkErrors = 50;

    public UnifiedSearchIndexService(
        IElasticClient client,
        IProductSearchIndexService productIndexService,
        IOptions<ElasticsearchSettings> settings,
        ILogger<UnifiedSearchIndexService> logger)
    {
        _client = client;
        _productIndexService = productIndexService;
        _settings = settings.Value;
        _logger = logger;
    }

    #region Product Operations

    /// <inheritdoc />
    public async Task<bool> IndexProductAsync(ProductSearchDocument document, CancellationToken cancellationToken = default)
    {
        document.BuildSearchText();
        return await _productIndexService.IndexAsync(document, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteProductAsync(Guid tenantId, Guid productId, CancellationToken cancellationToken = default)
    {
        return await _productIndexService.DeleteAsync(tenantId, productId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> TombstoneProductAsync(
        Guid tenantId,
        Guid productId,
        long productGeneration,
        bool isDeleted,
        Guid? eventId = null,
        CancellationToken cancellationToken = default)
    {
        return _productIndexService.TombstoneAsync(
            tenantId,
            productId,
            productGeneration,
            isDeleted,
            eventId,
            cancellationToken);
    }

    #endregion

    #region Category Operations

    /// <inheritdoc />
    public async Task<bool> IndexCategoryAsync(CategorySearchDocument document, CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled)
        {
            _logger.LogDebug("Elasticsearch disabled - skipping category index");
            return true;
        }

        try
        {
            document.BuildSearchText();
            var documentId = document.GetDocumentId();

            _logger.LogDebug(
                "Indexing category | CategoryId: {CategoryId} | TenantId: {TenantId}",
                document.CategoryId, document.TenantId);

            var response = await _client.IndexAsync(document, i => i
                .Index(CategoriesIndexName)
                .Id(documentId),
                cancellationToken);

            if (response.IsValid)
            {
                _logger.LogInformation(
                    "Category indexed successfully | CategoryId: {CategoryId} | TenantId: {TenantId}",
                    document.CategoryId, document.TenantId);
                return true;
            }

            _logger.LogError(
                "Failed to index category | CategoryId: {CategoryId} | Error: {Error}",
                document.CategoryId, response.ServerError?.Error?.Reason ?? "Unknown");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception indexing category | CategoryId: {CategoryId}", document.CategoryId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteCategoryAsync(Guid tenantId, Guid categoryId, CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled)
        {
            _logger.LogDebug("Elasticsearch disabled - skipping category delete");
            return true;
        }

        try
        {
            var documentId = $"{tenantId}_{categoryId}";
            var response = await _client.DeleteAsync(
                new DocumentPath<CategorySearchDocument>(documentId),
                d => d.Index(CategoriesIndexName),
                cancellationToken);

            if (response.IsValid || response.Result == Result.NotFound)
            {
                _logger.LogInformation(
                    "Category deleted from index | CategoryId: {CategoryId} | TenantId: {TenantId}",
                    categoryId, tenantId);
                return true;
            }

            _logger.LogError(
                "Failed to delete category | CategoryId: {CategoryId} | Error: {Error}",
                categoryId, response.ServerError?.Error?.Reason ?? "Unknown");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception deleting category | CategoryId: {CategoryId}", categoryId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<long> DeleteStaleCategoriesAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> authoritativeCategoryIds,
        CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled)
        {
            _logger.LogDebug("Elasticsearch disabled - skipping stale category cleanup");
            return 0;
        }

        try
        {
            QueryContainer BuildStaleCategoryQuery(QueryContainerDescriptor<CategorySearchDocument> query)
            {
                var filters = new List<Func<QueryContainerDescriptor<CategorySearchDocument>, QueryContainer>>
                {
                    filter => filter.Term(term => term.Field(document => document.TenantId).Value(tenantId))
                };

                if (authoritativeCategoryIds.Count > 0)
                {
                    filters.Add(filter => filter.Bool(boolean => boolean.MustNot(mustNot => mustNot.Terms(terms => terms
                        .Field(document => document.CategoryId)
                        .Terms(authoritativeCategoryIds)))));
                }

                return query.Bool(boolean => boolean.Filter(filters.ToArray()));
            }

            var response = await _client.DeleteByQueryAsync<CategorySearchDocument>(
                descriptor => descriptor
                    .Index(CategoriesIndexName)
                    .Query(BuildStaleCategoryQuery)
                    .Refresh(true),
                cancellationToken);

            if (response.IsValid)
            {
                _logger.LogInformation(
                    "Deleted stale tenant-scoped category documents from search | TenantId: {TenantId} | Deleted: {Deleted} | AuthoritativeCount: {AuthoritativeCount}",
                    tenantId,
                    response.Deleted,
                    authoritativeCategoryIds.Count);
                return response.Deleted;
            }

            _logger.LogError(
                "Failed to delete stale category documents from search | TenantId: {TenantId} | Error: {Error}",
                tenantId,
                response.ServerError?.Error?.Reason ?? response.OriginalException?.Message ?? "Unknown");
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Exception deleting stale category documents from search | TenantId: {TenantId}",
                tenantId);
            return 0;
        }
    }

    #endregion

    #region CMS Page Operations

    /// <inheritdoc />
    public async Task<bool> IndexCmsPageAsync(CmsPageSearchDocument document, CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled)
        {
            _logger.LogDebug("Elasticsearch disabled - skipping CMS page index");
            return true;
        }

        try
        {
            document.BuildSearchText();
            var documentId = document.GetDocumentId();

            _logger.LogDebug(
                "Indexing CMS page | Slug: {Slug} | TenantId: {TenantId}",
                document.Slug, document.TenantId);

            var response = await _client.IndexAsync(document, i => i
                .Index(CmsPagesIndexName)
                .Id(documentId),
                cancellationToken);

            if (response.IsValid)
            {
                _logger.LogInformation(
                    "CMS page indexed successfully | Slug: {Slug} | TenantId: {TenantId}",
                    document.Slug, document.TenantId);
                return true;
            }

            _logger.LogError(
                "Failed to index CMS page | Slug: {Slug} | Error: {Error}",
                document.Slug, response.ServerError?.Error?.Reason ?? "Unknown");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception indexing CMS page | Slug: {Slug}", document.Slug);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteCmsPageAsync(Guid tenantId, string slug, CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled)
        {
            _logger.LogDebug("Elasticsearch disabled - skipping CMS page delete");
            return true;
        }

        try
        {
            var documentId = $"{tenantId}_{slug}";
            var response = await _client.DeleteAsync(
                new DocumentPath<CmsPageSearchDocument>(documentId),
                d => d.Index(CmsPagesIndexName),
                cancellationToken);

            if (response.IsValid || response.Result == Result.NotFound)
            {
                _logger.LogInformation(
                    "CMS page deleted from index | Slug: {Slug} | TenantId: {TenantId}",
                    slug, tenantId);
                return true;
            }

            _logger.LogError(
                "Failed to delete CMS page | Slug: {Slug} | Error: {Error}",
                slug, response.ServerError?.Error?.Reason ?? "Unknown");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception deleting CMS page | Slug: {Slug}", slug);
            return false;
        }
    }

    #endregion

    #region Unified Search

    /// <inheritdoc />
    public async Task<UnifiedSearchResult> SearchAsync(
        Guid tenantId,
        string query,
        string[]? entityTypes = null,
        int page = 1,
        int pageSize = 20,
        string language = "en",
        CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled)
        {
            return UnifiedSearchResult.Failure("Elasticsearch is disabled");
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogInformation(
                "Unified search starting | TenantId: {TenantId} | Query: {Query} | Types: {Types} | Lang: {Lang}",
                tenantId, query, entityTypes != null ? string.Join(",", entityTypes) : "all", language);

            // Determine which indices to search
            var indices = GetIndicesToSearch(entityTypes);
            var skip = (page - 1) * pageSize;
            // Use promotion-aware ranking whenever products index is included
            var includesProducts = indices.Contains(_settings.ProductsIndexName);

            // Build language-aware search fields
            // When searching in Arabic, boost Arabic fields higher
            // When searching in English, boost English fields higher
            var searchFields = language == "ar"
                ? new[] { "nameAr^4", "name^2", "searchText^2", "description", "slug" }
                : new[] { "name^4", "nameAr^2", "searchText^2", "description", "slug" };

            ISearchResponse<object> searchResponse;

            if (includesProducts)
            {
                // PROMOTION-AWARE SEARCH: Use function_score for products
                // This boosts products with active promotions while preserving text relevance
                searchResponse = await _client.SearchAsync<object>(s => s
                    .Index(Indices.Index(indices))
                    .From(skip)
                    .Size(pageSize)
                    .Query(q => q
                        .FunctionScore(fs => fs
                            .Query(qq => qq
                                .Bool(b => b
                                    .Must(
                                        m => m.MultiMatch(mm => mm
                                            .Query(query)
                                            .Fields(searchFields)
                                            .Type(TextQueryType.BestFields)
                                            .Fuzziness(Fuzziness.Auto)
                                        )
                                    )
                                    .Filter(
                                        f => f.Term("tenantId", tenantId.ToString()),
                                        f => f.Term("isActive", true),
                                        f => f.Term("isOnlineVisible", true)
                                    )
                                )
                            )
                            .Functions(funcs => funcs
                                // Base promotion boost: +5 for any active promotion
                                .Weight(w => w
                                    .Weight(5.0)
                                    .Filter(f => f
                                        .Term(t => t.Field("hasActivePromotion").Value(true))
                                    )
                                )
                                // High discount boost: +10 for discounts >= 20%
                                .Weight(w => w
                                    .Weight(10.0)
                                    .Filter(f => f
                                        .Bool(b => b
                                            .Must(
                                                m => m.Term(t => t.Field("hasActivePromotion").Value(true)),
                                                m => m.Range(r => r.Field("promotionDiscountValue").GreaterThanOrEquals(20))
                                            )
                                        )
                                    )
                                )
                                // Manual boost override: directly add SearchBoostOverride value
                                .FieldValueFactor(fvf => fvf
                                    .Field("searchBoostOverride")
                                    .Factor(1.0)
                                    .Modifier(FieldValueFactorModifier.None)
                                    .Missing(0)
                                )
                            )
                            .ScoreMode(FunctionScoreMode.Sum)
                            .BoostMode(FunctionBoostMode.Sum)
                        )
                    )
                    .TrackTotalHits(true)
                    .Sort(sort => sort
                        .Descending(SortSpecialField.Score)
                    ),
                    cancellationToken);

                _logger.LogDebug(
                    "Promotion-aware search used | TenantId: {TenantId} | Query: {Query}",
                    tenantId, query);
            }
            else
            {
                // Standard multi-index search without promotion ranking
                searchResponse = await _client.SearchAsync<object>(s => s
                    .Index(Indices.Index(indices))
                    .From(skip)
                    .Size(pageSize)
                    .Query(q => q
                        .Bool(b => b
                            .Must(
                                m => m.MultiMatch(mm => mm
                                    .Query(query)
                                    .Fields(searchFields)
                                    .Type(TextQueryType.BestFields)
                                    .Fuzziness(Fuzziness.Auto)
                                )
                            )
                            .Filter(
                                f => f.Term("tenantId", tenantId.ToString()),
                                f => f.Term("isActive", true),
                                f => f.Term("isOnlineVisible", true)
                            )
                        )
                    )
                    .TrackTotalHits(true)
                    .Sort(sort => sort
                        .Descending(SortSpecialField.Score)
                    ),
                    cancellationToken);
            }

            stopwatch.Stop();

            if (!searchResponse.IsValid)
            {
                _logger.LogWarning(
                    "Unified search failed | TenantId: {TenantId} | Error: {Error}",
                    tenantId, searchResponse.ServerError?.Error?.Reason ?? "Unknown");
                return UnifiedSearchResult.Failure(searchResponse.ServerError?.Error?.Reason ?? "Search failed");
            }

            // Map results to unified format
            var items = MapSearchHitsToUnifiedItems(searchResponse.Hits);

            _logger.LogInformation(
                "Unified search success | TenantId: {TenantId} | Query: {Query} | Lang: {Lang} | Results: {Count} | TimeMs: {TimeMs} | PromotionAware: {PromotionAware}",
                tenantId, query, language, items.Count, stopwatch.ElapsedMilliseconds, includesProducts);

            return UnifiedSearchResult.Success(
                items,
                searchResponse.Total,
                stopwatch.ElapsedMilliseconds,
                query,
                page,
                pageSize);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex,
                "Unified search exception | TenantId: {TenantId} | Query: {Query}",
                tenantId, query);
            return UnifiedSearchResult.Failure(ex.Message);
        }
    }

    private string[] GetIndicesToSearch(string[]? entityTypes)
    {
        if (entityTypes == null || entityTypes.Length == 0)
        {
            return new[] { _settings.ProductsIndexName, CategoriesIndexName, CmsPagesIndexName };
        }

        var indices = new List<string>();
        foreach (var type in entityTypes.Select(t => t.ToLowerInvariant()))
        {
            switch (type)
            {
                case "product":
                case "products":
                    indices.Add(_settings.ProductsIndexName);
                    break;
                case "category":
                case "categories":
                    indices.Add(CategoriesIndexName);
                    break;
                case "cms":
                case "cmspages":
                case "page":
                case "pages":
                    indices.Add(CmsPagesIndexName);
                    break;
            }
        }

        return indices.Count > 0 ? indices.ToArray() : new[] { _settings.ProductsIndexName };
    }

    private List<UnifiedSearchItem> MapSearchHitsToUnifiedItems(IReadOnlyCollection<IHit<object>> hits)
    {
        var items = new List<UnifiedSearchItem>();

        foreach (var hit in hits)
        {
            try
            {
                var source = hit.Source as IDictionary<string, object>;
                if (source == null) continue;

                var item = new UnifiedSearchItem
                {
                    Score = hit.Score ?? 0
                };

                // Parse common fields
                if (source.TryGetValue("tenantId", out var tid) && Guid.TryParse(tid?.ToString(), out var tenantId))
                    item.TenantId = tenantId;

                if (source.TryGetValue("entityType", out var et))
                    item.EntityType = et?.ToString() ?? "unknown";

                if (source.TryGetValue("name", out var name))
                    item.Name = name?.ToString() ?? string.Empty;

                if (source.TryGetValue("nameAr", out var nameAr))
                    item.NameAr = nameAr?.ToString();

                if (source.TryGetValue("slug", out var slug))
                    item.Slug = slug?.ToString();

                if (source.TryGetValue("description", out var desc))
                    item.Description = desc?.ToString();

                // Entity-specific ID
                if (source.TryGetValue("productId", out var pid) && Guid.TryParse(pid?.ToString(), out var productId))
                    item.EntityId = productId;
                else if (source.TryGetValue("categoryId", out var cid) && Guid.TryParse(cid?.ToString(), out var categoryId))
                    item.EntityId = categoryId;
                else if (source.TryGetValue("pageId", out var pageid) && Guid.TryParse(pageid?.ToString(), out var pageId))
                    item.EntityId = pageId;

                // Product-specific fields
                if (source.TryGetValue("price", out var price) && double.TryParse(price?.ToString(), out var priceValue))
                    item.Price = priceValue;

                if (source.TryGetValue("categoryName", out var catName))
                    item.CategoryName = catName?.ToString();

                items.Add(item);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to map search hit");
            }
        }

        return items;
    }

    #endregion

    #region Header Search (Autocomplete)

    /// <inheritdoc />
    public async Task<HeaderSearchResult> HeaderSuggestAsync(
        Guid tenantId,
        string query,
        int limit = 5,
        string language = "en",
        CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled)
        {
            return HeaderSearchResult.Failure("Elasticsearch is disabled");
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Clamp limit between 1 and 8
            limit = Math.Clamp(limit, 1, 8);

            _logger.LogInformation(
                "Header search starting | TenantId: {TenantId} | Query: {Query} | Limit: {Limit} | Lang: {Lang}",
                tenantId, query, limit, language);

            // =========================================================================
            // SMART HEADER SEARCH QUERY (MULTILINGUAL)
            // 
            // ARCHITECTURE: Shopify-Grade Search
            // ===================================
            // 
            // Key Principles:
            // 1. SearchText is THE POWER FIELD (highest boost)
            // 2. Name with multi-fields for autocomplete and Arabic
            // 3. ASCII folding handles diacritic normalization
            // 4. NO translation between languages
            // 5. Promotion boosts are ADDITIVE (never override text relevance)
            // 
            // IMPORTANT: If product has English name only, Arabic search won't match.
            // Admin must enter Arabic text for Arabic discoverability.
            // =========================================================================

            var searchResponse = await _client.SearchAsync<ProductSearchDocument>(s => s
                .Index(_settings.ProductsIndexName)
                .Size(limit)
                .Query(q => q
                    .FunctionScore(fs => fs
                        .Query(innerQ => innerQ
                            .Bool(b => b
                                .Should(
                                    // SEARCHTEXT: THE POWER FIELD (highest boost)
                                    // Contains: name + nameAr + description + categoryName + SKU
                                    sh => sh.Match(m => m
                                        .Field(f => f.SearchText)
                                        .Query(query)
                                        .Fuzziness(Fuzziness.Auto)
                                        .Boost(6)
                                    ),
                                    // NAME: Primary name field with fuzziness
                                    sh => sh.Match(m => m
                                        .Field(f => f.Name)
                                        .Query(query)
                                        .Fuzziness(Fuzziness.Auto)
                                        .Boost(5)
                                    ),
                                    // NAME.AUTOCOMPLETE: Edge n-gram for prefix matching
                                    sh => sh.Match(m => m
                                        .Field("name.autocomplete")
                                        .Query(query)
                                        .Boost(4)
                                    ),
                                    // NAME.ARABIC: Arabic-specific analyzer
                                    sh => sh.Match(m => m
                                        .Field("name.arabic")
                                        .Query(query)
                                        .Boost(language == "ar" ? 5 : 3)
                                    ),
                                    // SEARCHTEXT.AUTOCOMPLETE: Prefix on aggregated content
                                    sh => sh.Match(m => m
                                        .Field("searchText.autocomplete")
                                        .Query(query)
                                        .Boost(4)
                                    ),
                                    // SEARCHTEXT.ARABIC: Arabic on aggregated content
                                    sh => sh.Match(m => m
                                        .Field("searchText.arabic")
                                        .Query(query)
                                        .Boost(language == "ar" ? 5 : 3)
                                    ),
                                    // SKU: Exact/prefix match
                                    sh => sh.Prefix(p => p
                                        .Field(f => f.SKU)
                                        .Value(query.ToUpperInvariant())
                                        .Boost(3)
                                    ),
                                    // NAMEAR: Optional Arabic name (if provided separately)
                                    sh => sh.Match(m => m
                                        .Field(f => f.NameAr)
                                        .Query(query)
                                        .Boost(language == "ar" ? 4 : 2)
                                    )
                                )
                                .Filter(
                                    f => f.Term(t => t.Field(p => p.TenantId).Value(tenantId)),
                                    f => f.Term(t => t.Field(p => p.IsActive).Value(true)),
                                    f => f.Term(t => t.Field(p => p.IsOnlineVisible).Value(true))
                                )
                                .MinimumShouldMatch(1)
                            )
                        )
                        // Promotion boosts (ADDITIVE - text relevance is NEVER overridden)
                        .Functions(funcs => funcs
                            .Weight(w => w
                                .Filter(f => f.Term(t => t.Field(p => p.HasActivePromotion).Value(true)))
                                .Weight(3.0)
                            )
                            .Weight(w => w
                                .Filter(f => f.Bool(bf => bf
                                    .Must(
                                        fm => fm.Term(t => t.Field(p => p.HasActivePromotion).Value(true)),
                                        fm => fm.Range(r => r.Field(p => p.PromotionDiscountValue).GreaterThanOrEquals(20))
                                    )
                                ))
                                .Weight(2.0)
                            )
                        )
                        .ScoreMode(FunctionScoreMode.Sum)
                        .BoostMode(FunctionBoostMode.Sum)
                    )
                )
                .Sort(sort => sort.Descending(SortSpecialField.Score)),
                cancellationToken);

            stopwatch.Stop();

            if (!searchResponse.IsValid)
            {
                _logger.LogWarning(
                    "Header search failed | TenantId: {TenantId} | Query: {Query} | Error: {Error}",
                    tenantId, query, searchResponse.ServerError?.Error?.Reason ?? "Unknown");
                return HeaderSearchResult.Failure(searchResponse.ServerError?.Error?.Reason ?? "Search failed");
            }

            // Map to HeaderSearchItem
            var items = searchResponse.Hits
                .Select(hit => MapToHeaderSearchItem(hit.Source, hit.Score ?? 0))
                .ToList();

            _logger.LogInformation(
                "Header search success | TenantId: {TenantId} | Query: {Query} | Results: {Count} | TimeMs: {TimeMs}",
                tenantId, query, items.Count, stopwatch.ElapsedMilliseconds);

            return HeaderSearchResult.Success(items, query, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex,
                "Header search exception | TenantId: {TenantId} | Query: {Query}",
                tenantId, query);
            return HeaderSearchResult.Failure(ex.Message);
        }
    }

    /// <summary>
    /// Maps Elasticsearch ProductSearchDocument to HeaderSearchItem.
    /// </summary>
    private static HeaderSearchItem MapToHeaderSearchItem(ProductSearchDocument doc, double score)
    {
        return new HeaderSearchItem
        {
            ProductId = doc.ProductId,
            Name = doc.Name,
            NameAr = doc.NameAr,
            Slug = doc.Slug,
            HasVariants = doc.HasVariants,
            Price = doc.Price,
            HasActivePromotion = doc.HasActivePromotion,
            PromotionBadge = doc.HasActivePromotion 
                ? BuildPromotionBadge(doc.PromotionDiscountValue, doc.PromotionDiscountType)
                : null,
            PromotionDiscountValue = doc.PromotionDiscountValue,
            ImageUrl = doc.ImageUrl,
            CategoryName = doc.CategoryName,
            Score = score
        };
    }

    /// <summary>
    /// Builds a promotion badge text from discount info.
    /// </summary>
    private static string? BuildPromotionBadge(double discountValue, string? discountType)
    {
        if (discountValue <= 0) return null;
        
        return discountType?.ToLowerInvariant() == "percentage"
            ? $"{discountValue:0}% OFF"
            : $"SALE";
    }

    #endregion

    #region Index Management

    /// <inheritdoc />
    public async Task<bool> EnsureIndicesExistAsync(CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("Elasticsearch disabled - skipping index creation");
            return true;
        }

        try
        {
            var success = true;

            // Products index ownership is delegated to ProductSearchIndexService.
            success &= await _productIndexService.EnsureIndexExistsAsync(cancellationToken);

            // Categories index
            success &= await EnsureIndexExistsAsync<CategorySearchDocument>(
                CategoriesIndexName, cancellationToken);

            // CMS Pages index
            success &= await EnsureIndexExistsAsync<CmsPageSearchDocument>(
                CmsPagesIndexName, cancellationToken);

            // Marketplace Services index (P2-12)
            success &= await EnsureIndexExistsAsync<MarketplaceServiceDocument>(
                "marketplace_services", cancellationToken);

            // Public Affiliate Products index (AF-11)
            success &= await EnsureIndexExistsAsync<PublicAffiliateProductSearchDocument>(
                _settings.PublicAffiliateProductsIndexName, cancellationToken);

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception ensuring indices exist");
            return false;
        }
    }

    private async Task<bool> EnsureIndexExistsAsync<T>(string indexName, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            var existsResponse = await _client.Indices.ExistsAsync(indexName, ct: cancellationToken);
            if (existsResponse.Exists)
            {
                _logger.LogDebug("Index '{IndexName}' already exists", indexName);
                return true;
            }

            _logger.LogInformation("Creating index '{IndexName}'", indexName);

            // Generic index creation for non-product types
            var createResponse = await _client.Indices.CreateAsync(indexName, c => c
                .Settings(s => s
                    .NumberOfShards(_settings.NumberOfShards)
                    .NumberOfReplicas(_settings.NumberOfReplicas)
                )
                .Map<T>(m => m.AutoMap()),
                cancellationToken);

            if (createResponse.IsValid)
            {
                _logger.LogInformation("Index '{IndexName}' created successfully", indexName);
                return true;
            }

            _logger.LogError(
                "Failed to create index '{IndexName}' | Error: {Error}",
                indexName, createResponse.ServerError?.Error?.Reason ?? "Unknown");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception creating index '{IndexName}'", indexName);
            return false;
        }
    }

    /// <summary>
    /// Creates the products index with multilingual analyzers.
    /// 
    /// ARCHITECTURE: Shopify-Grade Multilingual Search
    /// ================================================
    /// 
    /// Analyzers:
    /// - multilingual_analyzer: Main analyzer with ASCII folding for diacritic normalization
    /// - autocomplete_analyzer: Edge n-gram for prefix matching (1-20 chars)
    /// - arabic_custom: Arabic-specific with built-in stemmer
    /// 
    /// Key Fields:
    /// - name: Primary name with multi-field support
    /// - searchText: THE POWER FIELD - highest boost
    /// 
    /// NO EXTERNAL PLUGINS REQUIRED:
    /// All analyzers use built-in Elasticsearch components
    /// (asciifolding, standard Arabic stemmer, edge_ngram).
    /// 
    /// IMPORTANT: NO translation between languages.
    /// ASCII folding normalizes diacritics for cross-script matching.
    /// </summary>
    /// <inheritdoc />
    public async Task<BulkIndexResult> BulkIndexProductsAsync(
        IEnumerable<ProductSearchDocument> documents,
        CancellationToken cancellationToken = default)
    {
        var result = new BulkIndexResult();
        var stopwatch = Stopwatch.StartNew();

        foreach (var batch in BatchDocuments(documents, BulkRequestBatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var document in batch)
            {
                document.BuildSearchText();
                if (await _productIndexService.IndexAsync(document, cancellationToken))
                {
                    result.SuccessCount++;
                    continue;
                }

                result.FailureCount++;
                if (result.Errors.Count < MaxReportedBulkErrors)
                {
                    result.Errors.Add(document.GetDocumentId());
                }
            }
        }

        stopwatch.Stop();
        result.ElapsedMs = stopwatch.ElapsedMilliseconds;
        return result;
    }

    /// <inheritdoc />
    public async Task<BulkIndexResult> BulkIndexCategoriesAsync(
        IEnumerable<CategorySearchDocument> documents,
        CancellationToken cancellationToken = default)
    {
        return await BulkIndexAsync(
            documents,
            CategoriesIndexName,
            doc => doc.GetDocumentId(),
            "categories",
            cancellationToken);
    }

    private async Task<BulkIndexResult> BulkIndexAsync<T>(
        IEnumerable<T> documents,
        string indexName,
        Func<T, string> getDocumentId,
        string entityTypeName,
        CancellationToken cancellationToken)
        where T : class
    {
        var result = new BulkIndexResult();
        var stopwatch = Stopwatch.StartNew();

        if (!_settings.Enabled)
        {
            _logger.LogDebug("Elasticsearch disabled - skipping bulk index");
            return result;
        }

        try
        {
            var totalDocuments = 0;

            foreach (var batch in BatchDocuments(documents, BulkRequestBatchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();

                totalDocuments += batch.Count;

                _logger.LogDebug(
                    "Bulk indexing batch | {EntityType} | BatchSize: {BatchSize}",
                    entityTypeName,
                    batch.Count);

                var bulkResponse = await _client.BulkAsync(b =>
                {
                    foreach (var doc in batch)
                    {
                        b.Index<T>(i => i
                            .Index(indexName)
                            .Id(getDocumentId(doc))
                            .Document(doc));
                    }

                    return b;
                }, cancellationToken);

                if (bulkResponse.IsValid)
                {
                    var itemsWithErrors = bulkResponse.ItemsWithErrors?.ToList() ?? new List<BulkResponseItemBase>();
                    result.SuccessCount += batch.Count - itemsWithErrors.Count;
                    result.FailureCount += itemsWithErrors.Count;

                    if (itemsWithErrors.Count > 0 && result.Errors.Count < MaxReportedBulkErrors)
                    {
                        foreach (var itemWithError in itemsWithErrors.Take(MaxReportedBulkErrors - result.Errors.Count))
                        {
                            result.Errors.Add($"{itemWithError.Id}: {itemWithError.Error?.Reason}");
                        }
                    }
                }
                else
                {
                    result.FailureCount += batch.Count;

                    if (result.Errors.Count < MaxReportedBulkErrors)
                    {
                        result.Errors.Add(bulkResponse.ServerError?.Error?.Reason ?? "Bulk operation failed");
                    }

                    _logger.LogError(
                        "Bulk index batch failed | {EntityType} | BatchSize: {BatchSize} | Error: {Error}",
                        entityTypeName,
                        batch.Count,
                        bulkResponse.ServerError?.Error?.Reason ?? "Unknown");
                }
            }

            stopwatch.Stop();
            result.ElapsedMs = stopwatch.ElapsedMilliseconds;

            if (totalDocuments == 0)
            {
                return result;
            }

            _logger.LogInformation(
                "Bulk index completed | {EntityType} | Total: {Total} | Success: {Success} | Failed: {Failed} | TimeMs: {TimeMs}",
                entityTypeName,
                totalDocuments,
                result.SuccessCount,
                result.FailureCount,
                result.ElapsedMs);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            result.ElapsedMs = stopwatch.ElapsedMilliseconds;
            result.Errors.Add(ex.Message);
            _logger.LogError(ex, "Exception during bulk index | {EntityType}", entityTypeName);
        }

        return result;
    }

    private static IEnumerable<List<T>> BatchDocuments<T>(IEnumerable<T> documents, int batchSize)
    {
        ArgumentNullException.ThrowIfNull(documents);

        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        }

        using var enumerator = documents.GetEnumerator();
        while (true)
        {
            var batch = new List<T>(batchSize);
            while (batch.Count < batchSize && enumerator.MoveNext())
            {
                batch.Add(enumerator.Current);
            }

            if (batch.Count == 0)
            {
                yield break;
            }

            yield return batch;
        }
    }

    /// <inheritdoc />
    public async Task<long> DeleteStaleCmsPagesAsync(
        Guid tenantId,
        IReadOnlyCollection<string> authoritativeSlugs,
        CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled)
        {
            _logger.LogDebug("Elasticsearch disabled - skipping stale CMS page cleanup");
            return 0;
        }

        try
        {
            var normalizedSlugs = authoritativeSlugs
                .Where(slug => !string.IsNullOrWhiteSpace(slug))
                .Select(slug => slug.Trim().ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            QueryContainer BuildStaleCmsPageQuery(QueryContainerDescriptor<CmsPageSearchDocument> query)
            {
                var filters = new List<Func<QueryContainerDescriptor<CmsPageSearchDocument>, QueryContainer>>
                {
                    filter => filter.Term(term => term.Field(document => document.TenantId).Value(tenantId))
                };

                if (normalizedSlugs.Length > 0)
                {
                    filters.Add(filter => filter.Bool(boolean => boolean.MustNot(mustNot => mustNot.Terms(terms => terms
                        .Field(document => document.Slug)
                        .Terms(normalizedSlugs)))));
                }

                return query.Bool(boolean => boolean.Filter(filters.ToArray()));
            }

            var response = await _client.DeleteByQueryAsync<CmsPageSearchDocument>(
                descriptor => descriptor
                    .Index(CmsPagesIndexName)
                    .Query(BuildStaleCmsPageQuery)
                    .Refresh(true),
                cancellationToken);

            if (response.IsValid)
            {
                _logger.LogInformation(
                    "Deleted stale tenant-scoped CMS page documents from search | TenantId: {TenantId} | Deleted: {Deleted} | AuthoritativeCount: {AuthoritativeCount}",
                    tenantId,
                    response.Deleted,
                    normalizedSlugs.Length);
                return response.Deleted;
            }

            _logger.LogError(
                "Failed to delete stale CMS page documents from search | TenantId: {TenantId} | Error: {Error}",
                tenantId,
                response.ServerError?.Error?.Reason ?? response.OriginalException?.Message ?? "Unknown");
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Exception deleting stale CMS page documents from search | TenantId: {TenantId}",
                tenantId);
            return 0;
        }
    }

    #endregion
}
