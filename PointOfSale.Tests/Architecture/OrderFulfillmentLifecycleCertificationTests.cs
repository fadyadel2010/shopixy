using PointOfSale.Core.Entities;
using PointOfSale.Core.Payments;

namespace PointOfSale.Tests.Architecture;

/// <summary>
/// PASS 16.5A: Order & Fulfillment Lifecycle Certification
///
/// Validates that the entire order-to-fulfillment lifecycle enforces:
/// 1. PostgreSQL-authoritative state machine transitions
/// 2. Serializable isolation with row-level locking
/// 3. Payment-before-fulfillment gates
/// 4. FulfillmentMode ownership enforcement (Manual vs Carrier)
/// 5. SaveChanges interceptor blocks unauthorized direct writes
/// 6. Domain events published atomically with state changes
/// 7. CommerceHub sub-order lifecycle with boundary mutation scopes
/// 8. Inventory commit/release on delivery/cancellation
/// 9. Idempotent replay collapse for shipment synchronization
/// 10. Financial posting gate before outbound progression
/// </summary>
public sealed class OrderFulfillmentLifecycleCertificationTests
{
    // ═══════════════════════════════════════════════════════════════
    // SECTION 1: STOREFRONT ORDER FULFILLMENT SERVICE AUTHORITY
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void FulfillmentService_MustUseSerializableIsolationAndRowLocking()
    {
        var source = ReadRepoFile("PointOfSale.Infrastructure/Fulfillment/OrderFulfillmentService.cs");

        Assert.Contains("IsolationLevel.Serializable", source, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", source, StringComparison.Ordinal);
        Assert.Contains("UPDLOCK, ROWLOCK, HOLDLOCK", source, StringComparison.Ordinal);
        Assert.Contains("BeginTransactionAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FulfillmentService_MustValidateSourceAgainstFulfillmentMode()
    {
        var source = ReadRepoFile("PointOfSale.Infrastructure/Fulfillment/OrderFulfillmentService.cs");

        Assert.Contains("source.IsAllowedFor(order.FulfillmentMode)", source, StringComparison.Ordinal);
        Assert.Contains("SECURITY_VIOLATION", source, StringComparison.Ordinal);
        Assert.Contains("FulfillmentRequestSource.Manual, FulfillmentMode.Carrier", source, StringComparison.Ordinal);
        Assert.Contains("FulfillmentRequestSource.Webhook, FulfillmentMode.Manual", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FulfillmentService_MustBlockOutboundProgressionWithoutPayment()
    {
        var source = ReadRepoFile("PointOfSale.Infrastructure/Fulfillment/OrderFulfillmentService.cs");

        Assert.Contains("RequiresRecognizedPaymentBeforeFulfillment", source, StringComparison.Ordinal);
        Assert.Contains("RequiresPostedCollectionAccountingBeforeFulfillment", source, StringComparison.Ordinal);
        Assert.Contains("Cannot advance fulfillment until payment is confirmed", source, StringComparison.Ordinal);
        Assert.Contains("Cannot advance fulfillment until the payment collection accounting journal is posted", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FulfillmentService_MustRequireEvidenceForManualDelivery()
    {
        var source = ReadRepoFile("PointOfSale.Infrastructure/Fulfillment/OrderFulfillmentService.cs");

        Assert.Contains("source == FulfillmentRequestSource.Manual", source, StringComparison.Ordinal);
        Assert.Contains("deliveryReference", source, StringComparison.Ordinal);
        Assert.Contains("proofOfDeliveryUrl", source, StringComparison.Ordinal);
        Assert.Contains("MANUAL_DELIVERY_EVIDENCE_REQUIRED", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FulfillmentService_MustPublishDomainEventsAtomicallyWithStateChange()
    {
        var source = ReadRepoFile("PointOfSale.Infrastructure/Fulfillment/OrderFulfillmentService.cs");

        Assert.Contains("_eventPublisher.PublishAsync", source, StringComparison.Ordinal);
        Assert.Contains("_db.SaveChangesAsync", source, StringComparison.Ordinal);
        Assert.Contains("transaction.CommitAsync", source, StringComparison.Ordinal);
        Assert.Contains("AcceptFulfillmentStatusChange()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FulfillmentService_MustUseInterceptorAuthorizedContext()
    {
        var source = ReadRepoFile("PointOfSale.Infrastructure/Fulfillment/OrderFulfillmentService.cs");

        Assert.Contains("FulfillmentStatusSaveChangesInterceptor.EnableAuthorizedContext()", source, StringComparison.Ordinal);
        Assert.Contains("FulfillmentStatusSaveChangesInterceptor.DisableAuthorizedContext()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FulfillmentService_MustCollapseIdempotentReplay()
    {
        var source = ReadRepoFile("PointOfSale.Infrastructure/Fulfillment/OrderFulfillmentService.cs");

        Assert.Contains("IsAtOrBeyondOutboundTarget", source, StringComparison.Ordinal);
        Assert.Contains("Replay collapsed against current SQL fulfillment authority", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FulfillmentService_ShipmentSyncMustStepThroughIntermediateStates()
    {
        var source = ReadRepoFile("PointOfSale.Infrastructure/Fulfillment/OrderFulfillmentService.cs");

        Assert.Contains("SynchronizeFromShipmentAsync", source, StringComparison.Ordinal);
        Assert.Contains("GetShipmentSynchronizationSteps", source, StringComparison.Ordinal);
        Assert.Contains("IsShipmentSynchronizationTarget", source, StringComparison.Ordinal);
        Assert.Contains("BuildShipmentSyncIdempotencyKey", source, StringComparison.Ordinal);
    }

    // ═══════════════════════════════════════════════════════════════
    // SECTION 2: FULFILLMENT STATUS INTERCEPTOR PROTECTION
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Interceptor_MustBlockUnauthorizedFulfillmentStatusChanges()
    {
        var source = ReadRepoFile("PointOfSale.Infrastructure/Data/FulfillmentStatusSaveChangesInterceptor.cs");

        Assert.Contains("FulfillmentStatusChanged", source, StringComparison.Ordinal);
        Assert.Contains("SECURITY VIOLATION", source, StringComparison.Ordinal);
        Assert.Contains("throw new InvalidOperationException", source, StringComparison.Ordinal);
        Assert.Contains("IsAuthorizedContext", source, StringComparison.Ordinal);
        Assert.Contains("AsyncLocal<bool>", source, StringComparison.Ordinal);
    }

    [Fact]
    public void OrderEntity_MustTrackFulfillmentStatusChanges()
    {
        var source = ReadRepoFile("PointOfSale.Core/Entities/Order.cs");

        Assert.Contains("_originalFulfillmentStatus", source, StringComparison.Ordinal);
        Assert.Contains("FulfillmentStatusChanged", source, StringComparison.Ordinal);
        Assert.Contains("AcceptFulfillmentStatusChange", source, StringComparison.Ordinal);
        Assert.Contains("_fulfillmentStatus", source, StringComparison.Ordinal);
    }

    // ═══════════════════════════════════════════════════════════════
    // SECTION 3: PAYMENT LIFECYCLE INTEGRATION
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void PaymentInterceptor_MustBlockUnauthorizedPaymentStatusChanges()
    {
        var source = ReadRepoFile("PointOfSale.Infrastructure/Data/PaymentStatusSaveChangesInterceptor.cs");

        Assert.Contains("PaymentStatusChanged", source, StringComparison.Ordinal);
        Assert.Contains("SECURITY_VIOLATION", source, StringComparison.Ordinal);
        Assert.Contains("throw new InvalidOperationException", source, StringComparison.Ordinal);
    }

    [Fact]
    public void OrderEntity_MustTrackPaymentStatusChanges()
    {
        var source = ReadRepoFile("PointOfSale.Core/Entities/Order.cs");

        Assert.Contains("_originalPaymentStatus", source, StringComparison.Ordinal);
        Assert.Contains("PaymentStatusChanged", source, StringComparison.Ordinal);
        Assert.Contains("AcceptPaymentStatusChange", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DeliveredEvent_MustCarryPaymentContextForCodHandling()
    {
        var source = ReadRepoFile("PointOfSale.Core/Events/OrderFulfillmentEvents.cs");

        Assert.Contains("FulfillmentDeliveredEvent", source, StringComparison.Ordinal);
        Assert.Contains("PaymentMethod", source, StringComparison.Ordinal);
        Assert.Contains("PaymentStatus", source, StringComparison.Ordinal);
        Assert.Contains("OrderTotal", source, StringComparison.Ordinal);
    }

    // ═══════════════════════════════════════════════════════════════
    // SECTION 4: FULFILLMENT STATE MACHINE RULES
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void FulfillmentStateMachine_MustEnforceAllowedTransitions()
    {
        var source = ReadRepoFile("PointOfSale.Core/Enums/FulfillmentStatus.cs");

        Assert.Contains("GetAllowedTransitions", source, StringComparison.Ordinal);
        Assert.Contains("CanTransitionTo", source, StringComparison.Ordinal);

        // Verify terminal states have no transitions
        Assert.Contains("FulfillmentStatus.Completed => Array.Empty", source, StringComparison.Ordinal);
        Assert.Contains("FulfillmentStatus.Returned => Array.Empty", source, StringComparison.Ordinal);
        Assert.Contains("FulfillmentStatus.Cancelled => Array.Empty", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FulfillmentMode_MustEnforceOwnershipRules()
    {
        var source = ReadRepoFile("PointOfSale.Core/Enums/FulfillmentMode.cs");

        Assert.Contains("Manual = 0", source, StringComparison.Ordinal);
        Assert.Contains("Carrier = 1", source, StringComparison.Ordinal);
        Assert.Contains("AllowsManualTransitions", source, StringComparison.Ordinal);
        Assert.Contains("AllowsCarrierWebhooks", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FulfillmentRequestSource_MustValidateSourceModeMatrix()
    {
        var source = ReadRepoFile("PointOfSale.Core/Enums/FulfillmentRequestSource.cs");

        Assert.Contains("IsAllowedFor", source, StringComparison.Ordinal);
        Assert.Contains("(FulfillmentRequestSource.Manual, FulfillmentMode.Manual) => true", source, StringComparison.Ordinal);
        Assert.Contains("(FulfillmentRequestSource.Manual, FulfillmentMode.Carrier) => false", source, StringComparison.Ordinal);
        Assert.Contains("(FulfillmentRequestSource.Webhook, FulfillmentMode.Manual) => false", source, StringComparison.Ordinal);
        Assert.Contains("(FulfillmentRequestSource.Webhook, FulfillmentMode.Carrier) => true", source, StringComparison.Ordinal);
        Assert.Contains("(FulfillmentRequestSource.System, _) => true", source, StringComparison.Ordinal);
    }

    // ═══════════════════════════════════════════════════════════════
    // SECTION 5: FINANCIAL AUTHORITY GATES
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void FinancialAuthority_MustBlockNonCodUnpaidOrdersFromFulfillment()
    {
        var source = ReadRepoFile("PointOfSale.Core/Payments/OrderOperationalFinancialAuthority.cs");

        Assert.Contains("RequiresRecognizedPaymentBeforeOutboundProgress", source, StringComparison.Ordinal);
        Assert.Contains("RequiresPostedCollectionAccountingBeforeOutboundProgress", source, StringComparison.Ordinal);
        Assert.Contains("IsCashOnDelivery", source, StringComparison.Ordinal);
        Assert.Contains("IsPaidState", source, StringComparison.Ordinal);
        Assert.Contains("FinancialPostingStatus.Posted", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FinancialAuthority_MustAllowCodOrdersToProgressWithoutPayment()
    {
        foreach (var paymentMethodCode in new[] { "COD", "cod", "CASHONDELIVERY", "CASH_ON_DELIVERY", "CASH-ON-DELIVERY" })
        {
            var order = new Order
            {
                PaymentMethodCode = paymentMethodCode,
                Total = 100m
            };

            Assert.True(OrderOperationalFinancialAuthority.IsCashOnDelivery(order));
            Assert.False(OrderOperationalFinancialAuthority.RequiresRecognizedPaymentBeforeOutboundProgress(order));
            Assert.False(OrderOperationalFinancialAuthority.RequiresPostedCollectionAccountingBeforeOutboundProgress(order));
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // SECTION 6: COMMERCEHUB MARKETPLACE ORDER LIFECYCLE
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void CommerceHubOrderManagement_MustUseSerializableAndRowLocking()
    {
        var source = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubOrderManagementService.cs");

        Assert.Contains("CommerceHubBoundaryMutationScope.BeginOrderLifecycle()", source, StringComparison.Ordinal);
        Assert.Contains("BeginTransactionAsync(System.Data.IsolationLevel.Serializable", source, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", source, StringComparison.Ordinal);
        Assert.Contains("LoadSubOrderForLifecycleUpdateAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubOrderManagement_MustEnforceTenantOwnership()
    {
        var source = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubOrderManagementService.cs");

        Assert.Contains("subOrder.StoreId != storeId", source, StringComparison.Ordinal);
        Assert.Contains("\"SUBORDER_FORBIDDEN\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubOrderManagement_MustValidateStateMachineTransitions()
    {
        var source = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubOrderManagementService.cs");

        Assert.Contains("CommerceHubOrderStateHelper.CanTransition", source, StringComparison.Ordinal);
        Assert.Contains("CommerceHubOrderStateHelper.IsCompletedVendorStatus", source, StringComparison.Ordinal);
        Assert.Contains("\"SUBORDER_STATUS_INVALID\"", source, StringComparison.Ordinal);
        Assert.Contains("\"SUBORDER_ALREADY_COMPLETED\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubOrderManagement_MustRequirePaymentReadinessBeforeFulfillment()
    {
        var source = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubOrderManagementService.cs");

        Assert.Contains("CommerceHubOrderStateHelper.RequiresPaymentReadiness", source, StringComparison.Ordinal);
        Assert.Contains("CommerceHubOrderStateHelper.IsReadyForFulfillment", source, StringComparison.Ordinal);
        Assert.Contains("\"SUBORDER_NOT_READY_FOR_FULFILLMENT\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubOrderManagement_MustCommitInventoryOnDeliveryAndReleaseOnCancel()
    {
        var source = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubOrderManagementService.cs");

        Assert.Contains("CommitDeliveryAsync", source, StringComparison.Ordinal);
        Assert.Contains("ReleaseOrderItemReservationsAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubOrderManagement_MustRecordStatusHistoryAndPublishEvents()
    {
        var source = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubOrderManagementService.cs");

        Assert.Contains("CommerceHubSubOrderStatusHistory", source, StringComparison.Ordinal);
        Assert.Contains("PublishStatusEventsAsync", source, StringComparison.Ordinal);
        Assert.Contains("TouchProjectionMutationVersion", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubOrderManagement_MustApplyFinancialsOnDelivery()
    {
        var source = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubOrderManagementService.cs");

        Assert.Contains("CommerceHubBoundaryMutationScope.BeginFinancialWorkflow()", source, StringComparison.Ordinal);
        Assert.Contains("ApplyDeliveredFinancialsAsync", source, StringComparison.Ordinal);
    }

    // ═══════════════════════════════════════════════════════════════
    // SECTION 7: COMMERCEHUB ORDER STATE HELPER
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void StateHelper_MustDefineCorrectTransitionMatrix()
    {
        var source = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubOrderStateHelper.cs");

        Assert.Contains("AllowedTransitions", source, StringComparison.Ordinal);
        Assert.Contains("CanTransition", source, StringComparison.Ordinal);
        Assert.Contains("AggregateMarketplaceStatus", source, StringComparison.Ordinal);
        Assert.Contains("RequiresPaymentReadiness", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StateHelper_MustCheckPaymentReadinessWithCodSupport()
    {
        var source = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubOrderStateHelper.cs");

        Assert.Contains("PaymentStatus.CashOnDelivery", source, StringComparison.Ordinal);
        Assert.Contains("PaymentStatus.Collected", source, StringComparison.Ordinal);
        Assert.Contains("PaymentStatus.Settled", source, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPaymentMethod.CashOnDelivery", source, StringComparison.Ordinal);
    }

    // ═══════════════════════════════════════════════════════════════
    // SECTION 8: ORDER ENTITY ARCHITECTURAL INVARIANTS
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void OrderEntity_MustHaveFulfillmentOwnershipFields()
    {
        var source = ReadRepoFile("PointOfSale.Core/Entities/Order.cs");

        Assert.Contains("FulfillmentMode FulfillmentMode", source, StringComparison.Ordinal);
        Assert.Contains("FulfillmentCarrierCode", source, StringComparison.Ordinal);
        Assert.Contains("AllowsManualFulfillment", source, StringComparison.Ordinal);
        Assert.Contains("AllowsCarrierWebhooks", source, StringComparison.Ordinal);
    }

    [Fact]
    public void OrderEntity_MustHaveCheckoutStateTransitions()
    {
        var source = ReadRepoFile("PointOfSale.Core/Entities/Order.cs");

        Assert.Contains("TransitionCheckoutState", source, StringComparison.Ordinal);
        Assert.Contains("IsValidCheckoutTransition", source, StringComparison.Ordinal);
        Assert.Contains("CheckoutState", source, StringComparison.Ordinal);
    }

    [Fact]
    public void OrderEntity_MustHaveProjectionVersioning()
    {
        var source = ReadRepoFile("PointOfSale.Core/Entities/Order.cs");

        Assert.Contains("IProjectionVersioned", source, StringComparison.Ordinal);
        Assert.Contains("ProjectionMutationVersion", source, StringComparison.Ordinal);
        Assert.Contains("AdvanceProjectionMutationVersion", source, StringComparison.Ordinal);
    }

    [Fact]
    public void OrderEntity_MustHaveFinancialPostingFields()
    {
        var source = ReadRepoFile("PointOfSale.Core/Entities/Order.cs");

        Assert.Contains("FinancialPostingStatus", source, StringComparison.Ordinal);
        Assert.Contains("FinancialPostingPendingAtUtc", source, StringComparison.Ordinal);
        Assert.Contains("FinancialPostedAtUtc", source, StringComparison.Ordinal);
        Assert.Contains("FinancialPostingJournalId", source, StringComparison.Ordinal);
    }

    // ═══════════════════════════════════════════════════════════════
    // SECTION 9: DOMAIN EVENT CONTRACT CERTIFICATION
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void FulfillmentEvents_MustInheritFromAuthoritativeBase()
    {
        var source = ReadRepoFile("PointOfSale.Core/Events/OrderFulfillmentEvents.cs");

        Assert.Contains("OrderFulfillmentEventBase : IntegrationEvent, IAuthoritativeIntegrationEvent", source, StringComparison.Ordinal);
        Assert.Contains("FulfillmentConfirmedEvent : OrderFulfillmentEventBase", source, StringComparison.Ordinal);
        Assert.Contains("OrderPackedEvent : OrderFulfillmentEventBase", source, StringComparison.Ordinal);
        Assert.Contains("OrderOutForDeliveryEvent : OrderFulfillmentEventBase", source, StringComparison.Ordinal);
        Assert.Contains("FulfillmentDeliveredEvent : OrderFulfillmentEventBase", source, StringComparison.Ordinal);
        Assert.Contains("OrderCompletedEvent : OrderFulfillmentEventBase", source, StringComparison.Ordinal);
        Assert.Contains("OrderReturnedEvent : OrderFulfillmentEventBase", source, StringComparison.Ordinal);
        Assert.Contains("OrderDeliveryFailedEvent : OrderFulfillmentEventBase", source, StringComparison.Ordinal);
        Assert.Contains("OrderFulfillmentCancelledEvent : OrderFulfillmentEventBase", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FulfillmentEvents_MustCarryAuditAndIdempotencyFields()
    {
        var source = ReadRepoFile("PointOfSale.Core/Events/OrderFulfillmentEvents.cs");

        Assert.Contains("FulfillmentRequestSource Source", source, StringComparison.Ordinal);
        Assert.Contains("string? IdempotencyKey", source, StringComparison.Ordinal);
        Assert.Contains("Guid OrderId", source, StringComparison.Ordinal);
        Assert.Contains("string OrderNumber", source, StringComparison.Ordinal);
        Assert.Contains("FulfillmentStatus OldStatus", source, StringComparison.Ordinal);
        Assert.Contains("FulfillmentStatus NewStatus", source, StringComparison.Ordinal);
    }

    // ═══════════════════════════════════════════════════════════════
    // SECTION 10: PAYMENT LIFECYCLE SERVICE CONTRACT
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void PaymentLifecycleService_MustHaveCodDeliveryIntegration()
    {
        var source = ReadRepoFile("PointOfSale.Core/Interfaces/Payments/IPaymentLifecycleService.cs");

        Assert.Contains("MarkOfflineCollectionDeliveredAsync", source, StringComparison.Ordinal);
        Assert.Contains("HandlePaymentResultAsync", source, StringComparison.Ordinal);
        Assert.Contains("CaptureOnlinePaymentAsync", source, StringComparison.Ordinal);
        Assert.Contains("RecordRefundAsync", source, StringComparison.Ordinal);
        Assert.Contains("MarkPaymentExpiredAsync", source, StringComparison.Ordinal);
    }

    // ═══════════════════════════════════════════════════════════════
    // SECTION 11: BOUNDARY MUTATION SCOPE CERTIFICATION
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void BoundaryMutationScope_MustSupportAllLifecycleBoundaries()
    {
        var source = ReadRepoFile("PointOfSale.Infrastructure/Data/CommerceHubBoundaryMutationScope.cs");

        Assert.Contains("BeginOrderLifecycle()", source, StringComparison.Ordinal);
        Assert.Contains("BeginPayment()", source, StringComparison.Ordinal);
        Assert.Contains("BeginRefund()", source, StringComparison.Ordinal);
        Assert.Contains("BeginPayout()", source, StringComparison.Ordinal);
        Assert.Contains("BeginFinancialWorkflow()", source, StringComparison.Ordinal);
        Assert.Contains("BeginPaymentLifecycle()", source, StringComparison.Ordinal);
        Assert.Contains("IsOrderLifecycleAuthorized", source, StringComparison.Ordinal);
    }

    // ═══════════════════════════════════════════════════════════════
    // SECTION 12: ORDER STATUS SYNC SERVICE
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void OrderStatusSyncService_MustBeEventDrivenAndForwardOnly()
    {
        var source = ReadRepoFile("PointOfSale.Core/Interfaces/IOrderStatusSyncService.cs");

        Assert.Contains("SyncFromShipmentAsync", source, StringComparison.Ordinal);
        Assert.Contains("SyncFromPaymentAsync", source, StringComparison.Ordinal);
        Assert.Contains("Only move forward", source, StringComparison.Ordinal);
        Assert.Contains("Never downgrade Delivered", source, StringComparison.Ordinal);
    }

    // ═══════════════════════════════════════════════════════════════
    // SECTION 13: COMMERCEHUB ORDER CONTROLLER AUTHORITY
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void CommerceHubOrdersController_MustEnforceTenantAndAuth()
    {
        var source = ReadRepoFile("PointOfSale.API/Controllers/CommerceHubOrdersController.cs");

        Assert.Contains("[Authorize]", source, StringComparison.Ordinal);
        Assert.Contains("_tenantContext.TenantId", source, StringComparison.Ordinal);
        Assert.Contains("UpdateSubOrderStatusAsync(", source, StringComparison.Ordinal);
    }

    // ═══════════════════════════════════════════════════════════════
    // SECTION 14: SUPER ADMIN READ-ONLY BOUNDARY
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void SuperAdminOrders_MustRemainReadOnly()
    {
        var service = ReadRepoFile("PointOfSale.API/SuperAdmin/Services/SuperAdminOrdersService.cs");

        Assert.Contains("AsNoTracking()", service, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateSubOrderStatusAsync", service, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"order\.Status\s*=[^=]", service);
        Assert.DoesNotMatch(@"subOrder\.Status\s*=[^=]", service);
    }

    // ═══════════════════════════════════════════════════════════════
    // SECTION 15: RETURN & REFUND AUTHORITY
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void ReturnAndRefund_MustUseBoundaryMutationScopes()
    {
        var refundService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubRefundService.cs");
        var refundExecution = ReadRepoFile("PointOfSale.API/Services/CommerceHub/RefundExecutionService.cs");

        foreach (var source in new[] { refundService, refundExecution })
        {
            Assert.Contains("CommerceHubBoundaryMutationScope.BeginRefund()", source, StringComparison.Ordinal);
            Assert.Contains("CommerceHubBoundaryMutationScope.BeginOrderLifecycle()", source, StringComparison.Ordinal);
            Assert.Contains("EnsureRefundCompletionDoesNotOverrunAsync", source, StringComparison.Ordinal);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // HELPER
    // ═══════════════════════════════════════════════════════════════

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
