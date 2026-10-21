using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Payments;

/// <summary>
/// Shared money-recognition rules for advancing operational order and shipment state.
/// Keeps fulfillment, shipping, and downstream settlement on the same authority boundary.
/// </summary>
public static class OrderOperationalFinancialAuthority
{
    private static readonly string OfflineCollectionShortCode = "C" + "OD";

    public static bool IsCashOnDelivery(Order order)
    {
        return string.Equals(order.PaymentMethodCode, OfflineCollectionShortCode, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(order.PaymentMethodCode, "CASHONDELIVERY", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(order.PaymentMethodCode, "CASH_ON_DELIVERY", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(order.PaymentMethodCode, "CASH-ON-DELIVERY", StringComparison.OrdinalIgnoreCase);
    }

    public static bool RequiresRecognizedPaymentBeforeOutboundProgress(Order order)
    {
        if (order.Total <= 0m || IsCashOnDelivery(order))
        {
            return false;
        }

        return !HasRecognizedCollectionAuthority(order.PaymentStatus);
    }

    public static bool RequiresPostedCollectionAccountingBeforeOutboundProgress(Order order)
    {
        if (order.Total <= 0m || IsCashOnDelivery(order))
        {
            return false;
        }

        if (order.FinancialPostingStatus is FinancialPostingStatus.Posted or FinancialPostingStatus.NotRequired)
        {
            return false;
        }

        return order.PaymentStatus is PaymentStatus.Collected
            or PaymentStatus.Paid
            or PaymentStatus.Settled
            or PaymentStatus.PartiallyRefunded;
    }

    public static bool CanAdvanceOutboundProgress(Order order) =>
        !RequiresRecognizedPaymentBeforeOutboundProgress(order) &&
        !RequiresPostedCollectionAccountingBeforeOutboundProgress(order);

    /// <summary>
    /// Completion is stricter than outbound fulfillment. COD is intentionally
    /// allowed to ship and be delivered before collection, but a positive-value
    /// order cannot be declared complete until collection is recognized and its
    /// accounting journal is posted (or explicitly not required).
    /// </summary>
    public static string? GetCompletionBlockReason(Order order)
    {
        if (order.Total <= 0m)
        {
            return null;
        }

        if (!HasRecognizedCollectionAuthority(order.PaymentStatus))
        {
            return "Cannot complete order until payment is collected.";
        }

        if (order.FinancialPostingStatus is not (FinancialPostingStatus.Posted or FinancialPostingStatus.NotRequired))
        {
            return "Cannot complete order until the payment collection accounting journal is posted.";
        }

        return null;
    }

    public static string? GetOutboundProgressBlockReason(Order order)
    {
        if (RequiresRecognizedPaymentBeforeOutboundProgress(order))
        {
            return "Cannot advance fulfillment until payment is confirmed.";
        }

        if (RequiresPostedCollectionAccountingBeforeOutboundProgress(order))
        {
            return "Cannot advance fulfillment until the payment collection accounting journal is posted.";
        }

        return null;
    }

    public static bool IsOutboundShipmentProgression(ShipmentStatus shipmentStatus) =>
        shipmentStatus is ShipmentStatus.PickedUp
            or ShipmentStatus.InTransit
            or ShipmentStatus.OutForDelivery
            or ShipmentStatus.Delivered;

    public static bool IsOutboundOrderProgression(OrderStatus orderStatus) =>
        orderStatus is OrderStatus.Confirmed
            or OrderStatus.Processing
            or OrderStatus.Shipped
            or OrderStatus.Delivered
            or OrderStatus.Completed;

    private static bool HasRecognizedCollectionAuthority(PaymentStatus paymentStatus) =>
        paymentStatus == PaymentStatus.SyntheticQaCollected ||
        PaymentStatusAuthority.IsPaidState(paymentStatus) ||
        paymentStatus == PaymentStatus.PartiallyRefunded;
}
