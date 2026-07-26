using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Slug Resolver Controller - Read-only slug resolution endpoint.
/// 
/// Route: /api/storefront/resolve-slug
/// 
/// Endpoints:
/// - GET /{slug} → Resolve a slug to its resource type and ID
/// 
/// All endpoints:
/// - Require X-Tenant-Id header for multi-tenant isolation
/// - Validate tenant exists and is active before processing
/// - Are READ-ONLY (no mutations)
/// - Use Redis as primary cache (cache-first mindset)
/// - Return lightweight DTOs for fast frontend rendering
/// 
/// Performance Characteristics:
/// - READ-HEAVY: Designed for high-frequency slug resolution
/// - LOW-VOLATILITY: Data changes infrequently
/// - CACHE-FIRST: Redis is queried before MongoDB
/// </summary>
[ApiController]
[Route("api/storefront/resolve-slug")]
[AllowAnonymous]
public class SlugResolverController : ControllerBase
{
    private readonly ISeoService _seoService;
    private readonly ITenantService _tenantService;
    private readonly ILogger<SlugResolverController> _logger;
    private const string TenantIdHeader = "X-Tenant-Id";

    public SlugResolverController(
        ISeoService seoService,
        ITenantService tenantService,
        ILogger<SlugResolverController> logger)
    {
        _seoService = seoService;
        _tenantService = tenantService;
        _logger = logger;
    }

    /// <summary>
    /// Resolves a slug to its resource type and ID.
    /// Tries product first, then category.
    /// </summary>
    /// <param name="slug">The slug to resolve</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Slug resolution DTO with type, ID, and canonical slug</returns>
    /// <response code="200">Slug resolved successfully</response>
    /// <response code="400">Missing or invalid X-Tenant-Id header, or invalid slug</response>
    /// <response code="404">Slug not found</response>
    /// <response code="503">Service temporarily unavailable</response>
    [HttpGet("{slug}")]
    [ProducesResponseType(typeof(SlugResolutionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ResolveSlug(
        [FromRoute] string slug,
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

        // Step 2: Validate tenant exists and is active
        var isTenantActive = await _tenantService.IsTenantActiveAsync(tenantId, cancellationToken);
        if (!isTenantActive)
        {
            _logger.LogWarning(
                "Tenant not found or inactive | Endpoint: SlugResolver | TenantId: {TenantId} | TraceId: {TraceId}",
                tenantId, traceId);
            return BadRequest(new ApiErrorResponse(
                "TENANT_NOT_ACTIVE",
                "Tenant not found or is not active",
                traceId));
        }

        // Step 3: Validate slug
        if (string.IsNullOrWhiteSpace(slug))
        {
            _logger.LogWarning(
                "Invalid slug | Endpoint: SlugResolver | TenantId: {TenantId} | TraceId: {TraceId}",
                tenantId, traceId);
            return BadRequest(new ApiErrorResponse(
                "INVALID_SLUG", 
                "Slug parameter is required", 
                traceId));
        }

        _logger.LogInformation(
            "ResolveSlug request | TenantId: {TenantId} | Slug: {Slug} | TraceId: {TraceId}",
            tenantId, slug, traceId);

        try
        {
            var resolution = await _seoService.ResolveSlugAsync(
                tenantId, 
                slug.Trim().ToLowerInvariant(), 
                traceId, 
                cancellationToken);

            if (resolution == null)
            {
                _logger.LogInformation(
                    "ResolveSlug not found | TenantId: {TenantId} | Slug: {Slug} | TraceId: {TraceId}",
                    tenantId, slug, traceId);
                
                return NotFound(new ApiErrorResponse(
                    "SLUG_NOT_FOUND", 
                    $"No product or category found with slug '{slug}'", 
                    traceId));
            }

            _logger.LogInformation(
                "ResolveSlug success | TenantId: {TenantId} | Slug: {Slug} | Type: {Type} | Id: {Id} | TraceId: {TraceId}",
                tenantId, slug, resolution.Type, resolution.Id, traceId);

            return Ok(resolution);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "ResolveSlug failed | TenantId: {TenantId} | Slug: {Slug} | TraceId: {TraceId}",
                tenantId, slug, traceId);

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
