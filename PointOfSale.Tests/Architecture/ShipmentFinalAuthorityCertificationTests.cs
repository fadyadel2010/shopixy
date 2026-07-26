namespace PointOfSale.Tests.Architecture;

public sealed class ShipmentFinalAuthorityCertificationTests
{
    [Fact]
    public void FinalRuntimeAuthority_MustRemainBackendOwnedAcrossAllShipmentWriters()
    {
        var shipmentService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Shipping/ShipmentService.cs");
        var adminService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Admin/AdminShipmentService.cs");
        var carrierService = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs");
        var recovery = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Shipping/ShipmentAuthorityRecoveryHostedService.cs");

        Assert.Contains("IsolationLevel.Serializable", shipmentService, StringComparison.Ordinal);
        Assert.Contains("ShipmentLifecycleTransitionPolicy.CanTransition", shipmentService, StringComparison.Ordinal);
        Assert.Contains("ShipmentLifecycleTransitionPolicy.CanTransition", adminService, StringComparison.Ordinal);
        Assert.Contains("ShipmentLifecycleTransitionPolicy.CanTransition", carrierService, StringComparison.Ordinal);
        Assert.Contains("ValidateWebhookTrust(payload, signature, sourceIp", carrierService, StringComparison.Ordinal);
        Assert.Contains("PrepareShippingWebhookLogAsync(", carrierService, StringComparison.Ordinal);
        Assert.Contains("tenantContext.SetTenantId(candidate.TenantId)", recovery, StringComparison.Ordinal);
        Assert.DoesNotContain("shipment.Status = ShipmentStatus", recovery, StringComparison.Ordinal);
    }

    [Fact]
    public void FinalFrontendAuthority_MustRemainPresentationAndBackendBoundIntentOnly()
    {
        var actionAuthority = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/shipping/shipmentActionAuthority.js");
        var actions = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/shipping/components/ShipmentActions.jsx");
        var hook = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/shipping/hooks/useShipmentsApi.js");
        var identityAuthority = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/storefront/shipping/shipmentIdentityAuthority.js");
        var tracking = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/components/order/ShipmentTracking.tsx");

        Assert.Contains("requireShipmentActionAuthority", actionAuthority, StringComparison.Ordinal);
        Assert.Contains("currentAuthority = await refetchActionAuthority?.()", actions, StringComparison.Ordinal);
        Assert.Contains("requestIdRef", hook, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage", hook, StringComparison.Ordinal);
        Assert.DoesNotContain("sessionStorage", hook, StringComparison.Ordinal);
        Assert.Contains("requireBackendShipmentIdentity", identityAuthority, StringComparison.Ordinal);
        Assert.Contains("Read-only display using order timeline data.", tracking, StringComparison.Ordinal);
        Assert.DoesNotContain("fetch(", tracking, StringComparison.Ordinal);
    }

    [Fact]
    public void FinalCertificationRecord_MustFreezeNoFrontendShipmentAuthorityEmergence()
    {
        var findings = CertificationSource.ReadRepoFile("docs/architecture/shipment-final-authority-certification-findings.md");
        var projectContext = CertificationSource.ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = CertificationSource.ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var codexRules = CertificationSource.ReadRepoFile(".ai-context/CODEX_RULES.md");

        Assert.Contains("PASS M7-H Shipment Final Authority Certification", findings, StringComparison.Ordinal);
        Assert.Contains("No new shipment runtime code change was required", findings, StringComparison.Ordinal);
        Assert.Contains("Can frontend fabricate shipment authority? No.", findings, StringComparison.Ordinal);
        Assert.Contains("PASS M7-H Shipment Final Authority Certification", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS M7-H Shipment Final Authority", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS M7-H Shipment Final Authority Rule", codexRules, StringComparison.Ordinal);
    }
}
