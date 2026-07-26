using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Services.Storefront;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Storefront Wishlist Controller - Customer wishlist management.
/// 
/// Phase: 10.3.6 - Wishlist (Production-grade)
/// 
/// ARCHITECTURAL RULES:
/// - All endpoints require StorefrontJwt authentication
/// - X-Tenant-Id header is REQUIRED
/// - Writes go to PostgreSQL, emit domain events
/// - Reads from MongoDB with Redis cache
/// - Idempotent operations (safe to retry)
/// - Product validation via Catalog (cached)
/// 
/// ENDPOINTS:
/// - POST   /api/storefront/wishlist          - Add to wishlist
/// - DELETE /api/storefront/wishlist/{id}     - Remove from wishlist
/// - GET    /api/storefront/wishlist          - Get wishlist
/// - DELETE /api/storefront/wishlist          - Clear wishlist
/// - GET    /api/storefront/wishlist/count    - Get count (badge)
/// - GET    /api/storefront/wishlist/check/{id} - Check if in wishlist
/// </summary>
[ApiController]
[Route("api/storefront/wishlist")]
[Authorize(AuthenticationSchemes = "StorefrontJwt")]
public class StorefrontWishlistController : ControllerBase
{
    private readonly IStorefrontWishlistService _wishlistService;
    private readonly ILogger<StorefrontWishlistController> _logger;

    public StorefrontWishlistController(
        IStorefrontWishlistService wishlistService,
        ILogger<StorefrontWishlistController> logger)
    {
        _wishlistService = wishlistService;
        _logger = logger;
    }

    /// <summary>
    /// Add a product to the customer's wishlist.
    /// Idempotent - adding same product twice returns success.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(AddToWishlistResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> AddToWishlist(
        [FromBody] AddToWishlistRequest request,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var (customerId, tenantId) = GetCustomerAndTenantIds();

        if (customerId == null || tenantId == null)
            return Unauthorized(ApiErrorResponse.InvalidToken(traceId));

        _logger.LogInformation(
            "[StorefrontWishlist] {Action} | TenantId: {TenantId} | CustomerId: {CustomerId} | ProductId: {ProductId} | TraceId: {TraceId}",
            "ADD_TO_WISHLIST",
            tenantId,
            customerId,
            request.ProductId,
            traceId);

        var result = await _wishlistService.AddToWishlistAsync(
            tenantId.Value,
            customerId.Value,
            request.ProductId,
            cancellationToken);

        if (!result.Success)
        {
            return BadRequest(new ApiErrorResponse(
                result.ErrorCode ?? "ADD_FAILED",
                result.Error ?? "Failed to add to wishlist",
                traceId));
        }

