using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Helpers;
using PointOfSale.API.Services.Storefront;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Storefront Homepage Controller - Read-only homepage section endpoints.
/// 
/// Route: /api/storefront/home
/// 
/// Endpoints:
/// - GET /featured-products → Featured/curated products
/// - GET /new-arrivals → Latest products (sorted by CreatedAtUtc DESC)
/// - GET /best-sellers → Popular products from Elasticsearch candidates or MongoDB degraded candidates, SQL-revalidated before return
/// - GET /categories → Top-level navigation categories
/// 
/// All endpoints:
/// - Require X-Tenant-Id header for multi-tenant isolation
/// - Are READ-ONLY (no mutations)
/// - Use Redis as primary cache (cache-first mindset)
/// - Return lightweight DTOs for fast frontend rendering
/// 
/// Performance Characteristics:
/// - READ-HEAVY: Designed for high-frequency homepage loads
/// - LOW-VOLATILITY: Data changes infrequently
/// - CACHE-FIRST: Redis is queried before MongoDB/Elasticsearch
/// </summary>
[ApiController]
[Route("api/storefront/home")]
[AllowAnonymous]
public class StorefrontHomeController : ControllerBase
{
    private readonly IStorefrontHomeService _homeService;
    private readonly ILogger<StorefrontHomeController> _logger;
    private const string TenantIdHeader = "X-Tenant-Id";

    public StorefrontHomeController(
        IStorefrontHomeService homeService,
        ILogger<StorefrontHomeController> logger)
    {
        _homeService = homeService;
        _logger = logger;
    }

