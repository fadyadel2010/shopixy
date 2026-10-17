namespace PointOfSale.Tests.Architecture;

public sealed class ShipmentIdentityAuthorityCertificationTests
{
    [Fact]
    public void ShipmentIdentity_MustRemainServerGeneratedTenantBoundAndReplaySafe()
    {
        var shipmentService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Shipping/ShipmentService.cs");
        var carrierService = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs");
        var dbContext = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        Assert.Contains("IsolationLevel.Serializable", shipmentService, StringComparison.Ordinal);
        Assert.Contains("Id = Guid.NewGuid()", shipmentService, StringComparison.Ordinal);
        Assert.Contains("ShipmentAlreadyExistsException", shipmentService, StringComparison.Ordinal);
        Assert.Contains("Id = shipmentId", carrierService, StringComparison.Ordinal);
        Assert.Contains("entity.HasIndex(e => new { e.TenantId, e.OrderId })", dbContext, StringComparison.Ordinal);
        Assert.Contains("entity.HasIndex(e => new { e.TenantId, e.CarrierCode, e.ExternalReferenceId })", dbContext, StringComparison.Ordinal);
        Assert.Contains("entity.HasIndex(e => new { e.TenantId, e.CarrierCode, e.CarrierRequestId })", dbContext, StringComparison.Ordinal);
    }

    [Fact]
    public void TrackingFulfillmentAndRecovery_MustResolveExistingSqlShipmentLineage()
    {
        var carrierService = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs");
        var recovery = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Shipping/ShipmentAuthorityRecoveryHostedService.cs");
        var fulfillment = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Fulfillment/OrderFulfillmentService.cs");

        Assert.Contains("s.Id == shipmentId && s.TenantId == tenantId", carrierService, StringComparison.Ordinal);
        Assert.Contains("s.TrackingNumber == trackingNumber", carrierService, StringComparison.Ordinal);
        Assert.Contains("matchingShipments.Count > 1", carrierService, StringComparison.Ordinal);
        Assert.Contains("ExecuteCarrierForExistingShipmentAsync", recovery, StringComparison.Ordinal);
        Assert.Contains("ReconcileShipmentAuthorityAsync", recovery, StringComparison.Ordinal);
        Assert.Contains("SynchronizeFromShipmentAsync", fulfillment, StringComparison.Ordinal);
    }

    [Fact]
    public void Frontend_MustRejectFabricatedOrCrossOrderShipmentEvidence()
    {
        var authority = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/storefront/shipping/shipmentIdentityAuthority.js");
        var shippingHook = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/storefront/shipping/hooks/useShippingApi.js");
        var trackingPage = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/storefront/shipping/pages/OrderTrackingPage.jsx");

        Assert.Contains("requireBackendShipmentIdentity(response.data, orderId)", shippingHook, StringComparison.Ordinal);
        Assert.Contains("requireBackendTrackedOrder(response.data)", shippingHook, StringComparison.Ordinal);
        Assert.Contains("Shipment evidence does not belong to the requested order.", authority, StringComparison.Ordinal);
        Assert.Contains("Shipment evidence has conflicting shipment identities.", authority, StringComparison.Ordinal);
        Assert.DoesNotContain("shipment={{", trackingPage, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationArtifacts_MustRecordM7AShipmentIdentityTruth()
    {
        Assert.Contains("PASS M7-A Shipment Identity Authority", CertificationSource.ReadRepoFile("docs/architecture/shipment-identity-authority-certification-findings.md"), StringComparison.Ordinal);

        HistoricalShippingCertificationBoundary.AssertPriorClosureConditional();
    }
}