        return Ok(new AddToWishlistResponse
        {
            Success = true,
            Message = "Added to wishlist",
            WishlistCount = result.NewCount
        });
    }

    /// <summary>
    /// Remove a product from the customer's wishlist.
    /// Idempotent - removing non-existing product returns success.
    /// </summary>
    [HttpDelete("{productId:guid}")]
    [ProducesResponseType(typeof(RemoveFromWishlistResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RemoveFromWishlist(
        Guid productId,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var (customerId, tenantId) = GetCustomerAndTenantIds();

        if (customerId == null || tenantId == null)
            return Unauthorized(ApiErrorResponse.InvalidToken(traceId));

        _logger.LogInformation(
            "[StorefrontWishlist] {Action} | TenantId: {TenantId} | CustomerId: {CustomerId} | ProductId: {ProductId} | TraceId: {TraceId}",
            "REMOVE_FROM_WISHLIST",
            tenantId,
            customerId,
            productId,
            traceId);

        var result = await _wishlistService.RemoveFromWishlistAsync(
            tenantId.Value,
            customerId.Value,
            productId,
            cancellationToken);

        return Ok(new RemoveFromWishlistResponse
        {
            Success = true,
            Message = "Removed from wishlist",
            WishlistCount = result.NewCount
        });
    }

    /// <summary>
    /// Get the customer's wishlist.
    /// Returns product IDs with timestamps.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(GetWishlistResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetWishlist(CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var (customerId, tenantId) = GetCustomerAndTenantIds();

        if (customerId == null || tenantId == null)
            return Unauthorized(ApiErrorResponse.InvalidToken(traceId));

        var wishlist = await _wishlistService.GetWishlistAsync(
            tenantId.Value,
            customerId.Value,
            cancellationToken);

        return Ok(new GetWishlistResponse
        {
            Items = wishlist.Items.Select(i => new WishlistItemResponse
            {
                ProductId = i.ProductId,
                AddedAtUtc = i.AddedAtUtc
            }).ToList(),
            TotalCount = wishlist.TotalCount
        });
    }

    /// <summary>
    /// Clear the entire wishlist.
    /// </summary>
    [HttpDelete]
    [ProducesResponseType(typeof(ClearWishlistResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ClearWishlist(CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var (customerId, tenantId) = GetCustomerAndTenantIds();

        if (customerId == null || tenantId == null)
            return Unauthorized(ApiErrorResponse.InvalidToken(traceId));

        _logger.LogInformation(
            "[StorefrontWishlist] {Action} | TenantId: {TenantId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
            "CLEAR_WISHLIST",
            tenantId,
            customerId,
            traceId);

        var result = await _wishlistService.ClearWishlistAsync(
            tenantId.Value,
            customerId.Value,
            cancellationToken);

        return Ok(new ClearWishlistResponse
        {
            Success = true,
            Message = "Wishlist cleared"
        });
    }

    /// <summary>
    /// Get wishlist count for header badge.
    /// </summary>
    [HttpGet("count")]
    [ProducesResponseType(typeof(WishlistCountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetWishlistCount(CancellationToken cancellationToken)
    {
        var (customerId, tenantId) = GetCustomerAndTenantIds();

        if (customerId == null || tenantId == null)
            return Unauthorized(ApiErrorResponse.InvalidToken(GetTraceId()));

        var count = await _wishlistService.GetWishlistCountAsync(
            tenantId.Value,
            customerId.Value,
            cancellationToken);

        return Ok(new WishlistCountResponse
        {
            Count = count
        });
    }

    /// <summary>
    /// Check if a product is in the wishlist.
    /// </summary>
    [HttpGet("check/{productId:guid}")]
    [ProducesResponseType(typeof(CheckWishlistResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CheckInWishlist(
        Guid productId,
        CancellationToken cancellationToken)
    {
        var (customerId, tenantId) = GetCustomerAndTenantIds();

        if (customerId == null || tenantId == null)
            return Unauthorized(ApiErrorResponse.InvalidToken(GetTraceId()));

        var isInWishlist = await _wishlistService.IsInWishlistAsync(
            tenantId.Value,
            customerId.Value,
            productId,
            cancellationToken);

        return Ok(new CheckWishlistResponse
        {
            ProductId = productId,
            IsInWishlist = isInWishlist
        });
    }

    #region Helper Methods

    private (Guid? CustomerId, Guid? TenantId) GetCustomerAndTenantIds()
    {
        var customerIdClaim = User.FindFirst("CustomerId")?.Value;
        var tenantIdClaim = User.FindFirst("TenantId")?.Value;

        if (!Guid.TryParse(customerIdClaim, out var customerId) ||
            !Guid.TryParse(tenantIdClaim, out var tenantId))
        {
            return (null, null);
        }

        return (customerId, tenantId);
    }

    private string GetTraceId() => Activity.Current?.Id ?? HttpContext.TraceIdentifier;

    #endregion
}

#region DTOs

public class AddToWishlistRequest
{
    [Required]
    public Guid ProductId { get; set; }
}

public class AddToWishlistResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int WishlistCount { get; set; }
}

public class RemoveFromWishlistResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int WishlistCount { get; set; }
}

public class GetWishlistResponse
{
    public List<WishlistItemResponse> Items { get; set; } = new();
    public int TotalCount { get; set; }
}

public class WishlistItemResponse
{
    public Guid ProductId { get; set; }
    public DateTime AddedAtUtc { get; set; }
}

public class ClearWishlistResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class WishlistCountResponse
{
    public int Count { get; set; }
}

public class CheckWishlistResponse
{
    public Guid ProductId { get; set; }
    public bool IsInWishlist { get; set; }
}

#endregion
