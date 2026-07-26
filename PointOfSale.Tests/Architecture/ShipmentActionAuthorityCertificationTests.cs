namespace PointOfSale.Tests.Architecture;

public sealed class ShipmentActionAuthorityCertificationTests
{
    [Fact]
    public void ShipmentMutations_MustRemainBackendRoleAndTenantBound()
    {
        var shipmentController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/ShipmentController.cs");
        var adminController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AdminShipmentsController.cs");
        var storefrontController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/StorefrontShipmentController.cs");

        Assert.Contains("[Authorize(Roles = \"Owner,Admin,Manager\")]", shipmentController, StringComparison.Ordinal);
        Assert.Contains("[Authorize(Roles = \"Owner,Admin,Manager\")]", adminController, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"{orderId:guid}/shipment\")]", storefrontController, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpPost", storefrontController, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpPut", storefrontController, StringComparison.Ordinal);
    }

    [Fact]
    public void ShipmentCreationAndRecovery_MustBindEveryMutationToCurrentTenantAuthority()
    {
        var shipmentService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Shipping/ShipmentService.cs");
        var recovery = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Shipping/ShipmentAuthorityRecoveryHostedService.cs");

        Assert.Contains("sm.TenantId == tenantId", shipmentService, StringComparison.Ordinal);
        Assert.Equal(3, recovery.Split(".IgnoreQueryFilters()", StringSplitOptions.None).Length - 1);
        Assert.Contains("tenantContext.SetTenantId(candidate.TenantId);", recovery, StringComparison.Ordinal);
        Assert.Contains("ReconcileShipmentAuthorityAsync(", recovery, StringComparison.Ordinal);
        Assert.DoesNotContain("shipment.Status = ShipmentStatus", recovery, StringComparison.Ordinal);
    }

    [Fact]
    public void Frontend_MustTreatBackendActionEvidenceAsADisposableCandidate()
    {
        var authority = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/shipping/shipmentActionAuthority.js");
        var actions = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/shipping/components/ShipmentActions.jsx");

        Assert.Contains("requireShipmentActionAuthority", authority, StringComparison.Ordinal);
        Assert.Contains("availableActions", authority, StringComparison.Ordinal);
        Assert.Contains("refetchActionAuthority", actions, StringComparison.Ordinal);
        Assert.Contains("currentAuthority = await refetchActionAuthority?.()", actions, StringComparison.Ordinal);
        Assert.Contains("hasShipmentActionAuthority(currentAuthority, shipment, 'cancel')", actions, StringComparison.Ordinal);
    }
}
