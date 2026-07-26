namespace PointOfSale.Tests.Architecture;

public sealed class StorefrontOrderLineageAuthorityCertificationTests
{
    [Fact]
    public void StorefrontCheckout_MustPersistBackendOwnedEcommerceLineage()
    {
        var checkout = ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var order = ReadRepoFile("PointOfSale.Core/Entities/Order.cs");
        var db = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        Assert.Contains("Source = OrderSource.Ecommerce", checkout, StringComparison.Ordinal);
        Assert.Contains("OnlineCustomerId = customerId", checkout, StringComparison.Ordinal);
        Assert.Contains("CartId = request.CartId", checkout, StringComparison.Ordinal);
        Assert.Contains("OrderRequestIdempotency", checkout, StringComparison.Ordinal);
        Assert.Contains("public OrderSource Source", order, StringComparison.Ordinal);
        Assert.Contains("public string? SourceOrderId", order, StringComparison.Ordinal);
        Assert.Contains("IX_Orders_TenantId_Source_SourceOrderId", db, StringComparison.Ordinal);
        Assert.Contains("IX_Orders_TenantId_CartId_ActiveStatuses", db, StringComparison.Ordinal);
    }

    [Fact]
    public void AdminOrderPresentation_MustAdmitBackendSourceBeforeRenderingStorefrontLineage()
    {
        var dto = ReadRepoFile("PointOfSale.API/DTOs/Admin/AdminOrderDtos.cs");
        var service = ReadRepoFile("PointOfSale.API/Services/Admin/AdminOrderService.cs");
        var frontendPage = ReadRepoFile("PointOfSale.Web/src/pages/Orders.jsx");
        var frontendAdmission = ReadRepoFile("PointOfSale.Web/src/auth/commerceHubVendorIdentityAuthority.js");

        Assert.Contains("public OrderSource Source", dto, StringComparison.Ordinal);
        Assert.Contains("public string? SourceOrderId", dto, StringComparison.Ordinal);
        Assert.Contains("Source = o.Source", service, StringComparison.Ordinal);
        Assert.Contains("Source = order.Source", service, StringComparison.Ordinal);
        Assert.Contains("requireBackendOrderSourceAuthority", frontendAdmission, StringComparison.Ordinal);
        Assert.Contains("source !== 'Ecommerce'", frontendAdmission, StringComparison.Ordinal);
        Assert.Contains("source !== 'Marketplace'", frontendAdmission, StringComparison.Ordinal);
        Assert.Contains("getOrderSourcePresentation(order).label", frontendPage, StringComparison.Ordinal);
        Assert.DoesNotContain("|| 'Storefront'", frontendPage, StringComparison.Ordinal);
    }

    [Fact]
    public void RecoveryAndProjectionReads_MustReenterSqlStorefrontAuthority()
    {
        var storefrontReads = ReadRepoFile("PointOfSale.API/Controllers/StorefrontOrdersReadController.cs");
        var adminService = ReadRepoFile("PointOfSale.API/Services/Admin/AdminOrderService.cs");

        Assert.Contains("LoadOrderAuthoritySnapshotAsync", storefrontReads, StringComparison.Ordinal);
        Assert.Contains("order.Source == OrderSource.Ecommerce", storefrontReads, StringComparison.Ordinal);
        Assert.Contains("TryRepairOrderProjectionAsync", storefrontReads, StringComparison.Ordinal);
        Assert.Contains("ValidateGuestTrackingToken", storefrontReads, StringComparison.Ordinal);
        Assert.Contains("IOrderReadRepository", storefrontReads, StringComparison.Ordinal);
        Assert.DoesNotContain("IOrderReadRepository", adminService, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory!.FullName, relativePath));
    }
}
