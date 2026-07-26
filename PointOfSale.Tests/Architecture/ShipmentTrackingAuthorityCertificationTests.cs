namespace PointOfSale.Tests.Architecture;

public sealed class ShipmentTrackingAuthorityCertificationTests
{
    [Fact]
    public void CarrierTrackingInputs_MustResolveAnExistingTenantBoundShipmentBeforeMutation()
    {
        var carrierService = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs");
        var webhookPipeline = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Shipping/ShippingWebhookRequestPipeline.cs");

        Assert.Contains("s.Id == shipmentId && s.TenantId == tenantId", carrierService, StringComparison.Ordinal);
        Assert.Contains("s.TenantId == resolvedTenantId", carrierService, StringComparison.Ordinal);
        Assert.Contains("s.CarrierCode == normalizedCarrierCode", carrierService, StringComparison.Ordinal);
        Assert.Contains("s.TrackingNumber == trackingNumber", carrierService, StringComparison.Ordinal);
        Assert.Contains("!s.IsDeleted", carrierService, StringComparison.Ordinal);
        Assert.Contains("ValidateWebhookTrust(payload, signature, sourceIp", carrierService, StringComparison.Ordinal);
        Assert.Contains("PrepareShippingWebhookLogAsync(", carrierService, StringComparison.Ordinal);
        Assert.Contains("WebhookProcessingStatus.Completed", carrierService, StringComparison.Ordinal);
        Assert.Contains("s.CarrierCode == carrierCode && s.TrackingNumber == trackingNumber && !s.IsDeleted", webhookPipeline, StringComparison.Ordinal);
        Assert.Contains("s.Id == shipmentId && s.CarrierCode == carrierCode && !s.IsDeleted", webhookPipeline, StringComparison.Ordinal);
        Assert.Contains("WEBHOOK_TENANT_AMBIGUOUS", webhookPipeline, StringComparison.Ordinal);
    }

    [Fact]
    public void TrackingSyncAndWebhooks_MustRemainBackendGovernedLifecycleInputs()
    {
        var carrierService = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs");
        var lifecyclePolicy = CertificationSource.ReadRepoFile("PointOfSale.Core/Policies/ShipmentLifecycleTransitionPolicy.cs");

        Assert.Contains("!IsValidStatusProgression(previousStatus, trackingResult.CurrentStatus)", carrierService, StringComparison.Ordinal);
        Assert.Contains("!IsValidStatusProgression(previousStatus, parseResult.Status)", carrierService, StringComparison.Ordinal);
        Assert.Contains("SynchronizeFulfillmentAuthorityAsync(", carrierService, StringComparison.Ordinal);
        Assert.Contains("ShipmentLifecycleTransitionPolicy.CanTransition", carrierService, StringComparison.Ordinal);
        Assert.Contains("ShipmentStatus.Delivered", lifecyclePolicy, StringComparison.Ordinal);
    }

    [Fact]
    public void RecoveryAndFrontend_MustNotRestoreOrFabricateTrackingTruth()
    {
        var recovery = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Shipping/ShipmentAuthorityRecoveryHostedService.cs");
        var shipmentHook = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/shipping/hooks/useShipmentsApi.js");
        var storefrontTracking = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/components/order/ShipmentTracking.tsx");

        Assert.Contains("ReconcileShipmentAuthorityAsync(", recovery, StringComparison.Ordinal);
        Assert.DoesNotContain("shipment.Status = ShipmentStatus", recovery, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage", shipmentHook, StringComparison.Ordinal);
        Assert.DoesNotContain("sessionStorage", shipmentHook, StringComparison.Ordinal);
        Assert.Contains("Read-only display using order timeline data.", storefrontTracking, StringComparison.Ordinal);
        Assert.DoesNotContain("fetch(", storefrontTracking, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage", storefrontTracking, StringComparison.Ordinal);
        Assert.DoesNotContain("sessionStorage", storefrontTracking, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationRecord_MustRemainPresent()
    {
        var findings = CertificationSource.ReadRepoFile("docs/architecture/shipment-tracking-authority-certification-findings.md");

        Assert.Contains("PASS M7-F Shipment Tracking Authority", findings, StringComparison.Ordinal);
        Assert.Contains("Backend owns Tracking Authority", findings, StringComparison.Ordinal);
        Assert.Contains("production-certifiable", findings, StringComparison.Ordinal);
    }
}
