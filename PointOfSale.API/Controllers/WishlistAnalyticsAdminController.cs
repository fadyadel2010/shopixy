/**
 * Wishlist Analytics Admin Controller
 * Phase 10.3.6.2 - Wishlist Analytics
 * 
 * Admin endpoints for wishlist analytics data.
 * Used by POS Dashboard to display popular wishlisted products.
 */

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.API.Controllers;


/// <summary>
/// Admin controller for wishlist analytics.
/// </summary>
[ApiController]
[Route("api/admin/wishlist-analytics")]
[Authorize(Roles = "Admin,Owner,Manager")]
public class WishlistAnalyticsAdminController : ControllerBase
{
    private readonly IStorefrontWishlistService _wishlistService;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<WishlistAnalyticsAdminController> _logger;

    public WishlistAnalyticsAdminController(
        IStorefrontWishlistService wishlistService,
        ITenantContext tenantContext,
        ILogger<WishlistAnalyticsAdminController> logger)
    {
        _wishlistService = wishlistService;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    private Guid TenantId => _tenantContext.RequiredTenantId;

    /// <summary>
    /// Get wishlist analytics for admin dashboard.
    /// Returns top wishlisted products, customer counts, and activity stats.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(WishlistAnalyticsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetWishlistAnalytics(CancellationToken cancellationToken)
    {
        var tenantId = TenantId;

        _logger.LogInformation(
            "[WishlistAnalytics] Fetching analytics | TenantId: {TenantId} | User: {User}",
            tenantId, User.Identity?.Name);

        try
        {
            var analytics = await _wishlistService.GetWishlistAnalyticsAsync(tenantId, cancellationToken);
            return Ok(analytics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WishlistAnalytics] Failed to get analytics | TenantId: {TenantId}", tenantId);
            return StatusCode(500, new { error = "Failed to load wishlist analytics" });
        }
    }

    /// <summary>
    /// Get top wishlisted products for quick display.
    /// </summary>
    [HttpGet("top-products")]
    [ProducesResponseType(typeof(List<PopularWishlistProductDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTopWishlistedProducts(
        [FromQuery] int limit = 5,
        CancellationToken cancellationToken = default)
    {
        var tenantId = TenantId;

        try
        {
            var analytics = await _wishlistService.GetWishlistAnalyticsAsync(tenantId, cancellationToken);
            var topProducts = analytics.TopProducts.Take(limit).ToList();
            return Ok(topProducts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WishlistAnalytics] Failed to get top products | TenantId: {TenantId}", tenantId);
            return StatusCode(500, new { error = "Failed to load top products" });
        }
    }
}
