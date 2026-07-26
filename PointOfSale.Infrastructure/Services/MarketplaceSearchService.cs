using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Nest;
using PointOfSale.Core.DTOs.Marketplace;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Documents;

namespace PointOfSale.Infrastructure.Services;

/// <summary>
/// Marketplace search service using Elasticsearch.
/// Implements deterministic ranking with no manual boosts.
/// </summary>
public class MarketplaceSearchService : IMarketplaceSearchService
{
    private const string IndexName = "marketplace_services";
    private const string ScrollTimeout = "1m";
    private const int CandidateBatchSize = 500;

    private readonly IElasticClient _elasticClient;
    private readonly ApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IProviderTrustService _providerTrustService;
    private readonly ILogger<MarketplaceSearchService> _logger;

    public MarketplaceSearchService(
        IElasticClient elasticClient, 
        ApplicationDbContext context,
        ITenantContext tenantContext,
        IProviderTrustService providerTrustService,
        ILogger<MarketplaceSearchService> logger)
    {
        _elasticClient = elasticClient;
        _context = context;
        _tenantContext = tenantContext;
        _providerTrustService = providerTrustService;
        _logger = logger;
    }

    public async Task<MarketplaceSearchResponse> SearchServicesAsync(
        MarketplaceSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId ?? throw new InvalidOperationException("TenantId is required");

        _logger.LogInformation(
            "🔍 [SEARCH] Marketplace search request | Tenant: {TenantId} | Query: {Query} | Category: {Category} | Page: {Page} | Sort: {Sort}",
            tenantId, request.Query ?? "(empty)", request.EffectiveCategory ?? "(all)", request.Page, request.Sort);

        var searchDescriptor = new SearchDescriptor<MarketplaceServiceDocument>()
            .Index(IndexName)
            .Size(CandidateBatchSize)
            .Scroll(ScrollTimeout)
            .Query(q => BuildQuery(q, request, tenantId))
            .Sort(s => BuildSort(s, request));

        var response = await _elasticClient.SearchAsync<MarketplaceServiceDocument>(searchDescriptor, cancellationToken);

        if (!response.IsValid)
        {
            _logger.LogError(
                "🔴 [SEARCH] Elasticsearch search failed | Error: {Error} | DebugInfo: {DebugInfo}",
                response.OriginalException?.Message ?? response.ServerError?.Error?.Reason ?? "Unknown",
                response.DebugInformation);
            
            throw new InvalidOperationException("Marketplace search is temporarily unavailable.");
        }

        var documents = new List<MarketplaceServiceDocument>();
        var scrollId = response.ScrollId;

        try
        {
            while (response.Documents.Count > 0)
            {
                documents.AddRange(response.Documents);

                if (string.IsNullOrWhiteSpace(scrollId))
                {
                    break;
                }

                response = await _elasticClient.ScrollAsync<MarketplaceServiceDocument>(
                    ScrollTimeout,
                    scrollId,
                    ct: cancellationToken);

                if (!response.IsValid)
                {
                    _logger.LogError(
                        "Marketplace search candidate scroll failed | Error: {Error} | DebugInfo: {DebugInfo}",
                        response.OriginalException?.Message ?? response.ServerError?.Error?.Reason ?? "Unknown",
                        response.DebugInformation);

                    throw new InvalidOperationException("Marketplace search candidate scan failed.");
                }

                scrollId = response.ScrollId;
            }
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(scrollId))
            {
                await _elasticClient.ClearScrollAsync(
                    clear => clear.ScrollId(scrollId),
                    cancellationToken);
            }
        }

        var sqlAuthorityByServiceId = await LoadCurrentSqlListingAuthorityAsync(
            documents.Select(document => document.ServiceId).ToList(),
            cancellationToken);
        var requestStatsByServiceId = await LoadCurrentRequestStatsAsync(
            sqlAuthorityByServiceId.Keys.ToList(),
            cancellationToken);
        var trustByProviderProfileId = new Dictionary<Guid, ProviderTrustAggregate?>();

