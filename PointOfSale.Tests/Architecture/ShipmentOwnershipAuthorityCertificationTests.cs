namespace PointOfSale.Tests.Architecture;

public sealed class ShipmentOwnershipAuthorityCertificationTests
{
    [Fact]
    public void CustomerShipmentRead_MustRequireCurrentBackendCustomerAndTenantOwnership()
    {
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/StorefrontShipmentController.cs");

        Assert.Contains("[Authorize(AuthenticationSchemes = \"StorefrontJwt\")]", controller, StringComparison.Ordinal);
        Assert.Contains("TryGetAuthenticatedCustomerOwnership", controller, StringComparison.Ordinal);
        Assert.Contains("tokenTenantId != tenantId", controller, StringComparison.Ordinal);
        Assert.Contains("order.OnlineCustomerId == customerId", controller, StringComparison.Ordinal);
        Assert.Contains("order.TenantId == tenantId", controller, StringComparison.Ordinal);
        Assert.Contains("Guest tracking is intentionally served by the separate", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void StorefrontShipmentPresentation_MustUseTheBackendOwnedShipmentRead()
    {
        var hook = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/storefront/shipping/hooks/useShippingApi.js");

        Assert.Contains("/storefront/orders/${orderId}/shipment", hook, StringComparison.Ordinal);
        Assert.Contains("requireBackendShipmentIdentity(response.data, orderId)", hook, StringComparison.Ordinal);
    }

    [Fact]
    public void DirectShipmentReadAndMutation_MustUseTrustedTenantContextRatherThanCallerHeader()
    {
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/ShipmentController.cs");

        Assert.Contains("private readonly ITenantContext _tenantContext", controller, StringComparison.Ordinal);
        Assert.Contains("return (_tenantContext.RequiredTenantId, null);", controller, StringComparison.Ordinal);
        Assert.Contains("tenantId = _tenantContext.RequiredTenantId;", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("TenantIdHeader", controller, StringComparison.Ordinal);
    }
}
