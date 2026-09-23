using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Observability;

namespace PointOfSale.Tests.Infrastructure;

public sealed class InventoryReservationReleaseAuthorityTests
{
    [Fact]
    public void Processing_commerce_hub_cod_order_is_not_actionable_stale_reservation()
    {
        var now = DateTime.UtcNow;

        var canRelease = InventoryReservationReleaseAuthority.CanReleaseCommerceHubReservation(
            OrderStatus.Processing,
            PaymentStatus.CashOnDelivery,
            latestPaymentStatus: null,
            latestPaymentCreatedAtUtc: null,
            latestPaymentProviderIntentId: null,
            latestPaymentProviderTransactionId: null,
            latestPaymentFailureReason: null,
            hasBlockingUnresolvedWebhook: false,
            activePaymentCutoff: now.AddMinutes(-15),
            providerReconciliationCutoff: now.AddMinutes(-120),
            hasExpiredReservation: true);

        Assert.False(canRelease);
    }

    [Fact]
    public void Cancelled_commerce_hub_order_is_actionable_even_when_cod()
    {
        var now = DateTime.UtcNow;

        var canRelease = InventoryReservationReleaseAuthority.CanReleaseCommerceHubReservation(
            OrderStatus.Cancelled,
            PaymentStatus.CashOnDelivery,
            latestPaymentStatus: null,
            latestPaymentCreatedAtUtc: null,
            latestPaymentProviderIntentId: null,
            latestPaymentProviderTransactionId: null,
            latestPaymentFailureReason: null,
            hasBlockingUnresolvedWebhook: false,
            activePaymentCutoff: now.AddMinutes(-15),
            providerReconciliationCutoff: now.AddMinutes(-120),
            hasExpiredReservation: true);

        Assert.True(canRelease);
    }

    [Fact]
    public void Processing_pos_order_is_not_actionable_stale_reservation()
    {
        var canRelease = InventoryReservationReleaseAuthority.CanReleaseOrderReservation(
            OrderStatus.Processing,
            PaymentStatus.Pending,
            latestPaymentIntentStatus: null,
            latestPaymentIntentCreatedAtUtc: null,
            activePaymentCutoff: DateTime.UtcNow.AddMinutes(-15),
            hasExpiredReservation: true);

        Assert.False(canRelease);
    }
}
