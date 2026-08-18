using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces.Payments;

namespace PointOfSale.API.Services.Payments;

internal static class StorefrontSuspenseRecoveryPolicy
{
    public static void ApplyManualReviewMarker(
        PaymentTransitionResult lifecycleResult,
        PaymentWebhookLog webhookLog,
        string? paymentReference)
    {
        if (!ShouldRouteToSuspenseRecovery(lifecycleResult))
        {
            return;
        }

        webhookLog.ProcessingError = string.Equals(
            lifecycleResult.FailureCode,
            "SUPERSEDED_PAYMENT_ATTEMPT_SUCCESS_REJECTED",
            StringComparison.Ordinal)
            ? StorefrontUnresolvedPaymentLogState.ManualReview(
                lifecycleResult.FailureCode,
                lifecycleResult.ErrorMessage)
            : StorefrontUnresolvedPaymentLogState.RoutedToSuspense(paymentReference);
    }

    public static bool ShouldRouteToSuspenseRecovery(PaymentTransitionResult lifecycleResult) =>
        lifecycleResult.RequiresManualReview &&
        (lifecycleResult.NewStatus is PaymentStatus.Expired or PaymentStatus.AmountMismatch ||
         string.Equals(
             lifecycleResult.FailureCode,
             "SUPERSEDED_PAYMENT_ATTEMPT_SUCCESS_REJECTED",
             StringComparison.Ordinal));
}
