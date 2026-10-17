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
        Assert.Contains("order.availableOrderActions", webPage, StringComparison.Ordinal);
        Assert.Contains("action?.targetStatus", webPage, StringComparison.Ordinal);
        Assert.DoesNotContain("GENERIC_LIFECYCLE_CONTROLLED_ORDER_STATUSES", webPage, StringComparison.Ordinal);
        Assert.Contains("getAdminOrderFulfillmentActions", webPage, StringComparison.Ordinal);
        Assert.Contains("runAdminOrderFulfillmentAction", webPage, StringComparison.Ordinal);
        Assert.Contains("t('orders.management.details.submitCandidate')", webPage, StringComparison.Ordinal);
        Assert.Contains("onSubmit={handleSubmitStatus}", webPage, StringComparison.Ordinal);
        Assert.Contains("disabled={!canSubmitStatusCandidate || isStatusUpdating}", webPage, StringComparison.Ordinal);
        Assert.Contains("directStatusOptions.includes(statusCandidate)", webPage, StringComparison.Ordinal);
        Assert.Contains("if (!canSubmitStatusCandidate)", webPage, StringComparison.Ordinal);
        Assert.Contains("setStatusCandidate(order?.status || '')", webPage, StringComparison.Ordinal);
        Assert.Contains("statusMutation.mutate(candidate)", webPage, StringComparison.Ordinal);
        Assert.Contains("rowVersion: order.rowVersion", webPage, StringComparison.Ordinal);
        Assert.Contains("'If-Match': rowVersion", webService, StringComparison.Ordinal);
        Assert.Contains("api.get(`/orders/${orderId}/fulfillment/actions`", webService, StringComparison.Ordinal);
        Assert.Contains("`/orders/${orderId}/fulfillment/${actionCode}`", webService, StringComparison.Ordinal);
        Assert.Contains("requireMarketplaceOrderLifecycleAuthority", webService, StringComparison.Ordinal);
        Assert.Contains("missing status concurrency evidence", admission, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage", webPage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sessionStorage", webPage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FulfillmentActions_MustExposeEvidenceContractAndStableErrors()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/OrderFulfillmentController.cs");
        var fulfillment = ReadRepoFile("PointOfSale.Infrastructure/Fulfillment/OrderFulfillmentService.cs");
        var webPage = ReadRepoFile("PointOfSale.Web/src/pages/Orders.jsx");
        var webService = ReadRepoFile("PointOfSale.Web/src/services/adminOrdersApi.js");

        Assert.Contains("requiredFields = a.RequiredFields", controller, StringComparison.Ordinal);
        Assert.Contains("MANUAL_DELIVERY_EVIDENCE_REQUIRED", controller, StringComparison.Ordinal);
        Assert.Contains("FULFILLMENT_TRANSITION_INVALID", controller, StringComparison.Ordinal);
        Assert.Contains("GetCompletionBlockReason", fulfillment, StringComparison.Ordinal);
        Assert.Contains("deliveryReference", webPage, StringComparison.Ordinal);
        Assert.Contains("proofOfDeliveryUrl", webPage, StringComparison.Ordinal);
        Assert.Contains("'failed-delivery'", webService, StringComparison.Ordinal);
        Assert.Contains("payload", webService, StringComparison.Ordinal);
    }

    [Fact]
    public void PosAdminOrders_MustRenderBackendPaymentAndCancellationCapabilitiesWithoutShadowRules()
    {
        var dto = ReadRepoFile("PointOfSale.API/DTOs/Admin/AdminOrderDtos.cs");
        var adminService = ReadRepoFile("PointOfSale.API/Services/Admin/AdminOrderService.cs");
        var paymentService = ReadRepoFile("PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs");
        var paymentController = ReadRepoFile("PointOfSale.API/Controllers/PaymentLifecycleController.cs");
        var webPage = ReadRepoFile("PointOfSale.Web/src/pages/Orders.jsx");
        var webService = ReadRepoFile("PointOfSale.Web/src/services/adminOrdersApi.js");

        Assert.Contains("AvailableOrderActions", dto, StringComparison.Ordinal);
        Assert.Contains("AvailableShipmentActions", dto, StringComparison.Ordinal);
        Assert.Contains("TargetStatus", dto, StringComparison.Ordinal);
        Assert.Contains("BuildAvailableOrderActions(order, paymentIntents)", adminService, StringComparison.Ordinal);
        Assert.Contains("Code = \"place-on-hold\"", adminService, StringComparison.Ordinal);
        Assert.Contains("Code = \"resume-pending\"", adminService, StringComparison.Ordinal);
        Assert.Contains("BuildAvailableShipmentActions(shipment)", adminService, StringComparison.Ordinal);
        Assert.Contains("order.FulfillmentStatus == FulfillmentStatus.Delivered", paymentService, StringComparison.Ordinal);
        Assert.Contains("Record COD Collection", paymentController, StringComparison.Ordinal);
        Assert.Contains("getAdminOrderPaymentActions", webPage, StringComparison.Ordinal);
        Assert.Contains("paymentAuthority?.availableActions", webPage, StringComparison.Ordinal);
        Assert.Contains("getAdminOrderPaymentActions", webService, StringComparison.Ordinal);
        Assert.Contains("requireAdminOrderPaymentActionAuthority", webService, StringComparison.Ordinal);
        Assert.Contains("action?.targetStatus", webPage, StringComparison.Ordinal);
        Assert.DoesNotContain("order.paymentStatus ===", webPage, StringComparison.Ordinal);
        Assert.DoesNotContain("order.fulfillmentStatus ===", webPage, StringComparison.Ordinal);
        Assert.DoesNotContain("order.status !== 'OnHold'", webPage, StringComparison.Ordinal);
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
