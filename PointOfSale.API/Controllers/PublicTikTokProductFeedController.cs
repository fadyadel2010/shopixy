using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Infrastructure.RateLimiting;
using PointOfSale.API.Services.Feeds;

namespace PointOfSale.API.Controllers;

/// <summary>
/// FEED-3: Public anonymous TikTok Catalog product feed endpoint.
/// Channel adapter over the same certified Feed-1A/1B engine as Meta and
/// Google — the token model, tenant authority, projection, cache, rate
/// limiting, telemetry, and response safeguards are identical; only the XML
/// vocabulary differs.
///
/// AUTHENTICATION:
/// - [AllowAnonymous]; the hashed feed token in the URL IS the credential.
///   No JWT and no tenant header of any kind — tenant derives exclusively
///   from the tiktok-channel token row. A Meta or Google token never
///   authorizes this route (channel-scoped hash lookup).
///
/// RESPONSE CONTRACT:
/// - Unknown / revoked token or inactive tenant -> 404, publicly
///   indistinguishable, no diagnostics.
/// - Internal generation failure (e.g. storefront URL unresolved) -> generic
///   503 with no tenant diagnostics; never emits invalid URLs.
/// - Success -> application/rss+xml (RSS 2.0 with TikTok bare element names);
///   empty catalog -> valid RSS, zero items.
///
/// FEED-1B HARDENING PARITY:
/// - Same PublicFeedRateLimit policy (fixed window, token+IP partition).
/// - Rendered-XML Redis cache on the tiktok channel key with token
///   validation BEFORE cache lookup; Cache-Control mirrors the cache TTL.
/// - /api/public/feeds is already excluded from tenant/host resolution
///   (Feed-1A), so no additional middleware exclusion is required.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/public/feeds/tiktok")]
public sealed class PublicTikTokProductFeedController : ControllerBase
{
    private readonly ITikTokProductFeedService _feedService;
    private readonly ILogger<PublicTikTokProductFeedController> _logger;

    public PublicTikTokProductFeedController(
        ITikTokProductFeedService feedService,
        ILogger<PublicTikTokProductFeedController> logger)
    {
        _feedService = feedService;
        _logger = logger;
    }

    /// <summary>
    /// Serves the TikTok Catalog RSS product feed for a valid unrevoked
    /// tiktok-channel feed token. TikTok fetches this public URL as a file-based
    /// data source in Catalog Manager.
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
                    "TikTok feed generation failed | TraceId: {TraceId} | TenantId: {TenantId}",
                    traceId, result.TenantId);
                return StatusCode(
                    StatusCodes.Status503ServiceUnavailable,
                    ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }
}
