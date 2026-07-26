using System.Text.RegularExpressions;
using MongoDB.Bson;
using MongoDB.Driver;
using PointOfSale.Infrastructure.Elasticsearch.Documents;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.MongoDB.Documents;

namespace PointOfSale.Infrastructure.MongoDB.Services;

public interface ICommerceHubReadRepository
{
    Task<(List<CommerceHubProductDocument> Items, long TotalCount)> GetPublishedProductsAsync(
        int page,
        int pageSize,
        Guid? categoryId,
        Guid? tenantId,
        bool? inStock,
        decimal? minPrice,
        decimal? maxPrice,
        string? search,
        string? sort,
        CancellationToken cancellationToken = default);

    Task<CommerceHubSearchReadResult> SearchPublishedProductsAsync(
        CommerceHubSearchRequest request,
        CancellationToken cancellationToken = default);

    Task<CommerceHubSearchSuggestionsResult> GetSuggestionsAsync(
        CommerceHubSearchSuggestionsRequest request,
        CancellationToken cancellationToken = default);

    Task<CommerceHubSearchAutocompleteResult> GetAutocompleteAsync(
        CommerceHubSearchAutocompleteRequest request,
        CancellationToken cancellationToken = default);

    Task<CommerceHubZeroResultSuggestionsResult?> GetZeroResultSuggestionsAsync(
        CommerceHubSearchRequest request,
        CancellationToken cancellationToken = default);

    Task<CommerceHubProductDocument?> GetPublishedProductByIdAsync(
        Guid id,
        Guid? tenantId = null,
        CancellationToken cancellationToken = default);
}

public class CommerceHubReadRepository : ICommerceHubReadRepository
{
    private const int MaxAggregationBuckets = 20;
    private const int MaxCategoryAggregationBuckets = 100;
    private const int MaxSuggestionTerms = 8;

    private readonly MongoDbContext _mongoDbContext;

    public CommerceHubReadRepository(MongoDbContext mongoDbContext)
    {
        _mongoDbContext = mongoDbContext;
    }

    public async Task<(List<CommerceHubProductDocument> Items, long TotalCount)> GetPublishedProductsAsync(
        int page,
        int pageSize,
        Guid? categoryId,
        Guid? tenantId,
        bool? inStock,
        decimal? minPrice,
        decimal? maxPrice,
        string? search,
        string? sort,
        CancellationToken cancellationToken = default)
    {
        var result = await SearchPublishedProductsAsync(
            new CommerceHubSearchRequest
            {
                Page = page,
                PageSize = pageSize,
                CategoryId = categoryId,
                TenantId = tenantId,
                InStock = inStock,
                MinPrice = minPrice,
                MaxPrice = maxPrice,
                Query = search,
                Sort = sort
            },
            cancellationToken);

        return (result.Items, result.TotalCount);
    }

    public async Task<CommerceHubSearchReadResult> SearchPublishedProductsAsync(
        CommerceHubSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var filterDocument = BuildFilterDocument(request);
        var filter = new BsonDocumentFilterDefinition<CommerceHubProductDocument>(filterDocument);
        var totalCount = await _mongoDbContext.CommerceHubProducts.CountDocumentsAsync(filter, cancellationToken: cancellationToken);

        var page = request.Page <= 0 ? 1 : request.Page;
        var pageSize = request.PageSize <= 0 ? 20 : Math.Min(request.PageSize, 100);
        var skip = (page - 1) * pageSize;

        var items = await _mongoDbContext.CommerceHubProducts
            .Find(filter)
            .Sort(BuildSortDefinition(request))
            .Skip(skip)
            .Limit(pageSize)
            .ToListAsync(cancellationToken);

        var aggregations = await BuildAggregationsAsync(
            filterDocument,
            request.FacetDefinitions,
            cancellationToken);

        return new CommerceHubSearchReadResult
        {
            Items = items,
            TotalCount = totalCount,
            Aggregations = aggregations,
            CategoryBuckets = await ExecuteSearchCategoryBucketsAsync(
                filterDocument,
                cancellationToken),
            PriceStats = await ExecutePriceStatsAggregationAsync(
                filterDocument,
                cancellationToken)
        };
    }