        var items = new List<MarketplaceServiceResult>();
        foreach (var document in documents.Where(document => sqlAuthorityByServiceId.ContainsKey(document.ServiceId)))
        {
            var authority = sqlAuthorityByServiceId[document.ServiceId];
            var stats = requestStatsByServiceId.GetValueOrDefault(authority.ServiceId);

            if (!MatchesCurrentFilters(request, authority, stats))
            {
                continue;
            }

            if (!trustByProviderProfileId.TryGetValue(authority.ProviderProfileId, out var trust))
            {
                trust = await _providerTrustService.GetTrustAggregateAsync(
                    authority.ProviderProfileId,
                    cancellationToken);
                trustByProviderProfileId[authority.ProviderProfileId] = trust;
            }

            if (!MatchesCurrentTrustFilters(request, trust))
            {
                continue;
            }

            items.Add(new MarketplaceServiceResult
            {
                ServiceId = authority.ServiceId,
                Title = authority.Title,
                Description = authority.Description,
                CategoryId = authority.Category,
                ProviderProfileId = authority.ProviderProfileId,
                ProviderDisplayName = authority.ProviderDisplayName,
                AverageRating = trust?.AverageRating ?? 0,
                TotalReviews = trust?.TotalReviews ?? 0,
                TrustBadge = trust?.ComputeBadge().ToString() ?? TrustBadge.New.ToString(),
                TotalRequests = stats?.TotalRequests ?? 0,
                CompletedRequests = stats?.CompletedRequests ?? 0,
                CompletionRate = stats?.CompletionRate ?? 0,
                CreatedAt = authority.CreatedAt,
                StartingPrice = authority.StartingPrice,
                PricingType = authority.PricingType,
                Currency = authority.Currency,
                ServiceImages = authority.ServiceImages
            });
        }

        var orderedItems = ApplyCurrentAuthoritySort(items, request.Sort);
        var totalCount = orderedItems.Count;
        var pageItems = orderedItems
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        _logger.LogInformation(
            "Marketplace search recomposed from current authority | Candidates: {Candidates} | Visible: {Visible} | Returned: {Returned} | Page: {Page}",
            documents.Count,
            totalCount,
            pageItems.Count,
            request.Page);