    /// <summary>
    /// Gets featured products for homepage display.
    /// </summary>
    /// <returns>List of featured ProductListItemDto</returns>
    /// <response code="200">Featured products returned successfully</response>
    /// <response code="400">Missing or invalid X-Tenant-Id header</response>
    /// <response code="503">Service temporarily unavailable</response>
    [HttpGet("featured-products")]
    [ProducesResponseType(typeof(List<ProductListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetFeaturedProducts(CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();

        // Step 1: Validate X-Tenant-Id header
        var tenantValidation = ValidateTenantHeader(traceId);
        if (tenantValidation.Error != null)
        {
            return tenantValidation.Error;
        }
        var tenantId = tenantValidation.TenantId!.Value;

        // Step 2: Detect Affiliate Mode
        var affiliateMode = AffiliateModeResolver.IsAffiliateMode(HttpContext);

        // Step 2a: Record Affiliate Mode request metric
        HttpContext.RecordAffiliateModeRequest(tenantId);

        // Step 2b: Add response headers for frontend
        HttpContext.AddAffiliateModeHeaders();

        _logger.LogInformation(
            "GetFeaturedProducts request | TenantId: {TenantId} | AffiliateMode: {AffiliateMode} | TraceId: {TraceId}",
            tenantId, affiliateMode, traceId);

        try
        {
            var products = await _homeService.GetFeaturedProductsAsync(tenantId, traceId, affiliateMode, cancellationToken);

            _logger.LogInformation(
                "GetFeaturedProducts success | TenantId: {TenantId} | ResultCount: {ResultCount} | TraceId: {TraceId}",
                tenantId, products.Count, traceId);

            return Ok(products);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "GetFeaturedProducts failed | TenantId: {TenantId} | TraceId: {TraceId}",
                tenantId, traceId);

            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    /// <summary>
    /// Gets new arrival products for homepage display.
    /// Products are sorted by creation date (newest first).
    /// </summary>
    /// <returns>List of new arrival ProductListItemDto</returns>
    /// <response code="200">New arrivals returned successfully</response>
    /// <response code="400">Missing or invalid X-Tenant-Id header</response>
    /// <response code="503">Service temporarily unavailable</response>
    [HttpGet("new-arrivals")]
    [ProducesResponseType(typeof(List<ProductListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetNewArrivals(CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();

        // Step 1: Validate X-Tenant-Id header
        var tenantValidation = ValidateTenantHeader(traceId);
        if (tenantValidation.Error != null)
        {
            return tenantValidation.Error;
        }
        var tenantId = tenantValidation.TenantId!.Value;

        // Step 2: Detect Affiliate Mode
        var affiliateMode = AffiliateModeResolver.IsAffiliateMode(HttpContext);

        // Step 2a: Record Affiliate Mode request metric
        HttpContext.RecordAffiliateModeRequest(tenantId);

        // Step 2b: Add response headers for frontend
        HttpContext.AddAffiliateModeHeaders();

        _logger.LogInformation(
            "GetNewArrivals request | TenantId: {TenantId} | AffiliateMode: {AffiliateMode} | TraceId: {TraceId}",
            tenantId, affiliateMode, traceId);

        try
        {
            var products = await _homeService.GetNewArrivalsAsync(tenantId, traceId, affiliateMode, cancellationToken);

            _logger.LogInformation(
                "GetNewArrivals success | TenantId: {TenantId} | ResultCount: {ResultCount} | TraceId: {TraceId}",
                tenantId, products.Count, traceId);

            return Ok(products);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "GetNewArrivals failed | TenantId: {TenantId} | TraceId: {TraceId}",
                tenantId, traceId);

            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    /// <summary>
    /// Gets best-selling products for homepage display.
    /// Uses Elasticsearch for popularity candidates with MongoDB degraded candidates, then SQL-revalidates.
    /// </summary>
    /// <returns>List of best-selling ProductListItemDto</returns>
    /// <response code="200">Best sellers returned successfully</response>
    /// <response code="400">Missing or invalid X-Tenant-Id header</response>
    /// <response code="503">Service temporarily unavailable</response>
    [HttpGet("best-sellers")]
    [ProducesResponseType(typeof(List<ProductListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetBestSellers(CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();

        // Step 1: Validate X-Tenant-Id header
        var tenantValidation = ValidateTenantHeader(traceId);
        if (tenantValidation.Error != null)
        {
            return tenantValidation.Error;
        }
        var tenantId = tenantValidation.TenantId!.Value;

        // Step 2: Detect Affiliate Mode
        var affiliateMode = AffiliateModeResolver.IsAffiliateMode(HttpContext);

        // Step 2a: Record Affiliate Mode request metric
        HttpContext.RecordAffiliateModeRequest(tenantId);

        // Step 2b: Add response headers for frontend
        HttpContext.AddAffiliateModeHeaders();

        _logger.LogInformation(
            "GetBestSellers request | TenantId: {TenantId} | AffiliateMode: {AffiliateMode} | TraceId: {TraceId}",
            tenantId, affiliateMode, traceId);

        try
        {
            var products = await _homeService.GetBestSellersAsync(tenantId, traceId, affiliateMode, cancellationToken);

            _logger.LogInformation(
                "GetBestSellers success | TenantId: {TenantId} | ResultCount: {ResultCount} | TraceId: {TraceId}",
                tenantId, products.Count, traceId);

            return Ok(products);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "GetBestSellers failed | TenantId: {TenantId} | TraceId: {TraceId}",
                tenantId, traceId);

            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    /// <summary>
    /// Gets top-level categories for homepage navigation.
    /// Returns only categories with ParentId == null.
    /// </summary>
    /// <returns>List of CategoryReadDto for navigation</returns>
    /// <response code="200">Categories returned successfully</response>
    /// <response code="400">Missing or invalid X-Tenant-Id header</response>
    /// <response code="503">Service temporarily unavailable</response>
    [HttpGet("categories")]
    [ProducesResponseType(typeof(List<CategoryReadDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetHomepageCategories(CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();

        // Step 1: Validate X-Tenant-Id header
        var tenantValidation = ValidateTenantHeader(traceId);
        if (tenantValidation.Error != null)
        {
            return tenantValidation.Error;
        }
        var tenantId = tenantValidation.TenantId!.Value;

        _logger.LogInformation(
            "GetHomepageCategories request | TenantId: {TenantId} | TraceId: {TraceId}",
            tenantId, traceId);

        try
        {
            var categories = await _homeService.GetHomepageCategoriesAsync(tenantId, traceId, cancellationToken);

            _logger.LogInformation(
                "GetHomepageCategories success | TenantId: {TenantId} | ResultCount: {ResultCount} | TraceId: {TraceId}",
                tenantId, categories.Count, traceId);

            return Ok(categories);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "GetHomepageCategories failed | TenantId: {TenantId} | TraceId: {TraceId}",
                tenantId, traceId);

            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    /// <summary>
    /// Gets homepage products in a curated order.
    /// Returns the exact subset of products requested by ProductId.
    /// </summary>
    [HttpGet("products/by-ids")]
    [ProducesResponseType(typeof(List<ProductListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetHomepageProductsByIds(
        [FromQuery] List<Guid>? productIds,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();

        var tenantValidation = ValidateTenantHeader(traceId);
        if (tenantValidation.Error != null)
        {
            return tenantValidation.Error;
        }

        var tenantId = tenantValidation.TenantId!.Value;
        var sanitizedProductIds = (productIds ?? [])
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        if (sanitizedProductIds.Count == 0)
        {
            return Ok(new List<ProductListItemDto>());
        }

        var affiliateMode = AffiliateModeResolver.IsAffiliateMode(HttpContext);
        HttpContext.RecordAffiliateModeRequest(tenantId);
        HttpContext.AddAffiliateModeHeaders();

        _logger.LogInformation(
            "GetHomepageProductsByIds request | TenantId: {TenantId} | ProductCount: {ProductCount} | AffiliateMode: {AffiliateMode} | TraceId: {TraceId}",
            tenantId,
            sanitizedProductIds.Count,
            affiliateMode,
            traceId);

        try
        {
            var products = await _homeService.GetProductsByIdsAsync(
                tenantId,
                sanitizedProductIds,
                traceId,
                affiliateMode,
                cancellationToken);

            _logger.LogInformation(
                "GetHomepageProductsByIds success | TenantId: {TenantId} | Requested: {RequestedCount} | Returned: {ReturnedCount} | TraceId: {TraceId}",
                tenantId,
                sanitizedProductIds.Count,
                products.Count,
                traceId);

            return Ok(products);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "GetHomepageProductsByIds failed | TenantId: {TenantId} | ProductCount: {ProductCount} | TraceId: {TraceId}",
                tenantId,
                sanitizedProductIds.Count,
                traceId);

            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    #region Helper Methods

    /// <summary>
    /// Validates the X-Tenant-Id header.
    /// Accepts both UUID and slug formats.
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
