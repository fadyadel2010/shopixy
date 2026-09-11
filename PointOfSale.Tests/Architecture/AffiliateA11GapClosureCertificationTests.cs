using PointOfSale.API.Events;
using PointOfSale.API.Services.Checkout;

namespace PointOfSale.Tests.Architecture;

public sealed class AffiliateA11GapClosureCertificationTests
{
    [Fact]
    public void PaymentMethodProjection_MustUseSqlDerivedEventValueAcrossPlaceholderPaths()
    {
        var attributionConsumer = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/Consumers/Affiliate/AffiliateOrderAttributionConsumer.cs");
        var checkout = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var projectionConsumers = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/Consumers/Affiliate/AffiliateProjectionConsumers.cs");
        var document = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/MongoDB/Documents/Affiliate/AffiliateDocuments.cs");
        var portal = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Controllers/AffiliatePortalController.cs");
        var ordersUi = CertificationSource.ReadRepoFile(
            "PointOfSale.Web/src/modules/affiliate/pages/portal/OrdersTab.jsx");

        Assert.Contains("PaymentMethodCode = order.PaymentMethodCode", checkout, StringComparison.Ordinal);
        Assert.Contains(".Set(d => d.PaymentMethod, evt.PaymentMethodCode)", attributionConsumer, StringComparison.Ordinal);
        Assert.Contains(".Set(d => d.PaymentMethod, evt.PaymentMethodCode)", projectionConsumers, StringComparison.Ordinal);
        Assert.Contains("[BsonElement(\"paymentMethod\")]", document, StringComparison.Ordinal);
        Assert.Contains("PaymentMethod = d.PaymentMethod", portal, StringComparison.Ordinal);
        Assert.Contains("order.paymentMethod", ordersUi, StringComparison.Ordinal);
    }

    [Fact]
    public void InventoryReservedEvent_RequiresAtLeastOnePhysicalReservation()
    {
        Assert.False(CheckoutService.HasPhysicalInventoryReservation([]));
        Assert.True(CheckoutService.HasPhysicalInventoryReservation(
        [
            new InventoryReservationItem
            {
                ProductId = Guid.NewGuid(),
                VariantId = Guid.NewGuid(),
                SKU = "TRACKED-SKU",
                Quantity = 1
            }
        ]));
    }

    [Fact]
    public void InventoryReservationOutbox_MustBeGuardedAndCommittedWithOrderTransaction()
    {
        var checkout = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/Checkout/CheckoutService.cs");

        Assert.Contains(
            "emitExternalReservationSideEffects && HasPhysicalInventoryReservation(reservedItems)",
            checkout,
            StringComparison.Ordinal);
        Assert.Contains(
            "if (HasPhysicalInventoryReservation(inventoryReservation.ReservedItems))",
            checkout,
            StringComparison.Ordinal);
        Assert.Contains(
            "inventoryReservation != null && HasPhysicalInventoryReservation(inventoryReservation.ReservedItems)",
            checkout,
            StringComparison.Ordinal);
        Assert.True(
            checkout.IndexOf("PersistInventoryReservedOutboxAsync(", StringComparison.Ordinal) <
            checkout.IndexOf("await transaction.CommitAsync(cancellationToken)", StringComparison.Ordinal));
    }

    [Fact]
    public void GuestReplayContract_ReissuesSecureCookieButDoesNotExposeTokenInJson()
    {
        var checkoutService = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var controller = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Controllers/CheckoutController.cs");

        Assert.Contains("return await EnrichReplayResponseWithFreshTokensAsync(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("response.TrackingToken = ShouldIssueGuestTrackingToken(order)", checkoutService, StringComparison.Ordinal);
        Assert.Contains("GuestOrderAccessCookie.SetTrackingToken(", controller, StringComparison.Ordinal);
        Assert.Contains("response.TrackingToken = null;", controller, StringComparison.Ordinal);
    }
}
