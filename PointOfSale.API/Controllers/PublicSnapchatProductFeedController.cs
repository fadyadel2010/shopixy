using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Infrastructure.RateLimiting;
using PointOfSale.API.Services.Feeds;

namespace PointOfSale.API.Controllers;

/// <summary>
/// FEED-4: Public anonymous Snapchat Catalog product feed endpoint.
/// Channel adapter over the same certified Feed-1A/1B engine as Meta, Google,
/// and TikTok — the token model, tenant authority, projection, cache, rate
/// limiting, telemetry, and response safeguards are identical; only the XML
/// vocabulary differs (Snapchat uses the Google Merchant g: namespace).
///
/// AUTHENTICATION:
/// - [AllowAnonymous]; the hashed feed token in the URL IS the credential.
///   No JWT and no tenant header of any kind — tenant derives exclusively
///   from the snapchat-channel token row. A Meta, Google, or TikTok token
///   never authorizes this route (channel-scoped hash lookup).
///
/// RESPONSE CONTRACT:
/// - Unknown / revoked token or inactive tenant -> 404, publicly
///   indistinguishable, no diagnostics.
/// - Internal generation failure (e.g. storefront URL unresolved) -> generic
///   503 with no tenant diagnostics; never emits invalid URLs.
/// - Success -> application/rss+xml (RSS 2.0 with g: namespace);
///   empty catalog -> valid RSS, zero items.
///
/// FEED-1B HARDENING PARITY:
/// - Same PublicFeedRateLimit policy (fixed window, token+IP partition).
/// - Rendered-XML Redis cache on the snapchat channel key with token
///   validation BEFORE cache lookup; Cache-Control mirrors the cache TTL.
/// - /api/public/feeds is already excluded from tenant/host resolution
///   (Feed-1A), so no additional middleware exclusion is required.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/public/feeds/snapchat")]
public sealed class PublicSnapchatProductFeedController : ControllerBase
{
    private readonly ISnapchatProductFeedService _feedService;
    private readonly ILogger<PublicSnapchatProductFeedController> _logger;

    public PublicSnapchatProductFeedController(
        ISnapchatProductFeedService feedService,
        ILogger<PublicSnapchatProductFeedController> logger)
    {
        _feedService = feedService;
        _logger = logger;
    }

    /// <summary>
    /// Serves the Snapchat Catalog RSS product feed for a valid unrevoked
    /// snapchat-channel feed token. Snapchat fetches this public URL as a
    /// data feed source in Ads Manager.
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
                    "Snapchat feed generation failed | TraceId: {TraceId} | TenantId: {TenantId}",
                    traceId, result.TenantId);
                return StatusCode(
                    StatusCodes.Status503ServiceUnavailable,
                    ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }
}
