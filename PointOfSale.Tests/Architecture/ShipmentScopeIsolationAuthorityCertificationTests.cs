namespace PointOfSale.Tests.Architecture;

public sealed class ShipmentScopeIsolationAuthorityCertificationTests
{
    [Fact]
    public void CustomerAndAdminShipmentReads_MustDeriveScopeFromCurrentBackendAuthority()
    {
        var storefront = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/StorefrontShipmentController.cs");
        var admin = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Admin/AdminShipmentService.cs");

        Assert.Contains("order.TenantId == tenantId", storefront, StringComparison.Ordinal);
        Assert.Contains("order.OnlineCustomerId == customerId", storefront, StringComparison.Ordinal);
        Assert.Contains("s.TenantId == tenantId && s.Id == shipmentId", admin, StringComparison.Ordinal);
        Assert.Contains("s => s.TenantId == tenantId", admin, StringComparison.Ordinal);
    }

    [Fact]
    public void CarrierAndRecoveryPaths_MustExcludeDeletedAndForeignShipmentScope()
    {
        var carrier = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs");
        var webhook = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Shipping/ShippingWebhookRequestPipeline.cs");
        var recovery = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Shipping/ShipmentAuthorityRecoveryHostedService.cs");

        Assert.Contains("s.TenantId == resolvedTenantId", carrier, StringComparison.Ordinal);
        Assert.Contains("s.TrackingNumber == trackingNumber", carrier, StringComparison.Ordinal);
        Assert.Contains("!s.IsDeleted", carrier, StringComparison.Ordinal);
        Assert.Contains("s.Id == shipmentId && s.CarrierCode == carrierCode && !s.IsDeleted", webhook, StringComparison.Ordinal);
        Assert.Contains("s.CarrierCode == carrierCode && s.TrackingNumber == trackingNumber && !s.IsDeleted", webhook, StringComparison.Ordinal);
        Assert.Contains("tenantContext.SetTenantId(candidate.TenantId)", recovery, StringComparison.Ordinal);
        Assert.DoesNotContain("shipment.Status = ShipmentStatus", recovery, StringComparison.Ordinal);
    }

    [Fact]
    public void FrontendAndCertificationRecord_MustNotCreateShipmentScopeAuthority()
    {
        var shipmentHook = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/shipping/hooks/useShipmentsApi.js");
        var storefrontHook = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/storefront/shipping/hooks/useShippingApi.js");
        var findings = CertificationSource.ReadRepoFile("docs/architecture/shipment-scope-isolation-authority-certification-findings.md");

        Assert.DoesNotContain("localStorage", shipmentHook, StringComparison.Ordinal);
        Assert.DoesNotContain("sessionStorage", shipmentHook, StringComparison.Ordinal);
        Assert.Contains("requireBackendShipmentIdentity(response.data, orderId)", storefrontHook, StringComparison.Ordinal);
        Assert.Contains("PASS M7-G Shipment Scope Isolation Authority", findings, StringComparison.Ordinal);
        Assert.Contains("production-certifiable", findings, StringComparison.Ordinal);
    }
}
