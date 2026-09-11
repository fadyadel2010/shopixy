using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;

namespace PointOfSale.API.Services.CommerceHub;

internal static class CommerceHubOrderStateHelper
{
    private static readonly IReadOnlyDictionary<OrderStatus, IReadOnlyCollection<OrderStatus>> AllowedTransitions =
        new Dictionary<OrderStatus, IReadOnlyCollection<OrderStatus>>
        {
            [OrderStatus.Pending] = new[] { OrderStatus.Processing, OrderStatus.Cancelled },
            [OrderStatus.Confirmed] = new[] { OrderStatus.Processing, OrderStatus.Cancelled },
            [OrderStatus.Processing] = new[] { OrderStatus.Shipped, OrderStatus.Cancelled },
            [OrderStatus.Shipped] = new[] { OrderStatus.Delivered, OrderStatus.Cancelled },
            [OrderStatus.OnHold] = new[] { OrderStatus.Cancelled }
        };

    public static bool CanTransition(OrderStatus current, OrderStatus next)
    {
        if (current == next)
        {
            return true;
        }

        return AllowedTransitions.TryGetValue(current, out var allowedStatuses) &&
               allowedStatuses.Contains(next);
    }

    public static OrderStatus AggregateMarketplaceStatus(IEnumerable<CommerceHubSubOrder> subOrders, OrderStatus fallbackStatus)
    {
        var statuses = subOrders.Select(x => x.Status).ToList();
        if (statuses.Count == 0)
        {
            return fallbackStatus;
        }

        if (statuses.All(status => status == OrderStatus.Cancelled))
        {
            return OrderStatus.Cancelled;
        }

        if (statuses.All(status => status == OrderStatus.Refunded))
        {
            return OrderStatus.Refunded;
        }

        if (statuses.All(status => status is OrderStatus.Cancelled or OrderStatus.Refunded) &&
            statuses.Any(status => status == OrderStatus.Refunded))
        {
            return OrderStatus.Refunded;
        }

        if (statuses.All(status => status == OrderStatus.Completed))
        {
            return OrderStatus.Completed;
        }

        if (statuses.All(status => status is OrderStatus.Delivered or OrderStatus.Completed))
        {
            return OrderStatus.Delivered;
        }

        if (statuses.All(status => status is OrderStatus.Delivered or OrderStatus.Completed or OrderStatus.Refunded))
        {
            return statuses.Any(status => status == OrderStatus.Delivered)
                ? OrderStatus.Delivered
                : OrderStatus.Completed;
        }

        if (statuses.Any(status => status is OrderStatus.Shipped or OrderStatus.Delivered or OrderStatus.Completed))
        {
            return OrderStatus.Shipped;
        }

        if (statuses.Any(status => status == OrderStatus.Processing))
        {
            return OrderStatus.Processing;
        }

        if (statuses.Any(status => status == OrderStatus.Confirmed))
        {
            return OrderStatus.Confirmed;
        }

        if (statuses.Any(status => status == OrderStatus.OnHold))
        {
            return OrderStatus.OnHold;
        }

        if (statuses.Any(status => status == OrderStatus.Pending))
        {
            return OrderStatus.Pending;
        }

        return fallbackStatus;
    }

    public static bool RequiresPaymentReadiness(OrderStatus nextStatus) =>
        nextStatus is OrderStatus.Processing or OrderStatus.Shipped or OrderStatus.Delivered;

    public static bool HasCollectedPayment(PaymentStatus paymentStatus) =>
        paymentStatus is PaymentStatus.Collected or PaymentStatus.Settled;

    public static bool IsReadyForFulfillment(CommerceHubSubOrder subOrder)
    {
        ArgumentNullException.ThrowIfNull(subOrder);

        return HasCollectedPayment(subOrder.PaymentStatus) ||
               subOrder.PaymentStatus == PaymentStatus.CashOnDelivery ||
               subOrder.PaymentMethod == CommerceHubPaymentMethod.CashOnDelivery;
    }

    public static bool IsCompletedVendorStatus(OrderStatus status) =>
        status is OrderStatus.Delivered or OrderStatus.Completed or OrderStatus.Refunded;

    public static string DescribeAllowedTransitions(OrderStatus current)
    {
        if (!AllowedTransitions.TryGetValue(current, out var allowedStatuses) || allowedStatuses.Count == 0)
        {
            return "none";
        }

        return string.Join(", ", allowedStatuses.OrderBy(GetRank));
    }

    public static OrderStatus ConfirmAfterPayment(OrderStatus currentStatus)
    {
        return currentStatus switch
        {
            OrderStatus.Pending => OrderStatus.Confirmed,
            OrderStatus.OnHold => OrderStatus.Confirmed,
            _ => currentStatus
        };
    }

    public static OrderStatus CancelAfterPaymentFailure(OrderStatus currentStatus)
    {
        return currentStatus switch
        {
            OrderStatus.Pending => OrderStatus.Cancelled,
            OrderStatus.Confirmed => OrderStatus.Cancelled,
            OrderStatus.OnHold => OrderStatus.Cancelled,
            _ => currentStatus
        };
    }

    private static int GetRank(OrderStatus status)
    {
        return status switch
        {
            OrderStatus.Pending => 0,
            OrderStatus.Confirmed => 1,
            OrderStatus.Processing => 2,
            OrderStatus.Shipped => 3,
            OrderStatus.Delivered => 4,
            OrderStatus.Completed => 5,
            OrderStatus.Cancelled => 6,
            OrderStatus.Refunded => 7,
            OrderStatus.OnHold => 1,
            _ => 0
        };
    }
}
