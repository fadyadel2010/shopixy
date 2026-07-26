using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Attributes;
using PointOfSale.API.CommerceHub.Services;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services.Platform;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.MongoDB.Documents;

namespace PointOfSale.API.Controllers;

[AllowAnonymous]
[AllowAnonymousTenant]
[ApiController]
[Route("api/commercehub/products")]
public class CommerceHubController : ControllerBase
{
    private readonly ICommerceHubQueryService _queryService;
    private readonly ICommerceHubSearchQueryParser _searchQueryParser;
    private readonly ICommerceHubSearchIntelligenceService _searchIntelligenceService;
    private readonly IRateLimitService _rateLimitService;
    private readonly ApplicationDbContext _dbContext;

    public CommerceHubController(
        ICommerceHubQueryService queryService,
        ICommerceHubSearchQueryParser searchQueryParser,
        ICommerceHubSearchIntelligenceService searchIntelligenceService,
        IRateLimitService rateLimitService,
        ApplicationDbContext dbContext)
    {
        _queryService = queryService;
        _searchQueryParser = searchQueryParser;
        _searchIntelligenceService = searchIntelligenceService;
        _rateLimitService = rateLimitService;
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<CommerceHubProductListResponse>> GetProducts(
        [FromQuery] CommerceHubProductsQuery query,
        CancellationToken cancellationToken)
    {
        ApplyNoStoreHeaders();
        var result = await _queryService.GetProductsAsync(query, cancellationToken);
        return Ok(result);
    }

    [HttpGet("/api/commercehub/search")]
    public async Task<ActionResult<CommerceHubSearchResponse>> Search(
        [FromQuery] CommerceHubSearchQuery query,
        CancellationToken cancellationToken)
    {
        var rateLimitError = await EnforceSearchRateLimitAsync("search", 60, 60);
        if (rateLimitError != null)
        {
            return rateLimitError;
        }

        ApplyNoStoreHeaders();
        var filters = _searchQueryParser.Parse(Request.Query, query.Filters);
        var result = await _queryService.SearchAsync(query, filters, cancellationToken);
        return Ok(result);
    }

    [HttpGet("/api/commercehub/search/suggestions")]
    public async Task<ActionResult<CommerceHubSearchSuggestionsResponse>> GetSuggestions(
        [FromQuery] CommerceHubSearchSuggestionsQuery query,
        CancellationToken cancellationToken)
    {
        var rateLimitError = await EnforceSearchRateLimitAsync("search-suggestions", 120, 60);
        if (rateLimitError != null)
        {
            return rateLimitError;
        }

        ApplyNoStoreHeaders();
        var result = await _queryService.GetSuggestionsAsync(query, cancellationToken);
        return Ok(result);
    }

    [HttpGet("/api/commercehub/search/autocomplete")]
    public async Task<ActionResult<CommerceHubAutocompleteResponse>> GetAutocomplete(
        [FromQuery] CommerceHubSearchAutocompleteQuery query,
        CancellationToken cancellationToken)
    {
        var rateLimitError = await EnforceSearchRateLimitAsync("search-autocomplete", 180, 60);
        if (rateLimitError != null)
        {
            return rateLimitError;
        }

        ApplyNoStoreHeaders();
        var result = await _queryService.GetAutocompleteAsync(query, cancellationToken);
        return Ok(result);
    }

    [HttpGet("/api/commercehub/search/popular")]
    public async Task<ActionResult<CommerceHubPopularSearchesResponse>> GetPopularQueries(
        [FromQuery] CommerceHubPopularSearchesQuery query,
        CancellationToken cancellationToken)
    {
        var rateLimitError = await EnforceSearchRateLimitAsync("search-popular", 120, 60);
        if (rateLimitError != null)
        {
            return rateLimitError;
        }

        ApplyNoStoreHeaders();
        var result = await _queryService.GetPopularQueriesAsync(query, cancellationToken);
        return Ok(result);
    }

    [HttpPost("/api/commercehub/search/click")]
    public async Task<ActionResult> TrackClick(
        [FromBody] CommerceHubSearchClickRequest request,
        CancellationToken cancellationToken)
    {
        var visibleAuthority = await ResolveVisibleSearchAnalyticsAuthorityAsync(
            request.ProductId,
            request.VariantId,
            request.TenantId,
            cancellationToken);
        if (visibleAuthority == null)
        {
            return Accepted();
        }

        var sessionId = string.IsNullOrWhiteSpace(request.SessionId)
            ? Request.Headers["X-Session-Id"].FirstOrDefault()
            : request.SessionId;

        _searchIntelligenceService.TrackClick(new CommerceHubTrackedSearchClick(
            request.Query ?? string.Empty,
            visibleAuthority.ProductId,
            visibleAuthority.VariantId,
            request.Position,
            DateTime.UtcNow,
            sessionId,
            visibleAuthority.TenantId));

        return Accepted();
    }

    [HttpPost("/api/commercehub/search/impression")]
    public async Task<ActionResult> TrackImpression(
        [FromBody] CommerceHubSearchImpressionRequest request,
        CancellationToken cancellationToken)
    {
        var visibleProductsByTenant = new Dictionary<Guid, List<Guid>>();
        foreach (var productId in request.ProductIds.Where(id => id != Guid.Empty).Distinct())
        {
            var visibleAuthority = await ResolveVisibleSearchAnalyticsAuthorityAsync(
                productId,
                variantId: null,
                request.TenantId,
                cancellationToken);
            if (visibleAuthority != null)
            {
                if (!visibleProductsByTenant.TryGetValue(visibleAuthority.TenantId, out var productIds))
                {
                    productIds = new List<Guid>();
                    visibleProductsByTenant[visibleAuthority.TenantId] = productIds;
                }

                productIds.Add(visibleAuthority.ProductId);
            }
        }

        if (visibleProductsByTenant.Count == 0)
        {
            return Accepted();
        }

        var sessionId = string.IsNullOrWhiteSpace(request.SessionId)
            ? Request.Headers["X-Session-Id"].FirstOrDefault()
            : request.SessionId;

        foreach (var tenantGroup in visibleProductsByTenant)
        {
            _searchIntelligenceService.TrackImpression(new CommerceHubTrackedSearchImpression(
                request.Query ?? string.Empty,
                tenantGroup.Value,
                request.Timestamp ?? DateTime.UtcNow,
                sessionId,
                tenantGroup.Key));
        }

        return Accepted();
    }

    private async Task<VisibleSearchAnalyticsAuthority?> ResolveVisibleSearchAnalyticsAuthorityAsync(
        Guid productId,
        Guid? variantId,
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        if (productId == Guid.Empty)
        {
            return null;
        }

        var query = _dbContext.CommerceHubListings
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(listing =>
                !listing.IsDeleted &&
                listing.IsPublished &&
                listing.ProductId == productId);

        if (variantId.HasValue && variantId.Value != Guid.Empty)
        {
            query = query.Where(listing => listing.VariantId == variantId.Value);
        }

        if (tenantId.HasValue && tenantId.Value != Guid.Empty)
        {
            query = query.Where(listing => listing.TenantId == tenantId.Value);
        }

        var candidates = await query
            .OrderByDescending(listing => listing.UpdatedAt)
            .ThenBy(listing => listing.Id)
            .Select(listing => new
            {
                listing.Id,
                listing.TenantId
            })
            .Take(10)
            .ToListAsync(cancellationToken);

        foreach (var candidate in candidates)
        {
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
                return new VisibleSearchAnalyticsAuthority(
                    sqlAuthority.TenantId,
                    sqlAuthority.ProductId,
                    sqlAuthority.VariantId);
            }
        }

        return null;
    }

