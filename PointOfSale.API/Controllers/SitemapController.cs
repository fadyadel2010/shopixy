using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Services.Storefront;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Sitemap Controller - Read-only sitemap generation endpoint.
/// 
/// Route: /api/storefront/sitemap
/// 
/// Endpoints:
/// - GET / → Get sitemap data (JSON format)
/// - GET /xml → Get sitemap in XML format
/// 
/// All endpoints:
/// - Require X-Tenant-Id header for multi-tenant isolation
/// - Are READ-ONLY (no mutations)
/// - Use Redis as primary cache (cache-first mindset)
/// 
/// Performance Characteristics:
/// - READ-HEAVY: Designed for search engine crawlers
/// - LOW-VOLATILITY: Data changes infrequently
/// - CACHE-FIRST: Redis is queried before MongoDB (TTL: 6 hours)
/// </summary>
[ApiController]
[Route("api/storefront/sitemap")]
[AllowAnonymous]
public class SitemapController : ControllerBase
{
    private readonly ISeoService _seoService;
    private readonly ILogger<SitemapController> _logger;
    private const string TenantIdHeader = "X-Tenant-Id";

    public SitemapController(
        ISeoService seoService,
        ILogger<SitemapController> logger)
    {
        _seoService = seoService;
        _logger = logger;
    }

    /// <summary>
    /// Gets sitemap data in JSON format.
    /// </summary>
    /// <param name="baseUrl">Base URL for sitemap URLs (optional, uses request origin if not provided)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Sitemap DTO with all URLs</returns>
    /// <response code="200">Sitemap returned successfully</response>
    /// <response code="400">Missing or invalid X-Tenant-Id header</response>
    /// <response code="503">Service temporarily unavailable</response>
    [HttpGet]
    [ProducesResponseType(typeof(SitemapDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetSitemap(
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
            "GetSitemap request | TenantId: {TenantId} | BaseUrl: {BaseUrl} | TraceId: {TraceId}",
            tenantId, effectiveBaseUrl, traceId);

        try
        {
            var sitemap = await _seoService.GetSitemapAsync(
                tenantId, 
                effectiveBaseUrl,
                traceId, 
                cancellationToken);

            _logger.LogInformation(
                "GetSitemap success | TenantId: {TenantId} | UrlCount: {UrlCount} | TraceId: {TraceId}",
                tenantId, sitemap.TotalCount, traceId);

            return Ok(sitemap);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "GetSitemap failed | TenantId: {TenantId} | TraceId: {TraceId}",
                tenantId, traceId);

            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    /// <summary>
    /// Gets sitemap in XML format (standard sitemap.xml format).
    /// </summary>
    /// <param name="baseUrl">Base URL for sitemap URLs (optional)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>XML sitemap</returns>
    /// <response code="200">Sitemap XML returned successfully</response>
    /// <response code="400">Missing or invalid X-Tenant-Id header</response>
    /// <response code="503">Service temporarily unavailable</response>
    [HttpGet("xml")]
    [Produces("application/xml")]
    public async Task<IActionResult> GetSitemapXml(
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
            "GetSitemapXml request | TenantId: {TenantId} | BaseUrl: {BaseUrl} | TraceId: {TraceId}",
            tenantId, effectiveBaseUrl, traceId);

        try
        {
            var sitemap = await _seoService.GetSitemapAsync(
                tenantId, 
                effectiveBaseUrl,
                traceId, 
                cancellationToken);

            // Convert to XML format
            var xml = BuildSitemapXml(sitemap);

            _logger.LogInformation(
                "GetSitemapXml success | TenantId: {TenantId} | UrlCount: {UrlCount} | TraceId: {TraceId}",
                tenantId, sitemap.TotalCount, traceId);

            return Content(xml, "application/xml", Encoding.UTF8);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "GetSitemapXml failed | TenantId: {TenantId} | TraceId: {TraceId}",
                tenantId, traceId);

            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    #region Private Helpers

    private string BuildSitemapXml(SitemapDto sitemap)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");

        foreach (var url in sitemap.Urls)
        {
            sb.AppendLine("  <url>");
            sb.AppendLine($"    <loc>{EscapeXml(url.Loc)}</loc>");
            
            if (!string.IsNullOrEmpty(url.Lastmod))
            {
                sb.AppendLine($"    <lastmod>{url.Lastmod}</lastmod>");
            }
            
            sb.AppendLine($"    <changefreq>{url.ChangeFreq}</changefreq>");
            sb.AppendLine($"    <priority>{url.Priority:F1}</priority>");
            sb.AppendLine("  </url>");
        }

        sb.AppendLine("</urlset>");
        return sb.ToString();
    }

    private static string EscapeXml(string value)
    {
        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }

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
