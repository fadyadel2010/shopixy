namespace PointOfSale.Core.Analytics;

/// <summary>
/// Minimal info needed to resolve pixel value for an order item.
/// Phase M2: Per-Product Pixel Override.
/// </summary>
public record OrderItemPixelInfo(
    Guid ProductId,
    decimal LineItemTotal
);
