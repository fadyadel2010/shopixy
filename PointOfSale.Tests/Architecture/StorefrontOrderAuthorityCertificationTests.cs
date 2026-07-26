namespace PointOfSale.Tests.Architecture;

public sealed class StorefrontOrderAuthorityCertificationTests
{
    [Fact]
    public void StorefrontCheckout_MustUseDurableIdempotencyAndSerializableSqlAuthority()
    {
        var source = ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var dbSource = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        Assert.Contains("CheckoutRequestIdempotencyCoordinator", source, StringComparison.Ordinal);
        Assert.Contains("IsolationLevel.Serializable", source, StringComparison.Ordinal);
        Assert.Contains("ValidateAndReserveInventoryAsync", source, StringComparison.Ordinal);
        Assert.Contains("IX_Orders_TenantId_CartId_ActiveStatuses", source, StringComparison.Ordinal);
        Assert.Contains("IX_Orders_TenantId_CartId_ActiveStatuses", dbSource, StringComparison.Ordinal);
    }

    [Fact]
    public void OrderCreation_MustRecomputeFinancialsAndRequirePrivilegedAuthority()
    {
        var source = ReadRepoFile("PointOfSale.API/Controllers/OrdersController.cs");
        var dbSource = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        Assert.Contains("[Authorize(Roles = \"Owner,Admin,Manager\")]", source, StringComparison.Ordinal);
        Assert.Contains("CalculateAuthoritativeCreateOrderFinancialsAsync", source, StringComparison.Ordinal);
        Assert.Contains("ValidateCreateOrderCustomerAsync", source, StringComparison.Ordinal);
        Assert.Contains("has no valid current default unit", source, StringComparison.Ordinal);
        Assert.Contains("IX_Orders_TenantId_Source_SourceOrderId", dbSource, StringComparison.Ordinal);
        Assert.Contains("IsSourceOrderIdentityConflict", source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "configuredUnit.ConversionFactor > 0m ? configuredUnit.ConversionFactor : 1m",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Fulfillment_MustRemainTenantScopedAndRoleProtected()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/OrderFulfillmentController.cs");
        var service = ReadRepoFile("PointOfSale.Infrastructure/Fulfillment/OrderFulfillmentService.cs");
        var mutationLoader = Slice(
            service,
            "private async Task<Order?> LoadOrderForFulfillmentMutationAsync",
            "private static bool IsTerminalOrderLifecycle");

        Assert.Contains("[Authorize(Roles = \"Owner,Admin,Manager\")]", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("IgnoreQueryFilters()", mutationLoader, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", mutationLoader, StringComparison.Ordinal);
    }

    [Fact]
    public void Fulfillment_MustConvergeOrderStatusAndBlockTerminalResurrection()
    {
        var source = ReadRepoFile("PointOfSale.Infrastructure/Fulfillment/OrderFulfillmentService.cs");

        Assert.Contains("IsTerminalOrderLifecycle(order.Status)", source, StringComparison.Ordinal);
        Assert.Contains("ResolveOrderStatus(order.Status, targetStatus)", source, StringComparison.Ordinal);
        Assert.Contains("OrderStatusSaveChangesInterceptor.BeginAuthorizedContext()", source, StringComparison.Ordinal);
        Assert.Contains("OrderStatusChangedEvent", source, StringComparison.Ordinal);
        Assert.Contains("Replay collapsed against current SQL fulfillment authority", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CancellationAndRefund_MustUseDedicatedAuthorityServices()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/OrderFulfillmentController.cs");
        var service = ReadRepoFile("PointOfSale.Infrastructure/Fulfillment/OrderFulfillmentService.cs");
        var adminService = ReadRepoFile("PointOfSale.API/Services/Admin/AdminOrderService.cs");
        var paymentService = ReadRepoFile("PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs");

        Assert.Contains("ORDER_CANCELLATION_LIFECYCLE_REQUIRED", controller, StringComparison.Ordinal);
        Assert.Contains("ORDER_CANCELLATION_LIFECYCLE_REQUIRED", service, StringComparison.Ordinal);
        Assert.Contains("REFUND_LIFECYCLE_REQUIRED", controller, StringComparison.Ordinal);
        Assert.Contains("REFUND_LIFECYCLE_REQUIRED", service, StringComparison.Ordinal);
        Assert.Contains("ReleaseOrderReservationsAsync", adminService, StringComparison.Ordinal);
        Assert.Contains("PaymentCancellationOutboxMessage", adminService, StringComparison.Ordinal);
        Assert.Contains("remainingRefundableAmount", paymentService, StringComparison.Ordinal);
        Assert.Contains("IX_OrderRefunds_TenantId_OrderId_RefundId", paymentService, StringComparison.Ordinal);
    }

    [Fact]
    public void StorefrontReads_MustProveSqlOwnershipBeforeServingProjection()
    {
        var source = ReadRepoFile("PointOfSale.API/Controllers/StorefrontOrdersReadController.cs");

        Assert.Contains("LoadOrderAuthoritySnapshotAsync", source, StringComparison.Ordinal);
        Assert.Contains("authority.OnlineCustomerId != customerId.Value", source, StringComparison.Ordinal);
        Assert.Contains("TryRepairOrderProjectionAsync", source, StringComparison.Ordinal);
        Assert.Contains("ValidateGuestTrackingToken", source, StringComparison.Ordinal);
        Assert.Contains("SESSION_MISMATCH", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Reporting_MustNotUseStorefrontProjectionAsFinancialTruth()
    {
        var reports = ReadRepoFile("PointOfSale.API/Controllers/ReportsController.cs");
        var dashboard = ReadRepoFile("PointOfSale.API/Controllers/DashboardController.cs");

        Assert.Contains("_context.JournalEntries", reports, StringComparison.Ordinal);
        Assert.DoesNotContain("IOrderReadRepository", reports, StringComparison.Ordinal);
        Assert.DoesNotContain("IOrderReadRepository", dashboard, StringComparison.Ordinal);
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing start marker: {startMarker}");
        Assert.True(end > start, $"Missing end marker: {endMarker}");
        return source[start..end];
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
