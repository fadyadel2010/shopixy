namespace PointOfSale.Tests.Architecture;

public sealed class ShippingMethodEligibilityAuthorityCertificationTests
{
    [Fact]
    public void CheckoutAndCommerceHub_MustResolveOnlyCurrentActiveTenantMethods()
    {
        var checkout = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var commerceHub = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubShippingService.cs");
        var cart = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCartService.cs");
        var commerceHubCheckout = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");

        Assert.Contains("method.TenantId == tenantId", checkout, StringComparison.Ordinal);
        Assert.Contains("!method.IsDeleted", checkout, StringComparison.Ordinal);
        Assert.Contains("if (!shippingMethod.IsActive)", checkout, StringComparison.Ordinal);
        Assert.Contains("Carrier is not currently available", checkout, StringComparison.Ordinal);
        Assert.Contains("!method.IsDeleted && method.IsActive && method.TenantId == storeId", commerceHub, StringComparison.Ordinal);
        Assert.Contains("_dbContext.Set<CarrierConfiguration>().Any", commerceHub, StringComparison.Ordinal);
        Assert.Contains("bypassCache: true", cart, StringComparison.Ordinal);
        Assert.Contains("bypassCache: true", commerceHubCheckout, StringComparison.Ordinal);
        Assert.Contains("SHIPPING_CHANGED", commerceHubCheckout, StringComparison.Ordinal);
    }

    [Fact]
    public void ShipmentCreation_MustUseTheOrderBoundCurrentEligibleMethod()
    {
        var shipment = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Shipping/ShipmentService.cs");
        var carrierShipment = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs");

        Assert.Contains("order.ShippingMethodId.Value != shippingMethodId", shipment, StringComparison.Ordinal);
        Assert.Contains("sm.TenantId == tenantId", shipment, StringComparison.Ordinal);
        Assert.Contains("!sm.IsDeleted", shipment, StringComparison.Ordinal);
        Assert.Contains("!shippingMethod.IsActive", shipment, StringComparison.Ordinal);
        Assert.Contains("_dbContext.Set<CarrierConfiguration>()", shipment, StringComparison.Ordinal);
        Assert.Contains("ORDER_SHIPPING_METHOD_INELIGIBLE", carrierShipment, StringComparison.Ordinal);
        Assert.Contains("method.Id == order.ShippingMethodId.Value", carrierShipment, StringComparison.Ordinal);
        Assert.Contains("method.TenantId == tenantId", carrierShipment, StringComparison.Ordinal);
        Assert.Contains("!method.IsDeleted", carrierShipment, StringComparison.Ordinal);
        Assert.Contains("method.IsActive", carrierShipment, StringComparison.Ordinal);
        Assert.Contains("ORDER_SHIPPING_METHOD_CARRIER_MISMATCH", carrierShipment, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectionsAndCache_MustRemainTenantScopedSqlDerivedAndReplaySafe()
    {
        var projection = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Consumers/ShippingMethodProjectionConsumer.cs");
        var repository = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/MongoDB/Services/ShippingMethodReadRepository.cs");
        var storefront = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Storefront/ShippingMethodService.cs");

        Assert.Contains("x.Id == @event.ShippingMethodId && x.TenantId == @event.TenantId.Value", projection, StringComparison.Ordinal);
        Assert.Contains("ProjectionEventFence.TryAcquireAsync", projection, StringComparison.Ordinal);
        Assert.Contains("TombstoneIfFreshAsync", projection, StringComparison.Ordinal);
        Assert.Contains("BuildFreshnessFilter", repository, StringComparison.Ordinal);
        Assert.Contains("x.TenantId == tenantId && x.IsActive && !x.IsDeleted", repository, StringComparison.Ordinal);
        Assert.Contains("GetActiveShippingMethodsAsync(tenantId", storefront, StringComparison.Ordinal);
    }

    [Fact]
    public void WarehouseSelectedStorefrontReads_MustConsumeWarehouseCarrierAssignments()
    {
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/StorefrontShippingMethodsController.cs");
        var service = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Storefront/ShippingMethodService.cs");
        var runtimeEntities = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/ShippingAdministration/CarrierAccountRuntimeEntities.cs");
        var adminRuntime = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/Administration/CarrierAccountAdministrationRuntime.cs");

        Assert.Contains("[FromQuery] Guid? warehouseId", controller, StringComparison.Ordinal);
        Assert.Contains("warehouseId,", controller, StringComparison.Ordinal);
        Assert.Contains("ApplyWarehouseAssignmentFilterAsync", service, StringComparison.Ordinal);
        Assert.Contains("CarrierWarehouseAssignment", service, StringComparison.Ordinal);
        Assert.Contains("assignment.WarehouseId == warehouseId.Value", service, StringComparison.Ordinal);
        Assert.Contains("assignment.IsEnabled", service, StringComparison.Ordinal);
        Assert.Contains("assignment.OriginReadinessPublished", service, StringComparison.Ordinal);
        Assert.Contains("account.State == CarrierAccountState.Active", service, StringComparison.Ordinal);
        Assert.Contains("account.OperationalState == CarrierOperationalState.Ready", service, StringComparison.Ordinal);
        Assert.Contains("return new List<ShippingMethodDocument>();", service, StringComparison.Ordinal);
        Assert.Contains("public sealed class CarrierWarehouseAssignment", runtimeEntities, StringComparison.Ordinal);
        Assert.Contains("warehouse.TenantId == tenantId", adminRuntime, StringComparison.Ordinal);
        Assert.Contains("warehouse.IsActive", adminRuntime, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationArtifact_MustRecordM8CShippingMethodEligibilityTruth()
    {
        Assert.Contains(
            "PASS M8-C Shipping Method Eligibility Authority",
            CertificationSource.ReadRepoFile("docs/architecture/shipping-method-eligibility-authority-certification-findings.md"),
            StringComparison.Ordinal);
    }
}
