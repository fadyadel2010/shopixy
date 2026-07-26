using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Services.Storefront;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Robots.txt Controller - Read-only robots.txt generation endpoint.
/// 
/// Route: /api/storefront/robots.txt
/// 
/// Endpoints:
/// - GET / → Get robots.txt content
/// 
/// All endpoints:
/// - Require X-Tenant-Id header for multi-tenant isolation
/// - Are READ-ONLY (no mutations)
/// - Use Redis as primary cache (cache-first mindset)
/// 
/// Performance Characteristics:
/// - READ-HEAVY: Designed for search engine crawlers
/// - LOW-VOLATILITY: Data changes very infrequently
/// - CACHE-FIRST: Redis is queried before generation (TTL: 12 hours)
/// </summary>
[ApiController]
[Route("api/storefront/robots.txt")]
[AllowAnonymous]
public class RobotsController : ControllerBase
{
    private readonly ISeoService _seoService;
    private readonly ILogger<RobotsController> _logger;
    private const string TenantIdHeader = "X-Tenant-Id";

    public RobotsController(
        ISeoService seoService,
        ILogger<RobotsController> logger)
    {
        _seoService = seoService;
        _logger = logger;
    }

    /// <summary>
    /// Gets robots.txt content for the tenant.
    /// Includes tenant-aware sitemap URL.
    /// </summary>
    /// <param name="baseUrl">Base URL for sitemap URL (optional, uses request origin if not provided)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Robots.txt content as plain text</returns>
    /// <response code="200">Robots.txt returned successfully</response>
    /// <response code="400">Missing or invalid X-Tenant-Id header</response>
    /// <response code="503">Service temporarily unavailable</response>
    [HttpGet]
    [Produces("text/plain")]
    public async Task<IActionResult> GetRobotsTxt(
        [FromQuery] string? baseUrl,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();

        // Step 1: Validate X-Tenant-Id header
        var tenantValidation = ValidateTenantHeader(traceId);
        if (tenantValidation.Error != null)
        {
            return tenantValidation.Error;
        }
        var tenantId = tenantValidation.TenantId!.Value;

        // Use provided baseUrl or construct from request
        var effectiveBaseUrl = GetEffectiveBaseUrl(baseUrl);

        _logger.LogInformation(
            "GetRobotsTxt request | TenantId: {TenantId} | BaseUrl: {BaseUrl} | TraceId: {TraceId}",
            tenantId, effectiveBaseUrl, traceId);

        try
        {
            var robotsTxt = await _seoService.GetRobotsTxtAsync(
                tenantId, 
                effectiveBaseUrl,
                traceId, 
                cancellationToken);

            _logger.LogInformation(
                "GetRobotsTxt success | TenantId: {TenantId} | TraceId: {TraceId}",
                tenantId, traceId);

            return Content(robotsTxt, "text/plain", Encoding.UTF8);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "GetRobotsTxt failed | TenantId: {TenantId} | TraceId: {TraceId}",
                tenantId, traceId);

            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    #region Private Helpers

    private string GetEffectiveBaseUrl(string? providedBaseUrl)
    {
        if (!string.IsNullOrWhiteSpace(providedBaseUrl))
        {
            return providedBaseUrl.TrimEnd('/');
        }

        // Construct from request
        var scheme = Request.Scheme;
        var host = Request.Host.HasValue ? Request.Host.Value : "localhost";
        return $"{scheme}://{host}";
    }

    #endregion

    #region Tenant Validation

    /// <summary>
    /// Validates the X-Tenant-Id header.
    /// Returns parsed TenantId or error response.
    /// </summary>
    private (Guid? TenantId, IActionResult? Error) ValidateTenantHeader(string traceId)
    {
        return PointOfSale.API.Helpers.StorefrontTenantContextResolver.ValidateTenant(this, traceId);
    }

    /// <summary>
    /// Gets the trace ID for the current request.
    /// </summary>
    private string GetTraceId()
    {
        return Activity.Current?.Id ?? HttpContext.TraceIdentifier;
    }

    #endregion
}
