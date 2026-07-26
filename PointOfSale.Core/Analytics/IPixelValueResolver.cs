using PointOfSale.Core.Analytics;

namespace PointOfSale.Core.Analytics;

/// <summary>
/// Service that resolves the value to send to tracking pixels.
/// 
/// CRITICAL ARCHITECTURAL RULE:
/// This service is the ONLY place where pixel value logic resides.
/// It must be completely isolated from order management and internal reporting.
/// </summary>
public interface IPixelValueResolver
{
    /// <summary>
    /// Resolves the value to send to a pixel provider.
    /// </summary>
    /// <param name="tenantId">The tenant ID.</param>
    /// <param name="actualOrderTotal">The REAL order total from the source of truth.</param>
    /// <param name="provider">The provider being dispatched to.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The value to report to the pixel (may be overridden).</returns>
    Task<decimal> ResolvePixelValueAsync(
        Guid tenantId, 
        decimal actualOrderTotal, 
        PixelProvider provider,
        CancellationToken ct = default);

    /// <summary>
    /// Resolves the total pixel value for a list of order items, applying per-product overrides.
    /// Phase M2: Per-Product Pixel Override.
    /// </summary>
    Task<decimal> ResolvePixelValueAsync(
        Guid tenantId,
        IReadOnlyCollection<OrderItemPixelInfo> orderItems,
        PixelProvider provider,
        CancellationToken ct = default);
}