    private sealed record VisibleSearchAnalyticsAuthority(Guid TenantId, Guid ProductId, Guid VariantId);

    [HttpPost("/api/commercehub/search/filter-event")]
    public ActionResult TrackFilterEvent([FromBody] CommerceHubSearchFilterEventRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FilterKey) || string.IsNullOrWhiteSpace(request.FilterValue))
        {
            return Accepted();
        }

        var sessionId = string.IsNullOrWhiteSpace(request.SessionId)
            ? Request.Headers["X-Session-Id"].FirstOrDefault()
            : request.SessionId;

        _searchIntelligenceService.TrackFilterEvent(new CommerceHubTrackedSearchFilterEvent(
            request.FilterKey,
            request.FilterValue,
            request.Query ?? string.Empty,
            request.Timestamp ?? DateTime.UtcNow,
            sessionId,
            request.TenantId));

        return Accepted();
    }

    [HttpGet("{id:guid}")]
    [HttpGet("/api/marketplace/products/{id:guid}")]
    public async Task<ActionResult<CommerceHubProductDetailDto>> GetProductById(
        Guid id,
        CancellationToken cancellationToken)
    {
        ApplyNoStoreHeaders();
        var result = await _queryService.GetProductByIdAsync(id, cancellationToken);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpPost("{id:guid}/variant-resolution")]
    [HttpPost("/api/marketplace/products/{id:guid}/variant-resolution")]
    public async Task<ActionResult<CommerceHubVariantResolutionResponse>> ResolveProductVariant(
        Guid id,
        [FromBody] CommerceHubVariantResolutionRequest? request,
        CancellationToken cancellationToken)
    {
        ApplyNoStoreHeaders();
        var result = await _queryService.ResolveProductVariantAsync(
            id,
            request ?? new CommerceHubVariantResolutionRequest(),
            cancellationToken);

        return result == null ? Conflict() : Ok(result);
    }

    private void ApplyNoStoreHeaders()
    {
        Response.Headers.CacheControl = "no-store, no-cache, max-age=0, must-revalidate";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.Expires = "0";
        Response.Headers["Surrogate-Control"] = "no-store";
    }

    private async Task<ActionResult?> EnforceSearchRateLimitAsync(string endpoint, int limit, int windowSeconds)
    {
        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var result = await _rateLimitService.CheckRateLimitAsync(clientIp, $"commercehub:{endpoint}", limit, windowSeconds);

        if (result.Allowed)
        {
            return null;
        }

        await _rateLimitService.RecordRateLimitHitAsync(clientIp, $"commercehub:{endpoint}");

        Response.Headers.RetryAfter = windowSeconds.ToString();

        return StatusCode(StatusCodes.Status429TooManyRequests, new
        {
            error = "rate_limit_exceeded",
            message = "Too many requests. Please try again later.",
            retryAfterSeconds = windowSeconds
        });
    }
}
