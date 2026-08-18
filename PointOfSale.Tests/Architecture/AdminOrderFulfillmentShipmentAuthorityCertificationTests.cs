namespace PointOfSale.Tests.Architecture;

public sealed class AdminOrderFulfillmentShipmentAuthorityCertificationTests
{
    [Fact]
    public void AdminOrderDetailsFulfillmentShipmentPresentation_MustRemainBackendOwnedSqlDtoEvidence()
    {
        var dto = ReadRepoFile("PointOfSale.API/DTOs/Admin/AdminOrderDtos.cs");
        var service = ReadRepoFile("PointOfSale.API/Services/Admin/AdminOrderService.cs");
        var controller = ReadRepoFile("PointOfSale.API/Controllers/AdminOrdersController.cs");
        var fulfillmentController = ReadRepoFile("PointOfSale.API/Controllers/OrderFulfillmentController.cs");
        var webPage = ReadRepoFile("PointOfSale.Web/src/pages/Orders.jsx");
        var webService = ReadRepoFile("PointOfSale.Web/src/services/adminOrdersApi.js");

        Assert.Contains("[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]", controller, StringComparison.Ordinal);
        Assert.Contains("_tenantContext.RequiredTenantId", controller, StringComparison.Ordinal);
        Assert.Contains("public ShipmentStatus? ShipmentStatus", dto, StringComparison.Ordinal);
        Assert.Contains("public FulfillmentStatus? FulfillmentStatus", dto, StringComparison.Ordinal);
        Assert.Contains("public AdminShipmentDto? Shipment", dto, StringComparison.Ordinal);
        Assert.Contains("public string? TrackingNumber", dto, StringComparison.Ordinal);
        Assert.Contains("public string? CarrierName", dto, StringComparison.Ordinal);
        Assert.Contains("var shipment = await _context.Shipments", service, StringComparison.Ordinal);
        Assert.Contains(".Where(s => s.OrderId == orderId && s.TenantId == tenantId)", service, StringComparison.Ordinal);
        Assert.Contains("FulfillmentStatus = order.FulfillmentStatus", service, StringComparison.Ordinal);
        Assert.Contains("ShipmentStatus = shipment?.Status", service, StringComparison.Ordinal);
        Assert.Contains("TrackingNumber = shipment.TrackingNumber", service, StringComparison.Ordinal);
        Assert.Contains("CarrierName = shipment.CarrierCode", service, StringComparison.Ordinal);
        Assert.Contains("order.shipmentStatus || 'No shipment'", webPage, StringComparison.Ordinal);
        Assert.Contains("order.fulfillmentStatus || fulfillmentAuthority?.currentStatus", webPage, StringComparison.Ordinal);
        Assert.Contains("'Cache-Control': 'no-store'", webService, StringComparison.Ordinal);
        Assert.Contains("IsCurrentTenantOrderAsync", fulfillmentController, StringComparison.Ordinal);
        Assert.Contains("order.TenantId == tenantId", fulfillmentController, StringComparison.Ordinal);
        Assert.Contains("return NotFound(new { error = \"ORDER_NOT_FOUND\"", fulfillmentController, StringComparison.Ordinal);
    }

