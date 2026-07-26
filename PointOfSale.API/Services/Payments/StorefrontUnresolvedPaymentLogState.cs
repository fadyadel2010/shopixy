using PointOfSale.Core.Entities;

namespace PointOfSale.API.Services.Payments;

internal static class StorefrontUnresolvedPaymentLogState
{
    private const string RoutedToSuspenseMarker = "UNRESOLVED_SUSPENSE_ROUTED";
    private const string ClearedToOrderMarker = "UNRESOLVED_SUSPENSE_CLEARED";
    private const string ManualReviewMarker = "UNRESOLVED_SUSPENSE_MANUAL_REVIEW";

    public static string RoutedToSuspense(string? reference) =>
        string.IsNullOrWhiteSpace(reference)
            ? RoutedToSuspenseMarker
            : $"{RoutedToSuspenseMarker}|{reference}";

    public static string ClearedToOrder(Guid orderId, string orderNumber) =>
        $"{ClearedToOrderMarker}|{orderId:D}|{orderNumber}";

    public static string ManualReview(string failureCode, string? message) =>
        string.IsNullOrWhiteSpace(message)
            ? $"{ManualReviewMarker}|{failureCode}"
            : $"{ManualReviewMarker}|{failureCode}|{message}";

    public static bool IsSuspenseRouted(PaymentWebhookLog webhookLog) =>
        HasMarker(webhookLog.ProcessingError, RoutedToSuspenseMarker) ||
        string.Equals(
            webhookLog.ProcessingError,
            "Payment intent not found - Routed to Suspense Ledger",
            StringComparison.Ordinal);

    public static bool IsCleared(PaymentWebhookLog webhookLog) =>
        HasMarker(webhookLog.ProcessingError, ClearedToOrderMarker);

    private static bool HasMarker(string? value, string marker) =>
        !string.IsNullOrWhiteSpace(value) &&
        (string.Equals(value, marker, StringComparison.Ordinal) ||
         value.StartsWith(marker + "|", StringComparison.Ordinal));
}