        return new MarketplaceSearchResponse
        {
            Items = pageItems,
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize
        };
    }

    private async Task<Dictionary<Guid, ServiceListingAuthorityRow>> LoadCurrentSqlListingAuthorityAsync(
        IReadOnlyCollection<Guid> serviceIds,
        CancellationToken cancellationToken)
    {
        if (serviceIds.Count == 0)
        {
            return new Dictionary<Guid, ServiceListingAuthorityRow>();
        }

        var rows = await (
                from service in _context.Set<Service>().AsNoTracking().IgnoreQueryFilters()
                join tenant in _context.Tenants.AsNoTracking().IgnoreQueryFilters()
                    on service.TenantId equals tenant.Id
                join profile in _context.Set<MarketplaceProfile>().AsNoTracking().IgnoreQueryFilters()
                    on service.ProfileId equals profile.Id
                where serviceIds.Contains(service.Id) &&
                      !service.IsDeleted &&
                      service.IsActive &&
                      tenant.Status == TenantStatus.Active &&
                      tenant.DeletedAtUtc == null &&
                      !profile.IsDeleted &&
                      profile.IsActive &&
                      profile.TenantId == service.TenantId &&
                      !_context.UserGovernanceProfiles.AsNoTracking().Any(governance =>
                          governance.SubjectId == service.TenantId &&
                          governance.Role == UserGovernanceRole.Vendor &&
                          (governance.Status == UserGovernanceStatus.Suspended ||
                           governance.Status == UserGovernanceStatus.Banned))
                select new ServiceListingAuthorityRow
                {
                    ServiceId = service.Id,
                    TenantId = service.TenantId,
                    Title = service.Title,
                    Description = service.Description,
                    Category = service.Category,
                    ProviderProfileId = service.ProfileId,
                    ProviderDisplayName = profile.DisplayName,
                    CreatedAt = service.CreatedAt,
                    StartingPrice = service.StartingPrice,
                    PricingType = service.PricingType == null ? null : service.PricingType.ToString(),
                    Currency = service.Currency,
                    ServiceImages = service.ServiceImages
                })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(row => row.ServiceId);
    }

    private async Task<Dictionary<Guid, ServiceRequestStatsRow>> LoadCurrentRequestStatsAsync(
        IReadOnlyCollection<Guid> serviceIds,
        CancellationToken cancellationToken)
    {
        if (serviceIds.Count == 0)
        {
            return new Dictionary<Guid, ServiceRequestStatsRow>();
        }

        var rows = await _context.Set<ServiceRequest>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(request => serviceIds.Contains(request.ServiceId) && !request.IsDeleted)
            .GroupBy(request => request.ServiceId)
            .Select(group => new ServiceRequestStatsRow
            {
                ServiceId = group.Key,
                TotalRequests = group.Count(),
                CompletedRequests = group.Count(request =>
                    request.Status == ServiceRequestStatus.CompletedByBuyer)
            })
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            row.CompletionRate = row.TotalRequests == 0
                ? 0
                : (decimal)row.CompletedRequests / row.TotalRequests;
        }

        return rows.ToDictionary(row => row.ServiceId);
    }

    private static bool MatchesCurrentFilters(
        MarketplaceSearchRequest request,
        ServiceListingAuthorityRow authority,
        ServiceRequestStatsRow? stats)
    {
        if (!string.IsNullOrWhiteSpace(request.EffectiveCategory) &&
            !string.Equals(
                authority.Category,
                request.EffectiveCategory,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !request.MinCompletedRequests.HasValue ||
               (stats?.CompletedRequests ?? 0) >= request.MinCompletedRequests.Value;
    }

    private static bool MatchesCurrentTrustFilters(
        MarketplaceSearchRequest request,
        ProviderTrustAggregate? trust)
    {
        var rating = trust?.AverageRating ?? 0;
        if (request.MinRating.HasValue && rating < request.MinRating.Value)
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(request.TrustBadge) ||
               string.Equals(
                   trust?.ComputeBadge().ToString() ?? TrustBadge.New.ToString(),
                   request.TrustBadge,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static List<MarketplaceServiceResult> ApplyCurrentAuthoritySort(
        IEnumerable<MarketplaceServiceResult> items,
        string sort)
    {
        return sort switch
        {
            "rating" => items
                .OrderByDescending(item => item.AverageRating)
                .ThenByDescending(item => item.TotalReviews)
                .ThenBy(item => item.ServiceId)
                .ToList(),
            "newest" => items
                .OrderByDescending(item => item.CreatedAt)
                .ThenBy(item => item.ServiceId)
                .ToList(),
            "price_low" => items
                .OrderBy(item => item.StartingPrice.HasValue ? 0 : 1)
                .ThenBy(item => item.StartingPrice)
                .ThenBy(item => item.ServiceId)
                .ToList(),
            "price_high" => items
                .OrderBy(item => item.StartingPrice.HasValue ? 0 : 1)
                .ThenByDescending(item => item.StartingPrice)
                .ThenBy(item => item.ServiceId)
                .ToList(),
            _ => items.ToList()
        };
    }

    private QueryContainer BuildQuery(
        QueryContainerDescriptor<MarketplaceServiceDocument> q,
        MarketplaceSearchRequest request,
        Guid tenantId)
    {
        var queries = new List<Func<QueryContainerDescriptor<MarketplaceServiceDocument>, QueryContainer>>();

        // Tenant filter REMOVED for Marketplace (Global visibility)
        // queries.Add(qq => qq.Term(t => t.Field(f => f.TenantId).Value(tenantId)));

        // Active filter (mandatory)
        queries.Add(qq => qq.Term(t => t.Field(f => f.IsActive).Value(true)));

        // Text search — combine full-word (fuzzy) + prefix matching for best UX
        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            queries.Add(qq => qq.FunctionScore(fs => fs
                .Query(query => query.Bool(b => b
                    .Should(
                        // Full-word matching with fuzzy tolerance (handles typos)
                        sh => sh.MultiMatch(mm => mm
                            .Query(request.Query)
                            .Type(TextQueryType.BestFields)
                            .Fields(f => f
                                .Field(ff => ff.Title, boost: 4.0)
                                .Field(ff => ff.Description, boost: 2.0)
                                .Field(ff => ff.CategoryPath, boost: 2.0)
                                .Field(ff => ff.ProviderDisplayName)
                            )
                            .Fuzziness(Fuzziness.Auto)
                        ),
                        // Prefix matching (handles partial words / autocomplete)
                        sh => sh.MultiMatch(mm => mm
                            .Query(request.Query)
                            .Type(TextQueryType.PhrasePrefix)
                            .Fields(f => f
                                .Field(ff => ff.Title, boost: 4.0)
                                .Field(ff => ff.Description, boost: 1.0)
                                .Field(ff => ff.CategoryPath, boost: 1.5)
                                .Field(ff => ff.ProviderDisplayName)
                            )
                        )
                    )
                    .MinimumShouldMatch(1)
                ))
                .Functions(fn => fn
                    // CreatedAt is immutable SQL listing data; mutable trust and
                    // request projections must not influence relevance authority.
                    .GaussDate(gd => gd
                        .Field(f => f.CreatedAt)
                        .Origin(DateMath.Now)
                        .Scale("90d")
                        .Offset("7d")
                        .Decay(0.5)
                        .Weight(1.5)
                    )
                )
                .ScoreMode(FunctionScoreMode.Sum)
                .BoostMode(FunctionBoostMode.Sum)
            ));
        }

        // Elasticsearch narrows text candidates only. Category, trust, statistics,
        // sorting, counting, and pagination are recomposed from current SQL authority.

        return q.Bool(b => b.Must(queries.ToArray()));
    }

    private IPromise<IList<ISort>> BuildSort(
        SortDescriptor<MarketplaceServiceDocument> s,
        MarketplaceSearchRequest request)
    {
        switch (request.Sort)
        {
            case "rating":
                return s
                    .Descending(f => f.Trust.AverageRating)
                    .Descending(f => f.Trust.TotalReviews)
                    .Descending(SortSpecialField.Score);

            case "newest":
                return s
                    .Descending(f => f.CreatedAt)
                    .Descending(SortSpecialField.Score);

            case "price_low":
                return s
                    .Ascending(f => f.StartingPrice)
                    .Descending(SortSpecialField.Score);

            case "price_high":
                return s
                    .Descending(f => f.StartingPrice)
                    .Descending(SortSpecialField.Score);

            default:
                // relevance (score)
                return s.Descending(SortSpecialField.Score);
        }
    }

    private sealed class ServiceListingAuthorityRow
    {
        public Guid ServiceId { get; init; }
        public Guid TenantId { get; init; }
        public string Title { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string? Category { get; init; }
        public Guid ProviderProfileId { get; init; }
        public string ProviderDisplayName { get; init; } = string.Empty;
        public DateTime CreatedAt { get; init; }
        public decimal? StartingPrice { get; init; }
        public string? PricingType { get; init; }
        public string? Currency { get; init; }
        public string? ServiceImages { get; init; }
    }

    private sealed class ServiceRequestStatsRow
    {
        public Guid ServiceId { get; init; }
        public int TotalRequests { get; init; }
        public int CompletedRequests { get; init; }
        public decimal CompletionRate { get; set; }
    }
}
