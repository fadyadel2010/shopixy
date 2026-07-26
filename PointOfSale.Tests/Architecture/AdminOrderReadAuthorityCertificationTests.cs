namespace PointOfSale.Tests.Architecture;

public sealed class AdminOrderReadAuthorityCertificationTests
{
    [Fact]
    public void AdminOrderReadEndpoints_MustRemainBackendOwnedAndNoStore()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/AdminOrdersController.cs");
        var service = ReadRepoFile("PointOfSale.API/Services/Admin/AdminOrderService.cs");

        Assert.Contains("[Authorize(Roles = \"Owner,Admin,Manager\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]", controller, StringComparison.Ordinal);
        Assert.Contains("_tenantContext.RequiredTenantId", controller, StringComparison.Ordinal);
        Assert.Contains("ListOrdersAsync", controller, StringComparison.Ordinal);
        Assert.Contains("GetOrderDetailsAsync", controller, StringComparison.Ordinal);
        Assert.Contains("AsNoTracking()", service, StringComparison.Ordinal);
        Assert.Contains("Order entity does NOT have TenantId column", service, StringComparison.Ordinal);
        Assert.Contains("VerifyOrderOwnershipAsync", service, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSubOrders", service, StringComparison.Ordinal);
        Assert.DoesNotContain("IOrderReadRepository", service, StringComparison.Ordinal);
    }

    [Fact]
    public void AdminOrderList_SearchSortPaginationAndCurrency_MustComeFromBackendDto()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/AdminOrdersController.cs");
        var service = ReadRepoFile("PointOfSale.API/Services/Admin/AdminOrderService.cs");
        var dto = ReadRepoFile("PointOfSale.API/DTOs/Admin/AdminOrderDtos.cs");
        var frontendPage = ReadRepoFile("PointOfSale.Web/src/pages/Orders.jsx");
        var frontendService = ReadRepoFile("PointOfSale.Web/src/services/adminOrdersApi.js");

        Assert.Contains("[FromQuery] string? search", controller, StringComparison.Ordinal);
        Assert.Contains("[FromQuery] string sortBy", controller, StringComparison.Ordinal);
        Assert.Contains("[FromQuery] string sortDirection", controller, StringComparison.Ordinal);
        Assert.Contains("public string Currency", dto, StringComparison.Ordinal);
        Assert.Contains("public string? SourceOrderId", dto, StringComparison.Ordinal);
        Assert.Contains("public string? Search", dto, StringComparison.Ordinal);
        Assert.Contains("SupportedSortFields", dto, StringComparison.Ordinal);
        Assert.Contains("Search must be 100 characters or fewer", dto, StringComparison.Ordinal);
        Assert.Contains("Currency = o.Currency", service, StringComparison.Ordinal);
        Assert.Contains("Currency = subOrder.Currency", service, StringComparison.Ordinal);
        Assert.Contains("SourceOrderId = o.SourceOrderId", service, StringComparison.Ordinal);
        Assert.Contains("SourceOrderId = subOrder.MarketplaceOrderId.ToString()", service, StringComparison.Ordinal);
        Assert.Contains("ApplyOrderListSorting", service, StringComparison.Ordinal);
        Assert.Contains("var totalCount = await combinedQuery.CountAsync", service, StringComparison.Ordinal);
        Assert.Contains("formatCurrency(order.totalAmount, order.currency)", frontendPage, StringComparison.Ordinal);
        Assert.DoesNotContain("formatCurrency(order.totalAmount, 'EGP')", frontendPage, StringComparison.Ordinal);
        Assert.Contains("params.set('search', filters.search)", frontendService, StringComparison.Ordinal);
        Assert.Contains("params.set('sortBy', filters.sortBy)", frontendService, StringComparison.Ordinal);
        Assert.Contains("'Cache-Control': 'no-store'", frontendService, StringComparison.Ordinal);
    }

    [Fact]
    public void AdminOrderFrontend_MustAdmitBackendTenantAndRouteEvidenceBeforePresentation()
    {
        var page = ReadRepoFile("PointOfSale.Web/src/pages/Orders.jsx");
        var service = ReadRepoFile("PointOfSale.Web/src/services/adminOrdersApi.js");
        var admission = ReadRepoFile("PointOfSale.Web/src/auth/commerceHubVendorIdentityAuthority.js");

        Assert.Contains("requireCurrentVendorOrderVisibilityListAuthority(ordersQuery.data, user)", page, StringComparison.Ordinal);
        Assert.Contains("requireCurrentVendorOrderVisibilityAuthority(detailsQuery.data, user, selectedOrderId)", page, StringComparison.Ordinal);
        Assert.Contains("orderListAuthorityError", page, StringComparison.Ordinal);
        Assert.Contains("selectedOrderAuthorityError", page, StringComparison.Ordinal);
        Assert.Contains("requireMarketplaceOrderLifecycleListAuthority", service, StringComparison.Ordinal);
        Assert.Contains("requireVendorOrderVisibilityListAuthority", service, StringComparison.Ordinal);
        Assert.Contains("requireVendorOrderVisibilityAuthority", service, StringComparison.Ordinal);
        Assert.Contains("requireBackendOrderSourceAuthority", page, StringComparison.Ordinal);
        Assert.Contains("getOrderSourcePresentation(order).label", page, StringComparison.Ordinal);
        Assert.DoesNotContain("order.sourceLabel || order.source || 'Storefront'", page, StringComparison.Ordinal);
        Assert.DoesNotContain("order.sourceLabel || (order.isMarketplaceOrder ? 'Marketplace' : 'Storefront')", page, StringComparison.Ordinal);
        Assert.Contains("dtoOrderId !== routeOrderId", admission, StringComparison.Ordinal);
        Assert.Contains("seenOrderIds", admission, StringComparison.Ordinal);
        Assert.Contains("rejected missing or malformed order source evidence", admission, StringComparison.Ordinal);
        Assert.Contains("rejected non-storefront source evidence", admission, StringComparison.Ordinal);
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
