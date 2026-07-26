using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Nest;
using PointOfSale.Infrastructure.Elasticsearch.Configuration;
using PointOfSale.Infrastructure.Elasticsearch.Documents;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services;

namespace PointOfSale.Infrastructure.Elasticsearch.Services;

public interface ICommerceHubSearchReadService
{
    Task<CommerceHubSearchReadResult?> SearchPublishedProductsAsync(
        CommerceHubSearchRequest request,
        CancellationToken cancellationToken = default);

    Task<CommerceHubSearchSuggestionsResult?> GetSuggestionsAsync(
        CommerceHubSearchSuggestionsRequest request,
        CancellationToken cancellationToken = default);

    Task<CommerceHubSearchAutocompleteResult?> GetAutocompleteAsync(
        CommerceHubSearchAutocompleteRequest request,
        CancellationToken cancellationToken = default);
}

public class CommerceHubSearchReadService : ICommerceHubSearchReadService
{
    private const int MaxAggregationBuckets = 20;
    private const int MaxCategoryAggregationBuckets = 100;
    private const int MaxSuggestionTerms = 8;
    private const string CategoryFacetAggregationName = "facet_categories";
    private const string PriceFacetAggregationName = "facet_price";

    private readonly IElasticClient _client;
    private readonly ElasticsearchSettings _settings;
    private readonly MongoDbContext _mongoDbContext;
    private readonly ILogger<CommerceHubSearchReadService> _logger;

    public CommerceHubSearchReadService(
        IElasticClient client,
        IOptions<ElasticsearchSettings> settings,
        MongoDbContext mongoDbContext,
        ILogger<CommerceHubSearchReadService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _mongoDbContext = mongoDbContext;
        _logger = logger;
    }

    public async Task<CommerceHubSearchReadResult?> SearchPublishedProductsAsync(
        CommerceHubSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled)
        {
            return null;
        }

        var page = request.Page <= 0 ? 1 : request.Page;
        var pageSize = request.PageSize <= 0 ? 20 : Math.Min(request.PageSize, 100);
        var skip = (page - 1) * pageSize;

