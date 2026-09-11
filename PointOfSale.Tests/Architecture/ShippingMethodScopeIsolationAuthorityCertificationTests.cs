namespace PointOfSale.Tests.Architecture;

public sealed class ShippingMethodScopeIsolationAuthorityCertificationTests
{
    [Fact]
    public void StorefrontCache_MustCarryAndValidateCurrentTenantScope()
    {
        var dto = CertificationSource.ReadRepoFile("PointOfSale.API/DTOs/Storefront/ShippingMethodDto.cs");
        var service = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Storefront/ShippingMethodService.cs");

        Assert.Contains("public Guid TenantId { get; set; }", dto, StringComparison.Ordinal);
        Assert.Contains("TenantId = doc.TenantId", service, StringComparison.Ordinal);
        Assert.Contains("TryValidateCachedTenantScope(tenantId, cached", service, StringComparison.Ordinal);
        Assert.Contains("method.TenantId == tenantId", service, StringComparison.Ordinal);
        Assert.Contains("tenant_scope_mismatch", service, StringComparison.Ordinal);
        Assert.Contains("missing_tenant_scope", service, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubAndSplitOrders_MustBindShippingScopeToCurrentStoreSqlAuthority()
    {
        var shipping = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubShippingService.cs");
        var checkout = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");
        var boundary = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/CommerceHubBoundaryMutationScope.cs");

        Assert.Contains("!method.IsDeleted && method.IsActive && method.TenantId == storeId", shipping, StringComparison.Ordinal);
        Assert.Contains("_carrierIntegrationAuthority.GetIntegrationReadinessAsync", shipping, StringComparison.Ordinal);
        Assert.Contains("method.TenantId,", shipping, StringComparison.Ordinal);
        Assert.Contains("method.Id,", shipping, StringComparison.Ordinal);
        Assert.Contains("bypassCache: true", checkout, StringComparison.Ordinal);
        Assert.Contains("requestedStoreSelection.ShippingOptionId != storeSelection.ShippingOptionId", checkout, StringComparison.Ordinal);
        Assert.Contains("StoreId = group.Key", checkout, StringComparison.Ordinal);
        Assert.Contains("ShippingMethodId = shippingSelection.ShippingOptionId", checkout, StringComparison.Ordinal);
        Assert.Contains("BeginShippingAssignment", boundary, StringComparison.Ordinal);
    }

    [Fact]
    public void ShipmentAndCarrierRegistration_MustUseOrderBoundTenantMethodScope()
    {
        var shipment = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Shipping/ShipmentService.cs");
        var carrierShipment = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs");

        Assert.Contains("order.ShippingMethodId.Value != shippingMethodId", shipment, StringComparison.Ordinal);
        Assert.Contains("sm.TenantId == tenantId", shipment, StringComparison.Ordinal);
        Assert.Contains("configuration.TenantId == tenantId", shipment, StringComparison.Ordinal);
        Assert.Contains("method.Id == order.ShippingMethodId.Value", carrierShipment, StringComparison.Ordinal);
        Assert.Contains("method.TenantId == tenantId", carrierShipment, StringComparison.Ordinal);
        Assert.Contains("ORDER_SHIPPING_METHOD_CARRIER_MISMATCH", carrierShipment, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectionAndRecovery_MustRemainTenantKeyedAndFreshnessFenced()
    {
        var projection = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Consumers/ShippingMethodProjectionConsumer.cs");
        var repository = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/MongoDB/Services/ShippingMethodReadRepository.cs");
        var adminSync = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AdminDataSyncController.cs");

        Assert.Contains("x.Id == @event.ShippingMethodId && x.TenantId == @event.TenantId.Value", projection, StringComparison.Ordinal);
        Assert.Contains("ProjectionEventFence.TryAcquireAsync", projection, StringComparison.Ordinal);
        Assert.Contains("TombstoneIfFreshAsync", projection, StringComparison.Ordinal);
        Assert.Contains("Builders<ShippingMethodDocument>.Filter.Eq(x => x.TenantId, document.TenantId)", repository, StringComparison.Ordinal);
        Assert.Contains("BuildFreshnessFilter", repository, StringComparison.Ordinal);
        Assert.Contains("Builders<ShippingMethodDocument>.Filter.Eq(d => d.TenantId, shippingMethod.TenantId)", adminSync, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationArtifact_MustRecordM8GShippingMethodScopeTruth()
    {
        Assert.Contains(
            "PASS M8-G Shipping Method Scope Isolation Authority",
            CertificationSource.ReadRepoFile("docs/architecture/shipping-method-scope-isolation-authority-certification-findings.md"),
            StringComparison.Ordinal);
    }
}
