namespace PointOfSale.Tests.Architecture;

public sealed class AdminOrderLifecycleAuthorityCertificationTests
{
    [Fact]
    public void AdminOrderLifecycleMutations_MustRequireBackendRowVersionAndIfMatch()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/AdminOrdersController.cs");
        var service = ReadRepoFile("PointOfSale.API/Services/Admin/AdminOrderService.cs");
        var dto = ReadRepoFile("PointOfSale.API/DTOs/Admin/AdminOrderDtos.cs");
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        Assert.Contains("ParseRequiredIfMatch", controller, StringComparison.Ordinal);
        Assert.Contains("IF_MATCH_REQUIRED", controller, StringComparison.Ordinal);
        Assert.Contains("StatusCodes.Status428PreconditionRequired", controller, StringComparison.Ordinal);
        Assert.Contains("uint expectedRowVersion", service, StringComparison.Ordinal);
        Assert.Contains("GetOrderRowVersion(order)", service, StringComparison.Ordinal);
        Assert.Contains("ORDER_VERSION_CONFLICT", ReadRepoFile("PointOfSale.API/Services/Admin/IAdminOrderService.cs"), StringComparison.Ordinal);
        Assert.Contains("Property<uint>(\"xmin\")", dbContext, StringComparison.Ordinal);
        Assert.Contains("public string RowVersion", dto, StringComparison.Ordinal);
    }

    [Fact]
    public void AdminOrderLifecycleMutations_MustRemainBackendValidatedAndCandidateOnlyInWeb()
    {
        var service = ReadRepoFile("PointOfSale.API/Services/Admin/AdminOrderService.cs");
        var webPage = ReadRepoFile("PointOfSale.Web/src/pages/Orders.jsx");
        var webService = ReadRepoFile("PointOfSale.Web/src/services/adminOrdersApi.js");
        var admission = ReadRepoFile("PointOfSale.Web/src/auth/marketplaceOrderLifecycleAuthority.js");

        Assert.Contains("LoadOrderForAdminStatusMutationAsync", service, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", service, StringComparison.Ordinal);
        Assert.Contains("LIFECYCLE_BYPASS_ATTEMPT", service, StringComparison.Ordinal);
        Assert.Contains("OPERATIONAL_LIFECYCLE_BYPASS_ATTEMPT", service, StringComparison.Ordinal);
        Assert.Contains("OrderStatusSaveChangesInterceptor.BeginAuthorizedContext", service, StringComparison.Ordinal);
        Assert.Contains("OrderStatusChangedEvent", service, StringComparison.Ordinal);
        Assert.Contains("getDirectOrderStatusMutationOptions", webPage, StringComparison.Ordinal);
        Assert.Contains("GENERIC_LIFECYCLE_CONTROLLED_ORDER_STATUSES", webPage, StringComparison.Ordinal);
        Assert.Contains("!GENERIC_LIFECYCLE_CONTROLLED_ORDER_STATUSES.has(status)", webPage, StringComparison.Ordinal);
        Assert.Contains("getAdminOrderFulfillmentActions", webPage, StringComparison.Ordinal);
        Assert.Contains("runAdminOrderFulfillmentAction", webPage, StringComparison.Ordinal);
        Assert.Contains("Submit candidate", webPage, StringComparison.Ordinal);
        Assert.Contains("rowVersion: order.rowVersion", webPage, StringComparison.Ordinal);
        Assert.Contains("'If-Match': rowVersion", webService, StringComparison.Ordinal);
        Assert.Contains("api.get(`/orders/${orderId}/fulfillment/actions`", webService, StringComparison.Ordinal);
        Assert.Contains("`/orders/${orderId}/fulfillment/${actionCode}`", webService, StringComparison.Ordinal);
        Assert.Contains("requireMarketplaceOrderLifecycleAuthority", webService, StringComparison.Ordinal);
        Assert.Contains("missing status concurrency evidence", admission, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage", webPage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sessionStorage", webPage, StringComparison.OrdinalIgnoreCase);
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