        try
        {
            var response = await _client.SearchAsync<CommerceHubProductSearchDocument>(
                s => s
                    .Index(_settings.CommerceHubProductsIndexName)
                    .From(skip)
                    .Size(pageSize)
                    .TrackTotalHits(true)
                    .Source(false)
                    .Query(q => BuildQuery(q, request))
                    .Sort(sort => BuildSort(sort, request))
                    .Aggregations(agg => BuildAggregations(agg, request.FacetDefinitions)),
                cancellationToken);

            if (!response.IsValid)
            {
                _logger.LogWarning(
                    "CommerceHub Elasticsearch read failed, falling back to Mongo | Query: {Query} | CategoryId: {CategoryId} | Error: {Error}",
                    request.Query,
                    request.CategoryId,
                    response.ServerError?.Error?.Reason ?? response.OriginalException?.Message ?? "Unknown error");
                return null;
            }

            var ids = response.Hits
                .Select(TryParseListingId)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToList();

            var items = await HydrateDocumentsAsync(
                ids,
                request.TenantId,
                request.ExcludedTenantIds,
                cancellationToken);
            var aggregations = MapAggregations(response.Aggregations, request.FacetDefinitions);

            return new CommerceHubSearchReadResult
            {
                Items = items,
                TotalCount = response.Total,
                Aggregations = aggregations,
                CategoryBuckets = MapCategoryBuckets(response.Aggregations),
                PriceStats = MapPriceStats(response.Aggregations)
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "CommerceHub Elasticsearch query failed, falling back to Mongo | Query: {Query} | CategoryId: {CategoryId}",
                request.Query,
                request.CategoryId);
            return null;
        }
    }

    public async Task<CommerceHubSearchSuggestionsResult?> GetSuggestionsAsync(
        CommerceHubSearchSuggestionsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled || string.IsNullOrWhiteSpace(request.Query))
        {
            return null;
        }

        var normalizedLimit = request.Limit <= 0 ? 8 : Math.Min(request.Limit, 10);

        try
        {
            var response = await _client.SearchAsync<CommerceHubProductSearchDocument>(
                s => s
                    .Index(_settings.CommerceHubProductsIndexName)
                    .Size(normalizedLimit * 2)
                    .TrackTotalHits(false)
                    .Query(q => BuildSuggestionsQuery(q, request))
                    .Sort(sort => sort
                        .Descending(SortSpecialField.Score)
                        .Descending(x => x.InStock)
                        .Descending(x => x.CreatedAtUtc))
                    .Aggregations(agg => agg
                        .Terms("popular_terms", terms => terms
                            .Field("productName.keyword")
                            .Size(normalizedLimit)
                            .MinimumDocumentCount(1))),
                cancellationToken);

            if (!response.IsValid)
            {
                _logger.LogWarning(
                    "CommerceHub Elasticsearch suggestions failed, falling back to Mongo | Query: {Query} | CategoryId: {CategoryId} | Error: {Error}",
                    request.Query,
                    request.CategoryId,
                    response.ServerError?.Error?.Reason ?? response.OriginalException?.Message ?? "Unknown error");
                return null;
            }

            var products = response.Hits
                .Select(hit => hit.Source)
                .Where(source => source != null)
                .GroupBy(source => source!.ProductId)
                .Select(group => group.First()!)
                .Take(normalizedLimit)
                .Select(source => new CommerceHubSearchSuggestionProduct
                {
                    Id = source.Id,
                    ProductId = source.ProductId,
                    VariantId = source.VariantId,
                    ProductName = source.ProductName,
                    Price = Convert.ToDecimal(source.Price),
                    Currency = source.Currency,
                    CategoryId = source.CategoryId,
                    CategoryName = source.CategoryName,
                    StoreName = source.StoreName
                })
                .ToList();

            var popularTerms = response.Aggregations.Terms("popular_terms")?.Buckets
                .Where(bucket => !string.IsNullOrWhiteSpace(bucket.Key))
                .Select(bucket => bucket.Key)
                .Take(normalizedLimit)
                .ToList()
                ?? new List<string>();

            var similarTerms = BuildSimilarTerms(
                request.Query,
                products.Select(product => product.ProductName),
                popularTerms,
                normalizedLimit);

            return new CommerceHubSearchSuggestionsResult
            {
                Query = request.Query.Trim(),
                Products = products,
                PopularTerms = popularTerms,
                SimilarTerms = similarTerms
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "CommerceHub Elasticsearch suggestions failed, falling back to Mongo | Query: {Query} | CategoryId: {CategoryId}",
                request.Query,
                request.CategoryId);
            return null;
        }
    }

    public async Task<CommerceHubSearchAutocompleteResult?> GetAutocompleteAsync(
        CommerceHubSearchAutocompleteRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled || !CommerceHubSearchTextUtility.HasAutocompleteQuery(request.Query))
        {
            return null;
        }

        var normalizedProductLimit = request.ProductLimit <= 0 ? 5 : Math.Min(request.ProductLimit, 10);
        var normalizedCategoryLimit = request.CategoryLimit <= 0 ? 3 : Math.Min(request.CategoryLimit, 8);
        var normalizedSuggestionLimit = request.SuggestionLimit <= 0 ? 5 : Math.Min(request.SuggestionLimit, 10);
        var requestSize = Math.Max(normalizedProductLimit * 3, normalizedSuggestionLimit * 2);

        try
        {
            var response = await _client.SearchAsync<CommerceHubProductSearchDocument>(
                s => s
                    .Index(_settings.CommerceHubProductsIndexName)
                    .Size(requestSize)
                    .TrackTotalHits(false)
                    .Source(false)
                    .Query(q => BuildAutocompleteQuery(q, request))
                    .Highlight(h => h
                        .PreTags("<em>")
                        .PostTags("</em>")
                        .Fields(
                            field => field.Field(x => x.ProductName).NumberOfFragments(1),
                            field => field.Field(x => x.CategoryName).NumberOfFragments(1)))
                    .Sort(sort => sort
                        .Descending(SortSpecialField.Score)
                        .Descending(x => x.InStock)
                        .Descending(x => x.PopularityScore)
                        .Descending(x => x.CreatedAtUtc))
                    .Aggregations(agg => agg
                        .Terms("category_ids", terms => terms
                            .Field(x => x.CategoryId)
                            .Size(normalizedCategoryLimit))
                        .Terms("popular_terms", terms => terms
                            .Field("productName.keyword")
                            .Size(normalizedSuggestionLimit)
                            .MinimumDocumentCount(1))),
                cancellationToken);

            if (!response.IsValid)
            {
                _logger.LogWarning(
                    "CommerceHub Elasticsearch autocomplete failed, falling back to Mongo | Query: {Query} | CategoryId: {CategoryId} | Error: {Error}",
                    request.Query,
                    request.CategoryId,
                    response.ServerError?.Error?.Reason ?? response.OriginalException?.Message ?? "Unknown error");
                return null;
            }

            var ids = response.Hits
                .Select(TryParseListingId)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToList();

            var items = await HydrateDocumentsAsync(
                ids,
                request.TenantId,
                request.ExcludedTenantIds,
                cancellationToken);
            var itemLookup = items.ToDictionary(x => x.Id, x => x);
            var hitLookup = response.Hits
                .Select(hit => new
                {
                    ListingId = TryParseListingId(hit),
                    Hit = hit
                })
                .Where(item => item.ListingId.HasValue)
                .ToDictionary(item => item.ListingId!.Value, item => item.Hit);

            var products = new List<CommerceHubSearchAutocompleteProduct>();
            var seenProductIds = new HashSet<Guid>();

            foreach (var id in ids)
            {
                if (!itemLookup.TryGetValue(id, out var document) || !seenProductIds.Add(document.ProductId))
                {
                    continue;
                }

                hitLookup.TryGetValue(id, out var highlightHit);

                products.Add(new CommerceHubSearchAutocompleteProduct
                {
                    Id = document.Id,
                    ProductId = document.ProductId,
                    VariantId = document.VariantId,
                    ProductName = document.ProductName,
                    ProductNameHighlight = highlightHit == null ? null : ExtractHighlight(highlightHit, "productName"),
                    Image = document.Image,
                    Price = document.Price,
                    Currency = document.Currency,
                    StoreName = document.StoreName,
                    CategoryId = document.CategoryId,
                    CategoryName = document.CategoryName,
                    CategoryNameHighlight = highlightHit == null ? null : ExtractHighlight(highlightHit, "categoryName")
                });

                if (products.Count >= normalizedProductLimit)
                {
                    break;
                }
            }

            var categories = response.Aggregations.Terms("category_ids")?.Buckets
                .Select(bucket =>
                {
                    var value = bucket.KeyAsString ?? bucket.Key?.ToString();
                    return Guid.TryParse(value, out var categoryId)
                        ? new CommerceHubSearchAutocompleteCategoryBucket
                        {
                            CategoryId = categoryId,
                            Count = bucket.DocCount.GetValueOrDefault()
                        }
                        : null;
                })
                .Where(category => category != null)
                .Select(category => category!)
                .Take(normalizedCategoryLimit)
                .ToList()
                ?? new List<CommerceHubSearchAutocompleteCategoryBucket>();

            var popularTerms = response.Aggregations.Terms("popular_terms")?.Buckets
                .Where(bucket => !string.IsNullOrWhiteSpace(bucket.Key))
                .Select(bucket => bucket.Key)
                .Take(normalizedSuggestionLimit)
                .ToList()
                ?? new List<string>();

            var suggestedTerms = BuildSimilarTerms(
                request.Query,
                products.Select(product => product.ProductName),
                popularTerms.Concat(request.QueryExpansions),
                normalizedSuggestionLimit);

            return new CommerceHubSearchAutocompleteResult
            {
                Query = request.Query!.Trim(),
                Products = products,
                Categories = categories,
                SuggestedTerms = suggestedTerms
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "CommerceHub Elasticsearch autocomplete failed, falling back to Mongo | Query: {Query} | CategoryId: {CategoryId}",
                request.Query,
                request.CategoryId);
            return null;
        }
    }

    private QueryContainer BuildQuery(
        QueryContainerDescriptor<CommerceHubProductSearchDocument> query,
        CommerceHubSearchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return query.FunctionScore(fs => fs
                .Query(q => BuildBaseSearchQuery(q, request))
                    .Functions(fn => fn
                        .Weight(w => w
                            .Filter(f => f.Term(t => t.Field(x => x.InStock).Value(true)))
                            .Weight(2.25))
                        .FieldValueFactor(fvf => fvf
                            .Field(x => x.PopularityScore)
                            .Factor(0.45)
                            .Modifier(FieldValueFactorModifier.Log1P)
                            .Missing(0))
                        .Weight(w => w
                            .Filter(f => BuildIntentPriceFilter(f, request.Intent))
                            .Weight(3.5))
                        .GaussDate(gd => gd
                            .Field(x => x.CreatedAtUtc)
                            .Origin(DateMath.Now)
                            .Scale("30d")
                            .Offset("7d")
                            .Decay(0.6)
                            .Weight(1.1)))
                .ScoreMode(FunctionScoreMode.Sum)
                .BoostMode(FunctionBoostMode.Sum));
        }

        var rawQuery = request.Query.Trim();
        var normalizedQuery = CommerceHubSearchDocumentMapper.NormalizeText(rawQuery);

        return query.FunctionScore(fs => fs
            .Query(q => BuildBaseSearchQuery(q, request))
            .Functions(fn => fn
                .Weight(w => w
                    .Filter(f => f.Term(t => t.Field(x => x.InStock).Value(true)))
                    .Weight(2.25))
                .FieldValueFactor(fvf => fvf
                    .Field(x => x.PopularityScore)
                    .Factor(0.45)
                    .Modifier(FieldValueFactorModifier.Log1P)
                    .Missing(0))
                .Weight(w => w
                    .Filter(f => BuildIntentPriceFilter(f, request.Intent))
                    .Weight(3.5))
                .GaussDate(gd => gd
                    .Field(x => x.CreatedAtUtc)
                    .Origin(DateMath.Now)
                    .Scale("30d")
                    .Offset("7d")
                    .Decay(0.6)
                    .Weight(1.1))
                .Weight(w => w
                    .Filter(f => f.Bool(b => b
                        .Should(
                            sh => sh.Term(t => t.Field("productName.keyword").Value(rawQuery)),
                            sh => sh.Term(t => t.Field("productNameNormalized.keyword").Value(normalizedQuery)))
                        .MinimumShouldMatch(1)))
                    .Weight(8.0)))
            .ScoreMode(FunctionScoreMode.Sum)
            .BoostMode(FunctionBoostMode.Sum));
    }

    private QueryContainer BuildSuggestionsQuery(
        QueryContainerDescriptor<CommerceHubProductSearchDocument> query,
        CommerceHubSearchSuggestionsRequest request)
    {
        var normalizedQuery = CommerceHubSearchDocumentMapper.NormalizeText(request.Query);

        return query.FunctionScore(fs => fs
            .Query(q => q.Bool(b => b
                .Filter(BuildSuggestionFilters(request))
                .Should(
                    sh => sh.Term(t => t.Field("productNameNormalized.keyword").Value(normalizedQuery).Boost(10)),
                    sh => sh.Prefix(p => p.Field("productNameNormalized.keyword").Value(normalizedQuery).Boost(8)),
                    sh => sh.MatchPhrasePrefix(mpp => mpp.Field(x => x.ProductName).Query(request.Query).Boost(6)),
                    sh => sh.MatchPhrasePrefix(mpp => mpp.Field(x => x.ProductNameNormalized).Query(normalizedQuery).Boost(5)),
                    sh => sh.MultiMatch(mm => mm
                        .Query(request.Query)
                        .Fields(f => f
                            .Field(x => x.ProductName, 6.0)
                            .Field(x => x.ProductNameNormalized, 5.0)
                            .Field(x => x.Description, 1.5))
                        .Type(TextQueryType.BestFields)
                        .Fuzziness(Fuzziness.Auto)
                        .PrefixLength(1)
                        .Boost(3)))
                .MinimumShouldMatch(1)))
            .Functions(fn => fn
                .Weight(w => w
                    .Filter(f => f.Term(t => t.Field(x => x.InStock).Value(true)))
                    .Weight(2.0))
                .FieldValueFactor(fvf => fvf
                    .Field(x => x.PopularityScore)
                    .Factor(0.35)
                    .Modifier(FieldValueFactorModifier.Log1P)
                    .Missing(0))
                .GaussDate(gd => gd
                    .Field(x => x.CreatedAtUtc)
                    .Origin(DateMath.Now)
                    .Scale("30d")
                    .Offset("7d")
                    .Decay(0.6)
                    .Weight(1.25)))
            .ScoreMode(FunctionScoreMode.Sum)
            .BoostMode(FunctionBoostMode.Sum));
    }

    private QueryContainer BuildAutocompleteQuery(
        QueryContainerDescriptor<CommerceHubProductSearchDocument> query,
        CommerceHubSearchAutocompleteRequest request)
    {
        var rawQuery = request.Query!.Trim();
        var normalizedQuery = CommerceHubSearchDocumentMapper.NormalizeText(rawQuery);
        var categoryBoost = request.Intent.Type == CommerceHubSearchIntentType.Category ? 9d : 5d;
        var shouldQueries = new List<Func<QueryContainerDescriptor<CommerceHubProductSearchDocument>, QueryContainer>>
        {
            sh => sh.Term(t => t.Field("productNameNormalized.keyword").Value(normalizedQuery).Boost(12)),
            sh => sh.Prefix(p => p.Field("productNameNormalized.keyword").Value(normalizedQuery).Boost(10)),
            sh => sh.MatchPhrasePrefix(mpp => mpp.Field(x => x.ProductName).Query(rawQuery).Boost(8)),
            sh => sh.MatchPhrasePrefix(mpp => mpp.Field(x => x.ProductNameNormalized).Query(normalizedQuery).Boost(7)),
            sh => sh.MatchPhrasePrefix(mpp => mpp.Field(x => x.CategoryName).Query(rawQuery).Boost(categoryBoost)),
            sh => sh.MultiMatch(mm => mm
                .Query(rawQuery)
                .Fields(f => f
                    .Field(x => x.ProductName, 6.0)
                    .Field(x => x.ProductNameNormalized, 5.0)
                    .Field(x => x.CategoryName, request.Intent.Type == CommerceHubSearchIntentType.Category ? 5.0 : 3.0)
                    .Field(x => x.StoreName, 2.0)
                    .Field(x => x.Description, 1.5))
                .Type(TextQueryType.BestFields)
                .Fuzziness(Fuzziness.Auto)
                .PrefixLength(1)
                .MinimumShouldMatch("75%")
                .Boost(4))
        };

        foreach (var categoryTerm in request.Intent.CategoryTerms
                     .Select(CommerceHubSearchDocumentMapper.NormalizeText)
                     .Where(term => !string.IsNullOrWhiteSpace(term))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            shouldQueries.Add(sh => sh.MatchPhrasePrefix(mpp => mpp.Field(x => x.CategoryName).Query(categoryTerm).Boost(10)));
            shouldQueries.Add(sh => sh.Term(t => t.Field("categoryName.keyword").Value(categoryTerm).Boost(12)));
        }

        foreach (var expansion in request.QueryExpansions
                     .Select(CommerceHubSearchDocumentMapper.NormalizeText)
                     .Where(expansion => !string.IsNullOrWhiteSpace(expansion))
                     .Where(expansion => !string.Equals(expansion, normalizedQuery, StringComparison.OrdinalIgnoreCase))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            shouldQueries.Add(sh => sh.Term(t => t.Field("productNameNormalized.keyword").Value(expansion).Boost(6)));
            shouldQueries.Add(sh => sh.MatchPhrase(mp => mp.Field(x => x.ProductNameNormalized).Query(expansion).Boost(4)));
            shouldQueries.Add(sh => sh.MultiMatch(mm => mm
                .Query(expansion)
                .Fields(f => f
                    .Field(x => x.ProductName, 3.0)
                    .Field(x => x.ProductNameNormalized, 3.0)
                    .Field(x => x.CategoryName, 2.0)
                    .Field(x => x.Description, 1.0))
                .Type(TextQueryType.BestFields)
                .Fuzziness(Fuzziness.Auto)
                .Boost(2)));
        }

        return query.FunctionScore(fs => fs
            .Query(q => q.Bool(b => b
                .Filter(BuildAutocompleteFilters(request))
                .Should(shouldQueries.ToArray())
                .MinimumShouldMatch(1)))
            .Functions(fn => fn
                .Weight(w => w
                    .Filter(f => f.Term(t => t.Field(x => x.InStock).Value(true)))
                    .Weight(2.0))
                .FieldValueFactor(fvf => fvf
                    .Field(x => x.PopularityScore)
                    .Factor(0.4)
                    .Modifier(FieldValueFactorModifier.Log1P)
                    .Missing(0))
                .Weight(w => w
                    .Filter(f => BuildIntentPriceFilter(f, request.Intent))
                    .Weight(3.5))
                .GaussDate(gd => gd
                    .Field(x => x.CreatedAtUtc)
                    .Origin(DateMath.Now)
                    .Scale("30d")
                    .Offset("7d")
                    .Decay(0.6)
                    .Weight(1.15)))
            .ScoreMode(FunctionScoreMode.Sum)
            .BoostMode(FunctionBoostMode.Sum));
    }

    private QueryContainer BuildBaseSearchQuery(
        QueryContainerDescriptor<CommerceHubProductSearchDocument> query,
        CommerceHubSearchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return query.Bool(b => b
                .Must(m => m.MatchAll())
                .Filter(BuildFilterQueries(request)));
        }

        var rawQuery = request.Query.Trim();
        var normalizedQuery = CommerceHubSearchDocumentMapper.NormalizeText(rawQuery);

        return query.Bool(b => b
            .Filter(BuildFilterQueries(request))
            .Should(BuildTextQueries(request))
            .MinimumShouldMatch(1));
    }

    private static Func<QueryContainerDescriptor<CommerceHubProductSearchDocument>, QueryContainer>[] BuildTextQueries(
        CommerceHubSearchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return [q => q.MatchAll()];
        }

        var rawQuery = request.Query.Trim();
        var normalizedQuery = CommerceHubSearchDocumentMapper.NormalizeText(rawQuery);
        var categoryBoost = request.Intent.Type == CommerceHubSearchIntentType.Category ? 9d : 4d;
        var shouldQueries = new List<Func<QueryContainerDescriptor<CommerceHubProductSearchDocument>, QueryContainer>>
        {
            sh => sh.Term(t => t.Field("productName.keyword").Value(rawQuery).Boost(14)),
            sh => sh.Term(t => t.Field("productNameNormalized.keyword").Value(normalizedQuery).Boost(12)),
            sh => sh.MatchPhrase(mp => mp.Field(x => x.ProductName).Query(rawQuery).Boost(8)),
            sh => sh.MatchPhrase(mp => mp.Field(x => x.ProductNameNormalized).Query(normalizedQuery).Boost(7)),
            sh => sh.MatchPhrasePrefix(mpp => mpp.Field(x => x.CategoryName).Query(rawQuery).Boost(categoryBoost)),
            sh => sh.MultiMatch(mm => mm
                .Query(rawQuery)
                .Fields(f => f
                    .Field(x => x.ProductName, 6.0)
                    .Field(x => x.ProductNameNormalized, 5.0)
                    .Field(x => x.CategoryName, request.Intent.Type == CommerceHubSearchIntentType.Category ? 5.0 : 2.0)
                    .Field(x => x.Description, 1.5))
                .Type(TextQueryType.BestFields)
                .Fuzziness(Fuzziness.Auto)
                .PrefixLength(1)
                .MinimumShouldMatch("75%")
                .Boost(4)),
            sh => sh.MultiMatch(mm => mm
                .Query(rawQuery)
                .Fields(f => f
                    .Field(x => x.ProductName, 4.0)
                    .Field(x => x.ProductNameNormalized, 3.5))
                .Type(TextQueryType.PhrasePrefix)
                .Boost(3))
        };

        foreach (var categoryTerm in request.Intent.CategoryTerms
                     .Select(CommerceHubSearchDocumentMapper.NormalizeText)
                     .Where(term => !string.IsNullOrWhiteSpace(term))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            shouldQueries.Add(sh => sh.MatchPhrasePrefix(mpp => mpp.Field(x => x.CategoryName).Query(categoryTerm).Boost(10)));
            shouldQueries.Add(sh => sh.Term(t => t.Field("categoryName.keyword").Value(categoryTerm).Boost(12)));
        }

        foreach (var facet in request.FacetDefinitions
                     .Where(facet => !facet.IsNumeric)
                     .GroupBy(facet => facet.Key, StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.First()))
        {
            shouldQueries.Add(sh => sh.Term(t => t.Field($"attributes.{facet.Key}").Value(normalizedQuery).Boost(1.5)));
        }

        foreach (var expansion in request.QueryExpansions
                     .Select(CommerceHubSearchDocumentMapper.NormalizeText)
                     .Where(expansion => !string.IsNullOrWhiteSpace(expansion))
                     .Where(expansion => !string.Equals(expansion, normalizedQuery, StringComparison.OrdinalIgnoreCase))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            shouldQueries.Add(sh => sh.Term(t => t.Field("productNameNormalized.keyword").Value(expansion).Boost(5)));
            shouldQueries.Add(sh => sh.MatchPhrase(mp => mp.Field(x => x.ProductNameNormalized).Query(expansion).Boost(4)));
            shouldQueries.Add(sh => sh.MultiMatch(mm => mm
                .Query(expansion)
                .Fields(f => f
                    .Field(x => x.ProductName, 3.0)
                    .Field(x => x.ProductNameNormalized, 3.0)
                    .Field(x => x.Description, 1.0))
                .Type(TextQueryType.BestFields)
                .Fuzziness(Fuzziness.Auto)
                .Boost(2)));
        }

        return shouldQueries.ToArray();
    }

    private static Func<QueryContainerDescriptor<CommerceHubProductSearchDocument>, QueryContainer>[] BuildFilterQueries(
        CommerceHubSearchRequest request)
    {
        var filters = new List<Func<QueryContainerDescriptor<CommerceHubProductSearchDocument>, QueryContainer>>
        {
            f => f.Term(t => t.Field(x => x.IsPublished).Value(true))
        };

        if (request.TenantId.HasValue)
        {
            filters.Add(f => f.Term(t => t.Field(x => x.TenantId).Value(request.TenantId.Value)));
        }

        if (request.ExcludedTenantIds.Count > 0)
        {
            filters.Add(f => f.Bool(b => b.MustNot(mn => mn.Terms(t => t
                .Field(x => x.TenantId)
                .Terms(request.ExcludedTenantIds.Select(id => (object)id))))));
        }

        if (request.CategoryId.HasValue)
        {
            filters.Add(f => f.Term(t => t.Field(x => x.CategoryId).Value(request.CategoryId.Value)));
        }

        if (request.InStock.HasValue)
        {
            filters.Add(f => f.Term(t => t.Field(x => x.InStock).Value(request.InStock.Value)));
        }

        if (request.MinPrice.HasValue || request.MaxPrice.HasValue)
        {
            filters.Add(f => f.Range(r =>
            {
                var range = r.Field(x => x.Price);
                if (request.MinPrice.HasValue)
                {
                    range = range.GreaterThanOrEquals(Convert.ToDouble(request.MinPrice.Value));
                }

                if (request.MaxPrice.HasValue)
                {
                    range = range.LessThanOrEquals(Convert.ToDouble(request.MaxPrice.Value));
                }

                return range;
            }));
        }

        foreach (var filter in request.Filters)
        {
            filters.Add(f => BuildAttributeFilter(f, filter));
        }

        return filters.ToArray();
    }

    private static Func<QueryContainerDescriptor<CommerceHubProductSearchDocument>, QueryContainer>[] BuildSuggestionFilters(
        CommerceHubSearchSuggestionsRequest request)
    {
        var filters = new List<Func<QueryContainerDescriptor<CommerceHubProductSearchDocument>, QueryContainer>>
        {
            f => f.Term(t => t.Field(x => x.IsPublished).Value(true))
        };

        if (request.TenantId.HasValue)
        {
            filters.Add(f => f.Term(t => t.Field(x => x.TenantId).Value(request.TenantId.Value)));
        }

        if (request.ExcludedTenantIds.Count > 0)
        {
            filters.Add(f => f.Bool(b => b.MustNot(mn => mn.Terms(t => t
                .Field(x => x.TenantId)
                .Terms(request.ExcludedTenantIds.Select(id => (object)id))))));
        }

        if (request.CategoryId.HasValue)
        {
            filters.Add(f => f.Term(t => t.Field(x => x.CategoryId).Value(request.CategoryId.Value)));
        }

        return filters.ToArray();
    }

    private static Func<QueryContainerDescriptor<CommerceHubProductSearchDocument>, QueryContainer>[] BuildAutocompleteFilters(
        CommerceHubSearchAutocompleteRequest request)
    {
        var filters = new List<Func<QueryContainerDescriptor<CommerceHubProductSearchDocument>, QueryContainer>>
        {
            f => f.Term(t => t.Field(x => x.IsPublished).Value(true))
        };

        if (request.TenantId.HasValue)
        {
            filters.Add(f => f.Term(t => t.Field(x => x.TenantId).Value(request.TenantId.Value)));
        }

        if (request.ExcludedTenantIds.Count > 0)
        {
            filters.Add(f => f.Bool(b => b.MustNot(mn => mn.Terms(t => t
                .Field(x => x.TenantId)
                .Terms(request.ExcludedTenantIds.Select(id => (object)id))))));
        }

        if (request.CategoryId.HasValue)
        {
            filters.Add(f => f.Term(t => t.Field(x => x.CategoryId).Value(request.CategoryId.Value)));
        }

        return filters.ToArray();
    }

    private static QueryContainer BuildAttributeFilter(
        QueryContainerDescriptor<CommerceHubProductSearchDocument> query,
        CommerceHubSearchFilter filter)
    {
        var field = new Field($"attributes.{filter.Key}");

        if (filter.HasRange)
        {
            return query.Range(r =>
            {
                var range = r.Field(field);
                if (filter.GreaterThan.HasValue)
                {
                    range = range.GreaterThan(filter.GreaterThan.Value);
                }

                if (filter.GreaterThanOrEqualTo.HasValue)
                {
                    range = range.GreaterThanOrEquals(filter.GreaterThanOrEqualTo.Value);
                }

                if (filter.LessThan.HasValue)
                {
                    range = range.LessThan(filter.LessThan.Value);
                }

                if (filter.LessThanOrEqualTo.HasValue)
                {
                    range = range.LessThanOrEquals(filter.LessThanOrEqualTo.Value);
                }

                return range;
            });
        }

        if (filter.ExactBoolean.HasValue)
        {
            return query.Term(t => t.Field(field).Value(filter.ExactBoolean.Value));
        }

        if (filter.ExactNumber.HasValue)
        {
            return query.Term(t => t.Field(field).Value(filter.ExactNumber.Value));
        }

        if (filter.Terms.Count <= 1)
        {
            return query.Term(t => t.Field(field).Value(filter.Terms.FirstOrDefault()));
        }

        return query.Terms(t => t
            .Field(field)
            .Terms(filter.Terms.Select(term => (object)term)));
    }

    private static SortDescriptor<CommerceHubProductSearchDocument> BuildSort(
        SortDescriptor<CommerceHubProductSearchDocument> sort,
        CommerceHubSearchRequest request)
    {
        return request.Sort switch
        {
            "price_asc" => sort
                .Ascending(x => x.Price)
                .Descending(x => x.InStock)
                .Descending(SortSpecialField.Score),
            "price_desc" => sort
                .Descending(x => x.Price)
                .Descending(x => x.InStock)
                .Descending(SortSpecialField.Score),
            "newest" => sort
                .Descending(x => x.CreatedAtUtc)
                .Descending(x => x.InStock),
            _ => string.IsNullOrWhiteSpace(request.Query)
                ? sort
                    .Descending(x => x.CreatedAtUtc)
                    .Descending(x => x.InStock)
                : sort
                    .Descending(SortSpecialField.Score)
                    .Descending(x => x.InStock)
                    .Descending(x => x.CreatedAtUtc)
                    .Ascending(x => x.Price)
        };
    }

    private static AggregationContainerDescriptor<CommerceHubProductSearchDocument> BuildAggregations(
        AggregationContainerDescriptor<CommerceHubProductSearchDocument> aggregation,
        IReadOnlyCollection<CommerceHubSearchFacetDefinition> facetDefinitions)
    {
        var result = aggregation
            .Terms(CategoryFacetAggregationName, terms => terms
                .Field(x => x.CategoryId)
                .Size(MaxCategoryAggregationBuckets)
                .MinimumDocumentCount(1))
            .Stats(PriceFacetAggregationName, stats => stats
                .Field(x => x.Price));

        foreach (var facet in facetDefinitions
                     .OrderBy(f => f.SortOrder)
                     .ThenBy(f => f.Key, StringComparer.OrdinalIgnoreCase))
        {
            var field = new Field($"attributes.{facet.Key}");
            var aggregationName = GetAggregationName(facet);

            result = facet.IsNumeric
                ? result.Stats(aggregationName, stats => stats.Field(field))
                : result.Terms(aggregationName, terms => terms
                    .Field(field)
                    .Size(MaxAggregationBuckets)
                    .MinimumDocumentCount(1));
        }

        return result;
    }

    private async Task<List<CommerceHubProductDocument>> HydrateDocumentsAsync(
        IReadOnlyCollection<Guid> ids,
        Guid? tenantId,
        IReadOnlyCollection<Guid> excludedTenantIds,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return new List<CommerceHubProductDocument>();
        }

        var filters = new List<FilterDefinition<CommerceHubProductDocument>>
        {
            Builders<CommerceHubProductDocument>.Filter.In(x => x.Id, ids),
            Builders<CommerceHubProductDocument>.Filter.Eq(x => x.IsPublished, true)
        };

        if (tenantId.HasValue)
        {
            filters.Add(Builders<CommerceHubProductDocument>.Filter.Eq(x => x.TenantId, tenantId.Value));
        }

        if (excludedTenantIds.Count > 0)
        {
            filters.Add(Builders<CommerceHubProductDocument>.Filter.Nin(x => x.TenantId, excludedTenantIds));
        }

        var documents = await _mongoDbContext.CommerceHubProducts
            .Find(Builders<CommerceHubProductDocument>.Filter.And(filters))
            .ToListAsync(cancellationToken);

        var documentLookup = documents.ToDictionary(x => x.Id, x => x);
        return ids
            .Where(documentLookup.ContainsKey)
            .Select(id => documentLookup[id])
            .ToList();
    }

    private static Guid? TryParseListingId(IHit<CommerceHubProductSearchDocument> hit)
    {
        var sourceId = hit.Source?.Id;
        if (sourceId.HasValue && sourceId.Value != Guid.Empty)
        {
            return sourceId.Value;
        }

        if (Guid.TryParse(hit.Id, out var listingId))
        {
            return listingId;
        }

        var separatorIndex = hit.Id?.LastIndexOf(':') ?? -1;
        if (separatorIndex >= 0 &&
            separatorIndex < hit.Id!.Length - 1 &&
            Guid.TryParse(hit.Id[(separatorIndex + 1)..], out var scopedListingId))
        {
            return scopedListingId;
        }

        return null;
    }

    private static Dictionary<string, CommerceHubSearchAggregationResult> MapAggregations(
        AggregateDictionary aggregations,
        IReadOnlyCollection<CommerceHubSearchFacetDefinition> facetDefinitions)
    {
        var result = new Dictionary<string, CommerceHubSearchAggregationResult>(StringComparer.OrdinalIgnoreCase);

        foreach (var facet in facetDefinitions)
        {
            var aggregationName = GetAggregationName(facet);
            if (!aggregations.TryGetValue(aggregationName, out var aggregationValue))
            {
                continue;
            }

            if (facet.IsNumeric && aggregationValue is StatsAggregate statsAggregate)
            {
                result[facet.Key] = new CommerceHubSearchAggregationResult
                {
                    Type = "stats",
                    Stats = new CommerceHubSearchStatsResult
                    {
                        Min = statsAggregate.Min,
                        Max = statsAggregate.Max,
                        Avg = statsAggregate.Average,
                        Count = statsAggregate.Count
                    }
                };

                continue;
            }

            if (aggregationValue is BucketAggregate bucketAggregate)
            {
                result[facet.Key] = new CommerceHubSearchAggregationResult
                {
                    Type = "terms",
                    Buckets = bucketAggregate.Items
                        .OfType<KeyedBucket<object>>()
                        .Where(bucket => bucket.Key != null)
                        .Select(bucket => new CommerceHubSearchAggregationBucket
                        {
                            Value = bucket.Key!.ToString() ?? string.Empty,
                            Count = bucket.DocCount.GetValueOrDefault()
                        })
                        .OrderByDescending(bucket => bucket.Count)
                        .ThenBy(bucket => bucket.Value, StringComparer.OrdinalIgnoreCase)
                        .Take(MaxAggregationBuckets)
                        .ToList()
                };
            }
        }

        return result;
    }

    private static List<CommerceHubSearchCategoryBucket> MapCategoryBuckets(AggregateDictionary aggregations)
    {
        return aggregations.Terms(CategoryFacetAggregationName)?.Buckets
            .Select(bucket =>
            {
                var value = bucket.KeyAsString ?? bucket.Key?.ToString();
                return Guid.TryParse(value, out var categoryId)
                    ? new CommerceHubSearchCategoryBucket
                    {
                        CategoryId = categoryId,
                        Count = bucket.DocCount.GetValueOrDefault()
                    }
                    : null;
            })
            .Where(bucket => bucket != null)
            .Select(bucket => bucket!)
            .OrderByDescending(bucket => bucket.Count)
            .ThenBy(bucket => bucket.CategoryId)
            .Take(MaxCategoryAggregationBuckets)
            .ToList()
            ?? new List<CommerceHubSearchCategoryBucket>();
    }

    private static CommerceHubSearchStatsResult? MapPriceStats(AggregateDictionary aggregations)
    {
        var stats = aggregations.Stats(PriceFacetAggregationName);
        if (stats == null)
        {
            return null;
        }

        return new CommerceHubSearchStatsResult
        {
            Min = stats.Min,
            Max = stats.Max,
            Avg = stats.Average,
            Count = stats.Count
        };
    }

    private static List<string> BuildSimilarTerms(
        string? query,
        IEnumerable<string> productNames,
        IEnumerable<string> popularTerms,
        int limit)
    {
        return CommerceHubSearchTextUtility.BuildSimilarTerms(query, productNames, popularTerms, limit);
    }

    private static string GetAggregationName(CommerceHubSearchFacetDefinition facet) =>
        $"attr_{facet.Key}_{(facet.IsNumeric ? "stats" : "terms")}";

    private static QueryContainer BuildIntentPriceFilter(
        QueryContainerDescriptor<CommerceHubProductSearchDocument> query,
        CommerceHubSearchIntentContext intent)
    {
        if (intent.Type != CommerceHubSearchIntentType.Price ||
            (!intent.MinPrice.HasValue && !intent.MaxPrice.HasValue))
        {
            return query.MatchNone();
        }

        return query.Range(r =>
        {
            var range = r.Field(x => x.Price);
            if (intent.MinPrice.HasValue)
            {
                range = range.GreaterThanOrEquals(Convert.ToDouble(intent.MinPrice.Value));
            }

            if (intent.MaxPrice.HasValue)
            {
                range = range.LessThanOrEquals(Convert.ToDouble(intent.MaxPrice.Value));
            }

            return range;
        });
    }

    private static string? ExtractHighlight(
        IHit<CommerceHubProductSearchDocument> hit,
        string fieldName)
    {
        if (hit.Highlight == null ||
            !hit.Highlight.TryGetValue(fieldName, out var highlightField) ||
            highlightField == null)
        {
            return null;
        }

        return highlightField.FirstOrDefault();
    }
}