    [Fact]
    public void FrontendShipmentActions_MustSubmitBackendBoundCandidatesOnly()
    {
        var actionAuthority = ReadRepoFile("PointOfSale.Web/src/modules/shipping/shipmentActionAuthority.js");
        var actions = ReadRepoFile("PointOfSale.Web/src/modules/shipping/components/ShipmentActions.jsx");
        var hook = ReadRepoFile("PointOfSale.Web/src/modules/shipping/hooks/useShipmentsApi.js");

        Assert.Contains("requireShipmentActionAuthority", actionAuthority, StringComparison.Ordinal);
        Assert.Contains("normalizeId(authority.orderId) !== normalizeId(expectedOrderId)", actionAuthority, StringComparison.Ordinal);
        Assert.Contains("FULFILLMENT_ACTION_CODES.has(action.code)", actionAuthority, StringComparison.Ordinal);
        Assert.Contains("seenCodes.has(action.code)", actionAuthority, StringComparison.Ordinal);
        Assert.Contains("api.get(`/orders/${orderId}/fulfillment/actions`)", hook, StringComparison.Ordinal);
        Assert.Contains("Shipment status transitions require current backend fulfillment action authority.", hook, StringComparison.Ordinal);
        Assert.DoesNotContain("api.put(`/admin/shipments/${shipmentId}/status`", hook, StringComparison.Ordinal);
        Assert.Contains("currentAuthority = await refetchActionAuthority?.()", actions, StringComparison.Ordinal);
        Assert.Contains("hasShipmentActionAuthority(currentAuthority, shipment, 'cancel')", actions, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage", hook, StringComparison.Ordinal);
        Assert.DoesNotContain("sessionStorage", hook, StringComparison.Ordinal);
    }

    [Fact]
    public void BackendFulfillmentAndShipmentWriters_MustRemainAuthoritativeConcurrentAndRecoverySafe()
    {
        var fulfillment = ReadRepoFile("PointOfSale.Infrastructure/Fulfillment/OrderFulfillmentService.cs");
        var interceptor = ReadRepoFile("PointOfSale.Infrastructure/Data/FulfillmentStatusSaveChangesInterceptor.cs");
        var shipmentService = ReadRepoFile("PointOfSale.API/Services/Shipping/ShipmentService.cs");
        var adminShipmentService = ReadRepoFile("PointOfSale.API/Services/Admin/AdminShipmentService.cs");
        var carrierService = ReadRepoFile("PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs");
        var recovery = ReadRepoFile("PointOfSale.API/Services/Shipping/ShipmentAuthorityRecoveryHostedService.cs");

        Assert.Contains("private async Task<OrderFulfillmentResult> TransitionAsync<TEvent>", fulfillment, StringComparison.Ordinal);
        Assert.Contains("IsolationLevel.Serializable", fulfillment, StringComparison.Ordinal);
        Assert.Contains("LoadOrderForFulfillmentMutationAsync", fulfillment, StringComparison.Ordinal);
        Assert.Contains("FulfillmentStatusSaveChangesInterceptor.EnableAuthorizedContext()", fulfillment, StringComparison.Ordinal);
        Assert.Contains("oldStatus.CanTransitionTo(targetStatus)", fulfillment, StringComparison.Ordinal);
        Assert.Contains("FulfillmentStatus can ONLY be changed via IOrderFulfillmentService", interceptor, StringComparison.Ordinal);

        Assert.Contains("IsolationLevel.Serializable", shipmentService, StringComparison.Ordinal);
        Assert.Contains("LoadOrderForShipmentMutationAsync", shipmentService, StringComparison.Ordinal);
        Assert.Contains("ShipmentAlreadyExistsException", shipmentService, StringComparison.Ordinal);
        Assert.Contains("CanCreateShipmentForOrder(order)", shipmentService, StringComparison.Ordinal);
        Assert.Contains("order.ShippingMethodId.Value != shippingMethodId", shipmentService, StringComparison.Ordinal);
        Assert.Contains("ShipmentLifecycleTransitionPolicy.CanTransition", shipmentService, StringComparison.Ordinal);
        Assert.Contains("GetIdempotencyResultAsync", shipmentService, StringComparison.Ordinal);
        Assert.Contains("StoreIdempotencyResultAsync", shipmentService, StringComparison.Ordinal);
        Assert.Contains("EnsureCarrierManagedTrackingMutationAllowed", shipmentService, StringComparison.Ordinal);

        Assert.Contains("IsolationLevel.Serializable", adminShipmentService, StringComparison.Ordinal);
        Assert.Contains("ShipmentLifecycleTransitionPolicy.CanTransition", adminShipmentService, StringComparison.Ordinal);
        Assert.Contains("EnsureManualTrackingMutationAllowed", adminShipmentService, StringComparison.Ordinal);
        Assert.Contains("SynchronizeFulfillmentAuthorityAsync", adminShipmentService, StringComparison.Ordinal);

        Assert.Contains("ReloadCarrierPersistenceAuthorityAsync", carrierService, StringComparison.Ordinal);
        Assert.Contains("RejectStaleCarrierRegistrationAuthorityAsync", carrierService, StringComparison.Ordinal);
        Assert.Contains("SynchronizeFulfillmentAuthorityAsync", carrierService, StringComparison.Ordinal);
        Assert.Contains("tenantContext.SetTenantId(candidate.TenantId)", recovery, StringComparison.Ordinal);
        Assert.Contains("shipmentService.CreateShipmentAsync", recovery, StringComparison.Ordinal);
        Assert.Contains("shipmentService.ReconcileShipmentAuthorityAsync", recovery, StringComparison.Ordinal);
        Assert.DoesNotContain("shipment.Status = ShipmentStatus", recovery, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationArtifacts_MustRecordSfAdm34AdminOrderFulfillmentShipmentAuthorityTruth()
    {
        var findings = ReadRepoFile("docs/architecture/admin-order-fulfillment-shipment-authority-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        Assert.Contains("PASS SF-ADM3.4 Admin Order Fulfillment & Shipment Authority Certification", findings, StringComparison.Ordinal);
        Assert.Contains("No runtime code change was required", findings, StringComparison.Ordinal);
        Assert.Contains("Can frontend fabricate accepted shipment? No.", findings, StringComparison.Ordinal);
        Assert.Contains("Is Fulfillment Authority production-certifiable? Yes.", findings, StringComparison.Ordinal);
        Assert.Contains("PASS SF-ADM3.4 Admin Order Fulfillment & Shipment Authority Certification", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS SF-ADM3.4 Admin Order Fulfillment & Shipment Authority Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS SF-ADM3.4 admin order fulfillment shipment runtime certifiable", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS SF-ADM3.4 Admin Order Fulfillment & Shipment Authority Certification", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS SF-ADM3.4 Admin Order Fulfillment & Shipment Authority Freeze", codexRules, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
