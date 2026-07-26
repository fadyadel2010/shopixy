using PointOfSale.Core.Entities;

namespace PointOfSale.Core.Helpers;

/// <summary>
/// Shared query helpers for CommerceHub entities to ensure consistent ordering and filtering.
/// </summary>
public static class CommerceHubQueryHelpers
{
    /// <summary>
    /// Applies canonical ordering to SubOrders collection: OrderBy(OrderNumber).ThenBy(Id).
    /// This ensures deterministic ordering for projection checksums and UI display.
    /// </summary>
    public static IOrderedEnumerable<CommerceHubSubOrder> GetOrderedSubOrders(
        this IEnumerable<CommerceHubSubOrder> subOrders)
    {
        return subOrders
            .OrderBy(subOrder => subOrder.OrderNumber)
            .ThenBy(subOrder => subOrder.Id);
    }
}
