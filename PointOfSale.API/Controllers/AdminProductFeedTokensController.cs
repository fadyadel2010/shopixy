using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.DTOs.Feeds;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Services.Feeds;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.API.Controllers;

/// <summary>
/// FEED-1A/FEED-2/FEED-3: Administrative lifecycle for public product feed tokens.
/// One channel-aware controller serves every supported feed channel
/// (meta, google, tiktok) — the route regex admits each channel so no parallel
/// controller is introduced.
///
/// AUTHORIZATION:
/// - Owner/Admin only; SQL actor authority is enforced by AdminTenantAuthorityMiddleware
///   on top of the role attribute (existing Shopixy convention — no parallel system).
///
/// TENANT SCOPE:
/// - TenantId comes exclusively from the trusted tenant context (JWT-derived).
///   Never from body, query, or any client-supplied tenant header.
///
/// RAW TOKEN SECURITY:
/// - The raw token appears only inside the create/rotate response feedUrl.
///   List/revoke never expose the raw token or the stored hash.
/// </summary>
[ApiController]
[Authorize(Roles = "Owner,Admin")]
[Route("api/admin/feeds/{channel:regex(meta|google|tiktok|snapchat)}/tokens")]
public sealed class AdminProductFeedTokensController : ControllerBase
{
    private readonly IProductFeedTokenService _tokenService;
    private readonly ITenantContext _tenantContext;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AdminProductFeedTokensController> _logger;

    public AdminProductFeedTokensController(
        IProductFeedTokenService tokenService,
        ITenantContext tenantContext,
        IConfiguration configuration,
        ILogger<AdminProductFeedTokensController> logger)
    {
        _tokenService = tokenService;
        _tenantContext = tenantContext;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new active feed token for the route's channel. The returned
    /// feedUrl embeds the raw token and is shown exactly once; it is never
    /// returned by later operations.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(CreateProductFeedTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> CreateToken(CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        if (!TryGetChannel(out var channel))
        {
            return NotFound();
        }

        try
        {
            var created = await _tokenService.CreateTokenAsync(_tenantContext.RequiredTenantId, channel, cancellationToken);
            return Ok(BuildCreateResponse(created));
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Create product feed token failed | TraceId: {TraceId}", traceId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    /// <summary>
    /// Lists safe token metadata for the tenant. Never returns the raw token or hash.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ProductFeedTokenInfoDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ListTokens(CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        if (!TryGetChannel(out var channel))
        {
            return NotFound();
        }

        try
        {
            var tokens = await _tokenService.ListTokensAsync(_tenantContext.RequiredTenantId, channel, cancellationToken);
            IReadOnlyList<ProductFeedTokenInfoDto> response = tokens
                .Select(token => new ProductFeedTokenInfoDto
                {
                    TokenId = token.Id,
                    Channel = token.Channel,
                    CreatedAtUtc = token.CreatedAtUtc,
                    RevokedAtUtc = token.RevokedAtUtc
                })
                .ToList();
            return Ok(response);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "List product feed tokens failed | TraceId: {TraceId}", traceId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    /// <summary>
    /// Revokes an active token. The public feed URL stops working immediately.
    /// </summary>
    [HttpPost("{tokenId:guid}/revoke")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> RevokeToken(Guid tokenId, CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        if (!TryGetChannel(out var channel))
        {
            return NotFound();
        }

        try
        {
            var revoked = await _tokenService.RevokeTokenAsync(_tenantContext.RequiredTenantId, channel, tokenId, cancellationToken);
            return revoked
                ? NoContent()
                : NotFound(new ApiErrorResponse("FEED_TOKEN_NOT_FOUND", "Product feed token was not found.", traceId));
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Revoke product feed token failed | TraceId: {TraceId}", traceId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    /// <summary>
    /// Rotates a token: creates the replacement, returns the new feedUrl once,
    /// and revokes the previous token — all inside one transaction boundary.
    /// </summary>
    [HttpPost("{tokenId:guid}/rotate")]
    [ProducesResponseType(typeof(CreateProductFeedTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> RotateToken(Guid tokenId, CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        if (!TryGetChannel(out var channel))
        {
            return NotFound();
        }

        try
        {
            var rotated = await _tokenService.RotateTokenAsync(_tenantContext.RequiredTenantId, channel, tokenId, cancellationToken);
            return rotated == null
                ? NotFound(new ApiErrorResponse("FEED_TOKEN_NOT_FOUND", "Product feed token was not found.", traceId))
                : Ok(BuildCreateResponse(rotated));
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Rotate product feed token failed | TraceId: {TraceId}", traceId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    private CreateProductFeedTokenResponse BuildCreateResponse(CreatedProductFeedToken created)
    {
        return new CreateProductFeedTokenResponse
        {
            TokenId = created.TokenId,
            Channel = created.Channel,
            FeedUrl = BuildFeedUrl(created.Channel, created.RawToken),
            CreatedAtUtc = created.CreatedAtUtc
        };
    }

    /// <summary>
    /// FEED-2: channel segment from the route template. The regex constraint
    /// already limits it to supported channels; the defensive check keeps the
    /// controller safe if the template ever changes.
    /// </summary>
    private bool TryGetChannel(out string channel)
    {
        channel = RouteData.Values["channel"] as string ?? string.Empty;
        return PointOfSale.Core.Entities.ProductFeedToken.IsSupportedChannel(channel);
    }

    /// <summary>
    /// Public feed URL on the API host. Optional configuration override
    /// (ProductFeed:ApiBaseUrl) follows the AffiliateIdentity:PublicBaseUrl
    /// precedent; otherwise the request origin is used.
    /// </summary>
    private string BuildFeedUrl(string channel, string rawToken)
    {
        var configured = _configuration["ProductFeed:ApiBaseUrl"];
        var baseUrl = !string.IsNullOrWhiteSpace(configured)
            ? configured.Trim().TrimEnd('/')
            : $"{Request.Scheme}://{Request.Host.Value}";

        return $"{baseUrl}/api/public/feeds/{channel}/{rawToken}";
    }

    private string GetTraceId() => Activity.Current?.Id ?? HttpContext.TraceIdentifier;
}
