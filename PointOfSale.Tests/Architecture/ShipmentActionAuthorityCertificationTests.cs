namespace PointOfSale.Tests.Architecture;

public sealed class ShipmentActionAuthorityCertificationTests
{
    [Fact]
    public void ShipmentMutations_MustRemainBackendRoleAndTenantBound()
    {
        var shipmentController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/ShipmentController.cs");
        var adminController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AdminShipmentsController.cs");
        var storefrontController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/StorefrontShipmentController.cs");

        var permissions = CertificationSource.ReadRepoFile("PointOfSale.API/Security/PosEndpointPermissionCatalog.cs");
        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");
        Assert.Contains("[Authorize]", shipmentController, StringComparison.Ordinal);
        Assert.Contains("[\"Shipment\"] = R(PosPermissions.ShippingView, PosPermissions.ShippingFulfill)", permissions, StringComparison.Ordinal);
        Assert.Contains("[\"AdminShipments\"] = R(PosPermissions.ShippingView, PosPermissions.ShippingFulfill)", permissions, StringComparison.Ordinal);
        Assert.Contains("options.Conventions.Add(new PosPermissionApplicationModelConvention())", program, StringComparison.Ordinal);
        Assert.Contains("ValidateTenantHeader(traceId)", shipmentController, StringComparison.Ordinal);
        Assert.Contains("ValidateTenantHeader(traceId)", adminController, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"{orderId:guid}/shipment\")]", storefrontController, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpPost", storefrontController, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpPut", storefrontController, StringComparison.Ordinal);
    }

    [Fact]
    public void ShipmentCreationAndRecovery_MustBindEveryMutationToCurrentTenantAuthority()
    {
        var shipmentService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Shipping/ShipmentService.cs");
        var recovery = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Shipping/ShipmentAuthorityRecoveryHostedService.cs");

        Assert.Contains("LoadOrderForShipmentMutationAsync(tenantId, orderId", shipmentService, StringComparison.Ordinal);
        Assert.Contains("order.ShippingMethodId.Value != shippingMethodId", shipmentService, StringComparison.Ordinal);
        Assert.Contains("GetServiceLevelReadinessAsync(\n            tenantId,", shipmentService.Replace("\r\n", "\n"), StringComparison.Ordinal);
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