    public async Task<CommerceHubSearchSuggestionsResult> GetSuggestionsAsync(
        CommerceHubSearchSuggestionsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return new CommerceHubSearchSuggestionsResult();
        }

        var normalizedLimit = request.Limit <= 0 ? 8 : Math.Min(request.Limit, 10);
        var filterDocument = BuildSuggestionFilterDocument(request);
        var filter = new BsonDocumentFilterDefinition<CommerceHubProductDocument>(filterDocument);

        var products = await _mongoDbContext.CommerceHubProducts
            .Find(filter)
            .Sort(BuildSuggestionSortDefinition())
            .Limit(normalizedLimit * 2)
            .ToListAsync(cancellationToken);

        var dedupedProducts = products
            .GroupBy(product => product.ProductId)
            .Select(group => group.First())
            .Take(normalizedLimit)
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

        var popularTerms = await ExecutePopularTermsAggregationAsync(filterDocument, normalizedLimit, cancellationToken);
        var similarTerms = BuildSimilarTerms(
            request.Query,
            dedupedProducts.Select(product => product.ProductName),
            popularTerms,
            normalizedLimit);

        return new CommerceHubSearchSuggestionsResult
        {
            Query = request.Query.Trim(),
            Products = dedupedProducts,
            PopularTerms = popularTerms,
            SimilarTerms = similarTerms
        };
    }

    public async Task<CommerceHubSearchAutocompleteResult> GetAutocompleteAsync(
        CommerceHubSearchAutocompleteRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!CommerceHubSearchTextUtility.HasAutocompleteQuery(request.Query))
        {
            return new CommerceHubSearchAutocompleteResult
            {
                Query = CommerceHubSearchTextUtility.CollapseWhitespace(request.Query) ?? string.Empty
            };
        }

        var normalizedProductLimit = request.ProductLimit <= 0 ? 5 : Math.Min(request.ProductLimit, 10);
        var normalizedCategoryLimit = request.CategoryLimit <= 0 ? 3 : Math.Min(request.CategoryLimit, 8);
        var normalizedSuggestionLimit = request.SuggestionLimit <= 0 ? 5 : Math.Min(request.SuggestionLimit, 10);
        var filterDocument = BuildAutocompleteFilterDocument(request);
        var filter = new BsonDocumentFilterDefinition<CommerceHubProductDocument>(filterDocument);

        var products = await _mongoDbContext.CommerceHubProducts
            .Find(filter)
            .Sort(BuildAutocompleteSortDefinition(request))
            .Limit(normalizedProductLimit * 4)
            .ToListAsync(cancellationToken);

        var dedupedProducts = products
            .GroupBy(product => product.ProductId)
            .Select(group => group.First())
            .Take(normalizedProductLimit)
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

        var popularTerms = await ExecutePopularTermsAggregationAsync(filterDocument, normalizedSuggestionLimit, cancellationToken);
        var suggestedTerms = BuildSimilarTerms(
            request.Query,
            dedupedProducts.Select(product => product.ProductName),
            popularTerms.Concat(request.QueryExpansions),
            normalizedSuggestionLimit);

        return new CommerceHubSearchAutocompleteResult
        {
            Query = request.Query!.Trim(),
            Products = dedupedProducts,
            Categories = (await ExecuteAutocompleteCategoryBucketsAsync(
                    filterDocument,
                    normalizedCategoryLimit,
                    cancellationToken))
                .Select(category => new CommerceHubSearchAutocompleteCategoryBucket
                {
                    CategoryId = category.CategoryId,
                    Count = category.Count
                })
                .ToList(),
            SuggestedTerms = suggestedTerms
        };
    }

    public async Task<CommerceHubZeroResultSuggestionsResult?> GetZeroResultSuggestionsAsync(
        CommerceHubSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var hasSuggestionInputs =
            !string.IsNullOrWhiteSpace(request.Query) ||
            request.CategoryId.HasValue ||
            request.InStock.HasValue ||
            request.MinPrice.HasValue ||
            request.MaxPrice.HasValue ||
            request.Filters.Count > 0;

        if (!hasSuggestionInputs)
        {
            return null;
        }

        var filterSuggestionRequest = new CommerceHubSearchRequest
        {
            Query = request.Query,
            CategoryId = request.CategoryId,
            TenantId = request.TenantId,
            ExcludedTenantIds = request.ExcludedTenantIds,
            InStock = request.InStock,
            MinPrice = request.MinPrice,
            MaxPrice = request.MaxPrice,
            Sort = request.Sort,
            Page = 1,
            PageSize = 1,
            FacetDefinitions = request.FacetDefinitions,
            Filters = Array.Empty<CommerceHubSearchFilter>()
        };

        var categorySuggestionRequest = new CommerceHubSearchRequest
        {
            Query = request.Query,
            CategoryId = null,
            TenantId = request.TenantId,
            ExcludedTenantIds = request.ExcludedTenantIds,
            InStock = request.InStock,
            MinPrice = request.MinPrice,
            MaxPrice = request.MaxPrice,
            Sort = request.Sort,
            Page = 1,
            PageSize = 1,
            FacetDefinitions = Array.Empty<CommerceHubSearchFacetDefinition>(),
            Filters = Array.Empty<CommerceHubSearchFilter>()
        };

        var suggestedFilters = await BuildAggregationsAsync(
            BuildFilterDocument(filterSuggestionRequest),
            request.FacetDefinitions,
            cancellationToken);

        var categories = await ExecuteCategorySuggestionsAsync(
            BuildFilterDocument(categorySuggestionRequest),
            cancellationToken);

        var suggestions = await GetSuggestionsAsync(
            new CommerceHubSearchSuggestionsRequest
            {
                Query = request.Query,
                TenantId = request.TenantId,
                CategoryId = request.CategoryId,
                Limit = 5,
                ExcludedTenantIds = request.ExcludedTenantIds
            },
            cancellationToken);

        return new CommerceHubZeroResultSuggestionsResult
        {
            Products = suggestions.Products,
            PopularTerms = suggestions.PopularTerms,
            SimilarTerms = suggestions.SimilarTerms,
            Categories = categories,
            Filters = suggestedFilters
        };
    }

    public async Task<CommerceHubProductDocument?> GetPublishedProductByIdAsync(
        Guid id,
        Guid? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        var filters = new List<FilterDefinition<CommerceHubProductDocument>>
        {
            Builders<CommerceHubProductDocument>.Filter.Eq(x => x.Id, id),
            Builders<CommerceHubProductDocument>.Filter.Eq(x => x.IsPublished, true)
        };

        if (tenantId.HasValue)
        {
            filters.Add(Builders<CommerceHubProductDocument>.Filter.Eq(x => x.TenantId, tenantId.Value));
        }

        return await _mongoDbContext.CommerceHubProducts
            .Find(Builders<CommerceHubProductDocument>.Filter.And(filters))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static BsonDocument BuildFilterDocument(CommerceHubSearchRequest request)
    {
        var filters = BuildBaseFilters(
            request.CategoryId,
            request.TenantId,
            request.ExcludedTenantIds,
            request.InStock,
            request.MinPrice,
            request.MaxPrice);

        var searchTerms = GetSearchTerms(
            request.Query,
            request.QueryExpansions,
            request.Intent.CategoryTerms);
        if (searchTerms.Count > 0)
        {
            var searchClauses = new BsonArray();
            foreach (var term in searchTerms)
            {
                var regex = new BsonRegularExpression(Regex.Escape(term), "i");
                searchClauses.Add(new BsonDocument("ProductName", regex));
                searchClauses.Add(new BsonDocument("Description", regex));
                searchClauses.Add(new BsonDocument("StoreName", regex));
                searchClauses.Add(new BsonDocument("CategoryName", regex));
            }

            filters.Add(new BsonDocument("$or", searchClauses));
        }

        foreach (var filter in request.Filters)
        {
            filters.Add(BuildAttributeFilterDocument(filter));
        }

        return CombineFilters(filters);
    }

    private static List<string> GetSearchTerms(
        string? query,
        IReadOnlyCollection<string> expansions,
        IReadOnlyCollection<string>? additionalTerms = null)
    {
        var terms = new List<string>();

        var collapsedQuery = CommerceHubSearchTextUtility.CollapseWhitespace(query);
        if (!string.IsNullOrWhiteSpace(collapsedQuery))
        {
            terms.Add(collapsedQuery);
        }

        terms.AddRange(expansions
            .Where(expansion => !string.IsNullOrWhiteSpace(expansion))
            .Select(expansion => CommerceHubSearchTextUtility.CollapseWhitespace(expansion))
            .Where(expansion => !string.IsNullOrWhiteSpace(expansion))
            .Select(expansion => expansion!));

        if (additionalTerms != null)
        {
            terms.AddRange(additionalTerms
                .Where(term => !string.IsNullOrWhiteSpace(term))
                .Select(term => CommerceHubSearchTextUtility.CollapseWhitespace(term))
                .Where(term => !string.IsNullOrWhiteSpace(term))
                .Select(term => term!));
        }

        return terms
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static BsonDocument BuildSuggestionFilterDocument(CommerceHubSearchSuggestionsRequest request)
    {
        var filters = BuildBaseFilters(
            request.CategoryId,
            request.TenantId,
            request.ExcludedTenantIds,
            null,
            null,
            null);

        var escapedSearch = Regex.Escape(CommerceHubSearchTextUtility.CollapseWhitespace(request.Query)!);
        var prefixRegex = new BsonRegularExpression($"^{escapedSearch}", "i");
        var containsRegex = new BsonRegularExpression(escapedSearch, "i");

        filters.Add(new BsonDocument("$or", new BsonArray
        {
            new BsonDocument("ProductName", prefixRegex),
            new BsonDocument("ProductName", containsRegex),
            new BsonDocument("Description", containsRegex),
            new BsonDocument("CategoryName", prefixRegex)
        }));

        return CombineFilters(filters);
    }

    private static BsonDocument BuildAutocompleteFilterDocument(CommerceHubSearchAutocompleteRequest request)
    {
        var filters = BuildBaseFilters(
            request.CategoryId,
            request.TenantId,
            request.ExcludedTenantIds,
            null,
            null,
            null);

        var searchTerms = GetSearchTerms(
            request.Query,
            request.QueryExpansions,
            request.Intent.CategoryTerms);
        var searchClauses = new BsonArray();

        foreach (var term in searchTerms)
        {
            var escapedSearch = Regex.Escape(term);
            var prefixRegex = new BsonRegularExpression($"^{escapedSearch}", "i");
            var containsRegex = new BsonRegularExpression(escapedSearch, "i");

            searchClauses.Add(new BsonDocument("ProductName", prefixRegex));
            searchClauses.Add(new BsonDocument("ProductName", containsRegex));
            searchClauses.Add(new BsonDocument("Description", containsRegex));
            searchClauses.Add(new BsonDocument("CategoryName", prefixRegex));
            searchClauses.Add(new BsonDocument("CategoryName", containsRegex));
            searchClauses.Add(new BsonDocument("StoreName", containsRegex));
        }

        if (searchClauses.Count > 0)
        {
            filters.Add(new BsonDocument("$or", searchClauses));
        }

        return CombineFilters(filters);
    }

    private static List<BsonDocument> BuildBaseFilters(
        Guid? categoryId,
        Guid? tenantId,
        IReadOnlyCollection<Guid> excludedTenantIds,
        bool? inStock,
        decimal? minPrice,
        decimal? maxPrice)
    {
        var filters = new List<BsonDocument>
        {
            new("IsPublished", true)
        };

        if (categoryId.HasValue)
        {
            filters.Add(new BsonDocument("CategoryId", categoryId.Value.ToString()));
        }

        if (tenantId.HasValue)
        {
            filters.Add(new BsonDocument("TenantId", tenantId.Value.ToString()));
        }

        if (excludedTenantIds.Count > 0)
        {
            filters.Add(new BsonDocument(
                "TenantId",
                new BsonDocument(
                    "$nin",
                    new BsonArray(excludedTenantIds
                        .Where(excludedTenantId => excludedTenantId != Guid.Empty)
                        .Distinct()
                        .Select(excludedTenantId => excludedTenantId.ToString())))));
        }

        if (inStock.HasValue)
        {
            filters.Add(new BsonDocument("InStock", inStock.Value));
        }

        if (minPrice.HasValue)
        {
            filters.Add(new BsonDocument("Price", new BsonDocument("$gte", new BsonDecimal128(minPrice.Value))));
        }

        if (maxPrice.HasValue)
        {
            filters.Add(new BsonDocument("Price", new BsonDocument("$lte", new BsonDecimal128(maxPrice.Value))));
        }

        return filters;
    }

    private static BsonDocument CombineFilters(List<BsonDocument> filters)
    {
        return filters.Count == 1
            ? filters[0]
            : new BsonDocument("$and", new BsonArray(filters));
    }

    private static BsonDocument BuildAttributeFilterDocument(CommerceHubSearchFilter filter)
    {
        var field = $"Attributes.{filter.Key}";

        if (filter.HasRange)
        {
            var range = new BsonDocument();
            if (filter.GreaterThan.HasValue)
            {
                range["$gt"] = filter.GreaterThan.Value;
            }

            if (filter.GreaterThanOrEqualTo.HasValue)
            {
                range["$gte"] = filter.GreaterThanOrEqualTo.Value;
            }

            if (filter.LessThan.HasValue)
            {
                range["$lt"] = filter.LessThan.Value;
            }

            if (filter.LessThanOrEqualTo.HasValue)
            {
                range["$lte"] = filter.LessThanOrEqualTo.Value;
            }

            return new BsonDocument(field, range);
        }

        if (filter.ExactBoolean.HasValue)
        {
            return new BsonDocument(field, filter.ExactBoolean.Value);
        }

        if (filter.ExactNumber.HasValue)
        {
            return new BsonDocument(field, filter.ExactNumber.Value);
        }

        if (filter.Terms.Count <= 1)
        {
            return new BsonDocument(field, filter.Terms.FirstOrDefault() ?? string.Empty);
        }

        return new BsonDocument(field, new BsonDocument("$in", new BsonArray(filter.Terms)));
    }

    private static SortDefinition<CommerceHubProductDocument> BuildSortDefinition(CommerceHubSearchRequest request)
    {
        var sortBuilder = Builders<CommerceHubProductDocument>.Sort;

        return request.Sort switch
        {
            "price_asc" => sortBuilder
                .Ascending(x => x.Price)
                .Descending(x => x.InStock)
                .Descending(x => x.AvailableStock)
                .Ascending(x => x.ProductName),
            "price_desc" => sortBuilder
                .Descending(x => x.Price)
                .Descending(x => x.InStock)
                .Descending(x => x.AvailableStock)
                .Ascending(x => x.ProductName),
            "newest" => sortBuilder
                .Descending(x => x.CreatedAtUtc)
                .Descending(x => x.InStock)
                .Descending(x => x.AvailableStock),
            _ => string.IsNullOrWhiteSpace(request.Query)
                ? sortBuilder
                    .Descending(x => x.CreatedAtUtc)
                    .Descending(x => x.InStock)
                    .Descending(x => x.AvailableStock)
                : sortBuilder
                    .Descending(x => x.InStock)
                    .Descending(x => x.AvailableStock)
                    .Ascending(x => x.ProductName)
        };
    }

    private static SortDefinition<CommerceHubProductDocument> BuildSuggestionSortDefinition()
    {
        var sortBuilder = Builders<CommerceHubProductDocument>.Sort;
        return sortBuilder
            .Descending(x => x.InStock)
            .Descending(x => x.AvailableStock)
            .Descending(x => x.CreatedAtUtc)
            .Ascending(x => x.ProductName);
    }

    private static SortDefinition<CommerceHubProductDocument> BuildAutocompleteSortDefinition(
        CommerceHubSearchAutocompleteRequest request)
    {
        var sortBuilder = Builders<CommerceHubProductDocument>.Sort;

        if (request.Intent.Type == CommerceHubSearchIntentType.Price)
        {
            if (request.Intent.MinPrice.HasValue && !request.Intent.MaxPrice.HasValue)
            {
                return sortBuilder
                    .Descending(x => x.InStock)
                    .Descending(x => x.Price)
                    .Descending(x => x.PopularityScore)
                    .Descending(x => x.AvailableStock)
                    .Descending(x => x.CreatedAtUtc)
                    .Ascending(x => x.ProductName);
            }

            return sortBuilder
                .Descending(x => x.InStock)
                .Ascending(x => x.Price)
                .Descending(x => x.PopularityScore)
                .Descending(x => x.AvailableStock)
                .Descending(x => x.CreatedAtUtc)
                .Ascending(x => x.ProductName);
        }

        return sortBuilder
            .Descending(x => x.InStock)
            .Descending(x => x.PopularityScore)
            .Descending(x => x.AvailableStock)
            .Descending(x => x.CreatedAtUtc)
            .Ascending(x => x.ProductName);
    }

    private async Task<Dictionary<string, CommerceHubSearchAggregationResult>> BuildAggregationsAsync(
        BsonDocument renderedFilter,
        IReadOnlyCollection<CommerceHubSearchFacetDefinition> facetDefinitions,
        CancellationToken cancellationToken)
    {
        var aggregations = new Dictionary<string, CommerceHubSearchAggregationResult>(StringComparer.OrdinalIgnoreCase);
        if (facetDefinitions.Count == 0)
        {
            return aggregations;
        }

        foreach (var facet in facetDefinitions
                     .OrderBy(f => f.SortOrder)
                     .ThenBy(f => f.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (facet.IsNumeric)
            {
                var stats = await ExecuteStatsAggregationAsync(renderedFilter, facet.Key, cancellationToken);
                if (stats != null)
                {
                    aggregations[facet.Key] = new CommerceHubSearchAggregationResult
                    {
                        Type = "stats",
                        Stats = stats
                    };
                }

                continue;
            }

            var buckets = await ExecuteTermsAggregationAsync(renderedFilter, facet.Key, cancellationToken);
            aggregations[facet.Key] = new CommerceHubSearchAggregationResult
            {
                Type = "terms",
                Buckets = buckets
            };
        }

        return aggregations;
    }

    private async Task<List<CommerceHubSearchAggregationBucket>> ExecuteTermsAggregationAsync(
        BsonDocument renderedFilter,
        string key,
        CancellationToken cancellationToken)
    {
        var valuePath = $"$Attributes.{key}";
        var pipeline = new[]
        {
            new BsonDocument("$match", renderedFilter),
            new BsonDocument("$project", new BsonDocument("value", valuePath)),
            new BsonDocument("$match", new BsonDocument("value", new BsonDocument
            {
                { "$exists", true },
                { "$ne", BsonNull.Value }
            })),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", "$value" },
                { "count", new BsonDocument("$sum", 1) }
            }),
            new BsonDocument("$sort", new BsonDocument
            {
                { "count", -1 },
                { "_id", 1 }
            }),
            new BsonDocument("$limit", MaxAggregationBuckets)
        };

        var results = await _mongoDbContext.CommerceHubProducts
            .Aggregate<BsonDocument>(pipeline)
            .ToListAsync(cancellationToken);

        return results
            .Where(x => x.TryGetValue("_id", out var id) && !id.IsBsonNull)
            .Select(x => new CommerceHubSearchAggregationBucket
            {
                Value = x["_id"]?.ToString() ?? string.Empty,
                Count = x.GetValue("count", 0).ToInt64()
            })
            .OrderByDescending(bucket => bucket.Count)
            .ThenBy(bucket => bucket.Value, StringComparer.OrdinalIgnoreCase)
            .Take(MaxAggregationBuckets)
            .ToList();
    }

    private async Task<CommerceHubSearchStatsResult?> ExecuteStatsAggregationAsync(
        BsonDocument renderedFilter,
        string key,
        CancellationToken cancellationToken)
    {
        var valuePath = $"$Attributes.{key}";
        var pipeline = new[]
        {
            new BsonDocument("$match", renderedFilter),
            new BsonDocument("$project", new BsonDocument("value", valuePath)),
            new BsonDocument("$match", new BsonDocument("value", new BsonDocument
            {
                { "$exists", true },
                { "$ne", BsonNull.Value }
            })),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", BsonNull.Value },
                { "min", new BsonDocument("$min", "$value") },
                { "max", new BsonDocument("$max", "$value") },
                { "avg", new BsonDocument("$avg", "$value") },
                { "count", new BsonDocument("$sum", 1) }
            })
        };

        var result = await _mongoDbContext.CommerceHubProducts
            .Aggregate<BsonDocument>(pipeline)
            .FirstOrDefaultAsync(cancellationToken);

        if (result == null)
        {
            return null;
        }

        return new CommerceHubSearchStatsResult
        {
            Min = TryConvertToDouble(result.GetValue("min", BsonNull.Value)),
            Max = TryConvertToDouble(result.GetValue("max", BsonNull.Value)),
            Avg = TryConvertToDouble(result.GetValue("avg", BsonNull.Value)),
            Count = result.GetValue("count", 0).ToInt64()
        };
    }

    private async Task<List<string>> ExecutePopularTermsAggregationAsync(
        BsonDocument renderedFilter,
        int limit,
        CancellationToken cancellationToken)
    {
        var pipeline = new[]
        {
            new BsonDocument("$match", renderedFilter),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", "$ProductName" },
                { "count", new BsonDocument("$sum", 1) }
            }),
            new BsonDocument("$match", new BsonDocument("_id", new BsonDocument
            {
                { "$exists", true },
                { "$nin", new BsonArray { BsonNull.Value, string.Empty } }
            })),
            new BsonDocument("$sort", new BsonDocument
            {
                { "count", -1 },
                { "_id", 1 }
            }),
            new BsonDocument("$limit", Math.Min(limit, MaxSuggestionTerms))
        };

        var results = await _mongoDbContext.CommerceHubProducts
            .Aggregate<BsonDocument>(pipeline)
            .ToListAsync(cancellationToken);

        return results
            .Where(x => x.TryGetValue("_id", out var id) && !id.IsBsonNull)
            .Select(x => x["_id"]?.ToString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToList();
    }

    private async Task<List<CommerceHubSearchCategorySuggestion>> ExecuteCategorySuggestionsAsync(
        BsonDocument renderedFilter,
        CancellationToken cancellationToken)
    {
        var pipeline = new[]
        {
            new BsonDocument("$match", renderedFilter),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", new BsonDocument
                    {
                        { "categoryId", "$CategoryId" },
                        { "categoryName", "$CategoryName" }
                    }
                },
                { "count", new BsonDocument("$sum", 1) }
            }),
            new BsonDocument("$sort", new BsonDocument
            {
                { "count", -1 },
                { "_id.categoryName", 1 }
            }),
            new BsonDocument("$limit", 5)
        };

        var results = await _mongoDbContext.CommerceHubProducts
            .Aggregate<BsonDocument>(pipeline)
            .ToListAsync(cancellationToken);

        return results
            .Select(document =>
            {
                var key = document.GetValue("_id", new BsonDocument()).AsBsonDocument;
                var categoryIdValue = key.GetValue("categoryId", BsonNull.Value);
                var categoryNameValue = key.GetValue("categoryName", BsonNull.Value);

                if (categoryIdValue.IsBsonNull || !Guid.TryParse(categoryIdValue.ToString(), out var categoryId))
                {
                    return null;
                }

                return new CommerceHubSearchCategorySuggestion
                {
                    CategoryId = categoryId,
                    CategoryName = categoryNameValue.IsBsonNull ? null : categoryNameValue.ToString(),
                    Count = document.GetValue("count", 0).ToInt64()
                };
            })
            .Where(category => category != null)
            .Select(category => category!)
            .ToList();
    }

    private Task<List<CommerceHubSearchCategoryBucket>> ExecuteSearchCategoryBucketsAsync(
        BsonDocument renderedFilter,
        CancellationToken cancellationToken)
    {
        return ExecuteAutocompleteCategoryBucketsAsync(
            renderedFilter,
            MaxCategoryAggregationBuckets,
            cancellationToken);
    }

    private async Task<List<CommerceHubSearchCategoryBucket>> ExecuteAutocompleteCategoryBucketsAsync(
        BsonDocument renderedFilter,
        int limit,
        CancellationToken cancellationToken)
    {
        var pipeline = new[]
        {
            new BsonDocument("$match", renderedFilter),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", "$CategoryId" },
                { "count", new BsonDocument("$sum", 1) }
            }),
            new BsonDocument("$match", new BsonDocument("_id", new BsonDocument
            {
                { "$exists", true },
                { "$nin", new BsonArray { BsonNull.Value, string.Empty } }
            })),
            new BsonDocument("$sort", new BsonDocument
            {
                { "count", -1 },
                { "_id", 1 }
            }),
            new BsonDocument("$limit", limit)
        };

        var results = await _mongoDbContext.CommerceHubProducts
            .Aggregate<BsonDocument>(pipeline)
            .ToListAsync(cancellationToken);

        return results
            .Where(document => document.TryGetValue("_id", out var id) && !id.IsBsonNull)
            .Select(document =>
            {
                var value = document["_id"]?.ToString();
                return Guid.TryParse(value, out var categoryId)
                    ? new CommerceHubSearchCategoryBucket
                    {
                        CategoryId = categoryId,
                        Count = document.GetValue("count", 0).ToInt64()
                    }
                    : null;
            })
            .Where(category => category != null)
            .Select(category => category!)
            .ToList();
    }

    private async Task<CommerceHubSearchStatsResult?> ExecutePriceStatsAggregationAsync(
        BsonDocument renderedFilter,
        CancellationToken cancellationToken)
    {
        var pipeline = new[]
        {
            new BsonDocument("$match", renderedFilter),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", BsonNull.Value },
                { "min", new BsonDocument("$min", "$Price") },
                { "max", new BsonDocument("$max", "$Price") },
                { "avg", new BsonDocument("$avg", "$Price") },
                { "count", new BsonDocument("$sum", 1) }
            })
        };

        var result = await _mongoDbContext.CommerceHubProducts
            .Aggregate<BsonDocument>(pipeline)
            .FirstOrDefaultAsync(cancellationToken);

        if (result == null)
        {
            return null;
        }

        return new CommerceHubSearchStatsResult
        {
            Min = TryConvertToDouble(result.GetValue("min", BsonNull.Value)),
            Max = TryConvertToDouble(result.GetValue("max", BsonNull.Value)),
            Avg = TryConvertToDouble(result.GetValue("avg", BsonNull.Value)),
            Count = result.GetValue("count", 0).ToInt64()
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

    private static double? TryConvertToDouble(BsonValue value)
    {
        if (value == null || value.IsBsonNull)
        {
            return null;
        }

        return value.BsonType switch
        {
            BsonType.Int32 => value.AsInt32,
            BsonType.Int64 => value.AsInt64,
            BsonType.Double => value.AsDouble,
            BsonType.Decimal128 => Convert.ToDouble(value.AsDecimal),
            _ => null
        };
    }
}
