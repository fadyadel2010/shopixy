using PointOfSale.Core.Enums;

namespace PointOfSale.Infrastructure.Observability;

/// <summary>
/// Classifies aged reservations using the same lifecycle protections as cleanup.
/// Readiness must not treat reservations that cleanup is required to retain as stale.
/// </summary>
public static class InventoryReservationReleaseAuthority
{
    internal static readonly string[] BlockingUnresolvedPaymentStatuses =
    [
        "PendingResolution",
        "PendingTenantResolution",
        "RoutedToSuspense",
        "SuspenseMatchedPaymentPendingClearance"
    ];

    public static bool CanReleaseOrderReservation(
        OrderStatus? orderStatus,
        PaymentStatus? paymentStatus,
        PaymentIntentStatus? latestPaymentIntentStatus,
        DateTime? latestPaymentIntentCreatedAtUtc,
        DateTime activePaymentCutoff,
        bool hasExpiredReservation)
    {
        if (!orderStatus.HasValue)
        {
            return true;
        }

        if (orderStatus.Value is OrderStatus.Confirmed
            or OrderStatus.Processing
            or OrderStatus.Shipped
            or OrderStatus.Delivered
            or OrderStatus.Completed
            or OrderStatus.OnHold)
        {
            return false;
        }

        if (paymentStatus is PaymentStatus.Collected or PaymentStatus.Paid or PaymentStatus.Settled ||
            latestPaymentIntentStatus == PaymentIntentStatus.Paid)
        {
            return false;
        }

        var hasRecentlyActivePaymentIntent =
            latestPaymentIntentStatus is PaymentIntentStatus.Pending or PaymentIntentStatus.Authorized or PaymentIntentStatus.AwaitingVerification &&
            latestPaymentIntentCreatedAtUtc.HasValue &&
            latestPaymentIntentCreatedAtUtc.Value >= activePaymentCutoff;

        return !hasRecentlyActivePaymentIntent || hasExpiredReservation;
    }

    public static bool CanReleaseCommerceHubReservation(
        OrderStatus? orderStatus,
        PaymentStatus? paymentStatus,
        CommerceHubPaymentRecordStatus? latestPaymentStatus,
        DateTime? latestPaymentCreatedAtUtc,
        string? latestPaymentProviderIntentId,
        string? latestPaymentProviderTransactionId,
        string? latestPaymentFailureReason,
        bool hasBlockingUnresolvedWebhook,
        DateTime activePaymentCutoff,
        DateTime providerReconciliationCutoff,
        bool hasExpiredReservation)
    {
        if (!orderStatus.HasValue)
        {
            return true;
        }

        if (orderStatus.Value is OrderStatus.Confirmed
            or OrderStatus.Processing
            or OrderStatus.Shipped
            or OrderStatus.Delivered
            or OrderStatus.Completed
            or OrderStatus.OnHold)
        {
            return false;
        }

        if (orderStatus.Value is OrderStatus.Cancelled or OrderStatus.Refunded)
        {
            return true;
        }

        if (paymentStatus is PaymentStatus.Collected
            or PaymentStatus.Paid
            or PaymentStatus.Settled
            or PaymentStatus.CashOnDelivery ||
            latestPaymentStatus == CommerceHubPaymentRecordStatus.Paid ||
            hasBlockingUnresolvedWebhook)
        {
            return false;
        }

        var hasProviderCashAuthorityGap =
            latestPaymentStatus == CommerceHubPaymentRecordStatus.Pending &&
            latestPaymentCreatedAtUtc.HasValue &&
            latestPaymentCreatedAtUtc.Value >= providerReconciliationCutoff &&
            (!string.IsNullOrWhiteSpace(latestPaymentProviderIntentId) ||
             !string.IsNullOrWhiteSpace(latestPaymentProviderTransactionId) ||
             IsProviderSessionAuthorityUncertain(latestPaymentFailureReason));
        if (hasProviderCashAuthorityGap)
        {
            return false;
        }

        var hasRecentlyActivePayment =
            latestPaymentStatus == CommerceHubPaymentRecordStatus.Pending &&
            latestPaymentCreatedAtUtc.HasValue &&
            latestPaymentCreatedAtUtc.Value >= activePaymentCutoff;

        return !hasRecentlyActivePayment || hasExpiredReservation;
    }

    private static bool IsProviderSessionAuthorityUncertain(string? failureReason) =>
        !string.IsNullOrWhiteSpace(failureReason) &&
        (failureReason.StartsWith("PROVIDER_SESSION_OUTCOME_UNKNOWN:", StringComparison.Ordinal) ||
         failureReason.StartsWith("PROVIDER_SESSION_COMPENSATION_REQUIRED:", StringComparison.Ordinal));
}
