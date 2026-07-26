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

        webhookLog.ProcessingError = StorefrontUnresolvedPaymentLogState.RoutedToSuspense(paymentReference);
    }

    public static bool ShouldRouteToSuspenseRecovery(PaymentTransitionResult lifecycleResult) =>
        lifecycleResult.RequiresManualReview &&
        lifecycleResult.NewStatus is PaymentStatus.Expired or PaymentStatus.AmountMismatch;
}
