using PointOfSale.Core.Enums;
using PointOfSale.Core.Payments;

namespace PointOfSale.API.Services.Payments;

/// <summary>
/// Maps internal order/payment state into the storefront-safe payment contract.
/// Only PAID is a success state.
/// </summary>
public static class CanonicalPaymentStatusResolver
{
    public const string Pending = "PENDING";
    public const string Processing = "PROCESSING";
    public const string ManualReview = "MANUAL_REVIEW";
    public const string Failed = "FAILED";
    public const string Paid = "PAID";

    public static bool IsPaidState(PaymentStatus paymentStatus)
    {
        return PaymentStatusAuthority.IsPaidState(paymentStatus);
    }

    public static string Resolve(PaymentStatus paymentStatus, PaymentIntentStatus? paymentIntentStatus = null)
    {
        // Check terminal states first to prevent manual review from overriding settled/failed states
        if (IsFailed(paymentStatus, paymentIntentStatus))
        {
            return Failed;
        }

        if (IsPaid(paymentStatus))
        {
            return Paid;
        }

        if (IsManualReview(paymentStatus, paymentIntentStatus))
        {
            return ManualReview;
        }

        if (IsProcessing(paymentStatus, paymentIntentStatus))
        {
            return Processing;
        }

        return Pending;
    }

    private static bool IsManualReview(PaymentStatus paymentStatus, PaymentIntentStatus? paymentIntentStatus)
    {
        if (paymentStatus == PaymentStatus.AmountMismatch)
        {
            return true;
        }

        return paymentIntentStatus is PaymentIntentStatus.Suspicious
            or PaymentIntentStatus.AmountMismatch;
    }

    private static bool IsPaid(PaymentStatus paymentStatus)
    {
        // Don't treat as paid if in terminal failure/refund state
        if (paymentStatus is PaymentStatus.Failed 
            or PaymentStatus.Rejected
            or PaymentStatus.AmountMismatch
            or PaymentStatus.Refunded
            or PaymentStatus.PartiallyRefunded)
        {
            return false;
        }

        // PaymentIntent is provider-attempt evidence. Only canonical Order.PaymentStatus
        // can authorize a paid response to storefront consumers.
        return IsPaidState(paymentStatus);
    }

    private static bool IsFailed(PaymentStatus paymentStatus, PaymentIntentStatus? paymentIntentStatus)
    {
        // Don't treat as failed if payment is already settled
        if (IsPaidState(paymentStatus))
        {
            return false;
        }

        // AmountMismatch is quarantined external-money authority, not safe terminal failure.
        if (paymentStatus is PaymentStatus.Failed 
            or PaymentStatus.Rejected
            or PaymentStatus.Refunded
            or PaymentStatus.PartiallyRefunded)
        {
            return true;
        }

        return paymentIntentStatus is PaymentIntentStatus.Failed
            or PaymentIntentStatus.Cancelled
            or PaymentIntentStatus.Expired
            or PaymentIntentStatus.Rejected;
    }

    private static bool IsProcessing(PaymentStatus paymentStatus, PaymentIntentStatus? paymentIntentStatus)
    {
        if (paymentStatus is PaymentStatus.Authorized or PaymentStatus.AwaitingVerification)
        {
            return true;
        }

        return paymentIntentStatus is PaymentIntentStatus.Authorized
            or PaymentIntentStatus.AwaitingVerification;
    }
}
