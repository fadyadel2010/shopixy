namespace PointOfSale.Tests.Architecture;

public sealed class ShippingSplitOrderAuthorityCertificationTests
{
    [Fact]
    public void Checkout_MustResolveShippingPerStoreAndAggregateOnlyPersistedSubOrderValues()
    {
        var checkout = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");

        Assert.Contains("using var shippingAssignmentBoundary = CommerceHubBoundaryMutationScope.BeginShippingAssignment();", checkout, StringComparison.Ordinal);
        Assert.Contains("StoreId = group.Key", checkout, StringComparison.Ordinal);
        Assert.Contains("Shipping = shippingSelection.ShippingPrice", checkout, StringComparison.Ordinal);
        Assert.Contains("ShippingMethodId = shippingSelection.ShippingOptionId", checkout, StringComparison.Ordinal);
        Assert.Contains("marketplaceOrder.ShippingTotal = RoundCurrency(marketplaceOrder.SubOrders.Sum(subOrder => subOrder.Shipping))", checkout, StringComparison.Ordinal);
    }

    [Fact]
    public void PersistenceProjectionAndRepair_MustNotCreateOrRebindShippingAuthority()
    {
        var context = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");
        var projection = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Consumers/CommerceHubOrderProjectionConsumer.cs");
        var repair = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubOrderProjectionRepairSupport.cs");

        Assert.Contains("IsShippingAssignmentAuthorized", context, StringComparison.Ordinal);
        Assert.Contains("CommerceHub sub-order shipping assignment fields can only be changed", context, StringComparison.Ordinal);
        Assert.Contains("Shipping = subOrder.Shipping", projection, StringComparison.Ordinal);
        Assert.Contains("ShippingMethodId = subOrder.ShippingMethodId", projection, StringComparison.Ordinal);
        Assert.Contains("ShippingSnapshotJson = subOrder.ShippingSnapshotJson", repair, StringComparison.Ordinal);
    }

    [Fact]
    public void ShippingServices_MustRemainVendorScopedAndShipmentOrchestrationMustNotTouchMarketplaceSubOrders()
    {
        var shipping = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubShippingService.cs");
        var orchestration = CertificationSource.ReadRepoFile("PointOfSale.API/Consumers/OrderShipmentOrchestrationConsumer.cs");

        Assert.Contains("method.TenantId == storeId", shipping, StringComparison.Ordinal);
        Assert.Contains("bypassCache", shipping, StringComparison.Ordinal);
        Assert.DoesNotContain("CommerceHubSubOrder", orchestration, StringComparison.Ordinal);
        Assert.DoesNotContain("CommerceHubMarketplaceOrder", orchestration, StringComparison.Ordinal);
    }
}
