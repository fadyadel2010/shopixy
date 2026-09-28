using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Services.Storefront;
using PointOfSale.API.Localization;

namespace PointOfSale.API.Controllers;

/// <summary>
/// SEO Metadata Controller - Read-only SEO metadata endpoint.
/// 
/// Route: /api/storefront/seo
/// 
/// Endpoints:
/// - GET / → Get SEO metadata for a product or category
/// 
/// All endpoints:
/// - Require X-Tenant-Id header for multi-tenant isolation
/// - Are READ-ONLY (no mutations)
/// - Use Redis as primary cache (cache-first mindset)
/// - Return lightweight DTOs for fast frontend rendering
/// 
/// Performance Characteristics:
/// - READ-HEAVY: Designed for high-frequency SEO data requests
/// - LOW-VOLATILITY: Data changes infrequently
/// - CACHE-FIRST: Redis is queried before MongoDB
/// </summary>
[ApiController]
[Route("api/storefront/seo")]
[AllowAnonymous]
public class SeoController : ControllerBase
{
    private readonly ISeoService _seoService;
    private readonly ILogger<SeoController> _logger;
    private const string TenantIdHeader = "X-Tenant-Id";

    public SeoController(
        ISeoService seoService,
        ILogger<SeoController> logger)
    {
        _seoService = seoService;
        _logger = logger;
    }

    /// <summary>
    /// Gets SEO metadata for a product or category.
    /// </summary>
    /// <param name="query">Query parameters (type, slug, baseUrl)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>SEO metadata DTO</returns>
    /// <response code="200">SEO metadata returned successfully</response>
    /// <response code="400">Missing or invalid X-Tenant-Id header, or invalid query parameters</response>
    /// <response code="404">Resource not found</response>
    /// <response code="503">Service temporarily unavailable</response>
    [HttpGet]
    [ProducesResponseType(typeof(SeoMetadataDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetSeoMetadata(
        [FromQuery] SeoMetadataQuery query,
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

        // Step 2: Validate query parameters
        var (errorCode, errorMessage) = query.Validate();
        if (errorCode != null)
        {
            _logger.LogWarning(
                "Invalid query parameters | Endpoint: SEO | Error: {ErrorCode} | TenantId: {TenantId} | TraceId: {TraceId}",
                errorCode, tenantId, traceId);
            return BadRequest(new ApiErrorResponse(errorCode, errorMessage!, traceId));
        }

        _logger.LogInformation(
            "GetSeoMetadata request | TenantId: {TenantId} | Type: {Type} | Slug: {Slug} | TraceId: {TraceId}",
            tenantId, query.Type, query.Slug, traceId);

        try
        {
            var metadata = await _seoService.GetSeoMetadataAsync(
                tenantId, 
                query.Type, 
                query.Slug, 
                query.BaseUrl,
                traceId, 
                cancellationToken,
                StorefrontLanguageResolver.Resolve(query.Lang, Request.Headers.AcceptLanguage));

            if (metadata == null)
            {
                _logger.LogInformation(
                    "GetSeoMetadata not found | TenantId: {TenantId} | Type: {Type} | Slug: {Slug} | TraceId: {TraceId}",
                    tenantId, query.Type, query.Slug, traceId);
                
                return NotFound(new ApiErrorResponse(
                    "RESOURCE_NOT_FOUND", 
                    $"{query.Type} with slug '{query.Slug}' not found", 
                    traceId));
            }

            _logger.LogInformation(
                "GetSeoMetadata success | TenantId: {TenantId} | Type: {Type} | Slug: {Slug} | TraceId: {TraceId}",
                tenantId, query.Type, query.Slug, traceId);

            Response.Headers.ContentLanguage = metadata.Language;
            Response.Headers.Append("Vary", "Accept-Language");
            return Ok(metadata);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "GetSeoMetadata failed | TenantId: {TenantId} | Type: {Type} | Slug: {Slug} | TraceId: {TraceId}",
                tenantId, query.Type, query.Slug, traceId);

            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    #region Helper Methods

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
