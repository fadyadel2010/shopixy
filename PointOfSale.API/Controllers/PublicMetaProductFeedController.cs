using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Infrastructure.RateLimiting;
using PointOfSale.API.Services.Feeds;

namespace PointOfSale.API.Controllers;

/// <summary>
/// FEED-1A: Public anonymous Meta product feed endpoint.
///
/// AUTHENTICATION:
/// - [AllowAnonymous]; the hashed feed token in the URL IS the credential.
///   No JWT and no tenant header of any kind — tenant derives exclusively
///   from the token row.
///
/// RESPONSE CONTRACT:
/// - Unknown / revoked token or inactive tenant -> 404, publicly
///   indistinguishable, no diagnostics.
/// - Internal generation failure (e.g. storefront URL unresolved) -> generic
///   503 with no tenant diagnostics; never emits invalid URLs.
/// - Success -> application/rss+xml; empty catalog -> valid RSS, zero items.
///
/// FEED-1B HARDENING:
/// - Dedicated PublicFeedRateLimit policy (fixed window, token+IP partition);
///   abuse gets the standard 429 without touching any other endpoint.
/// - Rendered-XML Redis cache with token validation BEFORE cache lookup, so
///   revocation stays immediate; Cache-Control mirrors the cache TTL.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/public/feeds/meta")]
public sealed class PublicMetaProductFeedController : ControllerBase
{
    private readonly IMetaProductFeedService _feedService;
    private readonly ILogger<PublicMetaProductFeedController> _logger;

    public PublicMetaProductFeedController(
        IMetaProductFeedService feedService,
        ILogger<PublicMetaProductFeedController> logger)
    {
        _feedService = feedService;
        _logger = logger;
    }

    /// <summary>
    /// Serves the Meta RSS product feed for a valid unrevoked feed token.
    /// </summary>
    [HttpGet("{token}")]
    [EnableRateLimiting(PublicFeedRateLimitPolicy.PolicyName)]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetFeed(string token, CancellationToken cancellationToken = default)
    {
        var traceId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;

        var result = await _feedService.GenerateAsync(token, cancellationToken);

        switch (result.Outcome)
        {
            case MetaFeedGenerationOutcome.Success:
                // FEED-1B: client-side caching mirrors the server-side rendered
                // XML TTL — never longer than the Redis TTL.
                Response.Headers.CacheControl = $"public, max-age={result.CacheMaxAgeSeconds}";
                return new ContentResult
                {
                    Content = result.Xml,
                    ContentType = "application/rss+xml; charset=utf-8",
                    StatusCode = StatusCodes.Status200OK
                };

            case MetaFeedGenerationOutcome.TokenNotFound:
            case MetaFeedGenerationOutcome.TenantInactive:
                // Publicly indistinguishable: same status, no body, no diagnostics.
                return NotFound();

            case MetaFeedGenerationOutcome.GenerationFailed:
            default:
                _logger.LogError(
                    "Meta feed generation failed | TraceId: {TraceId} | TenantId: {TenantId}",
                    traceId, result.TenantId);
                return StatusCode(
                    StatusCodes.Status503ServiceUnavailable,
                    ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }
}
