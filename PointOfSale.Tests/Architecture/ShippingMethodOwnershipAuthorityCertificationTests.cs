namespace PointOfSale.Tests.Architecture;

public sealed class ShippingMethodOwnershipAuthorityCertificationTests
{
    [Fact]
    public void ShippingMethodOwnership_MustBeServerAssignedTenantBoundAndNotClientAssignable()
    {
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/ShippingMethodsController.cs");

        Assert.Contains("Id = Guid.NewGuid()", controller, StringComparison.Ordinal);
        Assert.Contains("TenantId = tenantId", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("TenantId { get; set; }", controller, StringComparison.Ordinal);
        Assert.Contains("s.Id == id && s.TenantId == tenantId && !s.IsDeleted", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void StorefrontCommerceHubAndProjections_MustRemainTenantScopedDerivativeViews()
    {
        var storefront = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Storefront/ShippingMethodService.cs");
        var commerceHub = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubShippingService.cs");
        var projection = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Consumers/ShippingMethodProjectionConsumer.cs");
        var repository = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/MongoDB/Services/ShippingMethodReadRepository.cs");

        Assert.Contains("GetActiveShippingMethodsAsync(tenantId", storefront, StringComparison.Ordinal);
        Assert.Contains("!method.IsDeleted && method.IsActive && method.TenantId == storeId", commerceHub, StringComparison.Ordinal);
        Assert.Contains("x.Id == @event.ShippingMethodId && x.TenantId == @event.TenantId.Value", projection, StringComparison.Ordinal);
        Assert.Contains("ProjectionEventFence.TryAcquireAsync", projection, StringComparison.Ordinal);
        Assert.Contains("TombstoneIfFreshAsync", projection, StringComparison.Ordinal);
        Assert.Contains("TenantId, document.TenantId", repository, StringComparison.Ordinal);
        Assert.Contains("ShippingMethodId, document.ShippingMethodId", repository, StringComparison.Ordinal);
    }

    [Fact]
    public void ShipmentCreation_MustConsumeTheOrderBoundMethodAndRejectSubstitution()
    {
        var service = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Shipping/ShipmentService.cs");
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/ShipmentController.cs");

        Assert.Contains("order.ShippingMethodId.Value != shippingMethodId", service, StringComparison.Ordinal);
        Assert.Contains("ShipmentShippingMethodMismatchException", service, StringComparison.Ordinal);
        Assert.Contains("sm.Id == shippingMethodId", service, StringComparison.Ordinal);
        Assert.Contains("sm.TenantId == tenantId", service, StringComparison.Ordinal);
        Assert.Contains("catch (ShipmentShippingMethodMismatchException ex)", controller, StringComparison.Ordinal);
        Assert.Contains("return Conflict", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationArtifact_MustRecordM8BShippingMethodOwnershipTruth()
    {
        Assert.Contains(
            "PASS M8-B Shipping Method Ownership Authority",
            CertificationSource.ReadRepoFile("docs/architecture/shipping-method-ownership-authority-certification-findings.md"),
            StringComparison.Ordinal);
    }
}
