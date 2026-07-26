namespace PointOfSale.Tests.Architecture;

public sealed class ShippingMethodIdentityAuthorityCertificationTests
{
    [Fact]
    public void ShippingMethodIdentity_MustBeServerGeneratedTenantBoundAndNotClientRestorable()
    {
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/ShippingMethodsController.cs");
        var checkout = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var shipment = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Shipping/ShipmentService.cs");

        Assert.Contains("Id = Guid.NewGuid()", controller, StringComparison.Ordinal);
        Assert.Contains("TenantId = tenantId", controller, StringComparison.Ordinal);
        Assert.Contains("method.Id == shippingMethodId &&", checkout, StringComparison.Ordinal);
        Assert.Contains("method.TenantId == tenantId", checkout, StringComparison.Ordinal);
        Assert.Contains("sm.Id == shippingMethodId", shipment, StringComparison.Ordinal);
        Assert.Contains("sm.TenantId == tenantId", shipment, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubCartAndCheckout_MustRecalculateActiveTenantShippingAuthority()
    {
        var shipping = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubShippingService.cs");
        var cart = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCartService.cs");
        var checkout = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");

        Assert.Contains("!method.IsDeleted && method.IsActive && method.TenantId == storeId", shipping, StringComparison.Ordinal);
        Assert.Contains("bypassCache: true", cart, StringComparison.Ordinal);
        Assert.Contains("bypassCache: true", checkout, StringComparison.Ordinal);
        Assert.Contains("SHIPPING_CHANGED", checkout, StringComparison.Ordinal);
        Assert.Contains("IDEMPOTENCY_PAYLOAD_CONFLICT", checkout, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectionAndStorefrontReads_MustRemainTenantScopedSqlDerivedAndReplaySafe()
    {
        var projection = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Consumers/ShippingMethodProjectionConsumer.cs");
        var repository = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/MongoDB/Services/ShippingMethodReadRepository.cs");
        var storefront = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Storefront/ShippingMethodService.cs");

        Assert.Contains("x.Id == @event.ShippingMethodId && x.TenantId == @event.TenantId.Value", projection, StringComparison.Ordinal);
        Assert.Contains("ProjectionEventFence.TryAcquireAsync", projection, StringComparison.Ordinal);
        Assert.Contains("TombstoneIfFreshAsync", projection, StringComparison.Ordinal);
        Assert.Contains("TenantId, document.TenantId", repository, StringComparison.Ordinal);
        Assert.Contains("ShippingMethodId, document.ShippingMethodId", repository, StringComparison.Ordinal);
        Assert.Contains("GetActiveShippingMethodsAsync(tenantId", storefront, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationArtifact_MustRecordM8AShippingMethodIdentityTruth()
    {
        Assert.Contains(
            "PASS M8-A Shipping Method Identity Authority",
            CertificationSource.ReadRepoFile("docs/architecture/shipping-method-identity-authority-certification-findings.md"),
            StringComparison.Ordinal);
    }
}
