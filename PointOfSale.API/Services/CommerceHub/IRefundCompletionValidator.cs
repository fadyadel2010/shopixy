using PointOfSale.Core.Entities;

namespace PointOfSale.API.Services.CommerceHub;

/// <summary>
/// Validates that refund completion does not exceed sub-order or payment totals.
/// Ensures single-sourced ceiling logic for CommerceHubSubOrder, CommerceHubPayment, and CommerceHubRefund.
/// </summary>
public interface IRefundCompletionValidator
{
    /// <summary>
    /// Validates that completing the specified refund will not exceed the sub-order total
    /// or the associated payment amount (if applicable).
    /// </summary>
    /// <param name="subOrder">The sub-order being refunded</param>
    /// <param name="payment">The payment associated with the refund (nullable for non-payment refunds)</param>
    /// <param name="trackedRefund">The refund being validated</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <exception cref="CommerceHubCheckoutException">
    /// Thrown when refund would exceed sub-order total (REFUND_COMPLETION_LIMIT_EXCEEDED)
    /// or payment amount (REFUND_PAYMENT_COMPLETION_LIMIT_EXCEEDED)
    /// </exception>
    Task ValidateRefundDoesNotExceedTotalsAsync(
        CommerceHubSubOrder subOrder,
        CommerceHubPayment? payment,
        CommerceHubRefund trackedRefund,
        CancellationToken cancellationToken);
}
