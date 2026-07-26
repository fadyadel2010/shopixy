using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Services.Storefront;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Storefront Categories Controller - Read-only e-commerce category endpoints.
/// 
/// Route: /api/storefront/categories
/// 
/// All endpoints require X-Tenant-Id header for multi-tenant isolation.
/// All endpoints are READ-ONLY.
/// </summary>
[ApiController]
[Route("api/storefront/categories")]
[AllowAnonymous]
public class StorefrontCategoriesController : ControllerBase
{
    private readonly IStorefrontCategoryService _categoryService;
    private readonly ILogger<StorefrontCategoriesController> _logger;
    private const string TenantIdHeader = "X-Tenant-Id";

    public StorefrontCategoriesController(
        IStorefrontCategoryService categoryService,
        ILogger<StorefrontCategoriesController> logger)
    {
        _categoryService = categoryService;
        _logger = logger;
    }

    /// <summary>
    /// Gets all visible categories for a tenant as a hierarchical tree.
    /// Used for navigation menus, mega menus, footer menus.
    /// </summary>
    /// <returns>List of CategoryTreeDto (root categories with children)</returns>
    /// <response code="200">Category tree returned successfully</response>
    /// <response code="400">Missing or invalid X-Tenant-Id header</response>
    /// <response code="503">Service temporarily unavailable</response>
    [HttpGet]
    [ProducesResponseType(typeof(List<CategoryTreeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetCategoryTree(CancellationToken cancellationToken)
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
            "GetCategoryTree request | TenantId: {TenantId} | TraceId: {TraceId}",
            tenantId, traceId);

        try
        {
            // Step 2: Get category tree from service
            var tree = await _categoryService.GetCategoryTreeAsync(tenantId, traceId, cancellationToken);

            _logger.LogInformation(
                "GetCategoryTree success | TenantId: {TenantId} | RootCategories: {RootCount} | TraceId: {TraceId}",
                tenantId, tree.Count, traceId);

            return Ok(tree);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "GetCategoryTree failed | TenantId: {TenantId} | TraceId: {TraceId}",
                tenantId, traceId);

            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    /// <summary>
    /// Gets a single category by its SEO-friendly slug.
    /// Used by category landing pages.
    /// </summary>
    /// <param name="slug">SEO-friendly category URL slug</param>
    /// <returns>CategoryReadDto if found, 404 if not found</returns>
    /// <response code="200">Category found and returned</response>
    /// <response code="400">Missing or invalid X-Tenant-Id header</response>
    /// <response code="404">Category not found</response>
    /// <response code="503">Service temporarily unavailable</response>
    [HttpGet("{slug}")]
    [ProducesResponseType(typeof(CategoryReadDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetCategoryBySlug(string slug, CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();

        // Step 1: Validate X-Tenant-Id header
        var tenantValidation = ValidateTenantHeader(traceId);
        if (tenantValidation.Error != null)
        {
            return tenantValidation.Error;
        }
        var tenantId = tenantValidation.TenantId!.Value;

        // Step 2: Validate slug
        if (string.IsNullOrWhiteSpace(slug))
        {
            _logger.LogWarning(
                "Invalid slug | TenantId: {TenantId} | Slug: {Slug} | TraceId: {TraceId}",
                tenantId, slug, traceId);
            return BadRequest(new ApiErrorResponse("INVALID_SLUG", "Category slug is required", traceId));
        }

        _logger.LogInformation(
            "GetCategoryBySlug request | TenantId: {TenantId} | Slug: {Slug} | TraceId: {TraceId}",
            tenantId, slug, traceId);

        try
        {
            // Step 3: Get category from service
            var category = await _categoryService.GetCategoryBySlugAsync(tenantId, slug, traceId, cancellationToken);

            if (category == null)
            {
                _logger.LogInformation(
                    "Category not found | TenantId: {TenantId} | Slug: {Slug} | TraceId: {TraceId}",
                    tenantId, slug, traceId);
                return NotFound(ApiErrorResponse.CategoryNotFound(traceId));
            }

            _logger.LogInformation(
                "GetCategoryBySlug success | TenantId: {TenantId} | Slug: {Slug} | CategoryId: {CategoryId} | TraceId: {TraceId}",
                tenantId, slug, category.Id, traceId);

            return Ok(category);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "GetCategoryBySlug failed | TenantId: {TenantId} | Slug: {Slug} | TraceId: {TraceId}",
                tenantId, slug, traceId);

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
