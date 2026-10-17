using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Policies;

/// <summary>Reservation age is not cancellation or payment authority for a live COD order.</summary>
public static class CodReservationExpiryPolicy
{
    public static bool ProtectsLiveOrder(string? paymentCode, PaymentStatus paymentStatus, OrderStatus orderStatus) =>
        orderStatus is not (OrderStatus.Cancelled or OrderStatus.Refunded) &&
        (string.Equals(paymentCode?.Trim(), "COD", StringComparison.OrdinalIgnoreCase) ||
         paymentStatus == PaymentStatus.CashOnDelivery);
}
