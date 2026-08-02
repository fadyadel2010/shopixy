using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nest;
using PointOfSale.Infrastructure.Elasticsearch.Configuration;
using PointOfSale.Infrastructure.Elasticsearch.Documents;
using PointOfSale.Infrastructure.ReadModels.Evolution;

namespace PointOfSale.Infrastructure.Elasticsearch.Services;

/// <summary>
/// Implementation of product search indexing service.
/// 
/// Design Decisions:
/// - Uses NEST high-level client for type safety
/// - Explicit mappings (no dynamic mapping)
/// - Single index with tenant isolation via TenantId field
/// - Graceful failure handling - never breaks the caller
/// - Comprehensive logging for observability
/// </summary>
public class ProductSearchIndexService : IProductSearchIndexService
{
    private const string SearchableFieldName = "isSearchable";

    private readonly IElasticClient _client;
    private readonly ElasticsearchSettings _settings;
    private readonly SearchTelemetry _telemetry;
    private readonly IReadModelEvolutionConvergenceService _readModelEvolution;
    private readonly ILogger<ProductSearchIndexService> _logger;

    public ProductSearchIndexService(
        IElasticClient client,
        IOptions<ElasticsearchSettings> settings,
        SearchTelemetry telemetry,
        IReadModelEvolutionConvergenceService readModelEvolution,
        ILogger<ProductSearchIndexService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _telemetry = telemetry;
        _readModelEvolution = readModelEvolution;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> IndexAsync(ProductSearchDocument document, CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled)
        {
            _logger.LogDebug(
                "Elasticsearch disabled - skipping index for ProductId: {ProductId}, TenantId: {TenantId}",
                document.ProductId, document.TenantId);
            return true;
        }

        var documentId = document.GetDocumentId();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            _logger.LogInformation(
                "PROJECTION_EVENT_TRACE | Stage=Elasticsearch | DocumentId={DocumentId} | IndexName={IndexName} | ProductId={ProductId} | TenantId={TenantId} | IndexAttempt={IndexAttempt} | IndexSucceeded={IndexSucceeded} | DeleteIssued={DeleteIssued} | EventId={EventId} | Timestamp={Timestamp:O}",
                documentId,
                _settings.ProductsIndexName,
                document.ProductId,
                document.TenantId,
                true,
                false,
                false,
                document.LastEventId,
                DateTime.UtcNow);

            // STALE UPDATE PROTECTION: Check if the existing document is newer
            // This prevents out-of-order events from overwriting fresher data.
            var existingDoc = await _client.GetAsync<ProductSearchDocument>(
                documentId, g => g.Index(_settings.ProductsIndexName), cancellationToken);

            if (existingDoc.Found && existingDoc.Source?.IndexedAtUtc > document.IndexedAtUtc)
            {
                sw.Stop();
                _logger.LogWarning(
                    "Skipping stale index update | ProductId: {ProductId} | ExistingIndexedAt: {ExistingIndexedAt} | NewIndexedAt: {NewIndexedAt}",
                    document.ProductId, existingDoc.Source.IndexedAtUtc, document.IndexedAtUtc);
                _telemetry.RecordIndexOperation("index_skipped_stale", sw.Elapsed.TotalMilliseconds, true);
                return true; // Not an error — just a stale event
            }

            _logger.LogDebug(
                "Indexing product document | ProductId: {ProductId} | TenantId: {TenantId} | DocumentId: {DocumentId}",
                document.ProductId, document.TenantId, documentId);

            var response = await _client.IndexAsync(document, i => i
                .Index(_settings.ProductsIndexName)
                .Id(documentId),
                cancellationToken);

            sw.Stop();

            if (response.IsValid)
            {
                _telemetry.RecordIndexOperation("index", sw.Elapsed.TotalMilliseconds, true);
                _logger.LogInformation(
                    "PROJECTION_EVENT_TRACE | Stage=Elasticsearch | DocumentId={DocumentId} | IndexName={IndexName} | ProductId={ProductId} | TenantId={TenantId} | IndexAttempt={IndexAttempt} | IndexSucceeded={IndexSucceeded} | DeleteIssued={DeleteIssued} | EventId={EventId} | Result={Result} | Timestamp={Timestamp:O}",
                    documentId,
                    _settings.ProductsIndexName,
                    document.ProductId,
                    document.TenantId,
                    true,
                    true,
                    false,
                    document.LastEventId,
                    response.Result,
                    DateTime.UtcNow);
                _logger.LogInformation(
                    "Successfully indexed product | ProductId: {ProductId} | TenantId: {TenantId} | DocumentId: {DocumentId} | Result: {Result} | LatencyMs: {LatencyMs}",
                    document.ProductId, document.TenantId, documentId, response.Result, sw.Elapsed.TotalMilliseconds);
                return true;
            }

            _telemetry.RecordIndexOperation("index", sw.Elapsed.TotalMilliseconds, false);
            _logger.LogError(
                "PROJECTION_EVENT_TRACE | Stage=Elasticsearch | DocumentId={DocumentId} | IndexName={IndexName} | ProductId={ProductId} | TenantId={TenantId} | IndexAttempt={IndexAttempt} | IndexSucceeded={IndexSucceeded} | DeleteIssued={DeleteIssued} | EventId={EventId} | Error={Error} | Timestamp={Timestamp:O}",
                documentId,
                _settings.ProductsIndexName,
                document.ProductId,
                document.TenantId,
                true,
                false,
                false,
                document.LastEventId,
                response.ServerError?.Error?.Reason ?? response.OriginalException?.Message ?? "Unknown error",
                DateTime.UtcNow);
            _logger.LogError(
                "Failed to index product | ProductId: {ProductId} | TenantId: {TenantId} | DocumentId: {DocumentId} | Error: {Error}",
                document.ProductId, document.TenantId, documentId,
                response.ServerError?.Error?.Reason ?? response.OriginalException?.Message ?? "Unknown error");
            return false;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _telemetry.RecordIndexOperation("index", sw.Elapsed.TotalMilliseconds, false);
            _logger.LogError(
                ex,
                "PROJECTION_EVENT_TRACE | Stage=Elasticsearch | DocumentId={DocumentId} | IndexName={IndexName} | ProductId={ProductId} | TenantId={TenantId} | IndexAttempt={IndexAttempt} | IndexSucceeded={IndexSucceeded} | DeleteIssued={DeleteIssued} | EventId={EventId} | Timestamp={Timestamp:O}",
                documentId,
                _settings.ProductsIndexName,
                document.ProductId,
                document.TenantId,
                true,
                false,
                false,
                document.LastEventId,
                DateTime.UtcNow);
            _logger.LogError(ex,
                "Exception while indexing product | ProductId: {ProductId} | TenantId: {TenantId} | DocumentId: {DocumentId}",
                document.ProductId, document.TenantId, documentId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid tenantId, Guid productId, CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled)
        {
            _logger.LogDebug(
                "Elasticsearch disabled - skipping delete for ProductId: {ProductId}, TenantId: {TenantId}",
                productId, tenantId);
            return true;
        }

        var documentId = $"{tenantId}_{productId}";
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            _logger.LogInformation(
                "PROJECTION_EVENT_TRACE | Stage=Elasticsearch | DocumentId={DocumentId} | IndexName={IndexName} | ProductId={ProductId} | TenantId={TenantId} | IndexAttempt={IndexAttempt} | IndexSucceeded={IndexSucceeded} | DeleteIssued={DeleteIssued} | Timestamp={Timestamp:O}",
                documentId,
                _settings.ProductsIndexName,
                productId,
                tenantId,
                false,
                false,
                true,
                DateTime.UtcNow);

            _logger.LogDebug(
                "Deleting product document | ProductId: {ProductId} | TenantId: {TenantId} | DocumentId: {DocumentId}",
                productId, tenantId, documentId);

            var response = await _client.DeleteAsync(
                new DocumentPath<ProductSearchDocument>(documentId),
                d => d.Index(_settings.ProductsIndexName),
                cancellationToken);

            sw.Stop();

            if (response.IsValid || response.Result == Result.NotFound)
            {
                _telemetry.RecordIndexOperation("delete", sw.Elapsed.TotalMilliseconds, true);
                _logger.LogInformation(
                    "PROJECTION_EVENT_TRACE | Stage=Elasticsearch | DocumentId={DocumentId} | IndexName={IndexName} | ProductId={ProductId} | TenantId={TenantId} | IndexAttempt={IndexAttempt} | IndexSucceeded={IndexSucceeded} | DeleteIssued={DeleteIssued} | Result={Result} | Timestamp={Timestamp:O}",
                    documentId,
                    _settings.ProductsIndexName,
                    productId,
                    tenantId,
                    false,
                    true,
                    true,
                    response.Result,
                    DateTime.UtcNow);
                _logger.LogInformation(
                    "Successfully deleted product (or not found) | ProductId: {ProductId} | TenantId: {TenantId} | Result: {Result}",
                    productId, tenantId, response.Result);
                return true;
            }

            _telemetry.RecordIndexOperation("delete", sw.Elapsed.TotalMilliseconds, false);
            _logger.LogError(
                "PROJECTION_EVENT_TRACE | Stage=Elasticsearch | DocumentId={DocumentId} | IndexName={IndexName} | ProductId={ProductId} | TenantId={TenantId} | IndexAttempt={IndexAttempt} | IndexSucceeded={IndexSucceeded} | DeleteIssued={DeleteIssued} | Error={Error} | Timestamp={Timestamp:O}",
                documentId,
                _settings.ProductsIndexName,
                productId,
                tenantId,
                false,
                false,
                true,
                response.ServerError?.Error?.Reason ?? response.OriginalException?.Message ?? "Unknown error",
                DateTime.UtcNow);
            _logger.LogError(
                "Failed to delete product | ProductId: {ProductId} | TenantId: {TenantId} | Error: {Error}",
                productId, tenantId,
                response.ServerError?.Error?.Reason ?? response.OriginalException?.Message ?? "Unknown error");
            return false;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _telemetry.RecordIndexOperation("delete", sw.Elapsed.TotalMilliseconds, false);
            _logger.LogError(
                ex,
                "PROJECTION_EVENT_TRACE | Stage=Elasticsearch | DocumentId={DocumentId} | IndexName={IndexName} | ProductId={ProductId} | TenantId={TenantId} | IndexAttempt={IndexAttempt} | IndexSucceeded={IndexSucceeded} | DeleteIssued={DeleteIssued} | Timestamp={Timestamp:O}",
                documentId,
                _settings.ProductsIndexName,
                productId,
                tenantId,
                false,
                false,
                true,
                DateTime.UtcNow);
            _logger.LogError(ex,
                "Exception while deleting product | ProductId: {ProductId} | TenantId: {TenantId}",
                productId, tenantId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> EnsureIndexExistsAsync(CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("Elasticsearch disabled - skipping index creation");
            return true;
        }

        try
        {
            var indexName = _settings.ProductsIndexName;
            
            // Check if index exists
            var existsResponse = await _client.Indices.ExistsAsync(indexName, ct: cancellationToken);
            
            if (existsResponse.Exists)
            {
                var putMappingResponse = await _client.Indices.PutMappingAsync<ProductSearchDocument>(m => m
                    .Index(indexName)
                    .Properties(p => p
                        .Keyword(k => k.Name(n => n.ReadModelName))
                        .Number(n => n.Name(nn => nn.ReadModelSchemaVersion).Type(NumberType.Integer))
                        .Number(n => n.Name(nn => nn.ProjectionVersion).Type(NumberType.Integer))
                        .Date(d => d.Name(n => n.ProjectionConvergedAtUtc).Format("strict_date_optional_time||epoch_millis"))
                        .Boolean(b => b.Name(n => n.IsSearchable))),
                    cancellationToken);

                if (!putMappingResponse.IsValid)
                {
                    _logger.LogError(
                        "Failed to update Elasticsearch index '{IndexName}' mapping for SQL searchability authority | Error: {Error}",
                        indexName,
                        putMappingResponse.ServerError?.Error?.Reason ?? putMappingResponse.OriginalException?.Message);
                    return false;
                }

                _logger.LogInformation("Elasticsearch index '{IndexName}' already exists", indexName);
                return true;
            }

            _logger.LogInformation("Creating Elasticsearch index '{IndexName}' with multilingual analyzers", indexName);

            // =========================================================================
            // INDEX CREATION WITH MULTILINGUAL SUPPORT (Arabic + Latin)
            // 
            // ARCHITECTURE: Shopify-Grade Search
            // - SearchText is THE POWER FIELD
            // - Built-in ASCII folding handles diacritic normalization
            // - Standard Arabic stemmer for Arabic language support
            // - NO translation between languages
            // 
            // NO EXTERNAL PLUGINS REQUIRED:
            // All analyzers use built-in Elasticsearch components
            // (asciifolding, standard Arabic stemmer, edge_ngram).
            // =========================================================================

            var createResponse = await _client.Indices.CreateAsync(indexName, c => c
                .Settings(s => s
                    .NumberOfShards(_settings.NumberOfShards)
                    .NumberOfReplicas(_settings.NumberOfReplicas)
                    .Setting("index.mapping.total_fields.limit", 100)
                    .Analysis(a => a
                        .TokenFilters(tf => tf
                            .EdgeNGram("autocomplete_filter", en => en
                                .MinGram(1)
                                .MaxGram(20)
                            )
                            .Stemmer("arabic_stemmer", st => st
                                .Language("arabic")
                            )
                            // ASCII folding for Latin diacritic removal (café → cafe)
                            .AsciiFolding("ascii_folding", af => af)
                        )
                        .Analyzers(an => an
                            .Custom("multilingual_analyzer", ca => ca
                                .Tokenizer("standard")
                                .Filters("lowercase", "arabic_normalization", "ascii_folding")
                            )
                            .Custom("autocomplete_analyzer", ca => ca
                                .Tokenizer("standard")
                                .Filters("lowercase", "autocomplete_filter")
                            )
                            .Custom("arabic_custom", ca => ca
                                .Tokenizer("standard")
                                .Filters("lowercase", "arabic_normalization", "arabic_stemmer", "ascii_folding")
                            )
                        )
                    )
                )
                .Map<ProductSearchDocument>(m => m
                    .AutoMap()
                    .Properties(p => p
                        .Text(t => t
                            .Name(n => n.Name)
                            .Analyzer("multilingual_analyzer")
                            .Fields(f => f
                                .Text(tx => tx
                                    .Name("autocomplete")
                                    .Analyzer("autocomplete_analyzer")
                                    .SearchAnalyzer("multilingual_analyzer")
                                )
                                .Text(tx => tx
                                    .Name("arabic")
                                    .Analyzer("arabic_custom")
                                )
                                .Keyword(k => k.Name("keyword").IgnoreAbove(256))
                            )
                        )
                        .Text(t => t
                            .Name(n => n.SearchText)
                            .Analyzer("multilingual_analyzer")
                            .Fields(f => f
                                .Text(tx => tx
                                    .Name("autocomplete")
                                    .Analyzer("autocomplete_analyzer")
                                    .SearchAnalyzer("multilingual_analyzer")
                                )
                                .Text(tx => tx
                                    .Name("arabic")
                                    .Analyzer("arabic_custom")
                                )
                            )
                        )
                        .Text(t => t
                            .Name(n => n.NameAr)
                            .Analyzer("arabic_custom")
                            .Fields(f => f
                                .Keyword(k => k.Name("keyword").IgnoreAbove(256))
                            )
                        )
                        .Text(t => t
                            .Name(n => n.CategoryName)
                            .Analyzer("multilingual_analyzer")
                            .Fields(f => f
                                .Keyword(k => k.Name("keyword").IgnoreAbove(256))
                            )
                        )
                        .Text(t => t
                            .Name(n => n.Description)
                            .Analyzer("multilingual_analyzer")
                        )
                    )
                    .Dynamic(DynamicMapping.Strict)
                ),
                cancellationToken);

            if (createResponse.IsValid)
            {
                _logger.LogInformation(
                    "Created Elasticsearch index '{IndexName}' with multilingual analyzers | Shards: {Shards}",
                    indexName, _settings.NumberOfShards);
                return true;
            }

            _logger.LogError(
                "Failed to create Elasticsearch index '{IndexName}' | Error: {Error}",
                indexName, createResponse.ServerError?.Error?.Reason ?? createResponse.OriginalException?.Message);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while creating Elasticsearch index");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<ProductSearchResult> SearchAsync(
        Guid tenantId,
        string query,
        int page,
        int pageSize,
        Guid? categoryId = null,
        decimal? minPrice = null,
        decimal? maxPrice = null,
        PointOfSale.Core.Entities.PromotionRankingConfig? promotionRankingConfig = null,
        bool affiliateModeOnly = false,
        CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled)
        {
            _logger.LogDebug(
                "Elasticsearch disabled - returning failure for search | TenantId: {TenantId} | Query: {Query}",
                tenantId, query);
            return ProductSearchResult.Failure("Elasticsearch is disabled");
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var promotionBoostApplied = false;

        try
        {
            _logger.LogInformation(
                "Searching products | TenantId: {TenantId} | Query: {Query} | Page: {Page} | PageSize: {PageSize} | PromotionRankingEnabled: {PromotionRankingEnabled} | AffiliateMode: {AffiliateMode}",
                tenantId, query, page, pageSize, promotionRankingConfig?.Enabled ?? false, affiliateModeOnly);

            var skip = (page - 1) * pageSize;
            var productSearchCompatibility = await ResolveProductSearchCompatibilityAsync(cancellationToken);

            // Build filter clauses (don't affect scoring)
            var filters = new List<Func<QueryContainerDescriptor<ProductSearchDocument>, QueryContainer>>
            {
                // Tenant isolation filter (CRITICAL)
                f => f.Term(t => t.Field(p => p.TenantId).Value(tenantId)),
                // Active products only
                f => f.Term(t => t.Field(p => p.IsActive).Value(true)),
                // Online visible only
                f => f.Term(t => t.Field(p => p.IsOnlineVisible).Value(true)),
                // Searchable/discoverable only after search index convergence.
                f => ReadModelEvolutionQueryFilters.RequiredBoolean(
                    f,
                    SearchableFieldName,
                    requiredValue: true,
                    productSearchCompatibility)
            };

            // AF-12: Affiliate Mode filter
            if (affiliateModeOnly)
            {
                filters.Add(f => f.Term(t => t.Field(p => p.IsAffiliateEnabled).Value(true)));
            }

            // Optional category filter
            if (categoryId.HasValue)
            {
                filters.Add(f => f.Term(t => t.Field(p => p.CategoryId).Value(categoryId.Value)));
            }

            // Optional price range filter
            if (minPrice.HasValue || maxPrice.HasValue)
            {
                filters.Add(f => f.Range(r =>
                {
                    var range = r.Field(p => p.Price);
                    if (minPrice.HasValue)
                        range = range.GreaterThanOrEquals((double)minPrice.Value);
                    if (maxPrice.HasValue)
                        range = range.LessThanOrEquals((double)maxPrice.Value);
                    return range;
                }));
            }

            // Build the base text query
            Func<QueryContainerDescriptor<ProductSearchDocument>, QueryContainer> textQuery = m => m.MultiMatch(mm => mm
                .Query(query)
                .Fields(f => f
                    .Field(p => p.Name, boost: 3)
                    .Field(p => p.NameAr, boost: 3)
                    .Field(p => p.SKU, boost: 2)
                    .Field(p => p.CategoryName, boost: 1)
                )
                .Type(TextQueryType.BestFields)
                .Fuzziness(Fuzziness.Auto)
            );

            ISearchResponse<ProductSearchDocument> searchResponse;

            // ===========================================
            // PROMOTION-AWARE RANKING using function_score
            // SMART ENHANCEMENTS:
            // 1. Time Decay: Newer promotions rank higher
            // 2. Manual Override: SearchBoostOverride field
            // 3. Category-specific boosts (via config)
            // ===========================================
            if (promotionRankingConfig != null && promotionRankingConfig.Enabled)
            {
                promotionBoostApplied = true;

                _logger.LogDebug(
                    "Applying promotion-aware ranking | TenantId: {TenantId} | BaseBoost: {BaseBoost} | HighDiscountBoost: {HighDiscountBoost} | TimeDecayEnabled: {TimeDecay}",
                    tenantId, promotionRankingConfig.BaseBoost, promotionRankingConfig.HighDiscountBoost, promotionRankingConfig.EnableTimeDecay);

                searchResponse = await _client.SearchAsync<ProductSearchDocument>(s => s
                    .Index(_settings.ProductsIndexName)
                    .From(skip)
                    .Size(pageSize)
                    .Query(q => q
                        .FunctionScore(fs => fs
                            // Base query with text search and filters
                            .Query(innerQ => innerQ
                                .Bool(b => b
                                    .Must(textQuery)
                                    .Filter(filters.ToArray())
                                )
                            )
                            // Scoring functions for promotion boosts
                            .Functions(funcs =>
                            {
                                // Function 1: Base boost for any active promotion
                                funcs.Weight(w => w
                                    .Filter(f => f.Term(t => t.Field(p => p.HasActivePromotion).Value(true)))
                                    .Weight(promotionRankingConfig.BaseBoost)
                                );

                                // Function 2: Additional boost for high-value discounts
                                funcs.Weight(w => w
                                    .Filter(f => f.Bool(bf => bf
                                        .Must(
                                            fm => fm.Term(t => t.Field(p => p.HasActivePromotion).Value(true)),
                                            fm => fm.Range(r => r
                                                .Field(p => p.PromotionDiscountValue)
                                                .GreaterThanOrEquals(promotionRankingConfig.HighDiscountThreshold)
                                            )
                                        )
                                    ))
                                    .Weight(promotionRankingConfig.HighDiscountBoost)
                                );

                                // Function 3: Manual merchandising boost override
                                // Adds SearchBoostOverride value directly to score
                                funcs.FieldValueFactor(fvf => fvf
                                    .Filter(f => f.Bool(bf => bf
                                        .Must(
                                            fm => fm.Term(t => t.Field(p => p.HasActivePromotion).Value(true)),
                                            fm => fm.Range(r => r
                                                .Field(p => p.SearchBoostOverride)
                                                .GreaterThan(0)
                                            )
                                        )
                                    ))
                                    .Field(p => p.SearchBoostOverride)
                                    .Factor(1.0)
                                    .Modifier(FieldValueFactorModifier.None)
                                    .Missing(0)
                                );

                                // Function 4: Time decay for promotion freshness (if enabled)
                                // Newer promotions rank higher than older ones
                                if (promotionRankingConfig.EnableTimeDecay)
                                {
                                    funcs.GaussDate(gd => gd
                                        .Filter(f => f.Bool(bf => bf
                                            .Must(
                                                fm => fm.Term(t => t.Field(p => p.HasActivePromotion).Value(true)),
                                                fm => fm.Exists(e => e.Field(p => p.PromotionStartDate))
                                            )
                                        ))
                                        .Field(p => p.PromotionStartDate)
                                        .Origin(DateTime.UtcNow)
                                        .Scale($"{promotionRankingConfig.TimeDecayScaleDays}d")
                                        .Offset($"{promotionRankingConfig.TimeDecayOffsetDays}d")
                                        .Decay(promotionRankingConfig.TimeDecayMinimum)
                                    );
                                }

                                return funcs;
                            })
                            // CRITICAL: score_mode=sum and boost_mode=sum preserve text relevance
                            // Promotion boosts are ADDITIVE, not multiplicative
                            .ScoreMode(FunctionScoreMode.Sum)
                            .BoostMode(FunctionBoostMode.Sum)
                            // Optional: Cap max boost if configured
                            .MaxBoost(promotionRankingConfig.MaxTotalBoost > 0 
                                ? (float)promotionRankingConfig.MaxTotalBoost 
                                : float.MaxValue)
                        )
                    )
                    .TrackTotalHits(true),
                    cancellationToken);
            }
            else
            {
                // Standard search without promotion ranking
                searchResponse = await _client.SearchAsync<ProductSearchDocument>(s => s
                    .Index(_settings.ProductsIndexName)
                    .From(skip)
                    .Size(pageSize)
                    .Query(q => q
                        .Bool(b => b
                            .Must(textQuery)
                            .Filter(filters.ToArray())
                        )
                    )
                    .TrackTotalHits(true),
                    cancellationToken);
            }

            stopwatch.Stop();

            if (!searchResponse.IsValid)
            {
                // Log as WARNING because Elasticsearch failures are handled by callers as degraded or unavailable search.
                // MongoDB is not search authority; public paths still reapply SQL authority before returning data.
                _logger.LogWarning(
                    "Elasticsearch search failed (public caller must fail closed or revalidate degraded candidates) | TenantId: {TenantId} | Query: {Query} | Error: {Error}",
                    tenantId, query,
                    searchResponse.ServerError?.Error?.Reason ?? searchResponse.OriginalException?.Message ?? "Unknown error");
                return ProductSearchResult.Failure(
                    searchResponse.ServerError?.Error?.Reason ?? "Search failed");
            }

            // Extract product IDs preserving order
            var productIds = searchResponse.Documents
                .Select(d => d.ProductId)
                .ToList();

            var totalCount = searchResponse.Total;

            // Count how many promoted products are in results for metrics
            var promotedCount = searchResponse.Documents.Count(d => d.HasActivePromotion);

            _logger.LogInformation(
                "Elasticsearch search success | TenantId: {TenantId} | Query: {Query} | Results: {ResultCount} | TotalHits: {TotalHits} | PromotedInResults: {PromotedCount} | PromotionBoostApplied: {BoostApplied} | SearchTimeMs: {SearchTimeMs}",
                tenantId, query, productIds.Count, totalCount, promotedCount, promotionBoostApplied, stopwatch.ElapsedMilliseconds);

            _telemetry.RecordSearch("product_search", stopwatch.ElapsedMilliseconds, totalCount, true);

            return ProductSearchResult.Success(productIds, totalCount, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _telemetry.RecordSearch("product_search", stopwatch.ElapsedMilliseconds, 0, false);
            // Log as WARNING because callers decide whether to fail closed or use SQL-revalidated degraded candidates.
            // This exception results in ProductSearchResult.Failure; public callers must fail closed or revalidate candidates.
            _logger.LogWarning(ex,
                "Elasticsearch search exception (public caller must fail closed or revalidate degraded candidates) | TenantId: {TenantId} | Query: {Query} | ElapsedMs: {ElapsedMs}",
                tenantId, query, stopwatch.ElapsedMilliseconds);
            return ProductSearchResult.Failure(ex.Message);
        }
    }


    /// <inheritdoc />
    public async Task<ProductSearchResult> GetBestSellersAsync(
        Guid tenantId,
        int limit,
        bool affiliateModeOnly = false,
        CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled)
        {
            _logger.LogDebug(
                "Elasticsearch disabled - returning failure for best sellers | TenantId: {TenantId}",
                tenantId);
            return ProductSearchResult.Failure("Elasticsearch is disabled");
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            _logger.LogInformation(
                "Getting best sellers | TenantId: {TenantId} | Limit: {Limit} | AffiliateMode: {AffiliateMode}",
                tenantId, limit, affiliateModeOnly);

            var productSearchCompatibility = await ResolveProductSearchCompatibilityAsync(cancellationToken);

            // Build filter list
            var filters = new List<Func<QueryContainerDescriptor<ProductSearchDocument>, QueryContainer>>
            {
                // Tenant isolation filter (CRITICAL)
                f => f.Term(t => t.Field(p => p.TenantId).Value(tenantId)),
                // Active products only
                f => f.Term(t => t.Field(p => p.IsActive).Value(true)),
                // Online visible only
                f => f.Term(t => t.Field(p => p.IsOnlineVisible).Value(true)),
                // Searchable/discoverable only after search index convergence.
                f => ReadModelEvolutionQueryFilters.RequiredBoolean(
                    f,
                    SearchableFieldName,
                    requiredValue: true,
                    productSearchCompatibility)
            };

            // AF-12: Affiliate Mode filter
            if (affiliateModeOnly)
            {
                filters.Add(f => f.Term(t => t.Field(p => p.IsAffiliateEnabled).Value(true)));
            }

            // NOTE: In Phase 2.5, we don't have order/sales data indexed in Elasticsearch yet.
            // As a placeholder, we return most recently created visible products.
            // Future implementation will:
            // 1. Index order data with product aggregations
            // 2. Query aggregated sales counts
            // 3. Return products sorted by sales volume
            var searchResponse = await _client.SearchAsync<ProductSearchDocument>(s => s
                .Index(_settings.ProductsIndexName)
                .Size(limit)
                .Query(q => q
                    .Bool(b => b
                        .Filter(filters.ToArray())
                    )
                )
                .Sort(sort => sort
                    .Descending(p => p.CreatedAtUtc)  // Newest first (placeholder for popularity)
                )
                .TrackTotalHits(false),  // Don't need total count for this query
                cancellationToken);

            stopwatch.Stop();

            if (!searchResponse.IsValid)
            {
                _logger.LogWarning(
                    "Elasticsearch best sellers query failed (caller may use SQL-revalidated degraded candidates) | TenantId: {TenantId} | Error: {Error}",
                    tenantId,
                    searchResponse.ServerError?.Error?.Reason ?? searchResponse.OriginalException?.Message ?? "Unknown error");
                return ProductSearchResult.Failure(
                    searchResponse.ServerError?.Error?.Reason ?? "Best sellers query failed");
            }

            // Extract product IDs preserving order
            var productIds = searchResponse.Documents
                .Select(d => d.ProductId)
                .ToList();

            _logger.LogInformation(
                "Elasticsearch best sellers success | TenantId: {TenantId} | Results: {ResultCount} | SearchTimeMs: {SearchTimeMs}",
                tenantId, productIds.Count, stopwatch.ElapsedMilliseconds);

            return ProductSearchResult.Success(productIds, productIds.Count, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(ex,
                "Elasticsearch best sellers exception (caller may use SQL-revalidated degraded candidates) | TenantId: {TenantId} | ElapsedMs: {ElapsedMs}",
                tenantId, stopwatch.ElapsedMilliseconds);
            return ProductSearchResult.Failure(ex.Message);
        }
    }

    // =========================================================================
    // REINDEX OPERATIONS (For Promotion-Aware Search Pre-Experiment Checklist)
    // =========================================================================

    /// <inheritdoc />
    public async Task<bool> DeleteIndexAsync(CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("Elasticsearch disabled - skipping index deletion");
            return true;
        }

        try
        {
            var indexName = _settings.ProductsIndexName;
            
            // Check if index exists
            var existsResponse = await _client.Indices.ExistsAsync(indexName, ct: cancellationToken);
            
            if (!existsResponse.Exists)
            {
                _logger.LogInformation("Elasticsearch index '{IndexName}' does not exist - nothing to delete", indexName);
                return true;
            }

            _logger.LogWarning("DELETING Elasticsearch index '{IndexName}' for fresh reindex", indexName);

            var deleteResponse = await _client.Indices.DeleteAsync(indexName, ct: cancellationToken);

            if (deleteResponse.IsValid)
            {
                _logger.LogInformation("Successfully deleted Elasticsearch index '{IndexName}'", indexName);
                return true;
            }

            _logger.LogError(
                "Failed to delete Elasticsearch index '{IndexName}' | Error: {Error}",
                indexName, deleteResponse.ServerError?.Error?.Reason ?? deleteResponse.OriginalException?.Message);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while deleting Elasticsearch index");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<int> ReindexAllAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("Elasticsearch disabled - skipping reindex");
            return 0;
        }

        _logger.LogInformation(
            "Starting full reindex for tenant | TenantId: {TenantId}",
            tenantId);

        // NOTE: This method requires the caller to provide ProductDocuments
        // The actual implementation is in the controller which has access to MongoDB
        // This is a placeholder that logs the intent
        
        _logger.LogWarning(
            "ReindexAllAsync requires MongoDB access - use SearchReindexController.ReindexProducts instead | TenantId: {TenantId}",
            tenantId);
        
        return 0;
    }

    private async Task<ReadModelCompatibilityDecision> ResolveProductSearchCompatibilityAsync(
        CancellationToken cancellationToken)
    {
        return await _readModelEvolution.GetCompatibilityDecisionAsync(
            ReadModelEvolutionRegistry.StorefrontProduct,
            ReadModelStore.Elasticsearch,
            cancellationToken);
    }
}
