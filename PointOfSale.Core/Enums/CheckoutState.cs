namespace PointOfSale.Core.Enums;

/// <summary>
/// Explicit checkout lifecycle for e-commerce orders.
/// This is the authoritative state machine for order placement and payment progression.
/// </summary>
public enum CheckoutState
{
    Draft = 0,
    PendingPaymentIntent = 1,
    PaymentPending = 2,
    Paid = 3,
    Failed = 4,
    Cancelled = 5,
    Expired = 6
}
