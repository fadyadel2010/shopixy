using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PointOfSale.Tests.Architecture;

public sealed class CrossSystemCriticalRiskHardeningTests
{
    [Fact]
    public void AccountingOutboxProcessor_MustClaimMessageBeforePostingFinancialJournal()
    {
        var processor = File.ReadAllText(InfrastructurePath("Accounting", "AccountingOutboxProcessor.cs"));
        var message = File.ReadAllText(CorePath("Entities", "Accounting", "AccountingOutboxMessage.cs"));
        var migration = File.ReadAllText(InfrastructurePath("Migrations", "20260424170000_S32_AccountingOutboxProcessingLeases.sql"));

        Assert.Contains("ClaimedUntilUtc", message, StringComparison.Ordinal);
        Assert.Contains("TryClaim", message, StringComparison.Ordinal);
        Assert.Contains("freshMessage.TryClaim", processor, StringComparison.Ordinal);
        Assert.Matches(
            @"freshMessage\.TryClaim[\s\S]+SaveChangesAsync[\s\S]+ProcessMessageAsync",
            processor);
        Assert.Contains("\"ClaimedUntilUtc\"", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void AccountingOutboxService_MustRejectOutboxOnlyPostCommitEnqueue()
    {
        var source = File.ReadAllText(InfrastructurePath("Accounting", "AccountingOutboxService.cs"));

        Assert.Contains("EnsureTransactionalEnqueueBoundary", source, StringComparison.Ordinal);
        Assert.Contains("_db.Database.CurrentTransaction", source, StringComparison.Ordinal);
        Assert.Contains("hasPendingBusinessMutation", source, StringComparison.Ordinal);
        Assert.Contains("Accounting outbox enqueue must be paired with the authoritative business mutation inside an active database transaction", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubRefundService_MustPersistRefundIntentBeforeCallingGateway()
    {
        var source = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubRefundService.cs"));
        var createRefundMethod = ExtractMethodBody(source, "public async Task<CommerceHubRefundDto> CreateRefundAsync");
        var executeMethod = ExtractMethodBody(source, "ExecutePersistedRefundAsync");

        Assert.NotNull(createRefundMethod);
        Assert.NotNull(executeMethod);
        Assert.DoesNotContain("_refundGatewayService.RefundAsync", createRefundMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveGatewayExecutionResultAsync", createRefundMethod, StringComparison.Ordinal);
        AssertContainsInOrder(
            executeMethod!,
            "BeginTransactionAsync(IsolationLevel.Serializable",
            "ClaimPendingRefundForGatewayExecutionAsync",
            "CommitAsync(cancellationToken)",
            "ResolveGatewayExecutionResultAsync",
            "BeginTransactionAsync(IsolationLevel.Serializable");
        Assert.Contains("ExecuteUpdateAsync", source, StringComparison.Ordinal);
        Assert.Contains("SetProperty(refund => refund.Status, CommerceHubRefundStatus.Processing)", source, StringComparison.Ordinal);
        Assert.Contains("MarkRefundGatewayOutcomeUnknownAsync", source, StringComparison.Ordinal);
        Assert.Contains("execution_unknown", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AdminOrderStatusOverride_MustNotAdvanceOperationalLifecycle()
    {
        var source = File.ReadAllText(ApiPath("Services", "Admin", "AdminOrderService.cs"));
        var updateMethod = ExtractMethodBody(source, "public async Task<AdminOrderStatusUpdateResponse> UpdateOrderStatusAsync");
        var guardMethod = ExtractMethodBody(source, "EnsureAdminStatusOverrideDoesNotAdvanceOperationalLifecycle");

        Assert.NotNull(updateMethod);
        Assert.NotNull(guardMethod);
        Assert.Contains("OrderStatus.Completed", updateMethod!, StringComparison.Ordinal);
        Assert.Contains("OrderStatus.Refunded", updateMethod!, StringComparison.Ordinal);
        Assert.Contains("EnsureAdminStatusOverrideDoesNotAdvanceOperationalLifecycle", updateMethod!, StringComparison.Ordinal);
        Assert.Contains("requestedStatus is OrderStatus.Confirmed or OrderStatus.Processing", guardMethod!, StringComparison.Ordinal);
        Assert.Contains("OPERATIONAL_LIFECYCLE_BYPASS_ATTEMPT", guardMethod!, StringComparison.Ordinal);
        Assert.Contains("cannot advance ahead of financial truth", guardMethod!, StringComparison.Ordinal);
    }

    [Fact]
    public void PaymentIntentCreation_MustPersistLocalAuthorityBeforeProviderInitiation()
    {
        var paymentService = File.ReadAllText(ApiPath("Services", "Payments", "PaymentService.cs"));
        var stripeService = File.ReadAllText(ApiPath("Services", "Payments", "Stripe", "StripePaymentService.cs"));
        var paymobService = File.ReadAllText(ApiPath("Services", "Payments", "Paymob", "PaymobPaymentService.cs"));
        var fawryAdapter = File.ReadAllText(ApiPath("Services", "Payments", "Adapters", "FawryPaymentAdapter.cs"));
        var createMethod = ExtractMethodBody(paymentService, "public async Task<PaymentIntentResponseDto> CreatePaymentIntentAsync");
        var stripeCreateMethod = ExtractMethodBody(stripeService, "public async Task<StripePaymentIntentResponseDto> CreatePaymentIntentAsync");
        var paymobCreateMethod = ExtractMethodBody(paymobService, "public async Task<PaymobPaymentIntentResponseDto> CreatePaymentIntentAsync");

        Assert.NotNull(createMethod);
        Assert.NotNull(stripeCreateMethod);
        Assert.NotNull(paymobCreateMethod);
        AssertContainsInOrder(
            createMethod!,
            "_dbContext.PaymentIntents.Add(paymentIntent)",
            "await _dbContext.SaveChangesAsync(cancellationToken)",
            "await transaction.CommitAsync(cancellationToken)",
            "EnsurePersistedPaymentIntentReadyAsync");
        Assert.Contains("EnsurePersistedPaymentIntentReadyAsync", paymentService, StringComparison.Ordinal);
        AssertContainsInOrder(
            stripeCreateMethod!,
            "_dbContext.PaymentIntents.Add(paymentIntent)",
            "await _dbContext.SaveChangesAsync(cancellationToken)",
            "await transaction.CommitAsync(cancellationToken)",
            "CreateStripeProviderIntentAsync");
        AssertContainsInOrder(
            paymobCreateMethod!,
            "_dbContext.PaymentIntents.Add(paymentIntent)",
            "await _dbContext.SaveChangesAsync(cancellationToken)",
            "await transaction.CommitAsync(cancellationToken)",
            "AuthenticatePaymobAsync");
        Assert.Contains("IsProviderInitiationIncomplete", paymentService, StringComparison.Ordinal);
        Assert.Contains("FawryMerchantReference.Build(action)", fawryAdapter, StringComparison.Ordinal);
        Assert.DoesNotContain("Random.Shared", fawryAdapter, StringComparison.Ordinal);
    }

    [Fact]
    public void StorefrontCriticalOrderReads_MustRepairProjectionBeforeMongoRead()
    {
        var accountController = File.ReadAllText(ApiPath("Controllers", "StorefrontAccountController.cs"));
        var ordersController = File.ReadAllText(ApiPath("Controllers", "StorefrontOrdersReadController.cs"));
        var repairService = File.ReadAllText(ApiPath("Services", "Storefront", "StorefrontOrderProjectionRepairService.cs"));
        var accountOrdersMethod = ExtractMethodBody(accountController, "public async Task<IActionResult> GetOrders");
        var accountOrderMethod = ExtractMethodBody(accountController, "public async Task<IActionResult> GetOrderById");
        var storefrontOrderMethod = ExtractMethodBody(ordersController, "public async Task<IActionResult> GetOrderById");
        var storefrontTrackingMethod = ExtractMethodBody(ordersController, "public async Task<IActionResult> GetOrderTracking");
        var storefrontGuestTrackingMethod = ExtractMethodBody(ordersController, "public async Task<IActionResult> GetGuestOrderTracking");

        Assert.Contains("IStorefrontOrderProjectionRepairService", accountController, StringComparison.Ordinal);
        Assert.Contains("IStorefrontOrderProjectionRepairService", ordersController, StringComparison.Ordinal);
        Assert.Contains("EnsureCustomerOrdersProjectedPageAsync", accountController, StringComparison.Ordinal);
        Assert.Contains("EnsureOrderProjectedAsync", accountController, StringComparison.Ordinal);
        Assert.Contains("EnsureOrderProjectedAsync", ordersController, StringComparison.Ordinal);
        Assert.Contains("EnsureOrderByNumberProjectedAsync", ordersController, StringComparison.Ordinal);
        Assert.Contains("EnsureCustomerOrdersProjectedPageAsync", repairService, StringComparison.Ordinal);
        Assert.Contains("StorefrontOrderProjectionBuilder.Build", repairService, StringComparison.Ordinal);
        Assert.Contains("StorefrontOrderProjectionAuthoritySnapshotLoader.LoadAsync", repairService, StringComparison.Ordinal);
        Assert.Contains("ReplaceOneAsync", repairService, StringComparison.Ordinal);
        Assert.Contains("Skip(skip)", repairService, StringComparison.Ordinal);
        Assert.Contains("Take(normalizedPageSize)", repairService, StringComparison.Ordinal);

        Assert.NotNull(accountOrdersMethod);
        Assert.NotNull(accountOrderMethod);
        Assert.NotNull(storefrontOrderMethod);
        Assert.NotNull(storefrontTrackingMethod);
        Assert.NotNull(storefrontGuestTrackingMethod);

        AssertContainsInOrder(
            accountOrdersMethod!,
            "_dbContext.Orders",
            "CountAsync",
            "Skip(skip)",
            "EnsureCustomerOrdersProjectedPageAsync",
            "GetByIdsAsync");
        AssertContainsInOrder(
            accountOrderMethod!,
            "_dbContext.Orders",
            "EnsureOrderProjectedAsync",
            "GetByIdAsync");
        AssertContainsInOrder(
            storefrontOrderMethod!,
            "LoadOrderAuthoritySnapshotAsync",
            "TryRepairOrderProjectionAsync",
            "GetByIdAsync");
        AssertContainsInOrder(
            storefrontTrackingMethod!,
            "LoadOrderAuthoritySnapshotAsync",
            "TryRepairOrderProjectionAsync",
            "GetOrderTrackingAsync");
        AssertContainsInOrder(
            storefrontGuestTrackingMethod!,
            "LoadOrderAuthoritySnapshotAsync",
            "TryRepairOrderProjectionAsync",
            "GetOrderTrackingAsync");
        Assert.Contains("StatusCodes.Status503ServiceUnavailable", accountOrdersMethod!, StringComparison.Ordinal);
        Assert.Contains("StatusCodes.Status503ServiceUnavailable", accountOrderMethod!, StringComparison.Ordinal);
        Assert.Contains("StatusCodes.Status503ServiceUnavailable", storefrontOrderMethod!, StringComparison.Ordinal);
        Assert.Contains("StatusCodes.Status503ServiceUnavailable", storefrontTrackingMethod!, StringComparison.Ordinal);
        Assert.Contains("StatusCodes.Status503ServiceUnavailable", storefrontGuestTrackingMethod!, StringComparison.Ordinal);
        Assert.Contains("LoadOrderAuthorityByNumberSnapshotAsync", ordersController, StringComparison.Ordinal);
        Assert.Contains("LoadGuestOrderAuthorityByNumberAsync", ordersController, StringComparison.Ordinal);
        Assert.Contains("TryRepairOrderProjectionAsync", ordersController, StringComparison.Ordinal);
        Assert.Contains("TryRepairOrderByNumberProjectionAsync", ordersController, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubCriticalOrderReads_MustRepairProjectionBeforeMongoRead()
    {
        var orderController = File.ReadAllText(ApiPath("Controllers", "CommerceHubOrdersController.cs"));
        var repairService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubOrderProjectionRepairService.cs"));
        var orderMethod = ExtractMethodBody(orderController, "public async Task<IActionResult> GetOrder");

        Assert.Contains("ICommerceHubOrderProjectionRepairService", orderController, StringComparison.Ordinal);
        Assert.Contains("CommerceHubOrderProjectionRepairSupport.BuildProjectionDocument", repairService, StringComparison.Ordinal);
        Assert.Contains("BuildProjectionRecoveryFilter", repairService, StringComparison.Ordinal);
        Assert.Contains("ReplaceOneAsync", repairService, StringComparison.Ordinal);
        Assert.Contains("MarkProjectedAsync", repairService, StringComparison.Ordinal);

        Assert.NotNull(orderMethod);
        Assert.Contains("CommerceHubOrderAccessCookie.ReadOrderAccessToken", orderController, StringComparison.Ordinal);
        Assert.Contains("if (!authenticatedCustomerId.HasValue && !hasToken)", orderMethod!, StringComparison.Ordinal);
        Assert.Contains("LoadOrderAuthoritySnapshotAsync", orderMethod!, StringComparison.Ordinal);
        Assert.Contains("ValidateAccessToken(", orderMethod!, StringComparison.Ordinal);
        Assert.Contains("authority.OrderNumber", orderMethod!, StringComparison.Ordinal);
        AssertContainsInOrder(
            orderMethod!,
            "LoadOrderAuthoritySnapshotAsync",
            "ValidateAccessToken(",
            "EnsureOrderProjectedAsync",
            "GetByMarketplaceOrderIdAsync");
    }

    [Fact]
    public void CommerceHubReadinessGate_MustScopeBacklogInspectionToTenantBeforeFailingClosed()
    {
        var gate = File.ReadAllText(ApiPath("Services", "CommerceHub", "Finance", "CommerceHubFinancialReadinessGate.cs"));

        Assert.Contains("message.TenantId == tenantId ||", gate, StringComparison.Ordinal);
        Assert.Contains("message.AffectedStoreIdsJson.Contains(tenantIdText)", gate, StringComparison.Ordinal);
        Assert.Contains("return !foundAnyScope && (!messageTenantId.HasValue || messageTenantId.Value == Guid.Empty);", gate, StringComparison.Ordinal);
        Assert.Contains("webhook.SuspenseRoutedStoreIdsJson.Contains(tenantIdText)", gate, StringComparison.Ordinal);
        Assert.Contains("message.EventPayload.Contains(tenantIdText)", gate, StringComparison.Ordinal);
        Assert.Contains("message.Body.Contains(tenantIdText)", gate, StringComparison.Ordinal);
        Assert.Contains("message.PayloadJson.Contains(tenantIdText)", gate, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubReadinessGate_MustFailClosedOnUnscopedUnresolvedExternalMoney()
    {
        var gate = File.ReadAllText(ApiPath("Services", "CommerceHub", "Finance", "CommerceHubFinancialReadinessGate.cs"));
        var outwardMethod = ExtractMethodBody(gate, "public async Task EnsureOutwardMoneyMovementAllowedAsync");
        var intakeMethod = ExtractMethodBody(gate, "public async Task EnsureCommerceIntakeAllowedAsync");

        Assert.NotNull(outwardMethod);
        Assert.NotNull(intakeMethod);
        Assert.Contains("EnsureUnscopedUnresolvedExternalMoneyHealthyAsync", outwardMethod!, StringComparison.Ordinal);
        Assert.Contains("EnsureUnscopedUnresolvedExternalMoneyHealthyAsync", intakeMethod!, StringComparison.Ordinal);
        Assert.Contains("EnsureUnscopedUnresolvedWebhookHealthyAsync", gate, StringComparison.Ordinal);
        Assert.Contains("webhook.TenantId == null || webhook.TenantId == Guid.Empty", gate, StringComparison.Ordinal);
        Assert.Contains("HasDurableTenantScope", gate, StringComparison.Ordinal);
        Assert.Contains("unresolved external-money authority has no durable tenant scope", gate, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EcommerceRefundAccounting_MustTrackRefundPostingSeparatelyFromCollectionPosting()
    {
        var refund = File.ReadAllText(CorePath("Entities", "OrderRefund.cs"));
        var lifecycle = File.ReadAllText(InfrastructurePath("Payments", "PaymentLifecycleService.cs"));
        var processor = File.ReadAllText(InfrastructurePath("Accounting", "AccountingOutboxProcessor.cs"));
        var dbContext = File.ReadAllText(InfrastructurePath("Data", "ApplicationDbContext.cs"));
        var migration = File.ReadAllText(InfrastructurePath("Migrations", "20260505100000_S71_OrderRefundFinancialPostingStatus.sql"));

        Assert.Contains("FinancialPostingStatus", refund, StringComparison.Ordinal);
        Assert.Contains("FinancialPostingPendingAtUtc", refund, StringComparison.Ordinal);
        Assert.Contains("FinancialPostingFailureReason", refund, StringComparison.Ordinal);
        Assert.Contains("FinancialPostingStatus = FinancialPostingStatus.Pending", lifecycle, StringComparison.Ordinal);
        Assert.Contains("MarkOrderRefundFinancialPostingPostedAsync", processor, StringComparison.Ordinal);
        Assert.Contains("MarkOrderRefundFinancialPostingFailedAsync", processor, StringComparison.Ordinal);
        Assert.Contains("IsOrderRefundPostingEvent", processor, StringComparison.Ordinal);
        Assert.Contains("CK_OrderRefunds_FinancialPostingStatus_Valid", dbContext, StringComparison.Ordinal);
        Assert.Contains("IX_OrderRefunds_TenantId_FinancialPostingStatus_RefundedAtUtc", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubGatewayRefundExecution_MustPassOutwardMoneyReadinessGate()
    {
        var source = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubRefundService.cs"));
        var executeMethod = ExtractMethodBody(source, "ExecutePersistedRefundAsync");
        var resolveMethod = ExtractMethodBody(source, "ResolveGatewayExecutionResultAsync");

        Assert.NotNull(executeMethod);
        Assert.NotNull(resolveMethod);
        Assert.Contains("ICommerceHubFinancialReadinessGate", source, StringComparison.Ordinal);
        AssertContainsInOrder(
            executeMethod!,
            "ResolveRefundPaymentAsync",
            "EnsureOutwardMoneyMovementAllowedAsync",
            "GetStateSnapshotAsync",
            "ResolveGatewayExecutionResultAsync");
        Assert.Contains("RefreshRefundStatusAsync", resolveMethod!, StringComparison.Ordinal);
        Assert.Contains("_refundGatewayService.RefundAsync", resolveMethod!, StringComparison.Ordinal);
        Assert.Contains("\"CommerceHubRefund.GatewayExecute\"", executeMethod!, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubCheckoutAndCart_MustUseCentralizedPriceAuthority()
    {
        var checkout = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubCheckoutService.cs"));
        var cart = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubCartService.cs"));

        Assert.Contains("_priceAuthority.ResolveAsync(variant, listing, cancellationToken)", checkout, StringComparison.Ordinal);
        Assert.Contains("_priceAuthority.ResolveAsync(variant, listing, cancellationToken)", cart, StringComparison.Ordinal);
        Assert.DoesNotContain("CommerceHubPrice ?? variant.EffectiveOnlinePrice", checkout, StringComparison.Ordinal);
        Assert.DoesNotContain("CommerceHubPrice ?? variant.EffectiveOnlinePrice", cart, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubPaymentWebhook_MustReconcileFailureBeforeSuccessForSameProviderIntent()
    {
        var source = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPaymentService.cs"));

        Assert.Contains("CanReconcileProcessedFailureToSuccess", source, StringComparison.Ordinal);
        Assert.Contains("isReconcilingEarlierFailure", source, StringComparison.Ordinal);
        Assert.Contains("CanAcceptSuccessTransactionAfterFailure", source, StringComparison.Ordinal);
        Assert.Contains("payment.Status == CommerceHubPaymentRecordStatus.Failed", source, StringComparison.Ordinal);
        Assert.Contains("reconciling earlier failed provider event", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CommerceHubPaymentSuccess_MustNotCreateFinancialTruthWithoutActiveInventoryReservation()
    {
        var paymentService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPaymentService.cs"));
        var inventoryService = File.ReadAllText(ApiPath("Services", "CommerceHub", "InventoryConsistencyService.cs"));

        Assert.Contains("EnsureMarketplaceOrderReservationsActiveAsync", inventoryService, StringComparison.Ordinal);
        Assert.Contains("MARKETPLACE_ORDER_RESERVATION_MISSING", inventoryService, StringComparison.Ordinal);
        Assert.Contains("Captured payment queued for refund compensation", paymentService, StringComparison.Ordinal);

        AssertContainsInOrder(
            paymentService,
            "EnsureMarketplaceOrderReservationsActiveAsync(",
            "ApplyOnlinePaymentSuccessAsync(");

        Assert.Contains("ReleaseMarketplaceOrderReservationsAsync(", paymentService, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPaymentFailedWebhook", paymentService, StringComparison.Ordinal);
        Assert.Contains("Reserved stock was released immediately", paymentService, StringComparison.Ordinal);
        Assert.DoesNotContain("reserved stock is retained until reservation expiry", paymentService, StringComparison.Ordinal);
    }

    [Fact]
    public void StorefrontShipmentAuthority_MustBeDeferredUntilOrderConfirmationOrPayment()
    {
        var checkout = File.ReadAllText(ApiPath("Services", "Checkout", "CheckoutService.cs"));
        var shipmentService = File.ReadAllText(ApiPath("Services", "Shipping", "ShipmentService.cs"));
        var consumer = File.ReadAllText(ApiPath("Consumers", "OrderShipmentOrchestrationConsumer.cs"));

        Assert.DoesNotContain("CreateShipmentInTransactionAsync", checkout, StringComparison.Ordinal);
        Assert.DoesNotContain("EmitShipmentCreatedEventAsync(", checkout, StringComparison.Ordinal);
        Assert.DoesNotContain("EmitShipmentExternalRegistrationRequestedEventAsync(", checkout, StringComparison.Ordinal);
        Assert.Contains("IConsumer<OrderConfirmedEvent>", consumer, StringComparison.Ordinal);
        Assert.Contains("IConsumer<OrderPaidEvent>", consumer, StringComparison.Ordinal);
        Assert.Contains("CreateShipmentAsync(", consumer, StringComparison.Ordinal);
        Assert.Contains("ShipmentStatus.PendingExternalRegistration", shipmentService, StringComparison.Ordinal);
        Assert.Contains("OrderOperationalFinancialAuthority.CanAdvanceOutboundProgress(order)", shipmentService, StringComparison.Ordinal);
    }

    [Fact]
    public void ShipmentCancellationCompensation_MustFollowAuthoritativeOrderCancellation()
    {
        var lifecycle = File.ReadAllText(InfrastructurePath("Payments", "PaymentLifecycleService.cs"));
        var consumer = File.ReadAllText(ApiPath("Consumers", "OrderShipmentOrchestrationConsumer.cs"));

        Assert.Contains("new OrderCancelledEvent", lifecycle, StringComparison.Ordinal);
        Assert.Contains("order.Status == OrderStatus.Cancelled", lifecycle, StringComparison.Ordinal);
        Assert.Contains("IConsumer<OrderCancelledEvent>", consumer, StringComparison.Ordinal);
        Assert.Contains("_carrierShipmentService.CancelShipmentAsync", consumer, StringComparison.Ordinal);
        Assert.Contains("GetShipmentByOrderIdAsync", consumer, StringComparison.Ordinal);
    }

    [Fact]
    public void ShipmentCancellationCompensation_MustPersistReconciliationMarkerWhenCarrierCannotCancel()
    {
        var consumer = File.ReadAllText(ApiPath("Consumers", "OrderShipmentOrchestrationConsumer.cs"));

        Assert.Contains("MarkShipmentCancellationReconciliationRequiredAsync", consumer, StringComparison.Ordinal);
        Assert.Contains("ExternalRegistrationState = \"ReconciliationRequired\"", consumer, StringComparison.Ordinal);
        Assert.Contains("SHIPMENT_CANCELLATION_RECONCILIATION_REQUIRED", consumer, StringComparison.Ordinal);
        Assert.Contains("LogCritical", consumer, StringComparison.Ordinal);
    }

    [Fact]
    public void ShipmentAndFulfillment_MustRejectOperationalProgressWithoutFinancialAuthority()
    {
        var statusSync = File.ReadAllText(ApiPath("Services", "OrderStatusSyncService.cs"));
        var shipmentService = File.ReadAllText(ApiPath("Services", "Shipping", "ShipmentService.cs"));
        var adminShipmentService = File.ReadAllText(ApiPath("Services", "Admin", "AdminShipmentService.cs"));
        var carrierService = File.ReadAllText(InfrastructurePath("Shipping", "CarrierShipmentService.cs"));
        var fulfillment = File.ReadAllText(InfrastructurePath("Fulfillment", "OrderFulfillmentService.cs"));

        Assert.Contains("Blocked shipment-driven promotion of pending order", statusSync, StringComparison.Ordinal);
        Assert.Contains("Blocked shipment-driven operational promotion without financial authority", statusSync, StringComparison.Ordinal);
        Assert.Contains("EnsureOutboundShipmentFinancialAuthority", shipmentService, StringComparison.Ordinal);
        Assert.Contains("OrderOperationalFinancialAuthority", shipmentService, StringComparison.Ordinal);
        Assert.Contains("FINANCIAL_AUTHORITY_REQUIRED", adminShipmentService, StringComparison.Ordinal);
        Assert.Contains("EnsureOutboundShipmentFinancialAuthority", adminShipmentService, StringComparison.Ordinal);
        Assert.Contains("ShipmentStatus.PendingExternalRegistration", carrierService, StringComparison.Ordinal);
        Assert.Contains("OrderOperationalFinancialAuthority.CanAdvanceOutboundProgress(order)", carrierService, StringComparison.Ordinal);
        Assert.Contains("EnsureOutboundShipmentFinancialAuthority", carrierService, StringComparison.Ordinal);
        Assert.Contains("RequiresRecognizedPaymentBeforeFulfillment", fulfillment, StringComparison.Ordinal);
        Assert.Contains("Cannot advance fulfillment until payment is confirmed.", fulfillment, StringComparison.Ordinal);
    }

    [Fact]
    public void ShipmentAuthorityRecovery_MustRepairMissingShipmentAndPendingCarrierRegistrationGaps()
    {
        var worker = File.ReadAllText(ApiPath("Services", "Shipping", "ShipmentAuthorityRecoveryHostedService.cs"));
        var program = File.ReadAllText(ApiPath("Program.cs"));

        Assert.Contains("RecoverMissingShipmentsAsync", worker, StringComparison.Ordinal);
        Assert.Contains("RecoverPendingCarrierRegistrationsAsync", worker, StringComparison.Ordinal);
        Assert.Contains("RecoverShipmentAuthorityGapsAsync", worker, StringComparison.Ordinal);
        Assert.Contains("CreateShipmentAsync(", worker, StringComparison.Ordinal);
        Assert.Contains("ExecuteCarrierForExistingShipmentAsync(", worker, StringComparison.Ordinal);
        Assert.Contains("ReconcileShipmentAuthorityAsync(", worker, StringComparison.Ordinal);
        Assert.Contains("ShipmentAuthorityRecoveryHostedService", worker, StringComparison.Ordinal);
        Assert.Contains("AddHostedService<PointOfSale.API.Services.Shipping.ShipmentAuthorityRecoveryHostedService>", program, StringComparison.Ordinal);
    }

    [Fact]
    public void ShipmentStatusAuthority_MustPublishShipmentOrderAndFulfillmentBridgesBeforeCommit()
    {
        var shipmentService = File.ReadAllText(ApiPath("Services", "Shipping", "ShipmentService.cs"));
        var updateMethod = ExtractMethodBody(shipmentService, "public async Task<ShipmentStatusUpdateResponseDto> UpdateStatusAsync");

        Assert.NotNull(updateMethod);
        Assert.Contains("PrepareShipmentAuthorityContextAsync", shipmentService, StringComparison.Ordinal);
        Assert.Contains("SynchronizeFulfillmentAuthorityAsync", shipmentService, StringComparison.Ordinal);
        Assert.Contains("_fulfillmentService.SynchronizeFromShipmentAsync", shipmentService, StringComparison.Ordinal);
        Assert.DoesNotContain("new FulfillmentDeliveredEvent", shipmentService, StringComparison.Ordinal);
        Assert.Contains("EventVersion = authorityContext.OrderEventVersion", shipmentService, StringComparison.Ordinal);
        AssertContainsInOrder(
            updateMethod!,
            "PrepareShipmentAuthorityContextAsync(",
            "SynchronizeFulfillmentAuthorityAsync(",
            "EmitStatusChangedEventAsync(",
            "await _dbContext.SaveChangesAsync(cancellationToken)",
            "await transaction.CommitAsync(cancellationToken)");
    }

    [Fact]
    public void CarrierShipmentAuthority_MustVersionAndPublishBridgesBeforeSave()
    {
        var carrierService = File.ReadAllText(InfrastructurePath("Shipping", "CarrierShipmentService.cs"));

        Assert.Contains("PrepareShipmentAuthorityContextAsync", carrierService, StringComparison.Ordinal);
        Assert.Contains("SynchronizeFulfillmentAuthorityAsync", carrierService, StringComparison.Ordinal);
        Assert.Contains("_fulfillmentService.SynchronizeFromShipmentAsync", carrierService, StringComparison.Ordinal);
        Assert.DoesNotContain("new FulfillmentDeliveredEvent", carrierService, StringComparison.Ordinal);
        Assert.Contains("new OrderDeliveredEvent", carrierService, StringComparison.Ordinal);
        Assert.Contains("new OrderShippedEvent", carrierService, StringComparison.Ordinal);
        Assert.Contains("EventVersion = authorityContext.OrderEventVersion", carrierService, StringComparison.Ordinal);
        Assert.DoesNotContain("PublishStatusChangeEventSafelyAsync", carrierService, StringComparison.Ordinal);

        Assert.Matches(
            @"PrepareShipmentAuthorityContextAsync[\s\S]+EmitStatusChangeEventAsync[\s\S]+SaveChangesAsync",
            carrierService);
    }

    [Fact]
    public void OrderStatusAuthority_MustBeFailClosedByDefault()
    {
        var interceptorSource = File.ReadAllText(InfrastructurePath("Data", "OrderStatusSaveChangesInterceptor.cs"));

        // Verify enforcement is enabled by default (fail-closed)
        Assert.Contains("private static volatile bool _enforcementEnabled = true;", interceptorSource, StringComparison.Ordinal);
        
        // Verify documentation mentions fail-closed behavior
        Assert.Contains("Enabled by default (fail-closed)", interceptorSource, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubInventoryMutations_MustJoinOuterCheckoutAndPaymentTransactions()
    {
        var source = File.ReadAllText(ApiPath("Services", "CommerceHub", "InventoryConsistencyService.cs"));
        var method = ExtractMethodBody(source, "ExecuteInTransactionAsync<T>");

        Assert.NotNull(method);
        Assert.Contains("_dbContext.Database.CurrentTransaction == null", method!, StringComparison.Ordinal);
        Assert.Contains("if (!ownsTransaction)", method!, StringComparison.Ordinal);
        AssertContainsInOrder(
            method!,
            "var ownsTransaction",
            "if (!ownsTransaction)",
            "return await action();",
            "BeginTransactionAsync");
    }

    [Fact]
    public void CommerceHubPayoutCompletion_MustReconcileAlreadyExecutedExternalTransfers()
    {
        var payoutService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPayoutService.cs"));
        var dto = File.ReadAllText(ApiPath("DTOs", "CommerceHub", "CommerceHubFinanceDtos.cs"));
        var markCompletedMethod = ExtractMethodBody(payoutService, "public async Task<CommerceHubPayoutDto> MarkCompletedAsync");

        Assert.NotNull(markCompletedMethod);
        Assert.DoesNotContain("EnsureOutwardMoneyMovementAllowedAsync", markCompletedMethod!, StringComparison.Ordinal);
        Assert.DoesNotContain("PAYOUT_BLOCKED_BY_REFUND_LOCK", markCompletedMethod!, StringComparison.Ordinal);
        Assert.Contains("may already have executed", markCompletedMethod!, StringComparison.Ordinal);
        Assert.Contains("ExternalTransferConfirmedExecuted", dto, StringComparison.Ordinal);
        Assert.Contains("EnsureExternalTransferExecutionConfirmed", payoutService, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_EXTERNAL_TRANSFER_EXECUTION_CONFIRMATION_REQUIRED", payoutService, StringComparison.Ordinal);
        AssertContainsInOrder(
            markCompletedMethod!,
            "EnsureExternalTransferExecutionConfirmed",
            "CapturePayoutFinancialStateForCompletionAsync",
            "EnsureTerminalPayoutEvidence",
            "RecordPayoutAsync",
            "VerifyStateTransitionAsync");
    }

    [Fact]
    public void PayoutReconciliation_MustNotBeBlockedByVendorGovernanceAfterExternalHandoff()
    {
        var payoutService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPayoutService.cs"));
        var superAdminOrchestration = File.ReadAllText(ApiPath("SuperAdmin", "Services", "SuperAdminPayoutOrchestrationService.cs"));
        var markCompletedMethod = ExtractMethodBody(payoutService, "public async Task<CommerceHubPayoutDto> MarkCompletedAsync");
        var markFailedMethod = ExtractMethodBody(payoutService, "public async Task<CommerceHubPayoutDto> MarkFailedAsync");
        var superAdminCompleteMethod = ExtractMethodBody(superAdminOrchestration, "public async Task<CommerceHubPayoutDto> CompleteAsync");

        Assert.NotNull(markCompletedMethod);
        Assert.NotNull(markFailedMethod);
        Assert.NotNull(superAdminCompleteMethod);
        Assert.DoesNotContain("EnsureVendorPayoutAllowedAsync", markCompletedMethod!, StringComparison.Ordinal);
        Assert.Contains("if (!IsExternallyLockedPayoutStatus(payout.Status))", markFailedMethod!, StringComparison.Ordinal);
        AssertContainsInOrder(
            markFailedMethod!,
            "var payout = await LoadTrackedPayoutAsync",
            "PAYOUT_EXTERNAL_TRANSFER_RECONCILIATION_REQUIRED",
            "EnsureProcessingFailureNonExecutionEvidence",
            "if (!IsExternallyLockedPayoutStatus(payout.Status))",
            "EnsureVendorPayoutAllowedAsync");
        Assert.DoesNotContain("EnsureVendorPayoutAllowedAsync", superAdminCompleteMethod!, StringComparison.Ordinal);
        Assert.Contains("_canonicalPayoutService.MarkCompletedAsync", superAdminCompleteMethod!, StringComparison.Ordinal);
        Assert.Contains("blocking it here would desynchronize bank/provider cash", payoutService, StringComparison.Ordinal);
    }

    [Fact]
    public void StockSyncOutboxEventIdentity_MustBeWarehouseScoped()
    {
        var entity = File.ReadAllText(CorePath("Entities", "StockSyncOutboxMessage.cs"));
        var dbContext = File.ReadAllText(InfrastructurePath("Data", "ApplicationDbContext.cs"));
        var migration = File.ReadAllText(InfrastructurePath("Migrations", "20260501213000_S68_StockSyncOutboxWarehouseScopedEventIdentity.sql"));

        Assert.Contains("public Guid WarehouseId", entity, StringComparison.Ordinal);
        Assert.Contains("WarehouseId = @event.WarehouseId", entity, StringComparison.Ordinal);
        Assert.Contains("UX_StockSyncOutboxMessages_Tenant_Variant_Warehouse_EventVersion", dbContext, StringComparison.Ordinal);
        Assert.Contains("e.WarehouseId", dbContext, StringComparison.Ordinal);
        Assert.Contains("\"TenantId\", \"VariantId\", \"WarehouseId\", \"EventVersion\"", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("CREATE UNIQUE INDEX IF NOT EXISTS \"UX_StockSyncOutboxMessages_Tenant_Variant_EventVersion\"", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubDuplicateCapturedPayment_MustPersistRefundCompensationOutbox()
    {
        var paymentService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPaymentService.cs"));
        var worker = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPaymentCompensationOutboxWorker.cs"));
        var entity = File.ReadAllText(CorePath("Entities", "CommerceHubPaymentCompensationOutboxMessage.cs"));
        var dbContext = File.ReadAllText(InfrastructurePath("Data", "ApplicationDbContext.cs"));
        var migration = File.ReadAllText(InfrastructurePath("Migrations", "20260428143000_S46_CommerceHubPaymentCompensationOutbox.sql"));
        var program = File.ReadAllText(ApiPath("Program.cs"));

        Assert.Contains("RefundRequired", paymentService, StringComparison.Ordinal);
        Assert.Contains("MarkPaymentRequiresCompensation", paymentService, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPaymentCompensationOutboxMessages.Add", paymentService, StringComparison.Ordinal);
        Assert.Contains("BuildPaymentCompensationIdempotencyKey", paymentService, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPaymentCompensationOutboxMessage", entity, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPaymentCompensationOutboxMessages", dbContext, StringComparison.Ordinal);
        Assert.Contains("UX_CHPaymentCompOutbox_IdempotencyKey", migration, StringComparison.Ordinal);
        Assert.Contains("refundGateway.RefundAsync", worker, StringComparison.Ordinal);
        Assert.Contains("AddHostedService<PointOfSale.API.Services.CommerceHub.CommerceHubPaymentCompensationOutboxWorker>", program, StringComparison.Ordinal);
    }

    [Fact]
    public void MarketplaceProjectionCompensation_MustPreserveOriginalBackbonePayloadBeforeCurrentSqlRebuild()
    {
        var source = File.ReadAllText(InfrastructurePath("Eventing", "ProjectionFailureCompensation.cs"));
        var compensateMethod = ExtractMethodBody(source, "CompensateMarketplaceFailureAsync");

        Assert.NotNull(compensateMethod);
        Assert.Contains("TryBuildOriginalMarketplaceReplay", source, StringComparison.Ordinal);
        AssertContainsInOrder(
            compensateMethod!,
            "TryBuildOriginalMarketplaceReplay(message)",
            "BuildMarketplaceReplayAsync(message, cancellationToken)",
            "RefreshForAuthoritativeRetry(");
        Assert.Contains("TryDeserializeExistingPayload<MarketplaceEvents.PaymentCompletedEvent>", source, StringComparison.Ordinal);
        Assert.Contains("BuildOriginalPayloadReplay(message, aggregateId, nameof(Order))", source, StringComparison.Ordinal);
    }

    [Fact]
    public void InventoryReservationCleanup_MustHoldCommerceHubReservationsWhileProviderCashAuthorityIsUnresolved()
    {
        var job = File.ReadAllText(InfrastructurePath("Jobs", "InventoryReservationCleanupJob.cs"));
        var options = File.ReadAllText(InfrastructurePath("Observability", "InventoryReservationMetricsService.cs"));
        var program = File.ReadAllText(ApiPath("Program.cs"));
        var method = ExtractMethodBody(job, "ShouldReleaseCommerceHubReservation");

        Assert.NotNull(method);
        Assert.Contains("ProviderReconciliationGraceMinutes", options, StringComparison.Ordinal);
        Assert.Contains("ProviderReconciliationGraceMinutes", program, StringComparison.Ordinal);
        Assert.Contains("HasBlockingUnresolvedWebhook", job, StringComparison.Ordinal);
        Assert.Contains("IsProviderSessionAuthorityUncertain", job, StringComparison.Ordinal);
        AssertContainsInOrder(
            method!,
            "if (orderState.HasBlockingUnresolvedWebhook)",
            "var hasProviderCashAuthorityGap =",
            "var hasRecentlyActivePayment =");
        Assert.Contains("LatestPaymentProviderIntentId", job, StringComparison.Ordinal);
        Assert.Contains("LatestPaymentProviderTransactionId", job, StringComparison.Ordinal);
    }

    [Fact]
    public void StorefrontProviderRefundWebhooks_MustReconcileThroughPaymentLifecycle()
    {
        var stripe = File.ReadAllText(ApiPath("Services", "Payments", "Stripe", "StripePaymentService.cs"));
        var paymob = File.ReadAllText(ApiPath("Services", "Payments", "Paymob", "PaymobPaymentService.cs"));

        var stripeRefundMethod = ExtractMethodBody(stripe, "HandleChargeRefundedAsync");
        var paymobRefundMethod = ExtractMethodBody(paymob, "HandlePaymentRefundedAsync");

        Assert.NotNull(stripeRefundMethod);
        Assert.NotNull(paymobRefundMethod);
        Assert.DoesNotContain("not implemented", stripeRefundMethod!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("_paymentLifecycleService.RecordRefundAsync", stripeRefundMethod!, StringComparison.Ordinal);
        Assert.Contains("ResolveStripeCumulativeRefundDeltaAsync", stripe, StringComparison.Ordinal);
        Assert.Contains("throw new InvalidOperationException", stripeRefundMethod!, StringComparison.Ordinal);
        Assert.Contains("finality == PaymobTransactionFinality.Refunded", paymob, StringComparison.Ordinal);
        Assert.Contains("if (!CanReconcileProviderRefund(intent.Order.PaymentStatus))", paymob, StringComparison.Ordinal);
        Assert.Contains("_paymentLifecycleService.RecordRefundAsync", paymobRefundMethod!, StringComparison.Ordinal);
        Assert.Contains("ResolvePaymobCumulativeRefundDeltaAsync", paymob, StringComparison.Ordinal);
        Assert.Contains("PaymentStatus.Collected", paymob, StringComparison.Ordinal);
        Assert.Contains("PaymentStatus.Settled", paymob, StringComparison.Ordinal);
        Assert.Contains("PaymentStatus.PartiallyRefunded", paymob, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubProviderRefundWebhooks_MustQueueConfirmedAccountingWithoutCallingGatewayAgain()
    {
        var paymentService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPaymentService.cs"));
        var stripeAdapter = File.ReadAllText(ApiPath("Services", "Payments", "Stripe", "StripeAdapter.cs"));
        var worker = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPaymentCompensationOutboxWorker.cs"));

        var applyRefundMethod = ExtractMethodBody(paymentService, "ApplyProviderRefundWebhookAsync");
        var markRefundMethod = ExtractMethodBody(paymentService, "MarkPaymentProviderRefundConfirmedAsync");

        Assert.NotNull(applyRefundMethod);
        Assert.NotNull(markRefundMethod);
        Assert.Contains("charge.refunded", stripeAdapter, StringComparison.Ordinal);
        Assert.Contains("callback.IsRefunded", paymentService, StringComparison.Ordinal);
        Assert.Contains("providerAmountIsCumulativeRefunded", applyRefundMethod!, StringComparison.Ordinal);
        Assert.Contains("processedPayment.Amount - processedPayment.RefundedAmount", applyRefundMethod!, StringComparison.Ordinal);
        Assert.Contains("PAYMENT_REFUND_AMOUNT_EXCEEDS_REMAINING", applyRefundMethod!, StringComparison.Ordinal);
        Assert.Contains("GatewayStatus = \"confirmed\"", markRefundMethod!, StringComparison.Ordinal);
        Assert.Contains("GatewayReference = refundReferenceId", markRefundMethod!, StringComparison.Ordinal);
        Assert.Contains("BuildPaymentProviderRefundCompensationIdempotencyKey", paymentService, StringComparison.Ordinal);
        Assert.Contains("RefundAllocationSnapshotJson = SerializeRefundAllocationSnapshot", markRefundMethod!, StringComparison.Ordinal);
        Assert.Contains("BuildProviderRefundAllocationSnapshotAsync", paymentService, StringComparison.Ordinal);
        Assert.Contains("BuildGatewayResultFromPersistedOutcome(message, payment.Provider)", worker, StringComparison.Ordinal);
        Assert.Contains("ReconcileConfirmedProviderRefundAsync", worker, StringComparison.Ordinal);
        Assert.Contains("RecordCompensationRefundAccountingAsync", worker, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubPaymentCompensation_MustNotMarkPendingGatewayRefundAsProcessed()
    {
        var worker = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPaymentCompensationOutboxWorker.cs"));
        var gateway = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubRefundGatewayService.cs"));

        Assert.Contains("if (result.IsConfirmed)", worker, StringComparison.Ordinal);
        Assert.Contains("HasDurableGatewayOutcome(message)", worker, StringComparison.Ordinal);
        Assert.Contains("RefreshRefundStatusAsync", worker, StringComparison.Ordinal);
        Assert.Contains("RefreshRefundStatusAsync", gateway, StringComparison.Ordinal);
        Assert.Contains("_refundGateway.RefreshRefundStatusAsync", gateway, StringComparison.Ordinal);
        Assert.Contains("BuildGatewayResultFromPersistedOutcome(message, payment.Provider)", worker, StringComparison.Ordinal);
        Assert.Contains("await PersistGatewayOutcomeAsync(dbContext, message, result, cancellationToken)", worker, StringComparison.Ordinal);
        Assert.Contains("dbContext.ChangeTracker.Clear();", worker, StringComparison.Ordinal);
        Assert.Contains("FirstOrDefaultAsync(item => item.Id == message.Id, cancellationToken)", worker, StringComparison.Ordinal);
        Assert.Contains("message.ProcessedAtUtc = null", worker, StringComparison.Ordinal);
        Assert.Contains("still requires gateway confirmation", worker, StringComparison.Ordinal);
        Assert.Contains("message.NextRetryAtUtc = DateTime.UtcNow.AddMinutes(15)", worker, StringComparison.Ordinal);
        Assert.Contains("EscalationAttemptThreshold", worker, StringComparison.Ordinal);
        Assert.Contains("message.IsFailed = false", worker, StringComparison.Ordinal);
        Assert.DoesNotContain("payment.Status = CommerceHubPaymentRecordStatus.RefundFailed", worker, StringComparison.Ordinal);
        Assert.DoesNotContain("!other.IsFailed", worker, StringComparison.Ordinal);

        var persistGatewayOutcomeMethod = ExtractMethodBody(worker, "PersistGatewayOutcomeAsync");
        Assert.NotNull(persistGatewayOutcomeMethod);
        Assert.DoesNotContain("message.ProcessedAtUtc", persistGatewayOutcomeMethod!, StringComparison.Ordinal);
        Assert.DoesNotContain("message.ProcessingLeaseId = null", persistGatewayOutcomeMethod!, StringComparison.Ordinal);

        var uncertainOutcomeMethod = ExtractMethodBody(worker, "RecordGatewayOutcomeUncertainAsync");
        Assert.NotNull(uncertainOutcomeMethod);
        Assert.Contains("message.RequiresOperatorReconciliation = true", uncertainOutcomeMethod!, StringComparison.Ordinal);
        Assert.Contains("message.IsFailed = true", uncertainOutcomeMethod!, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubRefunds_MustAutoRecoverStaleProcessingState()
    {
        var service = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubRefundService.cs"));
        var worker = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubRefundRecoveryHostedService.cs"));
        var program = File.ReadAllText(ApiPath("Program.cs"));

        Assert.Contains("ReconcileProcessingRefundsAsync", service, StringComparison.Ordinal);
        Assert.Contains("RecoverProcessingRefundWithLockAsync", service, StringComparison.Ordinal);
        Assert.Contains("TryAcquireLockAsync", service, StringComparison.Ordinal);
        Assert.Contains("ResolveGatewayExecutionResultAsync", service, StringComparison.Ordinal);
        Assert.Contains("RefreshRefundStatusAsync", service, StringComparison.Ordinal);
        Assert.Contains("BuildRefundRecoveryLockKey", service, StringComparison.Ordinal);
        Assert.Contains("ReconcileProcessingRefundsAsync", worker, StringComparison.Ordinal);
        Assert.Contains("CommerceHubRefundRecoveryHostedService", worker, StringComparison.Ordinal);
        Assert.Contains("AddHostedService<PointOfSale.API.Services.CommerceHub.CommerceHubRefundRecoveryHostedService>", program, StringComparison.Ordinal);
    }

    [Fact]
    public void StorefrontUnresolvedPaymentRecovery_MustRunImmediatelyAndDrainMultipleBatchesPerCycle()
    {
        var worker = File.ReadAllText(ApiPath("Services", "Payments", "StorefrontUnresolvedPaymentReconciliationHostedService.cs"));

        Assert.Contains("RunRecoveryCycleAsync", worker, StringComparison.Ordinal);
        Assert.Contains("MaxPassesPerCycle", worker, StringComparison.Ordinal);
        AssertContainsInOrder(
            worker,
            "await RunRecoveryCycleAsync(stoppingToken);",
            "await Task.Delay(Interval, stoppingToken);");
        Assert.Contains("for (var pass = 0; pass < MaxPassesPerCycle; pass++)", worker, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectionRecoveryServices_MustStartImmediatelyAndDrainBatchesPerCycle()
    {
        var storefront = File.ReadAllText(ApiPath("Services", "Storefront", "StorefrontOrderProjectionReconciliationService.cs"));
        var commerceHub = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubProjectionReconciliationService.cs"));

        Assert.Contains("RunRecoveryCycleAsync", storefront, StringComparison.Ordinal);
        Assert.Contains("MaxPassesPerCycle", storefront, StringComparison.Ordinal);
        AssertContainsInOrder(
            storefront,
            "await RunRecoveryCycleAsync(stoppingToken);",
            "await Task.Delay(Interval, stoppingToken);");

        Assert.Contains("RunRecoveryCycleAsync", commerceHub, StringComparison.Ordinal);
        Assert.Contains("MaxPassesPerCycle", commerceHub, StringComparison.Ordinal);
        AssertContainsInOrder(
            commerceHub,
            "await RunRecoveryCycleAsync(stoppingToken);",
            "await Task.Delay(Interval, stoppingToken);");
    }

    [Fact]
    public void PaymentLifecycle_MustPublishDurableAffiliateAuthorityEvent_ThroughDirectPostOrOutboxRetry()
    {
        var lifecycle = File.ReadAllText(InfrastructurePath("Payments", "PaymentLifecycleService.cs"));
        var accountingProcessor = File.ReadAllText(InfrastructurePath("Accounting", "AccountingOutboxProcessor.cs"));
        var affiliateConsumer = File.ReadAllText(InfrastructurePath("Consumers", "Affiliate", "AffiliatePaymentCapturedConsumers.cs"));
        var successMethod = ExtractMethodBody(lifecycle, "private async Task<PaymentTransitionResult> HandleSuccessAsync");
        var postCommitMethod = ExtractMethodBody(lifecycle, "private async Task PostCommittedCollectionAccountingFromSqlTruthAsync");
        var accountingPostedMethod = ExtractMethodBody(accountingProcessor, "private async Task MarkOrderFinancialPostingPostedAsync");

        Assert.NotNull(successMethod);
        Assert.NotNull(postCommitMethod);
        Assert.NotNull(accountingPostedMethod);
        Assert.DoesNotContain("TryRecordAffiliateCommissionAfterAuthoritativeSuccessAsync", lifecycle, StringComparison.Ordinal);
        Assert.Contains("PostCommittedCollectionAccountingFromSqlTruthAsync", successMethod!, StringComparison.Ordinal);
        Assert.Contains("PublishOrderCollectionAccountingPostedEventAsync", postCommitMethod!, StringComparison.Ordinal);
        Assert.Contains("PublishOrderCollectionAccountingPostedEventAsync", accountingPostedMethod!, StringComparison.Ordinal);
        Assert.Contains("EnqueueAccountingConfirmedMarketplacePaymentCompletedAsync", accountingPostedMethod!, StringComparison.Ordinal);
        Assert.Contains("OrderCollectionAccountingPostedAffiliateConsumer", affiliateConsumer, StringComparison.Ordinal);
        Assert.Contains("IConsumer<OrderCollectionAccountingPostedEvent>", affiliateConsumer, StringComparison.Ordinal);
        Assert.Contains("RecordCommissionForOrderAsync(evt.OrderId, ct)", affiliateConsumer, StringComparison.Ordinal);
        AssertContainsInOrder(
            postCommitMethod!,
            "await PostSuccessfulCollectionAccountingAsync(",
            "await PublishOrderCollectionAccountingPostedEventAsync(",
            "await EnqueueAccountingConfirmedMarketplacePaymentCompletedAsync(",
            "await _db.SaveChangesAsync(ct)");
    }

    [Fact]
    public void PosStockMutationSupport_MustPopulateAuthoritativeStockSyncMetadata()
    {
        var support = File.ReadAllText(ApiPath("Services", "Inventory", "ProductVariantStockMutationSupport.cs"));

        Assert.Contains("ProjectionMutationVersion", support, StringComparison.Ordinal);
        Assert.Contains("ProductEventVersionHelper.ToProductEventVersion", support, StringComparison.Ordinal);
        Assert.Contains("CorrelationId = change.ReferenceId?.ToString(\"N\") ?? Guid.NewGuid().ToString(\"N\")", support, StringComparison.Ordinal);
        Assert.Contains("EventVersion = ProductEventVersionHelper.ToProductEventVersion(variant.ProjectionMutationVersion)", support, StringComparison.Ordinal);
        Assert.Contains("NormalizeUtcTimestamp", support, StringComparison.Ordinal);
        Assert.Contains("StockSyncOutboxMessage.Create(stockEvent, payload)", support, StringComparison.Ordinal);
    }

    [Fact]
    public void AffiliateProductIntelligenceProjection_MustUseFreshnessWatermarks()
    {
        var consumer = File.ReadAllText(InfrastructurePath("Consumers", "Affiliate", "AffiliateProductIntelligenceProjectionConsumer.cs"));
        var document = File.ReadAllText(InfrastructurePath("MongoDB", "Documents", "Affiliate", "AffiliateProductIntelligenceDocument.cs"));

        Assert.Contains("LastProductEventVersion", document, StringComparison.Ordinal);
        Assert.Contains("LastProductEventOccurredAtUtc", document, StringComparison.Ordinal);
        Assert.Contains("LastStockEventVersion", document, StringComparison.Ordinal);
        Assert.Contains("LastStockUpdatedAtUtc", document, StringComparison.Ordinal);
        Assert.Contains("BuildFreshProductEventFilter", consumer, StringComparison.Ordinal);
        Assert.Contains("BuildFreshStockEventFilter", consumer, StringComparison.Ordinal);
        Assert.Contains("GetProductVariantsAfterSqlStockAuthorityAsync", consumer, StringComparison.Ordinal);
        Assert.Contains("LoadSqlVariantStockSnapshotsAsync", consumer, StringComparison.Ordinal);
        Assert.Contains("Event rejected by SQL stock authority", consumer, StringComparison.Ordinal);
    }

    [Fact]
    public void AffiliatePolicyAndStoreProfileProjection_MustRejectStaleCrossSystemEvents()
    {
        var intelligenceConsumer = File.ReadAllText(InfrastructurePath("Consumers", "Affiliate", "AffiliateProductIntelligenceProjectionConsumer.cs"));
        var policyConsumer = File.ReadAllText(InfrastructurePath("Consumers", "Affiliate", "AffiliateProductPolicyProjectionConsumer.cs"));
        var intelligenceDocument = File.ReadAllText(InfrastructurePath("MongoDB", "Documents", "Affiliate", "AffiliateProductIntelligenceDocument.cs"));
        var publicProductDocument = File.ReadAllText(InfrastructurePath("MongoDB", "Documents", "Affiliate", "PublicAffiliateProductDocument.cs"));

        Assert.Contains("LastPolicyEventVersion", intelligenceDocument, StringComparison.Ordinal);
        Assert.Contains("LastPolicyEventOccurredAtUtc", intelligenceDocument, StringComparison.Ordinal);
        Assert.Contains("BuildFreshPolicyEventFilter", intelligenceConsumer, StringComparison.Ordinal);
        Assert.Contains("LastStoreProfileEventVersion", intelligenceDocument, StringComparison.Ordinal);
        Assert.Contains("LastStoreProfileEventOccurredAtUtc", intelligenceDocument, StringComparison.Ordinal);
        Assert.Contains("BuildFreshStoreProfileEventFilter", intelligenceConsumer, StringComparison.Ordinal);
        Assert.Contains("LastStoreProfileEventVersion", publicProductDocument, StringComparison.Ordinal);
        Assert.Contains("LastStoreProfileEventOccurredAtUtc", publicProductDocument, StringComparison.Ordinal);
        Assert.Contains("BuildFreshPublicProductStoreProfileEventFilter", policyConsumer, StringComparison.Ordinal);
        Assert.Contains("LastStoreProfileEventVersion == context.Message.EventVersion", policyConsumer, StringComparison.Ordinal);
    }

    [Fact]
    public void AffiliatePublicProductProjection_MustFailClosedOnProjectionFailure()
    {
        var policyConsumer = File.ReadAllText(InfrastructurePath("Consumers", "Affiliate", "AffiliateProductPolicyProjectionConsumer.cs"));
        var syncMethod = ExtractMethodBody(policyConsumer, "SyncPublicAffiliateProductAsync");

        Assert.NotNull(syncMethod);
        Assert.Contains("AFFILIATE_PRODUCT_DOCUMENT_PROJECTION_FAILED", policyConsumer, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_PRODUCT_SEARCH_PROJECTION_FAILED", policyConsumer, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_PUBLIC_STORE_PROJECTION_FAILED", policyConsumer, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_PUBLIC_PRODUCT_SEARCH_PROJECTION_FAILED", syncMethod!, StringComparison.Ordinal);
        Assert.Contains("throw;", syncMethod!, StringComparison.Ordinal);
        Assert.DoesNotContain("Don't throw here", policyConsumer, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubUnresolvedPaymentWebhooks_MustRouteKnownMultiStoreMoneyToTenantSuspense()
    {
        var paymentService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPaymentService.cs"));
        var entity = File.ReadAllText(CorePath("Entities", "CommerceHubUnresolvedPaymentWebhook.cs"));
        var dbContext = File.ReadAllText(InfrastructurePath("Data", "ApplicationDbContext.cs"));
        var migration = File.ReadAllText(InfrastructurePath("Migrations", "20260503160000_S69_CrossSystemCriticalRiskProductionFixes.sql"));

        Assert.Contains("BuildUnresolvedPaymentSuspenseAllocations", paymentService, StringComparison.Ordinal);
        Assert.Contains("suspenseAllocations.Count > 0", paymentService, StringComparison.Ordinal);
        Assert.Contains("CH_UNRESOLVED_PAYMENT:{allocation.TenantId}", paymentService, StringComparison.Ordinal);
        Assert.Contains("SuspenseTenantId = allocation.TenantId", paymentService, StringComparison.Ordinal);
        Assert.Contains("if (marketplaceOrder == null)", paymentService, StringComparison.Ordinal);
        Assert.Contains("if (subOrderTotals.Count == 0)", paymentService, StringComparison.Ordinal);
        Assert.DoesNotContain("AllocateEvenlyAcrossAffectedStores", paymentService, StringComparison.Ordinal);
        Assert.Contains("SuspenseAllocationsCoverAffectedStores(suspenseAllocations, affectedStoreIds)", paymentService, StringComparison.Ordinal);
        Assert.Contains("var expectedRoutedTenantIds = GetPositiveAllocationTenantIds(suspenseAllocations);", paymentService, StringComparison.Ordinal);
        Assert.Contains("normalizedRoutedTenantIds.SequenceEqual(expectedRoutedTenantIds)", paymentService, StringComparison.Ordinal);
        Assert.Contains("unresolved.SuspenseRoutedStoreIdsJson = SerializeTenantIds(normalizedRoutedTenantIds)", paymentService, StringComparison.Ordinal);
        Assert.Contains("unresolved.RoutedToSuspenseAtUtc ??= nowUtc", paymentService, StringComparison.Ordinal);
        Assert.Contains("AffectedStoreIdsJson", entity, StringComparison.Ordinal);
        Assert.Contains("SuspenseRoutedStoreIdsJson", entity, StringComparison.Ordinal);
        Assert.Contains("AffectedStoreIdsJson", dbContext, StringComparison.Ordinal);
        Assert.Contains("SuspenseRoutedStoreIdsJson", dbContext, StringComparison.Ordinal);
        Assert.Contains("CK_CommerceHubUnresolvedPaymentWebhooks_AffectedStoreIdsJson_Array", migration, StringComparison.Ordinal);
        Assert.Contains("CK_CommerceHubUnresolvedPaymentWebhooks_RoutedSuspenseStatusScope", migration, StringComparison.Ordinal);
        Assert.Contains("CK_CommerceHubUnresolvedPaymentWebhooks_PendingTenantResolutionNoSuspenseRoute", migration, StringComparison.Ordinal);
        Assert.Contains("\"SuspenseRoutedStoreIdsJson\"::jsonb = \"AffectedStoreIdsJson\"::jsonb", migration, StringComparison.Ordinal);
        Assert.Contains("jsonb_array_length(\"AffectedStoreIdsJson\"::jsonb) = 0", migration, StringComparison.Ordinal);
        Assert.Contains("jsonb_array_length(\"SuspenseRoutedStoreIdsJson\"::jsonb) = 0", migration, StringComparison.Ordinal);
        Assert.Contains("IX_CommerceHubUnresolvedPaymentWebhooks_AffectedStoreScope", migration, StringComparison.Ordinal);
        Assert.DoesNotMatch(
            @"canRouteToSuspense\s*=[\s\S]{0,400}affectedStoreIds\.Count\s*==\s*1",
            paymentService);
    }

    [Fact]
    public void CommerceHubStockProjection_MustUseStockEventVersionWhenTimestampsTie()
    {
        var consumer = File.ReadAllText(InfrastructurePath("Consumers", "CommerceHubProjectionConsumer.cs"));
        var document = File.ReadAllText(InfrastructurePath("MongoDB", "Documents", "CommerceHubProductDocument.cs"));

        Assert.Contains("LastStockEventVersion", document, StringComparison.Ordinal);
        Assert.Contains("IsStaleStockProjection", consumer, StringComparison.Ordinal);
        Assert.Contains("listing.LastStockEventVersion", consumer, StringComparison.Ordinal);
        Assert.Contains("message.EventVersion", consumer, StringComparison.Ordinal);
        Assert.Contains("filter.Eq(x => x.StockUpdatedAtUtc, stockUpdatedAtUtc)", consumer, StringComparison.Ordinal);
        Assert.Contains("ProjectionAuthorityValidator.IsIncomingStockStale", consumer, StringComparison.Ordinal);
        Assert.Contains(".Set(x => x.LastStockEventVersion, eventVersion)", consumer, StringComparison.Ordinal);
        Assert.DoesNotContain("listing.StockUpdatedAtUtc.Value >= message.StockUpdatedAtUtc", consumer, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubPaymobCompensationRefresh_MustQueryGatewayInsteadOfTrustingLocalStatus()
    {
        var gateway = File.ReadAllText(ApiPath("Services", "Payments", "Paymob", "PaymobCommerceHubRefundGateway.cs"));
        var refreshMethod = ExtractMethodBody(gateway, "public Task<CommerceHubGatewayRefundResult> RefreshRefundStatusAsync");

        Assert.NotNull(refreshMethod);
        Assert.Contains("modern Super Admin credential contract", gateway, StringComparison.Ordinal);
        Assert.Contains("GatewayStatus: \"not_supported\"", gateway, StringComparison.Ordinal);
        Assert.DoesNotContain("CommerceHubPaymobOptions", gateway, StringComparison.Ordinal);
        Assert.DoesNotContain("ApiKey", gateway, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubPaymobInitialRefund_MustNotConfirmFromPaymentLevelSuccessFields()
    {
        var gateway = File.ReadAllText(ApiPath("Services", "Payments", "Paymob", "PaymobCommerceHubRefundGateway.cs"));
        var refundMethod = ExtractMethodBody(gateway, "public Task<CommerceHubGatewayRefundResult> RefundAsync");

        Assert.NotNull(refundMethod);
        Assert.Contains("Task.FromResult(NotSupported", refundMethod!, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", gateway, StringComparison.Ordinal);
    }

    [Fact]
    public void UnresolvedCapturedProviderPayments_MustGateOutwardMoneyMovement()
    {
        var gate = File.ReadAllText(ApiPath("Services", "CommerceHub", "Finance", "CommerceHubFinancialReadinessGate.cs"));

        Assert.Contains("EnsureUnresolvedCapturedPaymentsHealthyAsync", gate, StringComparison.Ordinal);
        Assert.Contains("CommerceHubUnresolvedPaymentWebhooks", gate, StringComparison.Ordinal);
        Assert.Contains("BlockingUnresolvedPaymentStatuses", gate, StringComparison.Ordinal);
        Assert.Contains("UNRESOLVED_CAPTURED_PAYMENT_NOT_RECONCILED", gate, StringComparison.Ordinal);
        Assert.Contains("UnresolvedCapturedPaymentMayAffectTenant", gate, StringComparison.Ordinal);
        Assert.Contains(".AsAsyncEnumerable()", gate, StringComparison.Ordinal);
        Assert.Contains("LogLargeTenantScopeInspection(", gate, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubCheckoutAndPaymentIntake_MustGateOnCrossSystemBackboneHealth()
    {
        var gate = File.ReadAllText(ApiPath("Services", "CommerceHub", "Finance", "CommerceHubFinancialReadinessGate.cs"));
        var checkout = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubCheckoutService.cs"));
        var payment = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPaymentService.cs"));
        var intakeMethod = ExtractMethodBody(gate, "EnsureCommerceIntakeAllowedAsync");

        Assert.NotNull(intakeMethod);
        Assert.Contains("EnsureAccountingOutboxHealthyAsync", intakeMethod!, StringComparison.Ordinal);
        Assert.Contains("EnsurePaymentCompensationHealthyAsync", intakeMethod!, StringComparison.Ordinal);
        Assert.Contains("EnsureUnresolvedCapturedPaymentsHealthyAsync", intakeMethod!, StringComparison.Ordinal);
        Assert.Contains("EnsureCommerceHubLedgerAccountingCoverageAsync", intakeMethod!, StringComparison.Ordinal);
        Assert.Contains("EnsureCommerceHubReconciliationHealthyAsync", intakeMethod!, StringComparison.Ordinal);
        Assert.Contains("EnsureIntegrationBackboneHealthyAsync", intakeMethod!, StringComparison.Ordinal);
        Assert.Contains("EnsureCommerceIntakeAllowedAsync", checkout, StringComparison.Ordinal);
        Assert.Contains("\"CommerceHubCheckout.Create\"", checkout, StringComparison.Ordinal);
        Assert.Contains("EnsureCommerceIntakeAllowedAsync", payment, StringComparison.Ordinal);
        Assert.Contains("\"CommerceHubPayment.Initiate\"", payment, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubPayoutProcessing_MustBindExternalExecutionToPosIssuedTransferIntent()
    {
        var service = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPayoutService.cs"));
        var dto = File.ReadAllText(ApiPath("DTOs", "CommerceHub", "CommerceHubFinanceDtos.cs"));
        var markProcessing = ExtractMethodBody(service, "MarkProcessingAsync");

        Assert.NotNull(markProcessing);
        Assert.Contains("ExternalTransferIdempotencyKey", dto, StringComparison.Ordinal);
        Assert.Contains("EnsureExternalTransferIntentMatchesPayout", markProcessing!, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_EXTERNAL_TRANSFER_INTENT_REQUIRED", service, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_EXTERNAL_TRANSFER_INTENT_MISMATCH", service, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_EXTERNAL_HANDOFF_REFERENCE_REQUIRED", service, StringComparison.Ordinal);
    }

    [Fact]
    public void AffiliateAttributionPlaceholder_MustNotExposePrePaymentOrderAsPaid()
    {
        var consumer = File.ReadAllText(InfrastructurePath("Consumers", "Affiliate", "AffiliateOrderAttributionConsumer.cs"));
        var document = File.ReadAllText(InfrastructurePath("MongoDB", "Documents", "Affiliate", "AffiliateDocuments.cs"));

        Assert.Contains("AttributedPendingPayment", consumer, StringComparison.Ordinal);
        Assert.Contains(".SetOnInsert(d => d.OrderStatus, \"PendingPayment\")", consumer, StringComparison.Ordinal);
        Assert.Contains(".SetOnInsert(d => d.FulfillmentStatus, \"PendingPayment\")", consumer, StringComparison.Ordinal);
        Assert.Contains(".SetOnInsert(d => d.PaymentMethod, string.Empty)", consumer, StringComparison.Ordinal);
        Assert.Contains(".SetOnInsert(d => d.PaidAmount, 0m)", consumer, StringComparison.Ordinal);
        Assert.Contains("AttributedPendingPayment", document, StringComparison.Ordinal);
        Assert.Contains("Attribution placeholders keep this at 0 until payment is actually captured", document, StringComparison.Ordinal);
        Assert.DoesNotContain(".SetOnInsert(d => d.PaidAmount, evt.OrderTotal)", consumer, StringComparison.Ordinal);
        Assert.DoesNotContain(".SetOnInsert(d => d.OrderStatus, \"Confirmed\")", consumer, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubProviderRefundCompensation_MustReconcileThroughAuthoritativeRefundLifecycle()
    {
        var worker = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPaymentCompensationOutboxWorker.cs"));

        Assert.Contains("ICommerceHubFinancialLedgerService", worker, StringComparison.Ordinal);
        Assert.Contains("ReconcileConfirmedProviderRefundAsync", worker, StringComparison.Ordinal);
        Assert.Contains("ParseRefundAllocationSnapshot", worker, StringComparison.Ordinal);
        Assert.Contains("dbContext.CommerceHubRefunds", worker, StringComparison.Ordinal);
        Assert.Contains("ApplyRefundStatuses", worker, StringComparison.Ordinal);
        Assert.Contains("ApplyEarningReversal", worker, StringComparison.Ordinal);
        Assert.Contains("RecordRefundAsync", worker, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPaymentLifecycleAuthority.ApplyRefundPaymentState", worker, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPaymentRecordStatus.Paid", worker, StringComparison.Ordinal);
        Assert.Contains("await dbContext.Database.BeginTransactionAsync", worker, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubSearchProjection_MustCommitMongoAndDelegateFailedSearchSyncToAutoHealing()
    {
        var consumer = File.ReadAllText(InfrastructurePath("Consumers", "CommerceHubProjectionConsumer.cs"));

        Assert.Contains("RequiresSearchSyncRetry", consumer, StringComparison.Ordinal);
        Assert.Contains("SearchSyncFailed", consumer, StringComparison.Ordinal);
        Assert.Contains("SearchSyncPending", consumer, StringComparison.Ordinal);
        Assert.Contains("CommerceHub Mongo projection committed while Elasticsearch sync remains failed", consumer, StringComparison.Ordinal);
        Assert.Contains("CommerceHubAutoHealingService", consumer, StringComparison.Ordinal);
        Assert.DoesNotContain("will be retried by message redelivery", consumer, StringComparison.Ordinal);
        Assert.Contains("if (!succeeded)", consumer, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubCodReservations_MustNotBeReleasedByTtlWhileOrderIsLive()
    {
        var job = File.ReadAllText(InfrastructurePath("Jobs", "InventoryReservationCleanupJob.cs"));
        var releaseMethod = ExtractMethodBody(job, "ShouldReleaseCommerceHubReservation");

        Assert.NotNull(releaseMethod);
        Assert.Contains("PaymentStatus.CashOnDelivery", releaseMethod!, StringComparison.Ordinal);
        Assert.Contains("OrderStatus.Cancelled or OrderStatus.Refunded", releaseMethod!, StringComparison.Ordinal);
        Assert.Contains("Reservation TTL must not override the order lifecycle", releaseMethod!, StringComparison.Ordinal);
        Assert.DoesNotContain("They do not have active online payments, so either stale signal is releasable", releaseMethod!, StringComparison.Ordinal);
        Assert.DoesNotMatch(
            @"PaymentStatus\.CashOnDelivery[\s\S]{0,160}return\s+true",
            releaseMethod!);
    }

    [Fact]
    public void PaymentCompensationBacklog_MustGateOutwardMoneyMovement()
    {
        var gate = File.ReadAllText(ApiPath("Services", "CommerceHub", "Finance", "CommerceHubFinancialReadinessGate.cs"));

        Assert.Contains("PaymentCompensationMaxLag", gate, StringComparison.Ordinal);
        Assert.Contains("EnsurePaymentCompensationHealthyAsync", gate, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPaymentCompensationOutboxMessages", gate, StringComparison.Ordinal);
        Assert.Contains("PAYMENT_COMPENSATION_NOT_READY", gate, StringComparison.Ordinal);
        Assert.Contains("CompensationMayAffectTenant", gate, StringComparison.Ordinal);
        AssertContainsInOrder(
            gate,
            "EnsureAccountingOutboxHealthyAsync",
            "EnsurePaymentCompensationHealthyAsync",
            "EnsureCommerceHubLedgerAccountingCoverageAsync");
    }

    [Fact]
    public void StockSyncOutboxProcessing_MustUseWarehouseScopedOrderingAndLocks()
    {
        var processor = File.ReadAllText(InfrastructurePath("Eventing", "StockSyncOutboxProcessor.cs"));
        var configuration = File.ReadAllText(InfrastructurePath("Data", "Configurations", "StockSyncOutboxMessageConfiguration.cs"));
        var entity = File.ReadAllText(CorePath("Entities", "StockSyncOutboxMessage.cs"));
        var migration = File.ReadAllText(InfrastructurePath("Migrations", "20260501213000_S68_StockSyncOutboxWarehouseScopedEventIdentity.sql"));
        var orderingMethod = ExtractMethodBody(processor, "HasUnpublishedEarlierVariantEventAsync");

        Assert.NotNull(orderingMethod);
        Assert.Contains("other.WarehouseId == message.WarehouseId", orderingMethod!, StringComparison.Ordinal);
        Assert.Contains("freshMessage.WarehouseId", processor, StringComparison.Ordinal);
        Assert.Contains("context.Headers.Set(\"warehouse-id\", message.WarehouseId)", processor, StringComparison.Ordinal);
        Assert.Contains("Guid warehouseId", processor, StringComparison.Ordinal);
        Assert.Contains("x.WarehouseId", configuration, StringComparison.Ordinal);
        Assert.Contains("CK_StockSyncOutbox_WarehouseId_NotEmpty", configuration, StringComparison.Ordinal);
        Assert.Contains("@event.WarehouseId == Guid.Empty", entity, StringComparison.Ordinal);
        Assert.Contains("active stock sync outbox rows are missing WarehouseId", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubPaymobInitiation_MustFailClosedWhenProviderSessionPersistenceIsAmbiguous()
    {
        var source = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPaymentService.cs"));
        var adapter = File.ReadAllText(ApiPath("Services", "Payments", "Paymob", "PaymobAdapter.cs"));
        var initiateMethod = ExtractMethodBody(source, "public async Task<CommerceHubPaymentInitiateResponseDto> InitiateAsync");
        var createPaymobMethod = ExtractMethodBody(source, "private async Task<CommerceHubPaymentProviderSession> CreatePaymobSessionAsync");

        Assert.Contains("ProviderReference = payment.ProviderReference", source, StringComparison.Ordinal);
        Assert.Contains("PAYMENT_PROVIDER_SESSION_RECONCILIATION_REQUIRED", source, StringComparison.Ordinal);
        Assert.Contains("CommerceHubProviderSessionPersistenceException", source, StringComparison.Ordinal);
        Assert.Contains("CommerceHubProviderSessionOutcomeUnknownException", source, StringComparison.Ordinal);
        Assert.Contains("PersistProviderSessionAsync", source, StringComparison.Ordinal);
        Assert.Contains("TryPersistProviderSessionDirectlyAsync", source, StringComparison.Ordinal);
        Assert.Contains("ExecuteUpdateAsync", source, StringComparison.Ordinal);
        Assert.Contains("BeginTransactionAsync(IsolationLevel.Serializable", source, StringComparison.Ordinal);
        Assert.Contains("attemptRows != 1", source, StringComparison.Ordinal);
        Assert.Contains("transaction.CommitAsync", source, StringComparison.Ordinal);
        Assert.Contains("provider session persistence recovered through atomic direct durable update", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("payment attempt recovery failed", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("initiation log persistence failed", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Creating another session is blocked until reconciliation completes", source, StringComparison.Ordinal);

        Assert.NotNull(initiateMethod);
        Assert.NotNull(createPaymobMethod);
        AssertContainsInOrder(
            initiateMethod!,
            "catch (CommerceHubProviderSessionOutcomeUnknownException ex)",
            "RecordInitiationUnknownOutcomeAsync",
            "catch (CommerceHubProviderSessionPersistenceException ex)",
            "catch (CommerceHubCheckoutException ex)");
        AssertContainsInOrder(
            createPaymobMethod!,
            "ResolveMarketplaceOrderCountryCodeAsync",
            "GetProviderAdapter(CommerceHubPaymentMethods.Paymob).CreateSessionAsync",
            "BuildPaymobProviderSessionRequest",
            "ProtectPaymentSession");
        Assert.Contains("PAYMOB_INTENTION_RECOVERY_REQUIRED", adapter, StringComparison.Ordinal);
        Assert.Contains("OutcomeUnknown", createPaymobMethod!, StringComparison.Ordinal);
        Assert.Contains("Paymob intention creation has an unknown outcome", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubPaymentInitiation_MustPersistDurableReinitFenceBeforeProviderExecution()
    {
        var paymentService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPaymentService.cs"));
        var cleanupJob = File.ReadAllText(InfrastructurePath("Jobs", "InventoryReservationCleanupJob.cs"));
        var initiateMethod = ExtractMethodBody(paymentService, "public async Task<CommerceHubPaymentInitiateResponseDto> InitiateAsync");

        Assert.NotNull(initiateMethod);
        Assert.Contains("ProviderSessionReinitFencePrefix", paymentService, StringComparison.Ordinal);
        Assert.Contains("BuildProviderSessionReinitFenceReason", paymentService, StringComparison.Ordinal);
        Assert.Contains("payment.FailureReason = providerSessionReinitFenceReason", initiateMethod!, StringComparison.Ordinal);
        Assert.Contains("paymentAttempt.FailureReason = providerSessionReinitFenceReason", initiateMethod!, StringComparison.Ordinal);
        
        // Verify SaveChangesAsync is called
        Assert.Contains("await _dbContext.SaveChangesAsync(cancellationToken);", initiateMethod!, StringComparison.Ordinal);
        
        // Verify provider invocation happens
        Assert.Contains("var session = provider switch", initiateMethod!, StringComparison.Ordinal);
        
        // Verify the fence is persisted BEFORE provider execution by checking textual order
        // Both payment.FailureReason AND paymentAttempt.FailureReason must be written before SaveChangesAsync
        var paymentFenceIndex = initiateMethod!.IndexOf("payment.FailureReason = providerSessionReinitFenceReason", StringComparison.Ordinal);
        var attemptFenceIndex = initiateMethod.IndexOf("paymentAttempt.FailureReason = providerSessionReinitFenceReason", StringComparison.Ordinal);
        var providerSwitchIndex = initiateMethod.IndexOf("var session = provider switch", StringComparison.Ordinal);
        
        // Both fence writes must exist
        Assert.True(paymentFenceIndex >= 0, "payment.FailureReason fence write must exist");
        Assert.True(attemptFenceIndex >= 0, "paymentAttempt.FailureReason fence write must exist");
        
        // Use the maximum of both fence indices to ensure SaveChangesAsync comes after BOTH writes
        var fenceAnchor = Math.Max(paymentFenceIndex, attemptFenceIndex);
        
        // Verify SaveChangesAsync occurs between the last fence write and provider invocation
        var saveChangesAfterFence = initiateMethod.IndexOf("await _dbContext.SaveChangesAsync(cancellationToken);", fenceAnchor, StringComparison.Ordinal);
        
        Assert.True(
            providerSwitchIndex >= 0 && saveChangesAfterFence >= 0 &&
            fenceAnchor < saveChangesAfterFence && saveChangesAfterFence < providerSwitchIndex,
            "Both reinit fences (payment.FailureReason and paymentAttempt.FailureReason) must be written, then persisted via SaveChangesAsync, then provider invocation occurs to ensure durable fence before external call.");
        
        // Verify cleanup job understands the fence
        Assert.Contains("PROVIDER_SESSION_REINIT_FENCE:", cleanupJob, StringComparison.Ordinal);
        Assert.Contains("IsProviderSessionAuthorityUncertain", cleanupJob, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubPaymentInitiation_MustRequireCustomerOwnershipOrSignedOrderToken()
    {
        var controller = File.ReadAllText(ApiPath("Controllers", "CommerceHubPaymentsController.cs"));
        var paymentService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPaymentService.cs"));
        var paymentDtos = File.ReadAllText(ApiPath("DTOs", "CommerceHub", "CommerceHubOrderDtos.cs"));
        var initiateControllerMethod = ExtractMethodBody(controller, "public async Task<IActionResult> Initiate");
        var initiateServiceMethod = ExtractMethodBody(paymentService, "public async Task<CommerceHubPaymentInitiateResponseDto> InitiateAsync");

        Assert.Contains("public string? OrderAccessToken { get; set; }", paymentDtos, StringComparison.Ordinal);
        Assert.NotNull(initiateControllerMethod);
        Assert.Contains("ResolveAuthenticatedCustomerIdAsync", controller, StringComparison.Ordinal);
        Assert.Contains("CommerceHubOrderAccessCookie.ReadOrderAccessToken", controller, StringComparison.Ordinal);
        AssertContainsInOrder(
            initiateControllerMethod!,
            "var orderAccessToken = request.OrderAccessToken?.Trim();",
            "CommerceHubOrderAccessCookie.ReadOrderAccessToken",
            "ResolveAuthenticatedCustomerIdAsync",
            "if (!authenticatedCustomerId.HasValue && string.IsNullOrWhiteSpace(orderAccessToken))",
            "_paymentOrchestrationService.ExecuteAsync(");
        Assert.Contains("OrderAccessToken = orderAccessToken", initiateControllerMethod!, StringComparison.Ordinal);
        Assert.Contains("authenticatedCustomerId,", initiateControllerMethod!, StringComparison.Ordinal);

        Assert.NotNull(initiateServiceMethod);
        AssertContainsInOrder(
            initiateServiceMethod!,
            "ResolveMarketplaceOrderAsync",
            "EnsureInitiationAccessAllowed(marketplaceOrder, authenticatedCustomerId, request.OrderAccessToken);",
            "EnsureInitiationAllowedAsync");
        Assert.Contains("EnsureInitiationAccessAllowed(marketplaceOrder, authenticatedCustomerId, request.OrderAccessToken);", initiateServiceMethod!, StringComparison.Ordinal);
        Assert.Contains("marketplaceOrder.CustomerId == authenticatedCustomerId.Value", paymentService, StringComparison.Ordinal);
        Assert.Contains("_orderAccessService.ValidateAccessToken(", paymentService, StringComparison.Ordinal);
        Assert.Contains("\"ORDER_ACCESS_DENIED\"", paymentService, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubCheckoutPage_MustTreatRequiresInitiationAsPaymentPending()
    {
        var checkoutPage = File.ReadAllText(CommerceHubPath("src", "pages", "CheckoutPage.tsx"));
        var orderDetailsPage = File.ReadAllText(CommerceHubPath("src", "pages", "CustomerOrderDetailsPage.tsx"));
        var checkoutService = File.ReadAllText(CommerceHubPath("src", "services", "checkout-service.ts"));
        var endpoints = File.ReadAllText(CommerceHubPath("src", "api", "endpoints.ts"));
        var types = File.ReadAllText(CommerceHubPath("src", "types", "checkout.ts"));
        var dto = File.ReadAllText(ApiPath("DTOs", "CommerceHub", "CommerceHubOrderDtos.cs"));
        var paymentService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPaymentService.cs"));

        // Verify API endpoint and types
        Assert.Contains("paymentInitiate: \"/api/commercehub/payments/initiate\"", endpoints, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPaymentInitiateRequest", types, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPaymentInitiateResponse", types, StringComparison.Ordinal);
        Assert.Contains("handoffType", types, StringComparison.Ordinal);
        Assert.Contains("publishableKey", types, StringComparison.Ordinal);
        Assert.Contains("HandoffType", dto, StringComparison.Ordinal);
        Assert.Contains("PublishableKey", dto, StringComparison.Ordinal);
        Assert.Contains("ResolveHandoffType", paymentService, StringComparison.Ordinal);
        
        // Verify service method and flow
        Assert.Contains("async initiatePayment(payload: CommerceHubPaymentInitiateRequest", checkoutService, StringComparison.Ordinal);
        Assert.Contains("requirePaymentInitiationAuthority", checkoutService, StringComparison.Ordinal);
        Assert.Contains("checkoutService.initiatePayment({", checkoutPage, StringComparison.Ordinal);
        
        // Verify status check and navigation behavior
        Assert.Contains("response.paymentSession?.status === \"requires_initiation\" && response.paymentMethod !== \"COD\"", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("window.location.assign(initiationResponse.paymentUrl)", checkoutPage, StringComparison.Ordinal);
        
        // Verify embedded payment form handling
        Assert.Contains("EmbeddedStripePaymentForm", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("initiationResponse.handoffType === \"client_secret\"", checkoutPage, StringComparison.Ordinal);
        
        // Verify order details page integration
        Assert.Contains("paymentMethodCode", orderDetailsPage, StringComparison.Ordinal);
        Assert.Contains("EmbeddedStripePaymentForm", orderDetailsPage, StringComparison.Ordinal);
        Assert.Contains("order.canRetryPayment", orderDetailsPage, StringComparison.Ordinal);
        Assert.Contains("order.paymentRecoveryMessage", orderDetailsPage, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubPaymentRecoveryAuthority_MustFailClosed_OnProviderSessionReinitFence()
    {
        var authorityService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPaymentRecoveryAuthorityService.cs"));
        var recoveryAuthority = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPaymentSessionRecoveryAuthority.cs"));
        var paymentService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPaymentService.cs"));
        var orderDetailsPage = File.ReadAllText(CommerceHubPath("src", "pages", "CustomerOrderDetailsPage.tsx"));
        var recoveryPage = File.ReadAllText(CommerceHubPath("src", "pages", "OrderPaymentRecoveryPage.tsx"));

        Assert.Contains("ProviderSessionReinitFencePrefix", recoveryAuthority, StringComparison.Ordinal);
        Assert.Contains("HasProviderSessionPersistenceGap", recoveryAuthority, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPaymentSessionRecoveryAuthority.HasProviderSessionPersistenceGap", authorityService, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPaymentSessionRecoveryAuthority.HasProviderSessionPersistenceGap", paymentService, StringComparison.Ordinal);
        Assert.Contains("Include(payment => payment.Logs)", authorityService, StringComparison.Ordinal);
        Assert.Contains("ProviderSessionRecoveryBlockedMessage", authorityService, StringComparison.Ordinal);
        Assert.Contains("!hasProviderSessionRecoveryBlock", authorityService, StringComparison.Ordinal);

        Assert.Contains("if (order.canRetryPayment || order.paymentRecoveryMessage)", orderDetailsPage, StringComparison.Ordinal);
        Assert.DoesNotContain("order.paymentStatus === \"Pending\"", orderDetailsPage, StringComparison.Ordinal);
        Assert.Contains("if (order.canRetryPayment || order.paymentRecoveryMessage)", recoveryPage, StringComparison.Ordinal);
        Assert.DoesNotContain("order.paymentStatus === \"Pending\"", recoveryPage, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubCheckout_MustRequireExecutablePlatformProviderWithoutReadingStorefrontAuthority()
    {
        var checkoutService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubCheckoutService.cs"));
        var validateMethod = ExtractMethodBody(checkoutService, "ValidateRequestedPaymentMethod");
        var paymentAuthority = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPaymentMethodAuthority.cs"));
        var paymentService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPaymentService.cs"));
        var cartDtos = File.ReadAllText(ApiPath("DTOs", "CommerceHub", "CommerceHubCartDtos.cs"));
        var cartService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubCartService.cs"));
        var checkoutPage = File.ReadAllText(CommerceHubPath("src", "pages", "CheckoutPage.tsx"));
        var selector = File.ReadAllText(CommerceHubPath("src", "checkout", "PaymentMethodSelector.tsx"));

        Assert.NotNull(validateMethod);
        Assert.Contains("CommerceHubPaymentMethodAuthority.IsRequestedMethodAvailable", validateMethod!, StringComparison.Ordinal);
        Assert.Contains("string.Equals(method.Code, requestedPaymentMethod, StringComparison.OrdinalIgnoreCase)", paymentAuthority, StringComparison.Ordinal);
        Assert.Contains("capabilities.SupportsPaymentSessionCreation", paymentAuthority, StringComparison.Ordinal);
        Assert.Contains("capabilities.SupportsWebhookVerification", paymentAuthority, StringComparison.Ordinal);
        Assert.DoesNotContain("TenantPaymentMethod", paymentAuthority, StringComparison.Ordinal);
        Assert.DoesNotContain("TenantPaymentMethods", cartService, StringComparison.Ordinal);
        Assert.DoesNotContain("TenantPaymentMethods", checkoutService, StringComparison.Ordinal);
        Assert.DoesNotContain("TenantPaymentMethods", paymentService, StringComparison.Ordinal);
        Assert.Contains("PaymentMethods", cartDtos, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPaymentMethodAuthority.BuildAvailability", cartService, StringComparison.Ordinal);
        Assert.Contains("paymentMethods={paymentMethods}", checkoutPage, StringComparison.Ordinal);
        Assert.Matches(@"const\s+supportedPaymentMethods\s*=\s*paymentMethods\.filter", selector);
        Assert.Matches(@"supportedPaymentMethods\.map\(\s*\(?\s*paymentMethod", selector);
        Assert.DoesNotContain("const options: {", selector, StringComparison.Ordinal);
    }

    [Fact]
    public void PosCatalogMutations_MustQueueAuthoritativeProjectionEventsBeforeCommit()
    {
        var products = File.ReadAllText(ApiPath("Controllers", "ProductsController.cs"));

        Assert.DoesNotContain("post-commit event", products, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("saved but event", products, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("committed but projection event publication failed", products, StringComparison.OrdinalIgnoreCase);

        AssertProductMutationPublishesBeforeCommit(products, "CreateProduct", "PublishVariantCreatedEventAsync");
        AssertProductMutationPublishesBeforeCommit(products, "UpdateProduct", "ProductUpdatedEvent");
        AssertProductMutationPublishesBeforeCommit(products, "DeleteProduct", "ProductDeletedEvent");
        AssertProductMutationPublishesBeforeCommit(products, "DeactivateProduct", "ProductArchivedEvent");
        AssertProductMutationPublishesBeforeCommit(products, "AdjustStock", "PublishCommittedStockEventsAsync");
        AssertProductMutationPublishesBeforeCommit(products, "AddProductUnit", "PublishProductUnitsChangedAsync");
        AssertProductMutationPublishesBeforeCommit(products, "UpdateProductUnit", "PublishProductUnitsChangedAsync");
        AssertProductMutationPublishesBeforeCommit(products, "DeleteProductUnit", "PublishProductUnitsChangedAsync");
    }

    [Fact]
    public void VariantCatalogMutations_MustQueueAuthoritativeProjectionEventsBeforeCommit()
    {
        var variants = File.ReadAllText(ApiPath("Controllers", "ProductVariantsController.cs"));

        AssertVariantMutationPublishesBeforeCommit(variants, "CreateVariant", "ProductVariantCreatedEvent");
        AssertVariantMutationPublishesBeforeCommit(variants, "UpdateVariant", "ProductVariantUpdatedEvent");
        AssertVariantMutationPublishesBeforeCommit(variants, "DeleteVariant", "ProductVariantDeletedEvent");
        AssertVariantMutationPublishesBeforeCommit(variants, "SetDefaultVariant", "ProductVariantUpdatedEvent");
        Assert.Contains("TenantId = _tenantContext.RequiredTenantId", ExtractMethodBody(variants, "DeleteVariant"), StringComparison.Ordinal);
    }

    [Fact]
    public void VariantEvents_MustUseVariantAggregateForAuthoritativeOrdering()
    {
        var variantEvents = File.ReadAllText(CorePath("Events", "ProductVariantEvents.cs"));
        var auditEvents = File.ReadAllText(CorePath("Events", "CommerceHubAuditEvents.cs"));

        Assert.Contains("ProductVariantCreatedEvent : IntegrationEvent, IAggregateScopedIntegrationEvent", variantEvents, StringComparison.Ordinal);
        Assert.Contains("ProductVariantUpdatedEvent : IntegrationEvent, IAggregateScopedIntegrationEvent", variantEvents, StringComparison.Ordinal);
        Assert.Contains("VariantPriceUpdatedEvent : IntegrationEvent, IAggregateScopedIntegrationEvent", variantEvents, StringComparison.Ordinal);
        Assert.Contains("ProductVariantStockChangedEvent : IntegrationEvent, IAggregateScopedIntegrationEvent", variantEvents, StringComparison.Ordinal);
        Assert.Contains("ProductVariantDeletedEvent : IntegrationEvent, IAggregateScopedIntegrationEvent", variantEvents, StringComparison.Ordinal);
        Assert.Contains("VariantDeletedEvent : IntegrationEvent, IAggregateScopedIntegrationEvent", auditEvents, StringComparison.Ordinal);
        Assert.Equal(6, Regex.Matches(variantEvents + auditEvents, "GetAuthoritativeAggregateId\\(\\) => VariantId").Count);
    }

    [Fact]
    public void ProductStockPriceSearchIndex_MustTombstoneHiddenProductsAndRetryFailedWrites()
    {
        var consumer = File.ReadAllText(InfrastructurePath("Consumers", "ProductStockPriceSearchIndexConsumer.cs"));

        Assert.Contains("ProductSearchTombstoneWriter.WriteAsync", consumer, StringComparison.Ordinal);
        Assert.Contains("SearchIndexSyncException", consumer, StringComparison.Ordinal);
        Assert.Contains("Elasticsearch index failed", consumer, StringComparison.Ordinal);
        Assert.Contains("Elasticsearch delete failed", consumer, StringComparison.Ordinal);
        Assert.Contains("Elasticsearch sync failed for product", consumer, StringComparison.Ordinal);
        Assert.DoesNotContain("ES failures should not break the system", consumer, StringComparison.Ordinal);
    }

    [Fact]
    public void AffiliateOrderLevelRefundConsumer_MustNotOverReversePartialRefunds()
    {
        var consumer = File.ReadAllText(InfrastructurePath("Consumers", "Affiliate", "AffiliateRefundConsumers.cs"));

        Assert.Contains("OrderRefunds", consumer, StringComparison.Ordinal);
        Assert.Contains(".Include(item => item.Lines)", consumer, StringComparison.Ordinal);
        Assert.Contains("refund_item_allocation_missing", consumer, StringComparison.Ordinal);
        Assert.Contains("OrderItemRefundedEvent", consumer, StringComparison.Ordinal);
    }

    [Fact]
    public void PaymentLifecyclePartialRefunds_MustPublishItemLevelAffiliateRefundEvents()
    {
        var service = File.ReadAllText(InfrastructurePath("Payments", "PaymentLifecycleService.cs"));
        var publishItemLevelRefundEventsMethod = ExtractMethodBody(service, "PublishItemLevelRefundEventsAsync");

        Assert.Contains("OrderItemRefundedEvent", service, StringComparison.Ordinal);
        Assert.Contains("PublishItemLevelRefundEventsAsync", service, StringComparison.Ordinal);
        Assert.Contains("BuildRefundItemIdempotencyKey", service, StringComparison.Ordinal);
        Assert.NotNull(publishItemLevelRefundEventsMethod);
        
        // PRODUCTION HARDENING: PublishItemLevelRefundEventsAsync must be called unconditionally
        // (not guarded by !isFullRefund) so that item-level events are published for both
        // partial refunds AND full refunds (including single-item orders).
        Assert.Contains("await PublishItemLevelRefundEventsAsync(", service, StringComparison.Ordinal);
        
        // CRITICAL: Ensure the call is NOT wrapped by an isFullRefund guard
        // The method must be called for both partial and full refunds
        var publishCallIndex = service.IndexOf("await PublishItemLevelRefundEventsAsync(", StringComparison.Ordinal);
        Assert.True(publishCallIndex > 0, "PublishItemLevelRefundEventsAsync call not found");
        
        // Extract a reasonable context window around the call (500 chars before and after)
        var contextStart = Math.Max(0, publishCallIndex - 500);
        var contextEnd = Math.Min(service.Length, publishCallIndex + 500);
        var contextAroundCall = service.Substring(contextStart, contextEnd - contextStart);
        
        // Assert that the call is NOT guarded by "if (!isFullRefund" or similar patterns
        Assert.DoesNotContain("if (!isFullRefund", contextAroundCall, StringComparison.Ordinal);
        Assert.DoesNotContain("if(!isFullRefund", contextAroundCall, StringComparison.Ordinal);
        Assert.DoesNotContain("if (isFullRefund == false", contextAroundCall, StringComparison.Ordinal);
        Assert.DoesNotContain("if(isFullRefund==false", contextAroundCall, StringComparison.Ordinal);
        
        Assert.Contains("RefundedAmount = line.Amount", publishItemLevelRefundEventsMethod, StringComparison.Ordinal);
        Assert.Contains("CumulativeRefundedAmount = item.RefundedAmount", publishItemLevelRefundEventsMethod, StringComparison.Ordinal);
    }

    [Fact]
    public void PaymentLifecycleRefunds_MustRequireOriginalCollectionAccountingPosted()
    {
        var service = File.ReadAllText(InfrastructurePath("Payments", "PaymentLifecycleService.cs"));
        var recordRefundMethod = ExtractMethodBody(service, "RecordRefundAsync");
        var accountingGate = ExtractMethodBody(service, "EnsureCollectionAccountingPostedForRefundAsync");

        Assert.NotNull(recordRefundMethod);
        Assert.NotNull(accountingGate);
        Assert.Contains("EnsureCollectionAccountingPostedForRefundAsync(order, ct)", recordRefundMethod!, StringComparison.Ordinal);
        Assert.Contains("ResolveMissingPostedAccountingKeysAsync", accountingGate!, StringComparison.Ordinal);
        Assert.Contains("BuildCodDeliveredIdempotencyKey", accountingGate!, StringComparison.Ordinal);
        Assert.Contains("BuildWalletApprovedIdempotencyKey", accountingGate!, StringComparison.Ordinal);
        Assert.Contains("ResolveGatewayCollectionAccountingKeysByOrderAsync", accountingGate!, StringComparison.Ordinal);
        Assert.Contains("Cannot refund until the original collection accounting journal is posted", service, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubRefundExecution_MustNotReleaseProcessingPayouts()
    {
        var service = File.ReadAllText(ApiPath("Services", "CommerceHub", "RefundExecutionService.cs"));
        var releaseMethod = ExtractMethodBody(service, "ReleasePendingPayoutAsync");

        Assert.Contains("EnsureNoProcessingPayoutForRefundEarningsAsync", service, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPayoutStatus.Processing", service, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPayoutStatus.ReconciliationRequired", service, StringComparison.Ordinal);
        Assert.Contains("REFUND_BLOCKED_BY_PROCESSING_PAYOUT", service, StringComparison.Ordinal);
        Assert.NotNull(releaseMethod);
        
        // Verify reconciliation-before-failure ordering
        Assert.Contains("CommerceHubPayoutStatus.ReconciliationRequired", releaseMethod!, StringComparison.Ordinal);
        Assert.Contains("payout.Status = CommerceHubPayoutStatus.Failed", releaseMethod!, StringComparison.Ordinal);
        
        var reconciliationIndex = releaseMethod!.IndexOf("CommerceHubPayoutStatus.ReconciliationRequired", StringComparison.Ordinal);
        var failureIndex = releaseMethod!.IndexOf("payout.Status = CommerceHubPayoutStatus.Failed", StringComparison.Ordinal);
        
        Assert.True(reconciliationIndex >= 0, "ReconciliationRequired status check must exist");
        Assert.True(failureIndex >= 0, "Failed status assignment must exist");
        Assert.True(reconciliationIndex < failureIndex, 
            "ReconciliationRequired check must appear before Failed status assignment to ensure reconciliation happens before marking as failed");
    }

    [Fact]
    public void CommerceHubRefundService_MustNotCallGatewayOrReleaseExternallyProcessingPayouts()
    {
        var service = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubRefundService.cs"));
        var migration = File.ReadAllText(InfrastructurePath("Migrations", "20260504120000_S70_CrossSystemCriticalRiskFinalClosure.sql"));
        var dbContext = File.ReadAllText(InfrastructurePath("Data", "ApplicationDbContext.cs"));
        var executeMethod = ExtractMethodBody(service, "ExecutePersistedRefundAsync");
        var resolveMethod = ExtractMethodBody(service, "ResolveGatewayExecutionResultAsync");
        var releaseMethod = ExtractMethodBody(service, "ReleasePendingPayoutAsync");

        Assert.NotNull(executeMethod);
        Assert.NotNull(resolveMethod);
        Assert.NotNull(releaseMethod);

        Assert.Contains("EnsureNoProcessingPayoutForRefundEarningsAsync", service, StringComparison.Ordinal);
        AssertContainsInOrder(
            executeMethod!,
            "snapshotEarnings",
            "EnsureOutwardMoneyMovementAllowedAsync",
            "currentEarnings",
            "EnsureNoProcessingPayoutForRefundEarningsAsync",
            "ClaimPendingRefundForGatewayExecutionAsync",
            "ResolveGatewayExecutionResultAsync");
        Assert.Contains("RefreshRefundStatusAsync", resolveMethod!, StringComparison.Ordinal);
        Assert.Contains("_refundGatewayService.RefundAsync", resolveMethod!, StringComparison.Ordinal);

        Assert.Contains("CommerceHubPayoutStatus.ReconciliationRequired", releaseMethod!, StringComparison.Ordinal);
        Assert.Contains("REFUND_BLOCKED_BY_PROCESSING_PAYOUT", releaseMethod!, StringComparison.Ordinal);
        
        // Verify reconciliation-before-failure ordering
        Assert.Contains("payout.Status = CommerceHubPayoutStatus.Failed", releaseMethod!, StringComparison.Ordinal);
        
        var reconciliationIndex = releaseMethod!.IndexOf("CommerceHubPayoutStatus.ReconciliationRequired", StringComparison.Ordinal);
        var failureIndex = releaseMethod!.IndexOf("payout.Status = CommerceHubPayoutStatus.Failed", StringComparison.Ordinal);
        
        Assert.True(reconciliationIndex >= 0, "ReconciliationRequired status check must exist");
        Assert.True(failureIndex >= 0, "Failed status assignment must exist");
        Assert.True(reconciliationIndex < failureIndex, 
            "ReconciliationRequired check must appear before Failed status assignment to ensure reconciliation happens before marking as failed");

        foreach (var source in new[] { migration, dbContext })
        {
            Assert.Contains("CK_CommerceHubPayouts_NoRefundFailureAfterHandoff", source, StringComparison.Ordinal);
            Assert.Contains("system:refund-control", source, StringComparison.Ordinal);
        }

        Assert.Contains("\"Status\" <> 2", migration, StringComparison.Ordinal);
        Assert.Contains("\\\"Status\\\" <> 2", dbContext, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubUnresolvedCapturedPayment_MustAtomicallyRouteSuspenseAndHoldKnownOrders()
    {
        var paymentService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPaymentService.cs"));
        var persistMethod = ExtractMethodBody(paymentService, "PersistUnresolvedPaymentWebhookAsync");
        var holdMethod = ExtractMethodBody(paymentService, "MarkKnownUnresolvedProviderSuccessOrderOnHoldAsync");

        Assert.NotNull(persistMethod);
        Assert.NotNull(holdMethod);

        AssertContainsInOrder(
            persistMethod!,
            "BeginTransactionAsync(IsolationLevel.Serializable",
            "AccountingOutboxMessage.Create",
            "MarkKnownUnresolvedProviderSuccessOrderOnHoldAsync",
            "SaveChangesAsync",
            "CommitAsync");

        Assert.Contains("CommerceHubBoundaryMutationScope.BeginPayment()", persistMethod!, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginOrderLifecycle()", persistMethod!, StringComparison.Ordinal);
        Assert.Contains("ResolveFrozenAffectedStoreIds", persistMethod!, StringComparison.Ordinal);
        Assert.Contains("ResolveFrozenUnresolvedCurrency", persistMethod!, StringComparison.Ordinal);
        Assert.Contains("deferredAuthorityReason", persistMethod!, StringComparison.Ordinal);
        Assert.Contains("return true;", persistMethod!, StringComparison.Ordinal);
        Assert.DoesNotContain("return false;", persistMethod!, StringComparison.Ordinal);
        Assert.DoesNotContain("return requiresSuspenseRouting ? canRouteToSuspense : true;", persistMethod!, StringComparison.Ordinal);
        Assert.Contains("marketplaceOrder.PaymentStatus != PaymentStatus.Pending", holdMethod!, StringComparison.Ordinal);
        Assert.Contains("marketplaceOrder.Status = OrderStatus.OnHold", holdMethod!, StringComparison.Ordinal);
        Assert.Contains("marketplaceOrder.TouchProjectionMutationVersion()", holdMethod!, StringComparison.Ordinal);
        Assert.Contains("CommerceHubMarketplaceOrderStatusUpdatedEvent", holdMethod!, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubUnresolvedProviderCash_MustNotInventFallbackCurrencyAuthority()
    {
        var paymentService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPaymentService.cs"));

        Assert.Contains("RequireCommerceHubPaymentCurrency(callback.Currency, \"unresolved Stripe webhook\")", paymentService, StringComparison.Ordinal);
        Assert.DoesNotContain("UnknownCurrencyCode", paymentService, StringComparison.Ordinal);
        Assert.Contains("RequireCommerceHubPaymentCurrency(providerCurrency, \"gateway callback\")", paymentService, StringComparison.Ordinal);
        Assert.Contains("COMMERCEHUB_PAYMENT_CURRENCY_INVALID", paymentService, StringComparison.Ordinal);
        Assert.Contains("FinancialCurrencyGuard.PlatformBaseCurrencyCode", paymentService, StringComparison.Ordinal);
        Assert.DoesNotContain("NormalizeCurrency(payload.Obj.Currency, \"EGP\")", paymentService, StringComparison.Ordinal);
        Assert.DoesNotContain("NormalizeCurrency(paymentIntent.Currency, \"EGP\")", paymentService, StringComparison.Ordinal);
        Assert.DoesNotContain("NormalizeCurrency(currency, \"EGP\")", paymentService, StringComparison.Ordinal);
    }

    [Fact]
    public void MarketplaceProductProjection_MustTreatSqlSoftDeleteAsAuthoritative()
    {
        var projection = File.ReadAllText(InfrastructurePath("ReadModels", "Marketplace", "MarketplaceReadModelProjectionServices.cs"));
        var rebuildMethod = ExtractMethodBody(projection, "RebuildAsync");
        var availabilityMethod = ExtractMethodBody(projection, "ResolveIsAvailable");

        Assert.NotNull(rebuildMethod);
        Assert.NotNull(availabilityMethod);

        AssertContainsInOrder(
            rebuildMethod!,
            "if (product.IsDeleted ||",
            "DeleteIfFreshAsync");
        Assert.Contains("ProductVariantLifecycleAuthority.IsPubliclyVisibleLineage", rebuildMethod!, StringComparison.Ordinal);
        Assert.Contains("product.IsDeleted || !product.IsActive || !product.IsOnlineVisible", availabilityMethod!, StringComparison.Ordinal);
    }

    [Fact]
    public void AffiliatePayoutRequests_MustBeStoreScopedAndSerialized()
    {
        var service = File.ReadAllText(InfrastructurePath("Services", "Affiliate", "AffiliatePayoutService.cs"));
        var contract = File.ReadAllText(CorePath("Interfaces", "Affiliate", "IAffiliatePayoutService.cs"));
        var orchestrator = File.ReadAllText(InfrastructurePath("Services", "Affiliate", "AffiliateSettlementOrchestrator.cs"));

        Assert.Matches(@"GetAvailableBalanceAsync\(\s*Guid affiliateAccountId,\s*Guid storeTenantId", contract);
        Assert.Matches(@"RequestPayoutAsync\(\s*Guid affiliateAccountId,\s*decimal amount,\s*Guid storeTenantId", contract);
        Assert.Matches(@"AutoReconcileWithdrawalRequestsAsync\(\s*Guid affiliateAccountId,\s*Guid storeTenantId", contract);
        Assert.Contains("BeginTransactionAsync(IsolationLevel.Serializable", service, StringComparison.Ordinal);
        Assert.Contains("AcquireAffiliatePayoutBalanceLockAsync", service, StringComparison.Ordinal);
        Assert.Contains("StoreTenantId is required when an affiliate has balances across multiple stores", service, StringComparison.Ordinal);
        Assert.Contains("StoreTenantId is required for store-scoped affiliate payout reconciliation", service, StringComparison.Ordinal);
        Assert.Contains(@"AND """"StoreTenantId"""" = {storeTenantId}", service, StringComparison.Ordinal);
        Assert.Contains(@"AND [StoreTenantId] = {storeTenantId}", service, StringComparison.Ordinal);
        Assert.Contains("r.StoreTenantId == storeTenantId", service, StringComparison.Ordinal);
        Assert.Contains("l.StoreTenantId == storeTenantId.Value", service, StringComparison.Ordinal);
        Assert.Contains("r.StoreTenantId == storeTenantId.Value", service, StringComparison.Ordinal);
        Assert.Contains("GetAvailableBalanceAsync(affiliateAccountId, storeTenantId", orchestrator, StringComparison.Ordinal);
        Assert.Matches(@"AutoReconcileWithdrawalRequestsAsync\(\s*affiliateAccountId,\s*storeTenantId,", orchestrator);
        Assert.Contains("r.StoreTenantId == storeTenantId", orchestrator, StringComparison.Ordinal);
    }

    [Fact]
    public void SuperAdminVendorPayouts_MustRequireImmutableExternalCompletionEvidence()
    {
        var service = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPayoutService.cs"));
        var orchestration = File.ReadAllText(ApiPath("SuperAdmin", "Services", "SuperAdminPayoutOrchestrationService.cs"));
        var dto = File.ReadAllText(ApiPath("SuperAdmin", "DTOs", "SuperAdminPayoutDtos.cs"));
        var webPage = File.ReadAllText(WebPath("src", "super-admin", "payouts", "PayoutsPage.tsx"));
        var webService = File.ReadAllText(WebPath("src", "super-admin", "payouts", "payoutsService.ts"));

        Assert.Contains("ExternalReferenceId", dto, StringComparison.Ordinal);
        Assert.Contains("BankTransactionId", dto, StringComparison.Ordinal);
        Assert.Contains("EnsureTerminalPayoutEvidence", service, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_EXTERNAL_CONFIRMATION_REQUIRED", service, StringComparison.Ordinal);
        Assert.Contains("CompletionEvidenceFingerprint", service, StringComparison.Ordinal);
        Assert.Contains("BuildCompletionEvidenceFingerprint", service, StringComparison.Ordinal);
        Assert.Contains("ExternalTransferIdempotencyKey = BuildExternalTransferIdempotencyKey", service, StringComparison.Ordinal);
        Assert.Contains("BuildExternalTransferIdempotencyKey", service, StringComparison.Ordinal);
        Assert.Contains("EnsureExternalPayoutEvidenceIsUniqueAsync", service, StringComparison.Ordinal);
        Assert.Contains("IsExternalPayoutEvidenceUniqueViolation", service, StringComparison.Ordinal);
        Assert.Contains("ExternalTransferConfirmedExecuted = true", orchestration, StringComparison.Ordinal);
        Assert.Contains("ExternalTransferHandoffToken", orchestration, StringComparison.Ordinal);
        Assert.Contains("externalReferenceId?: string", webService, StringComparison.Ordinal);
        Assert.Contains("bankTransactionId?: string", webService, StringComparison.Ordinal);
        Assert.Contains("externalReferenceId", webPage, StringComparison.Ordinal);
        Assert.Contains("bankTransactionId", webPage, StringComparison.Ordinal);
    }

    [Fact]
    public void SuperAdminPayoutQueue_MustConsumeBackendExecutionAuthorityInsteadOfFrontendInference()
    {
        var dto = File.ReadAllText(ApiPath("SuperAdmin", "DTOs", "SuperAdminPayoutDtos.cs"));
        var service = File.ReadAllText(ApiPath("SuperAdmin", "Services", "SuperAdminPayoutsService.cs"));
        var readService = File.ReadAllText(ApiPath("Services", "CommerceHub", "Finance", "CommerceHubVendorPayoutReadService.cs"));
        var riskFlags = File.ReadAllText(CorePath("Constants", "PayoutRiskFlags.cs"));
        var webPage = File.ReadAllText(WebPath("src", "super-admin", "payouts", "PayoutsPage.tsx"));

        Assert.Contains("CanPreparePayout", dto, StringComparison.Ordinal);
        Assert.Contains("CanCompletePayout", dto, StringComparison.Ordinal);
        Assert.Contains("ExecutionBlockedCode", dto, StringComparison.Ordinal);
        Assert.Contains("ExecutionBlockedReason", dto, StringComparison.Ordinal);
        Assert.Contains("BuildQueueExecutionState", service, StringComparison.Ordinal);
        Assert.Contains("ExternalTransferReconciliationRequired", riskFlags, StringComparison.Ordinal);
        Assert.Contains("StaleExternalTransferProcessing", riskFlags, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPayoutStatus.ReconciliationRequired", readService, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPayoutStatus.Processing", readService, StringComparison.Ordinal);
        Assert.Contains("item.canPreparePayout", webPage, StringComparison.Ordinal);
        Assert.Contains("item.canCompletePayout", webPage, StringComparison.Ordinal);
        Assert.Contains("item.executionBlockedReason", webPage, StringComparison.Ordinal);
        Assert.DoesNotContain("!item.riskFlags.includes('VendorSuspended')", webPage, StringComparison.Ordinal);
    }

    [Fact]
    public void SuperAdminPayoutQueue_MustSurfacePaginationAndGlobalQueueTotals()
    {
        var dto = File.ReadAllText(ApiPath("SuperAdmin", "DTOs", "SuperAdminPayoutDtos.cs"));
        var service = File.ReadAllText(ApiPath("SuperAdmin", "Services", "SuperAdminPayoutsService.cs"));
        var webPage = File.ReadAllText(WebPath("src", "super-admin", "payouts", "PayoutsPage.tsx"));
        var webService = File.ReadAllText(WebPath("src", "super-admin", "payouts", "payoutsService.ts"));

        Assert.Contains("TotalEligibleAmount", dto, StringComparison.Ordinal);
        Assert.Contains("TotalHeldAmount", dto, StringComparison.Ordinal);
        Assert.Contains("TotalPayableAmount", dto, StringComparison.Ordinal);
        Assert.Contains("CurrencySummaries", dto, StringComparison.Ordinal);
        Assert.Contains("HasCurrencyMix", dto, StringComparison.Ordinal);
        Assert.Contains("AggregateCurrency", dto, StringComparison.Ordinal);
        Assert.Contains("GroupBy(snapshot => snapshot.Currency)", service, StringComparison.Ordinal);
        Assert.Contains("hasSingleAggregateCurrency", service, StringComparison.Ordinal);
        Assert.Contains("pageSize: number", webService, StringComparison.Ordinal);
        Assert.Contains("totalPages?: number", webService, StringComparison.Ordinal);
        Assert.Contains("currencySummaries", webPage, StringComparison.Ordinal);
        Assert.Contains("hasCurrencyMix", webPage, StringComparison.Ordinal);
        Assert.Contains("queueMeta?.totalPages", webPage, StringComparison.Ordinal);
        Assert.Contains("setPage((currentPage) => Math.min", webPage, StringComparison.Ordinal);
    }

    [Fact]
    public void VendorRepaymentProofs_MustUseDeterministicRequestIdentityForUploadRecovery()
    {
        var service = File.ReadAllText(ApiPath("Services", "CommerceHub", "Finance", "CommerceHubVendorPaymentsService.cs"));

        Assert.Contains("BuildDeterministicPaymentRequestId", service, StringComparison.Ordinal);
        Assert.Contains("commercehub-vendor-payment-request", service, StringComparison.Ordinal);
        Assert.Contains("BuildProofUploadPathForAttempt(vendorId, paymentRequestId, attemptId)", service, StringComparison.Ordinal);
        Assert.Contains("var attemptId = Guid.NewGuid();", service, StringComparison.Ordinal);
        
        // Extract BuildDeterministicPaymentRequestId helper body to ensure it doesn't use Guid.NewGuid()
        var helperStart = service.IndexOf("private static Guid BuildDeterministicPaymentRequestId(", StringComparison.Ordinal);
        var helperEnd = service.IndexOf("\n    }", helperStart, StringComparison.Ordinal);
        var helperBody = service.Substring(helperStart, helperEnd - helperStart);
        
        Assert.DoesNotContain("Guid.NewGuid()", helperBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Guid.NewGuid().ToString", helperBody, StringComparison.Ordinal);
    }

    [Fact]
    public void VendorRepaymentEvidence_MustBeOneTimeExternalFinancialEvidence()
    {
        var service = File.ReadAllText(ApiPath("Services", "CommerceHub", "Finance", "CommerceHubVendorPaymentsService.cs"));
        var requestConfig = File.ReadAllText(InfrastructurePath("Data", "Configurations", "CommerceHubVendorPaymentRequestConfiguration.cs"));
        var proofConfig = File.ReadAllText(InfrastructurePath("Data", "Configurations", "CommerceHubVendorPaymentProofConfiguration.cs"));
        var migration = File.ReadAllText(InfrastructurePath("Migrations", "20260501170000_S64_CrossSystemCriticalRiskProductionClosure.sql"));

        Assert.Contains("EnsureExternalVendorRepaymentEvidenceNotReusedAsync", service, StringComparison.Ordinal);
        Assert.Contains("PAYMENT_EVIDENCE_ALREADY_USED", service, StringComparison.Ordinal);
        Assert.Contains("UX_CommerceHubVendorPaymentRequests_Live_Method_TransferReference", requestConfig, StringComparison.Ordinal);
        Assert.Contains("UX_CommerceHubVendorPaymentProofs_ContentHash", proofConfig, StringComparison.Ordinal);
        Assert.Contains("CREATE UNIQUE INDEX IF NOT EXISTS \"UX_CommerceHubVendorPaymentRequests_Live_Method_TransferReference\"", migration, StringComparison.Ordinal);
        Assert.Contains("CREATE UNIQUE INDEX IF NOT EXISTS \"UX_CommerceHubVendorPaymentProofs_ContentHash\"", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void VendorRepaymentApproval_MustRecognizeInwardCashWithoutOutwardMoneyReadinessGate()
    {
        var service = File.ReadAllText(ApiPath("Services", "CommerceHub", "Finance", "CommerceHubVendorPaymentsService.cs"));
        var approveMethod = ExtractMethodBody(service, "public async Task<SuperAdminVendorPaymentRequestItemDto> ApprovePaymentRequestAsync");

        Assert.NotNull(approveMethod);
        Assert.DoesNotContain("EnsureOutwardMoneyMovementAllowedAsync", approveMethod!, StringComparison.Ordinal);
        Assert.DoesNotContain("ICommerceHubFinancialReadinessGate", service, StringComparison.Ordinal);
        Assert.Contains("EnsurePendingRequestHasNoLedgerEntriesAsync", approveMethod!, StringComparison.Ordinal);
        Assert.Contains("RecordTransactionAsync", approveMethod!, StringComparison.Ordinal);
        Assert.Contains("VendorDebtDelta: -paymentRequest.Amount", approveMethod!, StringComparison.Ordinal);
    }

    [Fact]
    public void CentralSettlementExecution_MustEnforceEgpOnlyCurrencyAuthority()
    {
        var currencyGuard = File.ReadAllText(CorePath("Financial", "FinancialCurrencyGuard.cs"));
        var vendorPaymentsService = File.ReadAllText(ApiPath("Services", "CommerceHub", "Finance", "CommerceHubVendorPaymentsService.cs"));
        var payoutService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPayoutService.cs"));

        Assert.Contains("PlatformBaseCurrencyCode = \"EGP\"", currencyGuard, StringComparison.Ordinal);
        Assert.Contains("RequireEgpCurrency", currencyGuard, StringComparison.Ordinal);
        Assert.Contains("PAYMENT_CURRENCY_NOT_SUPPORTED", vendorPaymentsService, StringComparison.Ordinal);
        Assert.Contains("RequireEgpCurrency", payoutService, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_CURRENCY_MISMATCH", payoutService, StringComparison.Ordinal);
    }

    [Fact]
    public void StorefrontAuthenticatedCustomerAuthority_MustRecheckCurrentSqlActiveStatus()
    {
        var checkoutService = File.ReadAllText(ApiPath("Services", "Checkout", "CheckoutService.cs"));
        var checkoutContract = File.ReadAllText(ApiPath("Services", "Checkout", "ICheckoutService.cs"));
        var checkoutController = File.ReadAllText(ApiPath("Controllers", "CheckoutController.cs"));
        var cartController = File.ReadAllText(ApiPath("Controllers", "StorefrontCartController.cs"));

        Assert.Contains("CheckoutCustomerInactiveException", checkoutContract, StringComparison.Ordinal);
        Assert.Contains("!customer.IsActive", checkoutService, StringComparison.Ordinal);
        Assert.Contains("throw new CheckoutCustomerInactiveException", checkoutService, StringComparison.Ordinal);
        Assert.Contains("catch (CheckoutCustomerInactiveException", checkoutController, StringComparison.Ordinal);
        Assert.Contains("ValidateAuthenticatedCustomerAuthorityAsync", cartController, StringComparison.Ordinal);
        Assert.Contains("customer.IsActive", cartController, StringComparison.Ordinal);
        Assert.Contains("CUSTOMER_INACTIVE", cartController, StringComparison.Ordinal);
    }

    [Fact]
    public void SaleMutation_MustNotDeleteClaimedOrRetryingAccountingOutboxWork()
    {
        var service = File.ReadAllText(ApiPath("Services", "SaleFinancialIntegrityService.cs"));

        Assert.Contains("ClaimedUntilUtc", service, StringComparison.Ordinal);
        Assert.Contains("AttemptCount > 0", service, StringComparison.Ordinal);
        Assert.Contains("NextRetryAtUtc.HasValue", service, StringComparison.Ordinal);
        Assert.Contains("LastError", service, StringComparison.Ordinal);
        Assert.Contains("claimed, retrying, or failed accounting outbox work", service, StringComparison.Ordinal);
        Assert.Matches(
            @"unsafeOutboxMessage[\s\S]+throw new InvalidOperationException[\s\S]+RemoveRange\(pendingOutboxMessages\)",
            service);
    }

    [Fact]
    public void CompletedVendorPayouts_MustRequireEvidenceForManualAndNonManualMethods()
    {
        var superAdminService = File.ReadAllText(ApiPath("SuperAdmin", "Services", "SuperAdminPayoutsService.cs"));
        var payoutService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPayoutService.cs"));
        var dbContext = File.ReadAllText(InfrastructurePath("Data", "ApplicationDbContext.cs"));
        var terminalEvidenceMethod = ExtractMethodBody(payoutService, "EnsureTerminalPayoutEvidence");

        Assert.NotNull(terminalEvidenceMethod);
        Assert.DoesNotContain("payoutMethod.MethodType != CommerceHubPayoutMethod.Manual", superAdminService, StringComparison.Ordinal);
        Assert.DoesNotContain("payout.Method == CommerceHubPayoutMethod.Manual", terminalEvidenceMethod!, StringComparison.Ordinal);
        Assert.Contains("Completed payouts require a terminal bank, provider, or manual transfer reference", payoutService, StringComparison.Ordinal);
        Assert.Contains("CK_CommerceHubPayouts_Completed_Evidence", dbContext, StringComparison.Ordinal);
        Assert.Contains("NULLIF(btrim(\\\"ExternalReferenceId\\\"), '') IS NOT NULL", dbContext, StringComparison.Ordinal);
        Assert.DoesNotContain("CK_CommerceHubPayouts_NonManual_Completed_Evidence", dbContext, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Method\" = 2", dbContext, StringComparison.Ordinal);
    }

    [Fact]
    public void OutwardMoneyMovement_MustUseVendorScopedFinancialReconciliation()
    {
        var gate = File.ReadAllText(ApiPath("Services", "CommerceHub", "Finance", "CommerceHubFinancialReadinessGate.cs"));
        var reconciliation = File.ReadAllText(ApiPath("Services", "CommerceHub", "FinancialReconciliationService.cs"));

        Assert.DoesNotContain("_ = tenantId;", gate, StringComparison.Ordinal);
        Assert.Contains("checkedVendorIds", gate, StringComparison.Ordinal);
        Assert.Contains("affectedVendorIds", gate, StringComparison.Ordinal);
        Assert.Contains("COMMERCEHUB_FINANCIAL_RECONCILIATION_VENDOR_SCOPE_REQUIRED", gate, StringComparison.Ordinal);
        Assert.Contains("allowFreshStartupBypass &&", gate, StringComparison.Ordinal);
        Assert.Contains("HasVendorFinancialFootprintAsync", gate, StringComparison.Ordinal);
        Assert.Contains("transaction.EntityType == CommerceHubFinancialEntityType.Vendor", gate, StringComparison.Ordinal);
        Assert.Contains("CheckedVendorIds = ResolveCheckedVendorIds(context)", reconciliation, StringComparison.Ordinal);
        Assert.Contains("AffectedVendorIds = ResolveAffectedVendorIds(context, findings)", reconciliation, StringComparison.Ordinal);
        Assert.Contains("scope.AffectedVendorIds.Count == 0 || scope.AffectedVendorIds.Contains(tenantId)", gate, StringComparison.Ordinal);
    }

    [Fact]
    public void FinancialIntegrityMigrations_MustValidateHistoricalRowsBeforeAllowingOutwardMoneyMovement()
    {
        var payoutAllocationMigration = File.ReadAllText(InfrastructurePath("Migrations", "20260428173000_S48_ComHubEarningPayoutAllocation.sql"));
        var criticalClosureMigration = File.ReadAllText(InfrastructurePath("Migrations", "20260429100000_S49_CrossSystemCriticalRiskClosure.sql"));

        Assert.Contains("payout_allocations", payoutAllocationMigration, StringComparison.Ordinal);
        Assert.Contains("historical allocation violations", payoutAllocationMigration, StringComparison.Ordinal);
        Assert.Contains("VALIDATE CONSTRAINT \"CK_CHVendorEarnings_PayoutAllocation_NonNegative\"", payoutAllocationMigration, StringComparison.Ordinal);
        Assert.Contains("VALIDATE CONSTRAINT \"CK_CHVendorEarnings_PayoutAllocation_NotOverAllocated\"", payoutAllocationMigration, StringComparison.Ordinal);
        Assert.Contains("Completed CommerceHub payouts without immutable transfer evidence", criticalClosureMigration, StringComparison.Ordinal);
        Assert.Contains("CK_CommerceHubPayouts_Completed_Evidence", criticalClosureMigration, StringComparison.Ordinal);
        Assert.Contains("VALIDATE CONSTRAINT %I", criticalClosureMigration, StringComparison.Ordinal);
        Assert.Contains("CK_CommerceHubPayments_Paid_State_Complete", criticalClosureMigration, StringComparison.Ordinal);
        Assert.Contains("CK_CommerceHubRefunds_Reversals_Total_Amount", criticalClosureMigration, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubListingProjection_MustRejectStaleListingLifecycleEvents()
    {
        var consumer = File.ReadAllText(InfrastructurePath("Consumers", "CommerceHubProjectionConsumer.cs"));
        var document = File.ReadAllText(InfrastructurePath("MongoDB", "Documents", "CommerceHubProductDocument.cs"));

        Assert.Contains("LastListingEventOccurredAtUtc", document, StringComparison.Ordinal);
        Assert.Contains("LastListingEventVersion", document, StringComparison.Ordinal);
        Assert.Contains("IsStaleListingEvent", consumer, StringComparison.Ordinal);
        Assert.Contains("BuildFreshListingEventFilter", consumer, StringComparison.Ordinal);
        Assert.Contains("SaveListingDocumentIfFreshAsync", consumer, StringComparison.Ordinal);
        Assert.Contains("LoadSqlVariantStockAuthorityAsync", consumer, StringComparison.Ordinal);
        Assert.Contains("document.AvailableStock = sqlStockAuthority?.TotalStockQuantity ?? 0m", consumer, StringComparison.Ordinal);
        Assert.Contains("filter.Lt(x => x.LastListingEventOccurredAtUtc", consumer, StringComparison.Ordinal);
        Assert.Contains("filter.Lt(x => x.LastListingEventVersion", consumer, StringComparison.Ordinal);
        Assert.Contains("ServerErrorCategory.DuplicateKey", consumer, StringComparison.Ordinal);
    }

    [Fact]
    public void SuperAdminVendorPayouts_MustBindCompletedPayoutsToEarningAllocations()
    {
        var orchestration = File.ReadAllText(ApiPath("SuperAdmin", "Services", "SuperAdminPayoutOrchestrationService.cs"));
        var payoutService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPayoutService.cs"));
        var earning = File.ReadAllText(CorePath("Entities", "CommerceHubVendorEarning.cs"));
        var dbContext = File.ReadAllText(InfrastructurePath("Data", "ApplicationDbContext.cs"));
        var migration = File.ReadAllText(InfrastructurePath("Migrations", "20260428173000_S48_ComHubEarningPayoutAllocation.sql"));

        Assert.Contains("PaidOutAmount", earning, StringComparison.Ordinal);
        Assert.Contains("PayoutReservedAmount", earning, StringComparison.Ordinal);
        Assert.Contains("SelectCanonicalPayoutEarningsAsync", orchestration, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_EARNING_ALLOCATION_CONFLICT", orchestration, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPayoutItems.Add", payoutService, StringComparison.Ordinal);
        Assert.Contains("PayoutReservedAmount =", payoutService, StringComparison.Ordinal);
        Assert.Contains("ResolvePayoutAllocatableAmount", payoutService, StringComparison.Ordinal);
        Assert.Contains("ApplyCompletedPayoutAllocation", payoutService, StringComparison.Ordinal);
        Assert.Contains("ReleasePayoutAllocation", payoutService, StringComparison.Ordinal);
        Assert.Contains("PaidOutAmount", dbContext, StringComparison.Ordinal);
        Assert.Contains("PayoutReservedAmount", dbContext, StringComparison.Ordinal);
        Assert.Contains("CK_CHVendorEarnings_PayoutAllocation_NotOverAllocated", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubOrderProjection_MustUseSourceMutationFreshnessAndStoreFanout()
    {
        var consumer = File.ReadAllText(InfrastructurePath("Consumers", "CommerceHubOrderProjectionConsumer.cs"));
        var document = File.ReadAllText(InfrastructurePath("MongoDB", "Documents", "CommerceHubOrderDocument.cs"));
        var management = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubOrderManagementService.cs"));
        var marketplaceOrder = File.ReadAllText(CorePath("Entities", "CommerceHubMarketplaceOrder.cs"));
        var subOrder = File.ReadAllText(CorePath("Entities", "CommerceHubSubOrder.cs"));
        var dbContext = File.ReadAllText(InfrastructurePath("Data", "ApplicationDbContext.cs"));
        var publisher = File.ReadAllText(InfrastructurePath("EventBus", "MassTransitEventPublisher.cs"));
        var migration = File.ReadAllText(InfrastructurePath("Migrations", "20260430100000_S60_CommerceHubOrderAggregateVersionHardening.sql"));

        Assert.Contains("LastSourceMutationTicks", document, StringComparison.Ordinal);
        Assert.Contains("ResolveSourceMutationVersion", consumer, StringComparison.Ordinal);
        Assert.Contains("order.ProjectionMutationVersion", consumer, StringComparison.Ordinal);
        Assert.Contains("subOrder.ProjectionMutationVersion", consumer, StringComparison.Ordinal);
        Assert.Contains("Filter.Lt(x => x.LastSourceMutationTicks, sourceMutationVersion)", consumer, StringComparison.Ordinal);
        Assert.Contains("CommerceHubMarketplaceOrder : BaseEntity, IProjectionVersioned", marketplaceOrder, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSubOrder : BaseEntity, IProjectionVersioned", subOrder, StringComparison.Ordinal);
        Assert.Contains("IncrementCommerceHubMarketplaceOrderProjectionMutationVersions", dbContext, StringComparison.Ordinal);
        Assert.Contains("projectionMarkerOnly", dbContext, StringComparison.Ordinal);
        Assert.Contains("IncrementProjectionMutationVersion(ChangeTracker.Entries<CommerceHubSubOrder>())", dbContext, StringComparison.Ordinal);
        Assert.Contains("TouchProjectionMutationVersion", marketplaceOrder, StringComparison.Ordinal);
        Assert.Contains("marketplaceOrder.TouchProjectionMutationVersion();", management, StringComparison.Ordinal);
        Assert.Contains("CommerceHubMarketplaceOrders", publisher, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS \"ProjectionMutationVersion\"", migration, StringComparison.Ordinal);
        Assert.Contains("CK_CommerceHubMarketplaceOrders_ProjectionMutationVersion_Positive", migration, StringComparison.Ordinal);
        Assert.Contains("GetAffectedStoreIds", marketplaceOrder, StringComparison.Ordinal);
        Assert.Contains(".OrderBy(storeId => storeId)", marketplaceOrder, StringComparison.Ordinal);
        Assert.Contains("var affectedStoreIds = marketplaceOrder.GetAffectedStoreIds();", management, StringComparison.Ordinal);
        Assert.Contains("AffectedStoreIds = affectedStoreIds", management, StringComparison.Ordinal);
    }

    [Fact]
    public void ProcessingPayoutFailure_MustRequireExternalTransferNonExecutionProof()
    {
        var service = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPayoutService.cs"));
        var dto = File.ReadAllText(ApiPath("DTOs", "CommerceHub", "CommerceHubFinanceDtos.cs"));
        var dbContext = File.ReadAllText(InfrastructurePath("Data", "ApplicationDbContext.cs"));
        var migration = File.ReadAllText(InfrastructurePath("Migrations", "20260501170000_S64_CrossSystemCriticalRiskProductionClosure.sql"));
        var globalEvidenceMigration = File.ReadAllText(InfrastructurePath("Migrations", "20260507223000_S73_GlobalPayoutEvidenceUniqueness.sql"));

        Assert.Contains("ExternalTransferConfirmedNotExecuted", dto, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_EXTERNAL_TRANSFER_RECONCILIATION_REQUIRED", service, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_FAILURE_WITH_EXTERNAL_EVIDENCE_BLOCKED", service, StringComparison.Ordinal);
        Assert.Contains("HasExternalPayoutEvidence", service, StringComparison.Ordinal);
        Assert.Contains("UX_CommerceHubPayouts_Live_ExternalReferenceId", dbContext, StringComparison.Ordinal);
        Assert.Contains("UX_CommerceHubPayouts_Live_BankTransactionId", dbContext, StringComparison.Ordinal);
        Assert.Contains("CREATE UNIQUE INDEX IF NOT EXISTS \"UX_CommerceHubPayouts_Live_ExternalReferenceId\"", migration, StringComparison.Ordinal);
        Assert.Contains("CREATE UNIQUE INDEX IF NOT EXISTS \"UX_CommerceHubPayouts_Live_BankTransactionId\"", migration, StringComparison.Ordinal);
        Assert.Contains("ON \"CommerceHubPayouts\" (\"ExternalReferenceId\")", globalEvidenceMigration, StringComparison.Ordinal);
        Assert.Contains("ON \"CommerceHubPayouts\" (\"BankTransactionId\")", globalEvidenceMigration, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductStockPriceSearchIndex_MustWaitForFreshMongoProjection()
    {
        var consumer = File.ReadAllText(InfrastructurePath("Consumers", "ProductStockPriceSearchIndexConsumer.cs"));

        Assert.Contains("IsMongoProjectionOlderThanEvent", consumer, StringComparison.Ordinal);
        Assert.Contains("ProjectionNotReadyException", consumer, StringComparison.Ordinal);
        Assert.Contains("product.LastEventVersion < eventVersion", consumer, StringComparison.Ordinal);
        Assert.Contains("product.LastEventVersion == eventVersion", consumer, StringComparison.Ordinal);
        Assert.Contains("product.LastEventOccurredAtUtc.Value < eventOccurredAtUtc", consumer, StringComparison.Ordinal);
        Assert.Contains("throw;", consumer, StringComparison.Ordinal);
    }

    [Fact]
    public void AffiliateCommissionForAttributedOrder_MustRequireCanonicalAttributionAuthority()
    {
        var service = File.ReadAllText(InfrastructurePath("Services", "Affiliate", "AffiliateService.cs"));
        var recordMethod = ExtractMethodBody(service, "RecordCommissionForOrderAsync");

        Assert.NotNull(recordMethod);
        Assert.Contains("order.AffiliateAccountId.HasValue", recordMethod!, StringComparison.Ordinal);
        Assert.Contains("order.AffiliateReferralCode", recordMethod!, StringComparison.Ordinal);
        Assert.Contains("Canonical affiliate attribution is missing", recordMethod!, StringComparison.Ordinal);
        Assert.Contains("attribution_missing_authoritative", recordMethod!, StringComparison.Ordinal);
        Assert.Contains("attribution_snapshot_incomplete", recordMethod!, StringComparison.Ordinal);
        Assert.DoesNotContain("AttributeOrderToAffiliateFromOrderSnapshotAsync", recordMethod!, StringComparison.Ordinal);
        Assert.Contains("Commission issuance must never call this directly", service, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubCheckout_MustFailClosedWhenPromoConsumptionFails()
    {
        var checkout = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubCheckoutService.cs"));

        Assert.Contains("aborting checkout to prevent silent repricing", checkout, StringComparison.Ordinal);
        Assert.Contains("PROMO_CONSUMPTION_FAILED", checkout, StringComparison.Ordinal);
        Assert.Contains("StatusCodes.Status409Conflict", checkout, StringComparison.Ordinal);
        Assert.DoesNotMatch(
            @"if\s*\(\s*!promoConsumption\.IsApplied\s*\)[\s\S]{0,500}ResetPromoFinancials\(marketplaceOrder\)",
            checkout);
    }

    [Fact]
    public void OutwardMoneyMovement_MustGateOnFailedOrStaleIntegrationBackbone()
    {
        var gate = File.ReadAllText(ApiPath("Services", "CommerceHub", "Finance", "CommerceHubFinancialReadinessGate.cs"));

        Assert.Contains("IntegrationBackboneMaxLag", gate, StringComparison.Ordinal);
        Assert.Contains("staleCutoffUtc", gate, StringComparison.Ordinal);
        Assert.Contains("message.RetryCount >= OutboxMessage.MaxRetries", gate, StringComparison.Ordinal);
        Assert.Contains("message.LastErrorAtUtc != null", gate, StringComparison.Ordinal);
        Assert.Contains("message.CreatedAtUtc < staleCutoffUtc", gate, StringComparison.Ordinal);
        Assert.Contains("BuildCriticalEventTypeFilter", gate, StringComparison.Ordinal);
        Assert.Contains("EventMayAffectTenant(message.TenantId, message.EventPayload, tenantId)", gate, StringComparison.Ordinal);
        Assert.Contains("authoritativeMessagesInspected", gate, StringComparison.Ordinal);
        Assert.Contains("LogLargeTenantScopeInspection(", gate, StringComparison.Ordinal);
        Assert.DoesNotContain(".Where(message => message.TenantId == null || message.TenantId == tenantId)", gate, StringComparison.Ordinal);
        Assert.Contains("return AffectedTenantScopeResolver.Resolve(baseTenantId, eventPayload).Count == 0", gate, StringComparison.Ordinal);
        Assert.Contains("AffectedTenantScopeResolver.ContainsTenant", gate, StringComparison.Ordinal);
        Assert.Contains("MassTransitOutboxMessage", gate, StringComparison.Ordinal);
        Assert.Contains("BuildCriticalMassTransitOutboxMessageTypeFilter", gate, StringComparison.Ordinal);
        Assert.Contains("MASS_TRANSIT_BUS_OUTBOX_NOT_READY", gate, StringComparison.Ordinal);
        Assert.Contains("AUTHORITATIVE_EVENT_BACKBONE_NOT_READY", gate, StringComparison.Ordinal);
        Assert.Contains("marketplaceMessagesInspected", gate, StringComparison.Ordinal);
        Assert.Contains("EventMayAffectTenant(message.TenantId, message.PayloadJson, tenantId)", gate, StringComparison.Ordinal);
        Assert.Contains("COMMERCEHUB_FINANCIAL_RECONCILIATION_NOT_READY", gate, StringComparison.Ordinal);
        Assert.Contains("Blocking CommerceHub outward money movement because marketplace event backbone has an unhealthy unpublished message", gate, StringComparison.Ordinal);
        Assert.Contains("Blocking CommerceHub outward money movement because stock sync backbone has an unhealthy unpublished message", gate, StringComparison.Ordinal);
        
        // SECURITY: Verify fail-closed branch for malformed event payloads
        Assert.Contains("catch (InvalidEventPayloadException)", gate, StringComparison.Ordinal);
        Assert.Matches(@"catch\s*\(\s*InvalidEventPayloadException\s*\)\s*\{\s*return\s+true\s*;\s*\}", gate);
    }

    [Fact]
    public void SuccessPaymentWebhookWithoutKnownIntent_MustKeepSuspenseOutboxRetryable()
    {
        var service = File.ReadAllText(ApiPath("Services", "Payments", "PaymentService.cs"));

        Assert.Contains("UnresolvedOnlinePayment", service, StringComparison.Ordinal);
        Assert.Contains("routedToSuspense", service, StringComparison.Ordinal);
        Assert.Contains("SUCCESS event - re-throwing to trigger gateway retry", service, StringComparison.Ordinal);
        Assert.Matches(
            @"if\s*\(\s*routedToSuspense\s*\)[\s\S]+throw;",
            service);
    }

    [Fact]
    public void StorefrontOrderProjection_MustRejectStaleEventsAndRehydrateMissingReadModel()
    {
        var consumer = File.ReadAllText(InfrastructurePath("Consumers", "OrderProjectionConsumer.cs"));
        var document = File.ReadAllText(InfrastructurePath("MongoDB", "Documents", "OrderDocument.cs"));
        var buildFreshEventFilterMethod = ExtractMethodBody(consumer, "BuildFreshEventFilter");

        Assert.Contains("LastEventOccurredAtUtc", document, StringComparison.Ordinal);
        Assert.Contains("BuildFreshEventFilter", consumer, StringComparison.Ordinal);
        Assert.NotNull(buildFreshEventFilterMethod);
        Assert.Contains("eventVersion <= 0", buildFreshEventFilterMethod, StringComparison.Ordinal);
        Assert.Contains("filter.Lt(x => x.LastEventVersion, eventVersion)", buildFreshEventFilterMethod, StringComparison.Ordinal);
        Assert.Contains("filter.Lt(x => x.LastEventOccurredAtUtc, sourceTimestamp)", buildFreshEventFilterMethod, StringComparison.Ordinal);
        Assert.Contains("TryRehydrateOrderProjectionAsync", consumer, StringComparison.Ordinal);
        Assert.Contains("_dbContext.Orders", consumer, StringComparison.Ordinal);
    }

    [Fact]
    public void AdminOrderCancellation_MustReleaseInventoryInsideCancellationTransaction()
    {
        var service = File.ReadAllText(ApiPath("Services", "Admin", "AdminOrderService.cs"));

        Assert.Contains("IProductVariantStockRepository", service, StringComparison.Ordinal);
        Assert.Contains("_stockRepository.ReleaseOrderReservationsAsync", service, StringComparison.Ordinal);
        Assert.Contains("RestoreInventory = false", service, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryReleaseSignaled", service, StringComparison.Ordinal);
        
        // PRODUCTION HARDENING: Verify we call ReleaseOrderReservationsAsync unconditionally
        // (not gated by a net-sum check that could hide positive reservations)
        Assert.Contains("RemainingReservedQuantity", service, StringComparison.Ordinal);
        
        // PRODUCTION HARDENING: Verify ReleaseOrderReservationsAsync is NOT guarded by a conditional
        // that checks RemainingReservedQuantity or GetOpenOrderReservationQuantityAsync.
        // The method should be called unconditionally, with internal filtering.
        // This regex looks for patterns like:
        //   if (quantity > 0) { ... ReleaseOrderReservationsAsync ... }
        //   if (quantity > 0) await ReleaseOrderReservationsAsync(...);
        //   if (RemainingReservedQuantity > 0) ReleaseOrderReservationsAsync(...);
        // Updated to catch both braced blocks and single-statement forms with optional await/async.
        var conditionalReleasePattern = @"if\s*\([^)]*(?:RemainingReservedQuantity|remainingReservedQuantity|GetOpenOrderReservationQuantityAsync|reservedQuantity)[^)]*[><=!][^)]*\)\s*(?:\{[^}]*ReleaseOrderReservationsAsync|(?:await\s+)?ReleaseOrderReservationsAsync)";
        Assert.DoesNotMatch(conditionalReleasePattern, service);
    }

    [Fact]
    public void AdminOrderCancellation_MustRejectRecognizedPaymentStatesBeforeMutatingOrderOrInventory()
    {
        var service = File.ReadAllText(ApiPath("Services", "Admin", "AdminOrderService.cs"));
        var contract = File.ReadAllText(ApiPath("Services", "Admin", "IAdminOrderService.cs"));
        var controller = File.ReadAllText(ApiPath("Controllers", "AdminOrdersController.cs"));
        var cancelMethod = ExtractMethodBody(service, "public async Task<AdminOrderCancellationResponse> CancelOrderAsync");

        Assert.NotNull(cancelMethod);
        Assert.Contains("AdminOrderPaymentRequiresRefundException", contract, StringComparison.Ordinal);
        Assert.Contains("ORDER_PAYMENT_REQUIRES_REFUND", contract, StringComparison.Ordinal);
        Assert.Contains("EnsureCancellationDoesNotBypassRecognizedPaymentAsync", service, StringComparison.Ordinal);
        Assert.Contains("PaymentStatusRecognizesMoney", service, StringComparison.Ordinal);
        Assert.Contains("PaymentIntentStatus.Paid", service, StringComparison.Ordinal);
        Assert.Contains("_context.Entry(order).ReloadAsync", service, StringComparison.Ordinal);
        Assert.Contains("ORDER_IMMUTABLE_AFTER_RELOAD", service, StringComparison.Ordinal);
        Assert.Contains("ALREADY_CANCELLED_AFTER_RELOAD", service, StringComparison.Ordinal);
        Assert.Contains("AdminOrderPaymentRequiresRefundException", controller, StringComparison.Ordinal);

        Assert.Matches(
            @"EnsureCancellationDoesNotBypassRecognizedPaymentAsync[\s\S]+if\s*\(order\.Status\s*==\s*OrderStatus\.Cancelled\)",
            cancelMethod!);
        Assert.Matches(
            @"BeginTransactionAsync[\s\S]+_context\.Entry\(order\)\.ReloadAsync[\s\S]+EnsureCancellationDoesNotBypassRecognizedPaymentAsync",
            cancelMethod!);

        var guardIndex = cancelMethod!.IndexOf("EnsureCancellationDoesNotBypassRecognizedPaymentAsync", StringComparison.Ordinal);
        var statusMutationIndex = cancelMethod!.IndexOf("order.Status = requestedStatus", StringComparison.Ordinal);
        var inventoryReleaseIndex = cancelMethod!.IndexOf("_stockRepository.ReleaseOrderReservationsAsync", StringComparison.Ordinal);
        var eventPublishIndex = cancelMethod!.IndexOf("new OrderCancelledEvent", StringComparison.Ordinal);

        Assert.True(guardIndex >= 0, "Cancellation payment guard was not found.");
        Assert.True(statusMutationIndex > guardIndex, "Order status is mutated before the recognized-payment guard.");
        Assert.True(inventoryReleaseIndex > guardIndex, "Inventory is released before the recognized-payment guard.");
        Assert.True(eventPublishIndex > guardIndex, "Cancellation event is published before the recognized-payment guard.");
    }

    [Fact]
    public void CommerceHubCartAndCheckout_MustUseCommerceHubCustomerIdentity_NotStorefrontOnlineCustomer()
    {
        var cartController = File.ReadAllText(ApiPath("Controllers", "CommerceHubCartController.cs"));
        var orderController = File.ReadAllText(ApiPath("Controllers", "CommerceHubOrdersController.cs"));
        var cartService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubCartService.cs"));
        var checkout = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubCheckoutService.cs"));
        var cartEntity = File.ReadAllText(CorePath("Entities", "CommerceHubCart.cs"));
        var marketplaceOrderConfig = File.ReadAllText(InfrastructurePath("Data", "Configurations", "CommerceHubMarketplaceOrderConfiguration.cs"));
        var dbContext = File.ReadAllText(InfrastructurePath("Data", "ApplicationDbContext.cs"));
        var migration = File.ReadAllText(InfrastructurePath("Migrations", "20260427190000_S44_CommerceHubCustomerCartIdentity.sql"));
        var closureMigration = File.ReadAllText(InfrastructurePath("Migrations", "20260430110000_S61_DataAuthorityAndEventScopeClosure.sql"));

        Assert.Contains("CustomerSecurityConstants.AuthenticationScheme", cartController, StringComparison.Ordinal);
        Assert.DoesNotContain("StorefrontJwt", cartController, StringComparison.Ordinal);
        Assert.Contains("CustomerSecurityConstants.AuthenticationScheme", orderController, StringComparison.Ordinal);
        Assert.DoesNotContain("StorefrontJwt", orderController, StringComparison.Ordinal);
        Assert.Contains("authenticatedCustomerId.Value == authority.CustomerId", orderController, StringComparison.Ordinal);
        Assert.DoesNotContain("document.OnlineCustomerId", orderController, StringComparison.Ordinal);
        Assert.Contains("CommerceHubCustomerId", cartEntity, StringComparison.Ordinal);
        Assert.Contains("CommerceHubCartTenantScopeId", cartService, StringComparison.Ordinal);
        // PRODUCTION HARDENING: OnlineCustomerId must be set to commerceHubCustomerId to avoid
        // unique index collision on (TenantId, OnlineCustomerId). Setting it to Guid.Empty
        // would cause multiple Commerce Hub customers to collide on the same tenant.
        Assert.Contains("OnlineCustomerId = commerceHubCustomerId", cartService, StringComparison.Ordinal);
        Assert.DoesNotContain("OnlineCustomerId = Guid.Empty", cartService, StringComparison.Ordinal);
        Assert.Contains("item.CommerceHubCustomerId == commerceHubCustomerId", checkout, StringComparison.Ordinal);
        Assert.Contains("_dbContext.CommerceHubCustomers", checkout, StringComparison.Ordinal);
        Assert.Contains("\"CommerceHubCustomerId\"", migration, StringComparison.Ordinal);
        Assert.Contains("Do not backfill from OnlineCustomerId", migration, StringComparison.Ordinal);
        Assert.Contains("CK_CommerceHubMarketplaceOrders_CustomerAuthorityBoundary", marketplaceOrderConfig, StringComparison.Ordinal);
        Assert.Contains("\\\"OnlineCustomerId\\\" IS NULL", marketplaceOrderConfig, StringComparison.Ordinal);
        Assert.Contains("CK_CommerceHubCarts_CustomerAuthorityBoundary", dbContext, StringComparison.Ordinal);
        Assert.Contains("\\\"OnlineCustomerId\\\" = \\\"CommerceHubCustomerId\\\"", dbContext, StringComparison.Ordinal);
        Assert.Contains("CK_CommerceHubMarketplaceOrders_CustomerAuthorityBoundary", closureMigration, StringComparison.Ordinal);
        Assert.Contains("CK_CommerceHubCarts_CustomerAuthorityBoundary", closureMigration, StringComparison.Ordinal);
        Assert.Contains("SET \"OnlineCustomerId\" = NULL", closureMigration, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubCookieAuthenticatedCustomerFlows_MustNotRequireRawAuthorizationHeaderPresence()
    {
        var browserAuthController = File.ReadAllText(ApiPath("Controllers", "CustomersAuthController.cs"));
        var httpClient = File.ReadAllText(CommerceHubPath("src", "api", "http-client.ts"));
        var cartController = File.ReadAllText(ApiPath("Controllers", "CommerceHubCartController.cs"));
        var orderController = File.ReadAllText(ApiPath("Controllers", "CommerceHubOrdersController.cs"));
        var authProgram = File.ReadAllText(ApiPath("Program.cs"));

        Assert.Contains("CreateBrowserAuthResponse", browserAuthController, StringComparison.Ordinal);
        Assert.Contains("AccessToken = string.Empty", browserAuthController, StringComparison.Ordinal);
        Assert.Contains("withCredentials: true", httpClient, StringComparison.Ordinal);
        Assert.Contains("context.Request.Cookies.TryGetValue(AuthCookieConfiguration.CommerceHubCustomerAccessTokenCookieName", authProgram, StringComparison.Ordinal);

        Assert.Contains("AuthenticateAsync(CustomerSecurityConstants.AuthenticationScheme)", cartController, StringComparison.Ordinal);
        Assert.DoesNotContain("Request.Headers.ContainsKey(\"Authorization\")", cartController, StringComparison.Ordinal);

        Assert.Contains("AuthenticateAsync(CustomerAuthenticationScheme)", orderController, StringComparison.Ordinal);
        Assert.DoesNotContain("Request.Headers.TryGetValue(\"Authorization\"", orderController, StringComparison.Ordinal);
        Assert.Contains("if (!authenticatedCustomerId.HasValue && !hasToken)", orderController, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubRecoveryAuthority_MustBeCleared_WhenCustomerAuthBoundaryChanges()
    {
        var authController = File.ReadAllText(ApiPath("Controllers", "CustomersAuthController.cs"));
        var authStore = File.ReadAllText(CommerceHubPath("src", "store", "customer-auth-store.ts"));
        var header = File.ReadAllText(CommerceHubPath("src", "components", "layout", "Header.tsx"));
        var recoveryCookie = File.ReadAllText(ApiPath("Security", "CommerceHubOrderAccessCookie.cs"));

        Assert.Contains("public static void ClearOrderAccessToken", recoveryCookie, StringComparison.Ordinal);
        Assert.Contains("ClearCommerceHubRecoveryCookie();", authController, StringComparison.Ordinal);

        // Extract the Register method body and verify the ordering within that method
        var registerMethod = ExtractMethodBody(authController, "Register");
        Assert.NotNull(registerMethod);
        AssertContainsInOrder(
            registerMethod,
            "ClearCommerceHubRecoveryCookie();",
            "return Ok(new { message = result.Message });");
        Assert.DoesNotContain("SetCustomerAccessCookie", registerMethod, StringComparison.Ordinal);

        // Extract the Login method body and verify the ordering within that method
        var loginMethod = ExtractMethodBody(authController, "Login");
        Assert.NotNull(loginMethod);
        AssertContainsInOrder(
            loginMethod,
            "SetCustomerAccessCookie(result.Response!);",
            "ClearCommerceHubRecoveryCookie();",
            "return Ok(CreateBrowserAuthResponse(result.Response!));");

        Assert.Contains("CommerceHubOrderAccessCookie.ClearOrderAccessToken", authController, StringComparison.Ordinal);
        Assert.Contains("clearActivePaymentRecoveryContext();", authStore, StringComparison.Ordinal);
        AssertContainsInOrder(
            authStore,
            "clearSession: () => {",
            "clearActivePaymentRecoveryContext();",
            "set(clearedSessionState);");
        Assert.Contains("set(clearedSessionState);", authStore, StringComparison.Ordinal);
        Assert.Contains("clearActivePaymentRecoveryContext();", header, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubCustomerJwt_MustEnforceDurableSessionCutoff()
    {
        var customerEntity = File.ReadAllText(CorePath("Entities", "CommerceHubCustomer.cs"));
        var authService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubCustomerAuthService.cs"));
        var revocationService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CustomerTokenRevocationService.cs"));
        var authController = File.ReadAllText(ApiPath("Controllers", "CustomersAuthController.cs"));
        var authProgram = File.ReadAllText(ApiPath("Program.cs"));
        var migration = File.ReadAllText(InfrastructurePath("Migrations", "20260511143000_S79_CommerceHubCustomerSessionCutoff.cs"));

        Assert.Contains("TokensValidAfterUtc", customerEntity, StringComparison.Ordinal);
        Assert.Contains("RevokeAllSessions(DateTime cutoffUtc)", customerEntity, StringComparison.Ordinal);
        Assert.Contains("CustomerSecurityConstants.SessionIssuedAtTicksClaim", authService, StringComparison.Ordinal);
        Assert.Contains("JwtRegisteredClaimNames.Iat", authService, StringComparison.Ordinal);
        Assert.Contains("EnsureTokenSessionStillValidAsync", revocationService, StringComparison.Ordinal);
        Assert.Contains("ResolveTokenIssuedAtUtc", revocationService, StringComparison.Ordinal);
        Assert.Contains("tokenIssuedAtUtc.Value <= authority.TokensValidAfterUtc.Value", revocationService, StringComparison.Ordinal);
        Assert.Contains("RevokeAllTokensAsync", revocationService, StringComparison.Ordinal);
        Assert.Contains("logout_blacklist_persistence_fallback", authController, StringComparison.Ordinal);
        Assert.Contains("[HttpPost(\"revoke-all-sessions\")]", authController, StringComparison.Ordinal);
        Assert.Contains("EnsureTokenSessionStillValidAsync", authProgram, StringComparison.Ordinal);
        Assert.Contains("CustomerTokenSessionRevokedException", authProgram, StringComparison.Ordinal);
        Assert.Contains("TokensValidAfterUtc", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void StorefrontCustomerJwt_MustUsePreciseIssuedAtCutoffValidation()
    {
        var authService = File.ReadAllText(ApiPath("Services", "Storefront", "StorefrontAuthService.cs"));
        var refreshTokenService = File.ReadAllText(ApiPath("Services", "Storefront", "StorefrontRefreshTokenService.cs"));
        var socialLoginService = File.ReadAllText(ApiPath("Services", "Storefront", "StorefrontSocialLoginService.cs"));
        var middleware = File.ReadAllText(ApiPath("Middleware", "StorefrontJwtBlacklistMiddleware.cs"));

        Assert.Contains("StorefrontSecurityConstants.SessionIssuedAtTicksClaim", authService, StringComparison.Ordinal);
        Assert.Contains("StorefrontSecurityConstants.SessionIssuedAtTicksClaim", refreshTokenService, StringComparison.Ordinal);
        Assert.Contains("StorefrontSecurityConstants.SessionIssuedAtTicksClaim", socialLoginService, StringComparison.Ordinal);
        Assert.Contains("ResolveTokenIssuedAtUtc", middleware, StringComparison.Ordinal);
        Assert.Contains("StorefrontSecurityConstants.SessionIssuedAtTicksClaim", middleware, StringComparison.Ordinal);
        Assert.Contains("tokenIssuedAt.Value <= customerIsActive.TokensValidAfterUtc.Value", middleware, StringComparison.Ordinal);
        Assert.Contains("JwtRegisteredClaimNames.Iat", middleware, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubCartBootstrap_MustScopePersistedStateToCustomerAuthority_AndWaitForAuthHydration()
    {
        var cartStore = File.ReadAllText(CommerceHubPath("src", "store", "cart-store.ts"));
        var cartSync = File.ReadAllText(CommerceHubPath("src", "cart", "useCommerceHubCartSync.tsx"));

        Assert.Contains("scopeKey: string;", cartStore, StringComparison.Ordinal);
        Assert.Contains("adoptGuestCartOnBootstrap: boolean;", cartStore, StringComparison.Ordinal);
        Assert.Contains("synchronizeAuthScope: (customerId: string | null) => void;", cartStore, StringComparison.Ordinal);
        Assert.Contains("const nextScopeKey = buildCartScopeKey(customerId);", cartStore, StringComparison.Ordinal);
        Assert.Contains("if (!currentScopeIsCustomer && nextScopeIsCustomer)", cartStore, StringComparison.Ordinal);
        Assert.Contains("adoptGuestCartOnBootstrap: hasLocalItems", cartStore, StringComparison.Ordinal);
        Assert.Contains("stores: {}", cartStore, StringComparison.Ordinal);
        Assert.Contains("promoCode: null", cartStore, StringComparison.Ordinal);
        AssertContainsInOrder(
            cartStore,
            "const shouldAdoptGuestCart = get().adoptGuestCartOnBootstrap;",
            "if (!shouldAdoptGuestCart)",
            "if (serverCart.isLocked)",
            "const mergedStores = mergeCartStores(localStores, serverStores);");

        Assert.Contains("const hasCustomerAuthHydrated = useCustomerAuthStore(selectCustomerAuthHydrated);", cartSync, StringComparison.Ordinal);
        Assert.Contains("const expectedScopeKey = useMemo(() => buildCartScopeKey(customer?.id ?? null), [customer?.id]);", cartSync, StringComparison.Ordinal);
        Assert.Contains("synchronizeAuthScope(customer?.id ?? null);", cartSync, StringComparison.Ordinal);
        Assert.Contains("if (!hasHydrated || !hasCustomerAuthHydrated || hasBootstrapped || isBootstrapping)", cartSync, StringComparison.Ordinal);
        Assert.Contains("if (scopeKey !== expectedScopeKey)", cartSync, StringComparison.Ordinal);
        Assert.Contains("if (!hasHydrated || !hasCustomerAuthHydrated || !hasBootstrapped || isBootstrapping || isSyncing)", cartSync, StringComparison.Ordinal);
    }

    [Fact]
    public void InventoryInbox_MustRejectSameEventIdWithDifferentPayload()
    {
        var source = File.ReadAllText(ApiPath("Services", "CommerceHub", "InventoryConsistencyService.cs"));

        Assert.Contains("EnsureInboxReplayMatchesOriginal", source, StringComparison.Ordinal);
        Assert.Contains("INVENTORY_EVENT_ID_PAYLOAD_CONFLICT", source, StringComparison.Ordinal);
        Assert.Contains("existing.PayloadHash", source, StringComparison.Ordinal);
        Assert.Contains("incomingPayloadHash", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AffiliateDailyEarningProjection_MustAtomicallyGuardDuplicateIncrement()
    {
        var source = File.ReadAllText(InfrastructurePath("Consumers", "Affiliate", "AffiliateAnalyticsDailyProjectionConsumer.cs"));
        var method = ExtractMethodBody(source, "Consume");
        Assert.NotNull(method);

        Assert.Contains("AffiliateAnalyticsDailyAtomicUpdate.ApplyOnceAsync", source, StringComparison.Ordinal);
        Assert.Contains("BuildNotProcessedFilter(docId, eventId)", source, StringComparison.Ordinal);
        Assert.Contains("!Builders<AffiliateAnalyticsDailyDocument>.Filter.AnyEq(d => d.ProcessedEventIds, eventId)", source, StringComparison.Ordinal);
        Assert.Contains(".Inc(d => d.Earnings, msg.TotalCommission)", source, StringComparison.Ordinal);
        Assert.Contains("AffiliateAnalyticsProjectionReceipt", source, StringComparison.Ordinal);
        Assert.Contains("AffiliateProjectionEventReceipts", source, StringComparison.Ordinal);
        Assert.Contains("TryClaimAsync", source, StringComparison.Ordinal);
        Assert.Contains("MarkProcessedAsync", source, StringComparison.Ordinal);
        Assert.Contains("MarkFailedAsync", source, StringComparison.Ordinal);
        Assert.Contains("catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)", source, StringComparison.Ordinal);
        Assert.Contains("UpdateProductEarningAsync", source, StringComparison.Ordinal);
        Assert.Contains(".Inc(\"productEarnings.$.earnings\", item.CommissionAmount)", source, StringComparison.Ordinal);
        Assert.Contains("!Builders<AffiliateAnalyticsDailyDocument>.Filter.ElemMatch", source, StringComparison.Ordinal);
        Assert.Contains("var pushResult = await _mongoDbContext.AffiliateAnalyticsDaily.UpdateOneAsync", source, StringComparison.Ordinal);
        Assert.Contains("if (pushResult.ModifiedCount > 0)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AffiliateMoneyProjections_MustNotEvictProcessedFinancialEventIds()
    {
        var source = File.ReadAllText(InfrastructurePath("Consumers", "Affiliate", "AffiliateProjectionConsumers.cs"));

        Assert.DoesNotContain("slice: -500", source, StringComparison.Ordinal);
        Assert.Contains(".AddToSet(d => d.ProcessedEventIds, eventId)", source, StringComparison.Ordinal);
        Assert.Contains("!Builders<AffiliateDashboardDocument>.Filter.AnyEq(d => d.ProcessedEventIds, eventId)", source, StringComparison.Ordinal);
        Assert.Contains("!Builders<AffiliateEarningsSummaryDocument>.Filter.AnyEq(d => d.ProcessedEventIds, eventId)", source, StringComparison.Ordinal);
        Assert.Contains("!Builders<AffiliateOrderDocument>.Filter.AnyEq(d => d.ProcessedEventIds, eventId)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AffiliateMoneyProjections_MustUseVersionFences_AndRecomputeCountersFromOrderState()
    {
        var source = File.ReadAllText(InfrastructurePath("Consumers", "Affiliate", "AffiliateProjectionConsumers.cs"));
        var affiliateDocument = File.ReadAllText(InfrastructurePath("MongoDB", "Documents", "Affiliate", "AffiliateDocuments.cs"));
        var orderDocument = File.ReadAllText(InfrastructurePath("MongoDB", "Documents", "OrderDocument.cs"));
        var orderProjection = File.ReadAllText(InfrastructurePath("Consumers", "OrderProjectionConsumer.cs"));

        foreach (var fieldName in new[]
        {
            "LastCommissionEarnedEventVersion",
            "LastCommissionReversedEventVersion",
            "LastCommissionApprovedEventVersion",
            "LastCommissionPaidEventVersion"
        })
        {
            Assert.Contains(fieldName, affiliateDocument, StringComparison.Ordinal);
            Assert.Contains(fieldName, source, StringComparison.Ordinal);
        }

        Assert.Contains("ValidateAuthorityMetadata", source, StringComparison.Ordinal);
        Assert.Contains("BuildEarnedFreshFilter", source, StringComparison.Ordinal);
        Assert.Contains("BuildReversalFreshFilter", source, StringComparison.Ordinal);
        Assert.Contains("BuildApprovalFreshFilter", source, StringComparison.Ordinal);
        Assert.Contains("BuildPaidFreshFilter", source, StringComparison.Ordinal);
        Assert.Contains("i.OrderItemId == orderItemId", source, StringComparison.Ordinal);
        Assert.Contains("i.ReversedAmount == expectedReversedAmount", source, StringComparison.Ordinal);
        Assert.Contains("items.$.reversedAmount", source, StringComparison.Ordinal);
        Assert.Contains("i => !i.IsReversed && i.SettlementStatus == \"Earned\"", source, StringComparison.Ordinal);
        Assert.Contains("i => i.SettlementStatus == \"Approved\"", source, StringComparison.Ordinal);
        Assert.Contains("RecomputeAffiliateMoneyAggregatesAsync", source, StringComparison.Ordinal);
        Assert.Contains(".Set(d => d.TotalEarned, totalEarned)", source, StringComparison.Ordinal);
        Assert.Contains(".Set(d => d.NetEarnings, totalEarned - totalReversed)", source, StringComparison.Ordinal);

        Assert.Contains("LastAffiliateCommissionEventVersion", orderDocument, StringComparison.Ordinal);
        Assert.Contains("LastAffiliateCommissionEventOccurredAtUtc", orderDocument, StringComparison.Ordinal);
        Assert.Contains("LastAffiliateCommissionEventId", orderDocument, StringComparison.Ordinal);
        Assert.Contains("LastAffiliateCommissionEventVersion", orderProjection, StringComparison.Ordinal);
        Assert.Contains("LastAffiliateCommissionEventOccurredAtUtc", orderProjection, StringComparison.Ordinal);
        Assert.Contains("LastAffiliateCommissionEventId", orderProjection, StringComparison.Ordinal);
        Assert.Contains("filter.Ne(x => x.LastAffiliateCommissionEventId, eventId)", orderProjection, StringComparison.Ordinal);
        Assert.Contains(".Set(x => x.LastAffiliateCommissionEventId, @event.EventId)", orderProjection, StringComparison.Ordinal);
        Assert.DoesNotContain("filter.Lte(x => x.LastAffiliateCommissionEventOccurredAtUtc", orderProjection, StringComparison.Ordinal);
    }

    [Fact]
    public void AffiliatePayoutExecution_MustRequireExternalEvidence_AndAvoidDuplicatePaidEvents()
    {
        var orchestrator = File.ReadAllText(InfrastructurePath("Services", "Affiliate", "AffiliateSettlementOrchestrator.cs"));
        var adminController = File.ReadAllText(ApiPath("Controllers", "AdminAffiliateController.cs"));
        var evidenceConfig = File.ReadAllText(InfrastructurePath("Data", "Configurations", "Affiliate", "AffiliatePayoutTransferEvidenceConfiguration.cs"));
        var allocationConfig = File.ReadAllText(InfrastructurePath("Data", "Configurations", "Affiliate", "AffiliatePayoutTransferAllocationConfiguration.cs"));
        var restoreMigration = File.ReadAllText(InfrastructurePath("Migrations", "20260510190000_RestoreGlobalAffiliatePayoutEvidenceAuthority.cs"));
        var markPaidMethod = ExtractMethodBody(adminController, "MarkPayoutPaid");

        Assert.Contains("EnsureExternalPaymentReference", orchestrator, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_PAYOUT_EXTERNAL_CONFIRMATION_REQUIRED", orchestrator, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_PAYOUT_EXTERNAL_EVIDENCE_ALREADY_USED", orchestrator, StringComparison.Ordinal);
        Assert.Contains("AllocateExternalTransferEvidenceAsync", orchestrator, StringComparison.Ordinal);
        Assert.Contains("AffiliatePayoutTransferEvidence", orchestrator, StringComparison.Ordinal);
        Assert.Contains("AffiliatePayoutTransferAllocation", orchestrator, StringComparison.Ordinal);
        Assert.Contains("AllocatedAmount", File.ReadAllText(CorePath("Entities", "Affiliate", "AffiliatePayoutTransferEvidence.cs")), StringComparison.Ordinal);
        Assert.Contains("UQ_AffiliatePayoutTransferEvidence_Rail_Reference", File.ReadAllText(InfrastructurePath("Migrations", "20260507224500_S74_AffiliatePayoutEvidenceAndOutboxArchive.sql")), StringComparison.Ordinal);
        Assert.Contains("new { e.RailNamespace, e.NormalizedPaymentReference }", evidenceConfig, StringComparison.Ordinal);
        Assert.DoesNotContain("new { e.StoreTenantId, e.RailNamespace, e.NormalizedPaymentReference }", evidenceConfig, StringComparison.Ordinal);
        Assert.Contains("HasDatabaseName(\"UQ_AffiliatePayoutTransferAllocation_Idempotency\")", allocationConfig, StringComparison.Ordinal);
        Assert.DoesNotContain("new { e.StoreTenantId, e.IdempotencyKey }", allocationConfig, StringComparison.Ordinal);
        Assert.DoesNotContain("item.StoreTenantId == storeTenantId", orchestrator, StringComparison.Ordinal);
        Assert.Contains("duplicate affiliate payout transfer evidence rail/reference groups", restoreMigration, StringComparison.Ordinal);
        Assert.Contains("duplicate affiliate payout allocation idempotency keys", restoreMigration, StringComparison.Ordinal);
        Assert.NotNull(markPaidMethod);
        Assert.DoesNotContain("new AffiliateCommissionPaidEvent", markPaidMethod!, StringComparison.Ordinal);
    }

    [Fact]
    public void AffiliatePaidAccounting_MustHaveSingleProductionWriter()
    {
        var program = File.ReadAllText(ApiPath("Program.cs"));
        var orchestrator = File.ReadAllText(InfrastructurePath("Services", "Affiliate", "AffiliateSettlementOrchestrator.cs"));
        var accountingConsumer = File.ReadAllText(InfrastructurePath("Consumers", "Affiliate", "AffiliateAccountingConsumer.cs"));

        Assert.Contains("AffiliateSettlementOrchestrator", program, StringComparison.Ordinal);
        Assert.DoesNotContain("AddConsumer<PointOfSale.Infrastructure.Consumers.Affiliate.AffiliateCommissionPaidAccountingConsumer>", program, StringComparison.Ordinal);
        Assert.Contains("AffiliateCommissionPaidEvent remains projection-only", program, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_COMM_PAID:{storeTenantId}:{paidOrder.Key}:{settlementReferenceId}", orchestrator, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_COMM_PAID:{tenantId}:{evt.OrderId}:{settlementReferenceId}", accountingConsumer, StringComparison.Ordinal);
    }

    [Fact]
    public void AffiliatePayoutExecution_MustNotReconstructHistoricalPaidLifecycleDuringLivePayout()
    {
        var orchestrator = File.ReadAllText(InfrastructurePath("Services", "Affiliate", "AffiliateSettlementOrchestrator.cs"));

        Assert.DoesNotContain("totalCumulativeWithdrawals", orchestrator, StringComparison.Ordinal);
        Assert.Contains("BuildExactPayoutPlan(", orchestrator, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_PAYOUT_AMOUNT_NOT_EXACTLY_PAYABLE", orchestrator, StringComparison.Ordinal);
        Assert.Contains("var remaining = requestedAmount;", orchestrator, StringComparison.Ordinal);
        Assert.Contains("Historical payout drift must be repaired by an explicit reconciliation job", orchestrator, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubCommissionAuthority_MustFailClosedWithoutSqlRuleFallback()
    {
        var commissionService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommissionService.cs"));

        Assert.Contains("COMMISSION_AUTHORITY_MISSING", commissionService, StringComparison.Ordinal);
        Assert.Contains("COMMISSION_AUTHORITY_AMBIGUOUS", commissionService, StringComparison.Ordinal);
        Assert.DoesNotContain("UsedLegacyFallback: true", commissionService, StringComparison.Ordinal);
        Assert.DoesNotContain("Source: CommissionCalculator.LegacySource", commissionService, StringComparison.Ordinal);
        Assert.DoesNotContain("Rate: NormalizeCommissionRate(_options.PlatformCommissionRate)", commissionService, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubVendorReceivable_MustUseDedicatedLedgerAccount()
    {
        var accounts = File.ReadAllText(CorePath("Interfaces", "Accounting", "IAccountResolver.cs"));
        var templates = File.ReadAllText(CorePath("Entities", "Accounting", "SystemAccountTemplate.cs"));
        var accounting = File.ReadAllText(InfrastructurePath("Accounting", "CommerceAccountingService.cs"));

        Assert.Contains("MarketplaceVendorReceivable = \"1420\"", accounts, StringComparison.Ordinal);
        Assert.Contains("Marketplace Vendor Receivable", templates, StringComparison.Ordinal);
        Assert.Contains("AccountCodes.MarketplaceVendorReceivable", accounting, StringComparison.Ordinal);
        Assert.DoesNotContain("CommerceHubFinancialBucket.Receivable => AccountCodes.AccountsReceivable", accounting, StringComparison.Ordinal);
    }

    [Fact]
    public void AffiliatePayoutReadinessGate_MustBlockOnAllAuthoritativeIntegrationEvents()
    {
        var orchestrator = File.ReadAllText(InfrastructurePath("Services", "Affiliate", "AffiliateSettlementOrchestrator.cs"));
        var readinessGate = ExtractMethodBody(orchestrator, "EnsureAffiliateOutwardMoneyMovementReadyAsync");
        var tenantScopeHelper = ExtractMethodBody(orchestrator, "TenantJsonScopeMayAffectStore");

        Assert.NotNull(readinessGate);
        Assert.NotNull(tenantScopeHelper);
        Assert.Contains("IntegrationEventAuthority.CriticalEventNameFragments", orchestrator, StringComparison.Ordinal);
        Assert.Contains("IntegrationEventAuthority.NonAuthoritativeEventTypeNames", orchestrator, StringComparison.Ordinal);
        Assert.Contains("BuildCriticalEventTypeFilter()", readinessGate!, StringComparison.Ordinal);
        Assert.Contains("EF.Functions.ILike(message.EventPayload", readinessGate!, StringComparison.Ordinal);
        Assert.Contains("AsAsyncEnumerable()", readinessGate!, StringComparison.Ordinal);
        Assert.Contains("LogLargeAffiliateReadinessInspection", orchestrator, StringComparison.Ordinal);
        Assert.DoesNotContain("Take(ReadinessInspectionLimit + 1)", orchestrator, StringComparison.Ordinal);
        Assert.DoesNotContain("message.EventType.Contains(\"Affiliate\")", readinessGate!, StringComparison.Ordinal);
        Assert.Contains("EnsureAffiliateUnscopedExternalMoneyReadyAsync(integrationStaleCutoffUtc, ct)", readinessGate!, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_PAYOUT_EVENT_BACKBONE_NOT_READY", readinessGate!, StringComparison.Ordinal);
        Assert.Contains("EnsureAffiliatePaymentCompensationReadyAsync", readinessGate!, StringComparison.Ordinal);
        Assert.Contains("EnsureAffiliateUnresolvedCapturedPaymentsReadyAsync", readinessGate!, StringComparison.Ordinal);
        Assert.Contains("EnsureAffiliateUnresolvedProviderRefundsReadyAsync", readinessGate!, StringComparison.Ordinal);
        Assert.Contains("EnsureAffiliateMassTransitBackboneReadyAsync", readinessGate!, StringComparison.Ordinal);
        Assert.Contains("EnsureAffiliateMarketplaceBackboneReadyAsync", readinessGate!, StringComparison.Ordinal);
        Assert.Contains("EnsureAffiliateStockBackboneReadyAsync", readinessGate!, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_PAYOUT_PAYMENT_COMPENSATION_NOT_READY", orchestrator, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_PAYOUT_UNRESOLVED_CAPTURED_PAYMENT_NOT_READY", orchestrator, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_PAYOUT_UNRESOLVED_PROVIDER_REFUND_NOT_READY", orchestrator, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_PAYOUT_PAYMENT_COMPENSATION_SCOPE_UNDETERMINED", orchestrator, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_PAYOUT_UNRESOLVED_CAPTURED_PAYMENT_SCOPE_UNDETERMINED", orchestrator, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_PAYOUT_UNRESOLVED_PROVIDER_REFUND_SCOPE_UNDETERMINED", orchestrator, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_PAYOUT_MASS_TRANSIT_BACKBONE_NOT_READY", orchestrator, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_PAYOUT_MARKETPLACE_EVENT_BACKBONE_NOT_READY", orchestrator, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_PAYOUT_STOCK_SYNC_BACKBONE_NOT_READY", orchestrator, StringComparison.Ordinal);
        Assert.Contains("HasExplicitTenantScope", orchestrator, StringComparison.Ordinal);
        Assert.Contains("AffectedTenantScopeResolver.RequiresGlobalFailClosedForAmbiguousMarketplaceAggregate", orchestrator, StringComparison.Ordinal);
        Assert.DoesNotMatch(
            @"if\s*\(baseTenantId\.HasValue\s*&&\s*baseTenantId\.Value\s*!=\s*Guid\.Empty\)\s*(\{\s*return false;\s*\}|return false;)",
            tenantScopeHelper!);
    }

    [Fact]
    public void AffiliatePayoutReadiness_MustRunInsideSerializablePayoutTransaction()
    {
        var orchestrator = File.ReadAllText(InfrastructurePath("Services", "Affiliate", "AffiliateSettlementOrchestrator.cs"));
        var publicExecution = ExtractMethodBody(orchestrator, "ExecuteAffiliatePayoutAsync");
        var coreExecution = ExtractMethodBody(orchestrator, "ExecutePayoutCoreAsync");

        Assert.NotNull(publicExecution);
        Assert.NotNull(coreExecution);

        Assert.DoesNotContain("EnsureAffiliateOutwardMoneyMovementReadyAsync(storeTenantId, ct)", publicExecution!, StringComparison.Ordinal);
        AssertContainsInOrder(
            coreExecution!,
            "BeginTransactionAsync(IsolationLevel.Serializable, ct)",
            "EnsureAffiliateOutwardMoneyMovementReadyAsync(storeTenantId, ct)",
            "_payoutService.GetAvailableBalanceAsync",
            "AllocateExternalTransferEvidenceAsync",
            "AffiliateBalanceLedger.CreateWithdrawal",
            "AffiliateCommissionLedger.CreatePaid");
    }

    [Fact]
    public void StorefrontCheckoutWrites_MustGateOnSqlStorefrontAvailability()
    {
        var checkout = File.ReadAllText(ApiPath("Services", "Checkout", "CheckoutService.cs"));
        var controller = File.ReadAllText(ApiPath("Controllers", "CheckoutController.cs"));
        var processCheckout = ExtractMethodBody(checkout, "ProcessCheckoutAsync");
        var createOrder = ExtractMethodBody(checkout, "CreateOrderAsync");
        var availabilityGuard = ExtractMethodBody(checkout, "EnsureStorefrontAcceptingOrdersAsync");
        var lockedStatusLoad = ExtractMethodBody(checkout, "LoadStorefrontStatusForUpdateAsync");

        Assert.NotNull(processCheckout);
        Assert.NotNull(createOrder);
        Assert.NotNull(availabilityGuard);
        Assert.NotNull(lockedStatusLoad);

        Assert.Contains("_dbContext.StoreConfigs", availabilityGuard!, StringComparison.Ordinal);
        Assert.Contains("StorefrontStatus", availabilityGuard!, StringComparison.Ordinal);
        Assert.Contains("STOREFRONT_NOT_ACCEPTING_ORDERS", File.ReadAllText(ApiPath("Services", "Checkout", "ICheckoutService.cs")), StringComparison.Ordinal);
        Assert.Contains("CheckoutStorefrontUnavailableException", controller, StringComparison.Ordinal);

        AssertContainsInOrder(
            processCheckout!,
            "DurableIdempotencyReplay",
            "EnsureStorefrontAcceptingOrdersAsync",
            "AcquireLock");

        AssertContainsInOrder(
            createOrder!,
            "BeginTransactionAsync",
            "EnsureStorefrontAcceptingOrdersAsync",
            "ValidateAndReserveInventoryAsync",
            "_dbContext.Orders.Add(order)");

        Assert.Contains("FOR UPDATE", lockedStatusLoad!, StringComparison.Ordinal);
        Assert.Contains("UPDLOCK, ROWLOCK, HOLDLOCK", lockedStatusLoad!, StringComparison.Ordinal);
    }

    [Fact]
    public void MarketplaceEventBackboneCleanup_MustArchiveUnresolvedFailedRowsWithoutDeletingLiveRecoveryState()
    {
        var cleanupWorker = File.ReadAllText(InfrastructurePath("Eventing", "MarketplaceEventBackboneCleanupWorker.cs"));
        var archiveEntity = File.ReadAllText(CorePath("Entities", "MarketplaceEventOutboxDeadLetterArchive.cs"));
        var migration = File.ReadAllText(InfrastructurePath("Migrations", "20260507224500_S74_AffiliatePayoutEvidenceAndOutboxArchive.sql"));

        Assert.Contains("message.PublishedAtUtc == null", cleanupWorker, StringComparison.Ordinal);
        Assert.Contains("ExecuteSqlRawAsync", cleanupWorker, StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT (\"\"OriginalOutboxMessageId\"\") DO NOTHING", cleanupWorker, StringComparison.Ordinal);
        Assert.Contains("ArchivedFailedOutbox", cleanupWorker, StringComparison.Ordinal);
        Assert.DoesNotContain("DeletedFailedOutbox", cleanupWorker, StringComparison.Ordinal);
        Assert.Contains("OriginalOutboxMessageId", archiveEntity, StringComparison.Ordinal);
        Assert.Contains("UQ_MarketplaceEventOutboxDeadLetterArchives_OriginalId", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void UnresolvedCapturedProviderCash_MustBeCriticalAccountingTruth()
    {
        var accountingProcessor = File.ReadAllText(InfrastructurePath("Accounting", "AccountingOutboxProcessor.cs"));

        Assert.Contains("\"UnresolvedOnlinePayment\"", accountingProcessor, StringComparison.Ordinal);
        Assert.Matches(
            @"_criticalFinancialTruthEventTypes[\s\S]+""UnresolvedOnlinePayment""[\s\S]+CriticalFinancialTruthEventTypes",
            accountingProcessor);
    }

    [Fact]
    public void AccountingOutboxCleanup_MustRetainEveryDispatchedFinancialTruthEvent()
    {
        var accountingProcessor = File.ReadAllText(InfrastructurePath("Accounting", "AccountingOutboxProcessor.cs"));
        var criticalSetMatch = Regex.Match(
            accountingProcessor,
            @"_criticalFinancialTruthEventTypes\s*=\s*new\([^)]*\)\s*\{(?<body>[\s\S]*?)\};");

        Assert.True(criticalSetMatch.Success, "Could not locate _criticalFinancialTruthEventTypes initializer.");
        var criticalSetBody = criticalSetMatch.Groups["body"].Value;

        foreach (var eventType in new[]
        {
            "PosSale",
            "SaleReversal",
            "PosPurchase",
            "PurchaseReversal",
            "SupplierPayment",
            "SupplierOpeningBalance",
            "PartyLedgerAdjustment",
            "PosRefund",
            "PurchaseReturn",
            "PosExpense",
            "ExpenseReversal",
            "OnlinePayment",
            "CodDelivered",
            "WalletApproved",
            "EcommerceRefund",
            "UnresolvedOnlinePayment",
            "ResolvedUnresolvedOnlinePayment",
            "GatewaySettlement",
            "CodSettlement",
            "CommerceHubLedgerPosting"
        })
        {
            Assert.Contains($"\"{eventType}\"", accountingProcessor, StringComparison.Ordinal);
            Assert.Contains($"\"{eventType}\"", criticalSetBody, StringComparison.Ordinal);
        }

        Assert.Contains("!CriticalFinancialTruthEventTypes.Contains(message.EventType)", accountingProcessor, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeOutboxConstraints_MustCoverEveryCriticalEventAuthorityFragment()
    {
        var eventAuthority = File.ReadAllText(CorePath("Events", "IntegrationEventAuthority.cs"));
        var config = File.ReadAllText(InfrastructurePath("Data", "Configurations", "OutboxMessageConfiguration.cs"));
        var migration = File.ReadAllText(InfrastructurePath("Migrations", "20260430120000_S62_AffiliateAuthorityAndEventOrderingClosure.sql"));

        Assert.Contains("IntegrationEventAuthority.CriticalEventNameFragments", config, StringComparison.Ordinal);
        Assert.Contains("IntegrationEventAuthority.NonAuthoritativeEventTypeNames", config, StringComparison.Ordinal);

        foreach (var fragment in new[]
        {
            "Order",
            "Payment",
            "Inventory",
            "Stock",
            "Product",
            "Variant",
            "CommerceHub",
            "Marketplace",
            "Affiliate",
            "Coupon",
            "Promotion",
            "Tenant",
            "StoreConfig",
            "ShippingMethod",
            "Shipment",
            "Customer",
            "Checkout",
            "Refund",
            "Payout",
            "Settlement",
            "Ledger",
            "Accounting",
            "Wallet",
            "Cod",
            "Currency",
            "ProviderTrust"
        })
        {
            Assert.Contains($"\"{fragment}\"", eventAuthority, StringComparison.Ordinal);
            Assert.Contains($"%{fragment.ToLowerInvariant()}%", migration, StringComparison.Ordinal);
        }

        Assert.Contains("NonAuthoritativeEventTypePredicate", config, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO \"__EFMigrationsHistory\"", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckoutConversion_MustStageDurableManualWalletProofSync()
    {
        var source = File.ReadAllText(InfrastructurePath("Services", "CheckoutSessionService.cs"));
        var method = ExtractMethodBody(source, "MarkAsConvertedAsync");

        Assert.NotNull(method);
        Assert.Matches(
            @"BuildSessionProofSyncRequestedEvent[\s\S]+session_proof_sync[\s\S]+SaveChangesAsync",
            method!);
    }

    [Fact]
    public void CheckoutReplayEnrichment_MustCreateMissingPaymentIntentForDurablePendingOrder()
    {
        var source = File.ReadAllText(ApiPath("Services", "Checkout", "CheckoutService.cs"));
        var method = ExtractMethodBody(source, "EnrichReplayResponseWithFreshTokensAsync");
        Assert.NotNull(method);

        Assert.Matches(
            @"paymentIntent\s*==\s*null\s*&&\s*order\.CheckoutState\s*==\s*CheckoutState\.PendingPaymentIntent[\s\S]+EnsureReplayPaymentIntentAsync",
            method!);
        Assert.Matches(
            @"EnsureReplayPaymentIntentAsync[\s\S]+replayResponse\.Status\s*=\s*CheckoutState\.PaymentPending\.ToString\(\)",
            method!);
    }

    [Fact]
    public void CheckoutPaymentIntentRecovery_MustRepairCommittedOrdersWithoutCustomerReplay()
    {
        var service = File.ReadAllText(ApiPath("Services", "Checkout", "CheckoutPaymentIntentRecoveryService.cs"));
        var program = File.ReadAllText(ApiPath("Program.cs"));

        Assert.Contains("CheckoutPaymentIntentRecoveryHostedService", service, StringComparison.Ordinal);
        Assert.Contains("RepairPendingPaymentIntentOrdersAsync", service, StringComparison.Ordinal);
        Assert.Contains("order.CheckoutState == CheckoutState.PendingPaymentIntent", service, StringComparison.Ordinal);
        Assert.Contains("_paymentService.CreatePaymentIntentAsync", service, StringComparison.Ordinal);
        Assert.Contains("TransitionCheckoutState(CheckoutState.PaymentPending)", service, StringComparison.Ordinal);
        Assert.Contains("BeginTransactionAsync", service, StringComparison.Ordinal);
        Assert.Contains("ReleaseOrderReservationsAsync", service, StringComparison.Ordinal);
        Assert.Contains("TransitionCheckoutState(CheckoutState.Expired)", service, StringComparison.Ordinal);
        Assert.Contains("OrderCancelledEvent", service, StringComparison.Ordinal);
        Assert.Contains("RestoreInventory = false", service, StringComparison.Ordinal);
        Assert.Contains("transaction.CommitAsync", service, StringComparison.Ordinal);
        Assert.Contains("AddHostedService<PointOfSale.API.Services.Checkout.CheckoutPaymentIntentRecoveryHostedService>", program, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubMarketplaceOrderCreatedEvent_MustCarryAllAffectedStoreTenants()
    {
        var eventSource = File.ReadAllText(CorePath("Events", "CommerceHubEvents.cs"));
        var orderEntity = File.ReadAllText(CorePath("Entities", "CommerceHubMarketplaceOrder.cs"));
        var checkout = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubCheckoutService.cs"));

        Assert.Contains("AffectedStoreIds", eventSource, StringComparison.Ordinal);
        Assert.Contains("IAffectedTenantScopedIntegrationEvent", eventSource, StringComparison.Ordinal);
        Assert.Contains("GetAffectedTenantIds() => AffectedStoreIds", eventSource, StringComparison.Ordinal);
        Assert.Contains("GetAffectedStoreIds", orderEntity, StringComparison.Ordinal);
        Assert.Contains(".Select(subOrder => subOrder.StoreId)", orderEntity, StringComparison.Ordinal);
        Assert.Contains(".Distinct()", orderEntity, StringComparison.Ordinal);
        Assert.Contains(".OrderBy(storeId => storeId)", orderEntity, StringComparison.Ordinal);
        Assert.Contains("var affectedStoreIds = marketplaceOrder.GetAffectedStoreIds();", checkout, StringComparison.Ordinal);
        Assert.Contains("TenantId = affectedStoreIds.First()", checkout, StringComparison.Ordinal);
        Assert.Contains("affectedStoreIds.Count == 0", checkout, StringComparison.Ordinal);
        Assert.Contains("AffectedStoreIds = affectedStoreIds", checkout, StringComparison.Ordinal);
    }

    [Fact]
    public void StorefrontCheckoutStockPolicy_MustUseSqlProductAuthority_NotMongoReadModel()
    {
        var source = File.ReadAllText(ApiPath("Services", "Checkout", "CheckoutService.cs"));
        var method = ExtractMethodBody(source, "ValidateAndReserveInventoryAsync");
        Assert.NotNull(method);

        // Assert against the extracted method body, not the whole file
        Assert.DoesNotContain("_productRead.GetByProductIdsAsync", method, StringComparison.Ordinal);
        Assert.Contains("LoadAuthoritativeCheckoutVariantLookupAsync", method, StringComparison.Ordinal);
        Assert.Contains("authoritativeVariant.ProductTrackStock", method, StringComparison.Ordinal);
        Assert.Contains("authoritativeVariant.ProductAllowBackorder", method, StringComparison.Ordinal);

        // Verify LoadAuthoritativeCheckoutVariantLookupAsync uses SQL Product authority
        var lookupMethod = ExtractMethodBody(source, "LoadAuthoritativeCheckoutVariantLookupAsync");
        Assert.NotNull(lookupMethod);
        Assert.Contains("variant.Product.TrackStock", lookupMethod, StringComparison.Ordinal);
        Assert.Contains("variant.Product.AllowBackorder", lookupMethod, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckoutInventoryReservationEvents_MustNotBeHiddenByRedisOrSwallowedPublishFailures()
    {
        var checkout = File.ReadAllText(ApiPath("Services", "Checkout", "CheckoutService.cs"));
        var checkoutEvents = File.ReadAllText(ApiPath("Events", "CheckoutEvents.cs"));
        var publisher = File.ReadAllText(InfrastructurePath("EventBus", "MassTransitEventPublisher.cs"));
        var validateMethod = ExtractMethodBody(checkout, "ValidateAndReserveInventoryAsync");
        var createOrderMethod = ExtractMethodBody(checkout, "CreateOrderAsync");
        var reserveEmitMethod = ExtractMethodBody(checkout, "EmitInventoryReservedEventAsync");
        var releaseEmitMethod = ExtractMethodBody(checkout, "EmitInventoryReleasedEventAsync");

        Assert.NotNull(validateMethod);
        Assert.NotNull(createOrderMethod);
        Assert.NotNull(reserveEmitMethod);
        Assert.NotNull(releaseEmitMethod);

        Assert.Contains("CheckoutEventBase : INonAuthoritativeIntegrationEvent", checkoutEvents, StringComparison.Ordinal);
        Assert.Contains("InventoryReservedEvent : IntegrationEvent, IAggregateScopedIntegrationEvent", checkoutEvents, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryReservedEvent : CheckoutEventBase", checkoutEvents, StringComparison.Ordinal);
        Assert.DoesNotContain("Redis", validateMethod!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("emitExternalReservationSideEffects", validateMethod!, StringComparison.Ordinal);
        Assert.Contains("await EmitInventoryReservedEventAsync", validateMethod!, StringComparison.Ordinal);
        AssertContainsInOrder(
            createOrderMethod!,
            "BeginTransactionAsync(",
            "ValidateAndReserveInventoryAsync(",
            "EmitOrderCreatedEventAsync(",
            "PersistInventoryReservedOutboxAsync(",
            "SaveChangesAsync(cancellationToken)",
            "CommitAsync(cancellationToken)");
        Assert.Contains("IsolationLevel.Serializable", createOrderMethod!, StringComparison.Ordinal);
        Assert.Contains("CheckoutConcurrencyConflictException", checkout, StringComparison.Ordinal);
        Assert.Contains("IsCheckoutTransactionSerializationFailure", checkout, StringComparison.Ordinal);
        Assert.DoesNotContain("Failed to emit InventoryReservedEvent", checkout, StringComparison.Ordinal);
        Assert.DoesNotContain("Failed to emit InventoryReservationReleasedEvent", checkout, StringComparison.Ordinal);
        
        // CRITICAL: Use Roslyn syntax tree analysis to detect try-catch blocks
        // Raw substring checks would incorrectly flag words like "retry", "country", "entry"
        AssertMethodHasNoTryCatchBlocks(checkout, "EmitInventoryReservedEventAsync");
        AssertMethodHasNoTryCatchBlocks(checkout, "EmitInventoryReleasedEventAsync");
        
        Assert.Contains("await _eventPublisher.PublishAsync(evt, cancellationToken)", reserveEmitMethod!, StringComparison.Ordinal);
        Assert.Contains("await _eventPublisher.PublishAsync(evt, cancellationToken)", releaseEmitMethod!, StringComparison.Ordinal);
        Assert.Contains("EnsureTransactionalPublishBoundary", publisher, StringComparison.Ordinal);
        Assert.Contains("_dbContext.Database.CurrentTransaction != null || HasPendingBusinessMutation()", publisher, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubDeliveryCommit_MustRejectNonReservedStock()
    {
        var source = File.ReadAllText(ApiPath("Services", "CommerceHub", "InventoryConsistencyService.cs"));
        var method = ExtractMethodBody(source, "CommitDeliveryAsync");
        Assert.NotNull(method);

        Assert.Contains("INVENTORY_DELIVERY_REQUIRES_ACTIVE_RESERVATION", method, StringComparison.Ordinal);
        Assert.DoesNotContain("committed non-reserved stock", method, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("syntheticReservation", method, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubInventoryStockSync_MustPublishSourceMutationEventVersion()
    {
        var source = File.ReadAllText(ApiPath("Services", "CommerceHub", "InventoryConsistencyService.cs"));

        Assert.Contains("using PointOfSale.Core.Helpers;", source, StringComparison.Ordinal);
        Assert.Contains("variant.ProjectionMutationVersion", source, StringComparison.Ordinal);
        Assert.Contains("EventVersion = ProductEventVersionHelper.ToProductEventVersion(variant.ProjectionMutationVersion)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EventVersion = 1", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CriticalOutboxMessages_MustRemainRetryableInsteadOfTerminalDeadLettering()
    {
        var accountingMessage = File.ReadAllText(CorePath("Entities", "Accounting", "AccountingOutboxMessage.cs"));
        var accountingProcessor = File.ReadAllText(InfrastructurePath("Accounting", "AccountingOutboxProcessor.cs"));
        var stockMessage = File.ReadAllText(CorePath("Entities", "StockSyncOutboxMessage.cs"));
        var stockProcessor = File.ReadAllText(InfrastructurePath("Eventing", "StockSyncOutboxProcessor.cs"));
        var marketplaceMessage = File.ReadAllText(CorePath("Entities", "MarketplaceEventOutboxMessage.cs"));
        var marketplaceProcessor = File.ReadAllText(InfrastructurePath("Eventing", "MarketplaceEventOutboxProcessor.cs"));

        Assert.Contains("RecordNonTerminalFinancialFailure", accountingMessage, StringComparison.Ordinal);
        Assert.Contains("IsCriticalFinancialTruthEvent", accountingProcessor, StringComparison.Ordinal);
        Assert.Contains("RecordNonTerminalFinancialFailure(ex.Message)", accountingProcessor, StringComparison.Ordinal);
        Assert.Contains("!candidate.IsFailed || CriticalFinancialTruthEventTypes.Contains(candidate.EventType)", accountingProcessor, StringComparison.Ordinal);

        Assert.Contains("RecordNonTerminalProjectionFailure", stockMessage, StringComparison.Ordinal);
        Assert.Contains("RecordNonTerminalProjectionFailure(ex.Message)", stockProcessor, StringComparison.Ordinal);
        Assert.Contains("RecordNonTerminalProjectionFailure", marketplaceMessage, StringComparison.Ordinal);
        Assert.Contains("RecordNonTerminalProjectionFailure(ex.Message)", marketplaceProcessor, StringComparison.Ordinal);
    }

    [Fact]
    public void AdminStockAdjustments_MustUseDurableDatabaseIdempotencyNotRedisAuthority()
    {
        var service = File.ReadAllText(ApiPath("Services", "Admin", "AdminInventoryService.cs"));
        var configuration = File.ReadAllText(InfrastructurePath("Data", "Configurations", "StockMovementConfiguration.cs"));
        var migration = File.ReadAllText(InfrastructurePath("Migrations", "20260504120000_S70_AdminStockAdjustmentDurableIdempotency.cs"));

        Assert.Contains("BuildStockAdjustmentIdempotencyKey", service, StringComparison.Ordinal);
        Assert.Contains("DUPLICATE_DURABLE_REQUEST", service, StringComparison.Ordinal);
        Assert.Contains("IdempotencyKey = idempotencyKey", service, StringComparison.Ordinal);
        Assert.Contains("Source = \"AdminInventoryAdjustment\"", service, StringComparison.Ordinal);
        Assert.Contains("IsStockAdjustmentIdempotencyUniqueViolation", service, StringComparison.Ordinal);
        Assert.Contains("Redis is cache-only", service, StringComparison.Ordinal);
        Assert.Matches(
            @"try\s*\{[\s\S]+StringSetAsync\(idempotencyKey[\s\S]+catch\s*\(Exception ex\)",
            service);

        Assert.Contains("UX_StockMovements_TenantId_IdempotencyKey", configuration, StringComparison.Ordinal);
        Assert.Contains("new { sm.TenantId, sm.IdempotencyKey }", configuration, StringComparison.Ordinal);
        Assert.Contains("\\\"IsDeleted\\\" = false AND \\\"IdempotencyKey\\\" IS NOT NULL", configuration, StringComparison.Ordinal);
        Assert.Contains("duplicate active stock movement idempotency keys exist", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void DirectOrderCreation_MustRejectCommerceSourcesBeforeIdempotencyClaim()
    {
        var controller = File.ReadAllText(ApiPath("Controllers", "OrdersController.cs"));
        var createMethod = ExtractMethodBody(controller, "public async Task<ActionResult<OrderDetailDto>> CreateOrder");

        Assert.NotNull(createMethod);
        Assert.Contains("dto.Source is OrderSource.Ecommerce or OrderSource.Marketplace", createMethod!, StringComparison.Ordinal);
        Assert.Contains("CommerceHub checkout pipeline", createMethod!, StringComparison.Ordinal);
        AssertContainsInOrder(
            createMethod!,
            "dto.Source is OrderSource.Ecommerce or OrderSource.Marketplace",
            "ResolveCreateOrderIdempotencyKey");
        Assert.Contains("public OrderSource Source { get; set; } = OrderSource.Manual;", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void StorefrontOrderHistory_MustNotFallbackToPosCustomerIdentity()
    {
        var repository = File.ReadAllText(InfrastructurePath("MongoDB", "Services", "OrderReadRepository.cs"));
        var projectionConsumer = File.ReadAllText(InfrastructurePath("Consumers", "OrderProjectionConsumer.cs"));

        Assert.Contains("filterBuilder.Eq(x => x.OnlineCustomerId, customerId)", repository, StringComparison.Ordinal);
        Assert.DoesNotContain("filterBuilder.Eq(x => x.CustomerId, customerId)", repository, StringComparison.Ordinal);
        Assert.DoesNotContain("order.OnlineCustomerId = customerId", repository, StringComparison.Ordinal);
        Assert.DoesNotContain("Legacy index for CustomerId fallback", projectionConsumer, StringComparison.Ordinal);
        Assert.DoesNotContain("IX_TenantId_CustomerId", projectionConsumer, StringComparison.Ordinal);
    }

    [Fact]
    public void CriticalOrderReads_MustPreferFreshestMongoProjection_WhenDuplicateDocumentsExist()
    {
        var storefrontRepository = File.ReadAllText(InfrastructurePath("MongoDB", "Services", "OrderReadRepository.cs"));
        var commerceHubRepository = File.ReadAllText(InfrastructurePath("MongoDB", "Services", "CommerceHubOrderReadRepository.cs"));
        var storefrontRepairService = File.ReadAllText(ApiPath("Services", "Storefront", "StorefrontOrderProjectionRepairService.cs"));
        var storefrontReconciliationService = File.ReadAllText(ApiPath("Services", "Storefront", "StorefrontOrderProjectionReconciliationService.cs"));
        var commerceHubRepairService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubOrderProjectionRepairService.cs"));
        var mongoIndexInitializer = File.ReadAllText(InfrastructurePath("MongoDB", "MongoDbIndexInitializer.cs"));
        var storefrontProjectionConsumer = File.ReadAllText(InfrastructurePath("Consumers", "OrderProjectionConsumer.cs"));
        var commerceHubProjectionConsumer = File.ReadAllText(InfrastructurePath("Consumers", "CommerceHubOrderProjectionConsumer.cs"));

        Assert.Contains("SelectFreshestProjection", storefrontRepository, StringComparison.Ordinal);
        Assert.Contains(".Find(filter).ToListAsync(cancellationToken)", storefrontRepository, StringComparison.Ordinal);
        Assert.Contains("OrderByDescending(document => document.LastEventVersion)", storefrontRepository, StringComparison.Ordinal);
        Assert.Contains("ThenByDescending(document => document.LastEventOccurredAtUtc", storefrontRepository, StringComparison.Ordinal);

        Assert.Contains(".Find(x => x.MarketplaceOrderId == marketplaceOrderId)", commerceHubRepository, StringComparison.Ordinal);
        Assert.Contains(".ToListAsync(cancellationToken)", commerceHubRepository, StringComparison.Ordinal);
        Assert.Contains("OrderByDescending(document => document.LastSourceMutationTicks)", commerceHubRepository, StringComparison.Ordinal);
        Assert.Contains("ThenByDescending(document => document.LastEventVersion)", commerceHubRepository, StringComparison.Ordinal);

        Assert.Contains(".SortByDescending(document => document.LastEventVersion)", storefrontRepairService, StringComparison.Ordinal);
        Assert.Contains(".ThenByDescending(document => document.LastEventOccurredAtUtc)", storefrontRepairService, StringComparison.Ordinal);
        Assert.Contains(".SortByDescending(doc => doc.LastEventVersion)", storefrontReconciliationService, StringComparison.Ordinal);
        Assert.Contains(".ThenByDescending(doc => doc.LastEventOccurredAtUtc)", storefrontReconciliationService, StringComparison.Ordinal);
        Assert.Contains(".SortByDescending(doc => doc.LastSourceMutationTicks)", commerceHubRepairService, StringComparison.Ordinal);
        Assert.Contains(".ThenByDescending(doc => doc.LastEventVersion)", commerceHubRepairService, StringComparison.Ordinal);

        Assert.Contains("Application will continue but queries may be slow.", mongoIndexInitializer, StringComparison.Ordinal);
        Assert.Contains("Failed to ensure MongoDB indexes for orders collection", storefrontProjectionConsumer, StringComparison.Ordinal);
        Assert.Contains("Failed to ensure MongoDB indexes for CommerceHub orders projection", commerceHubProjectionConsumer, StringComparison.Ordinal);
    }

    [Fact]
    public void StorefrontAffiliateCommissionSnapshots_MustBeMutuallyExclusiveAndCheckoutTimeOnly()
    {
        var checkout = File.ReadAllText(ApiPath("Services", "Checkout", "CheckoutService.cs"));
        var dbContext = File.ReadAllText(InfrastructurePath("Data", "ApplicationDbContext.cs"));
        var snapshotMigration = File.ReadAllText(InfrastructurePath("Migrations", "20260429163000_S58_OrderItemAffiliateSnapshotMutualExclusivity.sql"));

        Assert.Contains("shouldPersistAffiliateCommissionSnapshot = hasCommissionableAffiliatePolicy && hasPositiveAffiliateCommission", checkout, StringComparison.Ordinal);
        Assert.Contains("AffiliateCommissionEligible = shouldPersistAffiliateCommissionSnapshot", checkout, StringComparison.Ordinal);
        Assert.Contains("AffiliateCommissionPolicyIdSnapshot = shouldPersistAffiliateCommissionSnapshot ? affiliatePolicySnapshot!.PolicyId : null", checkout, StringComparison.Ordinal);
        Assert.Contains("AffiliateCommissionRateSnapshot = shouldPersistAffiliateCommissionSnapshot ? affiliatePolicySnapshot!.CommissionRate : null", checkout, StringComparison.Ordinal);
        Assert.Contains("AffiliateCommissionBaseAmountSnapshot = shouldPersistAffiliateCommissionSnapshot ? lineTotal : null", checkout, StringComparison.Ordinal);
        Assert.Contains("AffiliateCommissionAmountSnapshot = shouldPersistAffiliateCommissionSnapshot ? affiliateCommissionAmount : null", checkout, StringComparison.Ordinal);
        Assert.Contains("AffiliateCommissionSnapshotCapturedAtUtc = shouldPersistAffiliateCommissionSnapshot ? affiliateSnapshotCapturedAtUtc : null", checkout, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_POLICY_AUTHORITY_CONFLICT", checkout, StringComparison.Ordinal);
        Assert.Contains("checkout is blocked to prevent commission overpayment", checkout, StringComparison.Ordinal);
        Assert.DoesNotContain("selecting deterministic winner", checkout, StringComparison.Ordinal);
        Assert.Contains("UQ_AffiliateProductPolicies_Tenant_Product", dbContext, StringComparison.Ordinal);
        Assert.Contains("CK_AffiliateProductPolicies_Rate", dbContext, StringComparison.Ordinal);
        Assert.Contains("CK_AffiliateProductPolicies_EnabledRequiresCategory", dbContext, StringComparison.Ordinal);

        Assert.Contains("\"AffiliateCommissionEligible\" = true", snapshotMigration, StringComparison.Ordinal);
        Assert.Contains("\"AffiliateCommissionEligible\" = false", snapshotMigration, StringComparison.Ordinal);
        Assert.Contains("\"AffiliateCommissionSnapshotCapturedAtUtc\" IS NULL", snapshotMigration, StringComparison.Ordinal);
        Assert.Contains("\"AffiliateCommissionPolicyIdSnapshot\" IS NULL", snapshotMigration, StringComparison.Ordinal);
        Assert.Contains("\"AffiliateCommissionRateSnapshot\" IS NULL", snapshotMigration, StringComparison.Ordinal);
        Assert.Contains("\"AffiliateCommissionAmountSnapshot\" IS NULL", snapshotMigration, StringComparison.Ordinal);
    }

    [Fact]
    public void AffiliateCommissionRecording_MustNotRecalculateFromCurrentPolicy()
    {
        var source = File.ReadAllText(InfrastructurePath("Services", "Affiliate", "AffiliateService.cs"));
        var method = ExtractMethodBody(source, "RecordCommissionForOrderAsync");

        Assert.NotNull(method);
        Assert.Contains("CreateEarnedFromSnapshot", method!, StringComparison.Ordinal);
        Assert.Contains("No eligible immutable affiliate commission snapshot", method!, StringComparison.Ordinal);
        Assert.DoesNotContain("AffiliateProductPolicies", method!, StringComparison.Ordinal);
        Assert.DoesNotContain("AffiliateCommissionLedger.CreateEarned(", method!, StringComparison.Ordinal);
    }

    [Fact]
    public void TenantCurrencyChanges_MustBeCriticalVersionedOutboxEvents()
    {
        var currencyEvent = File.ReadAllText(CorePath("Events", "TenantCurrencyChangedEvent.cs"));
        var settings = File.ReadAllText(ApiPath("Controllers", "SettingsController.cs"));
        var outboxPublisher = File.ReadAllText(InfrastructurePath("Jobs", "OutboxPublisherJob.cs"));
        var eventAuthority = File.ReadAllText(CorePath("Events", "IntegrationEventAuthority.cs"));
        var tenantConstraint = File.ReadAllText(InfrastructurePath("Migrations", "20260429133000_S52_DataAuthorityBoundaryHardening.sql"));
        var aggregateConstraint = File.ReadAllText(InfrastructurePath("Migrations", "20260429143000_S54_EventOrderingHardening.sql"));
        var eventVersionConstraint = File.ReadAllText(InfrastructurePath("Migrations", "20260429153000_S56_DataAuthorityAndEventOrderingClosure.sql"));

        Assert.Contains("TenantCurrencyChangedEvent : IntegrationEvent, IAuthoritativeIntegrationEvent", currencyEvent, StringComparison.Ordinal);
        Assert.Contains("EventVersion = currencyChangedEvent.EventVersion", settings, StringComparison.Ordinal);
        
        // Verify centralized event classification authority is used
        Assert.Contains("IntegrationEventAuthority.IsCriticalEventTypeName(eventTypeName)", outboxPublisher, StringComparison.Ordinal);
        Assert.Contains("\"Currency\"", eventAuthority, StringComparison.Ordinal);
        Assert.Contains("CriticalEventNameFragments", eventAuthority, StringComparison.Ordinal);
        
        Assert.Contains("publishContext.MessageId = integrationEvent.EventId", outboxPublisher, StringComparison.Ordinal);
        Assert.Contains("publishContext.Headers.Set(\"idempotency-key\", integrationEvent.IdempotencyKey)", outboxPublisher, StringComparison.Ordinal);
        Assert.Contains("publishContext.Headers.Set(\"event-version\", integrationEvent.EventVersion)", outboxPublisher, StringComparison.Ordinal);
        Assert.Contains("lower(\"EventType\") LIKE '%currency%'", tenantConstraint, StringComparison.Ordinal);
        Assert.Contains("lower(\"EventType\") LIKE '%currency%'", aggregateConstraint, StringComparison.Ordinal);
        Assert.Contains("lower(\"EventType\") LIKE '%currency%'", eventVersionConstraint, StringComparison.Ordinal);
        Assert.Contains("VALIDATE CONSTRAINT \"CK_OutboxMessages_CriticalEvents_TenantId_Required\"", tenantConstraint, StringComparison.Ordinal);
        Assert.Contains("VALIDATE CONSTRAINT \"CK_OutboxMessages_CriticalEvents_AggregateId_Required\"", aggregateConstraint, StringComparison.Ordinal);
        Assert.Contains("VALIDATE CONSTRAINT \"CK_OutboxMessages_CriticalEvents_EventVersion_Required\"", eventVersionConstraint, StringComparison.Ordinal);
        Assert.Contains("VALIDATE CONSTRAINT \"CK_Orders_CustomerAuthorityBoundary\"", eventVersionConstraint, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubCheckoutIdempotency_MustBindKeyToServerRequestHash()
    {
        var entity = File.ReadAllText(CorePath("Entities", "CommerceHubCheckoutIdempotency.cs"));
        var dbContext = File.ReadAllText(InfrastructurePath("Data", "ApplicationDbContext.cs"));
        var service = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubCheckoutService.cs"));
        var migration = File.ReadAllText(InfrastructurePath("Migrations", "Sql", "20260424_CrossSystemCriticalRiskHardening.sql"));

        Assert.Contains("RequestHash", entity, StringComparison.Ordinal);
        Assert.Contains("record.RequestHash", dbContext, StringComparison.Ordinal);
        Assert.Contains("BuildCheckoutRequestHash", service, StringComparison.Ordinal);
        Assert.Contains("EnsureCheckoutIdempotencyRequestMatches", service, StringComparison.Ordinal);
        Assert.Contains("EnsureExistingCheckoutOrderReplayIsRequestHashBoundAsync", service, StringComparison.Ordinal);
        Assert.Contains("IDEMPOTENCY_PAYLOAD_CONFLICT", service, StringComparison.Ordinal);
        Assert.Contains("IDEMPOTENCY_REPLAY_UNBOUND", service, StringComparison.Ordinal);
        Assert.Contains("\"RequestHash\"", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectionOutboxHealth_MustBeReadinessGated()
    {
        var healthChecks = File.ReadAllText(InfrastructurePath("HealthChecks", "ProductionHealthChecks.cs"));
        var observability = File.ReadAllText(InfrastructurePath("Extensions", "ObservabilityServiceExtensions.cs"));

        Assert.Contains("ProjectionOutboxHealthCheck", healthChecks, StringComparison.Ordinal);
        Assert.Contains("MassTransitBusOutboxReadinessHealthCheck", healthChecks, StringComparison.Ordinal);
        Assert.Contains("BuildCriticalMassTransitOutboxMessageTypeFilter", healthChecks, StringComparison.Ordinal);
        Assert.Contains("StockSyncOutboxMessage", healthChecks, StringComparison.Ordinal);
        Assert.Contains("MarketplaceEventOutboxMessage", healthChecks, StringComparison.Ordinal);
        Assert.Contains("RepeatedFailureAttemptThreshold", healthChecks, StringComparison.Ordinal);
        Assert.Contains("HealthCheckResult.Unhealthy", healthChecks, StringComparison.Ordinal);
        Assert.Contains("AddCheck<ProjectionOutboxHealthCheck>(\"projection_outbox\", tags: new[] { \"ready\", \"projection\", \"critical\" })", observability, StringComparison.Ordinal);
        Assert.Contains("AddCheck<MassTransitBusOutboxReadinessHealthCheck>(\"mass_transit_bus_outbox_readiness\", tags: new[] { \"ready\", \"events\", \"critical\" })", observability, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubStatusEvents_MustBePublishedInsideTheSourceTransaction()
    {
        var orderManagement = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubOrderManagementService.cs"));
        var refundService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubRefundService.cs"));
        var refundExecution = File.ReadAllText(ApiPath("Services", "CommerceHub", "RefundExecutionService.cs"));

        // Extract PublishStatusEventsAsync method body from orderManagement
        var publishStatusEventsMethod = ExtractMethodBody(orderManagement, "PublishStatusEventsAsync");
        Assert.NotNull(publishStatusEventsMethod);

        Assert.Matches(
            @"PublishStatusEventsAsync[\s\S]+_dbContext\.SaveChangesAsync\(cancellationToken\)[\s\S]+transaction\.CommitAsync\(cancellationToken\)",
            orderManagement);
        Assert.DoesNotContain("CommerceHub status event publish failed", publishStatusEventsMethod, StringComparison.Ordinal);

        // Extract PublishRefundStatusEventsAsync method body from refundService
        var publishRefundStatusEventsMethodInService = ExtractMethodBody(refundService, "PublishRefundStatusEventsAsync");
        Assert.NotNull(publishRefundStatusEventsMethodInService);
        
        Assert.Matches(
            @"PublishRefundStatusEventsAsync[\s\S]+_dbContext\.SaveChangesAsync\(cancellationToken\)[\s\S]+transaction\.CommitAsync\(cancellationToken\)",
            refundService);
        Assert.DoesNotContain("CommerceHub refund status event publish failed", publishRefundStatusEventsMethodInService, StringComparison.Ordinal);

        // Extract PublishRefundStatusEventsAsync method body from refundExecution
        var publishRefundStatusEventsMethodInExecution = ExtractMethodBody(refundExecution, "PublishRefundStatusEventsAsync");
        Assert.NotNull(publishRefundStatusEventsMethodInExecution);
        
        Assert.Matches(
            @"PublishRefundStatusEventsAsync[\s\S]+_dbContext\.SaveChangesAsync\(cancellationToken\)[\s\S]+transaction\.CommitAsync\(cancellationToken\)",
            refundExecution);
        Assert.DoesNotContain("Refund-control status event publish failed", publishRefundStatusEventsMethodInExecution, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyOutboxPublisher_MustLockRowsAndKeepCriticalEventsRetryable()
    {
        var source = File.ReadAllText(InfrastructurePath("Jobs", "OutboxPublisherJob.cs"));
        var orderingMethod = ExtractMethodBody(source, "HasUnprocessedEarlierCriticalAggregateMessageAsync");

        Assert.NotNull(orderingMethod);

        Assert.Contains("LoadMessageForMutationAsync", source, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", source, StringComparison.Ordinal);
        Assert.Contains("UPDLOCK, ROWLOCK, HOLDLOCK", source, StringComparison.Ordinal);
        Assert.Contains("IsCriticalOutboxEvent", source, StringComparison.Ordinal);
        Assert.Contains("IntegrationEventAuthority.IsAuthoritativeEventType(eventType)", source, StringComparison.Ordinal);
        Assert.Contains("message.RetryCount >= OutboxMessage.MaxRetries && !isCriticalOutboxEvent", source, StringComparison.Ordinal);
        Assert.Contains("CriticalPoisonSlowReplayInterval", source, StringComparison.Ordinal);
        Assert.Contains("automatic slow replay remains active", source, StringComparison.Ordinal);
        Assert.Contains("m.LastErrorAtUtc <= criticalPoisonReplayCutoffUtc", source, StringComparison.Ordinal);
        Assert.Contains("message.RequiresOperatorReconciliation = false", source, StringComparison.Ordinal);
        Assert.Contains("message.EventVersion = @event.EventVersion", source, StringComparison.Ordinal);
        Assert.Contains("IsEarlierCriticalAggregateMessage", source, StringComparison.Ordinal);
        Assert.Contains("AffectedTenantScopeResolver.Resolve(message.TenantId, message.EventPayload)", orderingMethod!, StringComparison.Ordinal);
        Assert.Contains("AffectedTenantScopeResolver.Overlaps(messageTenantScope, other.TenantId, other.EventPayload)", orderingMethod!, StringComparison.Ordinal);
        Assert.DoesNotContain("other.TenantId == message.TenantId", orderingMethod!, StringComparison.Ordinal);
        Assert.DoesNotMatch(
            new Regex(
                @"Task\.WhenAll\s*\(|Task\.Run\s*\(|Parallel\.(ForEach|For)\b|WhenAll\s*\(.*Select\s*\(\s*ProcessMessageAsync",
                RegexOptions.Compiled | RegexOptions.Singleline),
            source);
    }

    [Fact]
    public void MultiTenantCriticalEvents_MustUseFullAffectedTenantScopeForOrderingAndReadiness()
    {
        var resolver = File.ReadAllText(CorePath("Events", "AffectedTenantScopeResolver.cs"));
        var publisher = File.ReadAllText(InfrastructurePath("Jobs", "OutboxPublisherJob.cs"));
        var gate = File.ReadAllText(ApiPath("Services", "CommerceHub", "Finance", "CommerceHubFinancialReadinessGate.cs"));
        var orderingMethod = ExtractMethodBody(publisher, "HasUnprocessedEarlierCriticalAggregateMessageAsync");

        Assert.NotNull(orderingMethod);
        Assert.Contains("\"AffectedStoreIds\"", resolver, StringComparison.Ordinal);
        Assert.Contains("\"AffectedTenantIds\"", resolver, StringComparison.Ordinal);
        Assert.Contains("public static bool Overlaps", resolver, StringComparison.Ordinal);
        Assert.Contains("AffectedTenantScopeResolver.Resolve(message.TenantId, message.EventPayload)", orderingMethod!, StringComparison.Ordinal);
        Assert.Contains("AffectedTenantScopeResolver.Overlaps(messageTenantScope, other.TenantId, other.EventPayload)", orderingMethod!, StringComparison.Ordinal);
        Assert.Contains("other.EventPayload", orderingMethod!, StringComparison.Ordinal);
        Assert.Contains("AddTenantScopeRecursive", resolver, StringComparison.Ordinal);
        Assert.Contains("RequiresGlobalFailClosedForAmbiguousMarketplaceAggregate", resolver, StringComparison.Ordinal);
        Assert.Contains("LooksLikeMarketplaceOrderAggregate", resolver, StringComparison.Ordinal);
        Assert.Contains("ContainsExplicitAffectedTenantArray", resolver, StringComparison.Ordinal);
        Assert.Contains("EnsureAmbiguousMarketplaceAggregateBackboneHealthyAsync", gate, StringComparison.Ordinal);
        Assert.Contains("AUTHORITATIVE_EVENT_BACKBONE_SCOPE_UNDETERMINED", gate, StringComparison.Ordinal);
        Assert.Contains("MASS_TRANSIT_BUS_OUTBOX_SCOPE_UNDETERMINED", gate, StringComparison.Ordinal);
        Assert.Contains("MARKETPLACE_EVENT_BACKBONE_SCOPE_UNDETERMINED", gate, StringComparison.Ordinal);
        Assert.Contains("Removed prefilter for \"MarketplaceOrderId\" to ensure malformed payloads", gate, StringComparison.Ordinal);
        Assert.Contains("IsAmbiguousCriticalMarketplaceAggregate(message.TenantId, message.EventPayload)", gate, StringComparison.Ordinal);
        Assert.Contains("IsAmbiguousCriticalMarketplaceAggregate(null, message.Body)", gate, StringComparison.Ordinal);
        Assert.Contains("IsAmbiguousCriticalMarketplaceAggregate(message.TenantId, message.PayloadJson)", gate, StringComparison.Ordinal);
        Assert.Contains("EventMayAffectTenant(message.TenantId, message.EventPayload, tenantId)", gate, StringComparison.Ordinal);
        Assert.Contains("EventMayAffectTenant(message.TenantId, message.PayloadJson, tenantId)", gate, StringComparison.Ordinal);
        Assert.Contains("EventMayAffectTenant(null, message.Body, tenantId)", gate, StringComparison.Ordinal);
        Assert.Contains("AffectedTenantScopeResolver.ContainsTenant(baseTenantId, eventPayload, tenantId)", gate, StringComparison.Ordinal);
        Assert.Contains("AffectedTenantScopeResolver.RequiresGlobalFailClosedForAmbiguousMarketplaceAggregate", gate, StringComparison.Ordinal);
        Assert.DoesNotContain("other.TenantId == message.TenantId", orderingMethod!, StringComparison.Ordinal);
        
        // SECURITY: Verify fail-closed branch for malformed event payloads in EventMayAffectTenant
        Assert.Contains("catch (InvalidEventPayloadException)", gate, StringComparison.Ordinal);
        Assert.Matches(@"catch\s*\(\s*InvalidEventPayloadException\s*\)\s*\{\s*return\s+true\s*;\s*\}", gate);
    }

    [Fact]
    public void AffiliatePayoutReadiness_MustGloballyBlockAmbiguousMarketplaceAggregateBacklog()
    {
        var orchestrator = File.ReadAllText(InfrastructurePath("Services", "Affiliate", "AffiliateSettlementOrchestrator.cs"));

        Assert.Contains("EnsureAffiliateAmbiguousMarketplaceAggregateBackboneReadyAsync", orchestrator, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_PAYOUT_EVENT_BACKBONE_SCOPE_UNDETERMINED", orchestrator, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_PAYOUT_MASS_TRANSIT_BACKBONE_SCOPE_UNDETERMINED", orchestrator, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_PAYOUT_MARKETPLACE_BACKBONE_SCOPE_UNDETERMINED", orchestrator, StringComparison.Ordinal);
        Assert.Contains("No literal payload prefilter", orchestrator, StringComparison.Ordinal);
        Assert.DoesNotContain("EF.Functions.ILike(message.EventPayload, \"%MarketplaceOrderId%\")", orchestrator, StringComparison.Ordinal);
        Assert.DoesNotContain("EF.Functions.ILike(message.Body, \"%MarketplaceOrderId%\")", orchestrator, StringComparison.Ordinal);
        Assert.DoesNotContain("EF.Functions.ILike(message.PayloadJson, \"%MarketplaceOrderId%\")", orchestrator, StringComparison.Ordinal);
        Assert.Contains("IsAmbiguousCriticalMarketplaceAggregate", orchestrator, StringComparison.Ordinal);
        Assert.Contains("AffectedTenantScopeResolver.RequiresGlobalFailClosedForAmbiguousMarketplaceAggregate", orchestrator, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubOutwardMoneyReadiness_MustGloballyBlockAmbiguousMarketplaceAggregateBacklog()
    {
        var gate = File.ReadAllText(ApiPath("Services", "CommerceHub", "Finance", "CommerceHubFinancialReadinessGate.cs"));
        var outwardMethod = ExtractMethodBody(gate, "public async Task EnsureOutwardMoneyMovementAllowedAsync");

        Assert.NotNull(outwardMethod);
        AssertContainsInOrder(
            outwardMethod!,
            "EnsureUnscopedUnresolvedExternalMoneyHealthyAsync",
            "EnsureAmbiguousMarketplaceAggregateBackboneHealthyAsync",
            "EnsureAccountingOutboxHealthyAsync");
        Assert.Contains("AUTHORITATIVE_EVENT_BACKBONE_SCOPE_UNDETERMINED", gate, StringComparison.Ordinal);
        Assert.Contains("MASS_TRANSIT_BUS_OUTBOX_SCOPE_UNDETERMINED", gate, StringComparison.Ordinal);
        Assert.Contains("MARKETPLACE_EVENT_BACKBONE_SCOPE_UNDETERMINED", gate, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubOutwardMoneyReadiness_MustRunInsideFinalSerializableAuthorityBoundary()
    {
        var payoutService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPayoutService.cs"));
        var refundService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubRefundService.cs"));

        var createPayout = ExtractMethodBody(payoutService, "public async Task<CommerceHubPayoutDto> CreatePayoutAsync");
        var markProcessing = ExtractMethodBody(payoutService, "public async Task<CommerceHubPayoutDto> MarkProcessingAsync");
        var executeRefund = ExtractMethodBody(refundService, "ExecutePersistedRefundAsync");

        Assert.NotNull(createPayout);
        Assert.NotNull(markProcessing);
        Assert.NotNull(executeRefund);

        AssertContainsInOrder(
            createPayout!,
            "BeginTransactionAsync(IsolationLevel.Serializable",
            "EnsureOutwardMoneyMovementAllowedAsync",
            "LockVendorEarningsForPayoutCreationAsync");
        AssertContainsInOrder(
            markProcessing!,
            "BeginTransactionAsync(IsolationLevel.Serializable",
            "EnsureOutwardMoneyMovementAllowedAsync",
            "CaptureAndValidatePayoutFinancialReadinessAsync");
        AssertContainsInOrder(
            executeRefund!,
            "BeginTransactionAsync(IsolationLevel.Serializable",
            "EnsureOutwardMoneyMovementAllowedAsync",
            "ClaimPendingRefundForGatewayExecutionAsync");
    }

    [Fact]
    public void AffectedTenantScopeResolver_MustDetectNestedAmbiguousMarketplaceAggregates()
    {
        var baseTenantId = Guid.NewGuid();
        var nestedTenantId = Guid.NewGuid();
        var payload = $$"""
        {
          "message": {
            "MarketplaceOrderId": "{{Guid.NewGuid()}}",
            "TenantId": "{{nestedTenantId}}",
            "Status": "Paid"
          }
        }
        """;

        Assert.True(
            PointOfSale.Core.Events.AffectedTenantScopeResolver
                .RequiresGlobalFailClosedForAmbiguousMarketplaceAggregate(baseTenantId, payload));
        Assert.True(
            PointOfSale.Core.Events.AffectedTenantScopeResolver
                .RequiresGlobalFailClosedForAmbiguousMarketplaceAggregate(null, payload));
    }

    [Fact]
    public void AffectedTenantScopeResolver_MustNotFailClosedForExplicitlyScopedAggregates()
    {
        // NEGATIVE CONTROL: Explicitly scoped aggregates (where nested TenantId matches baseTenantId)
        // should NOT trigger global fail-closed, only truly ambiguous nested tenant IDs should.
        var baseTenantId = Guid.NewGuid();

        // Case 1: Nested TenantId matches baseTenantId
        var payloadWithMatchingTenant = $$"""
        {
          "message": {
            "MarketplaceOrderId": "{{Guid.NewGuid()}}",
            "TenantId": "{{baseTenantId}}",
            "Status": "Paid"
          }
        }
        """;

        Assert.False(
            PointOfSale.Core.Events.AffectedTenantScopeResolver
                .RequiresGlobalFailClosedForAmbiguousMarketplaceAggregate(baseTenantId, payloadWithMatchingTenant),
            "Should not fail-closed when nested TenantId matches baseTenantId");

        // Case 2: Non-null scoped tenant equal to baseTenantId
        var payloadWithScopedTenant = $$"""
        {
          "message": {
            "MarketplaceOrderId": "{{Guid.NewGuid()}}",
            "TenantId": "{{baseTenantId}}",
            "ScopedTenantId": "{{baseTenantId}}",
            "Status": "Paid"
          }
        }
        """;

        Assert.False(
            PointOfSale.Core.Events.AffectedTenantScopeResolver
                .RequiresGlobalFailClosedForAmbiguousMarketplaceAggregate(baseTenantId, payloadWithScopedTenant),
            "Should not fail-closed when scoped tenant matches baseTenantId");
    }

    [Fact]
    public void InventoryReservationCleanup_MustReleaseOrphanCheckoutReservationsQuickly()
    {
        var job = File.ReadAllText(InfrastructurePath("Jobs", "InventoryReservationCleanupJob.cs"));
        var options = File.ReadAllText(InfrastructurePath("Observability", "InventoryReservationMetricsService.cs"));
        var hangfire = File.ReadAllText(ApiPath("Jobs", "HangfireInitializationHostedService.cs"));

        Assert.Contains("OrphanReservationGraceMinutes", options, StringComparison.Ordinal);
        Assert.Contains("orphanReservationCutoff", job, StringComparison.Ordinal);
        Assert.Contains("reservation.OrderItemId == Guid.Empty", job, StringComparison.Ordinal);
        Assert.Contains("Cron.Minutely", hangfire, StringComparison.Ordinal);
    }

    [Fact]
    public void CriticalAccountingOutboxLag_MustBeReadinessGated()
    {
        var healthChecks = File.ReadAllText(InfrastructurePath("HealthChecks", "ProductionHealthChecks.cs"));
        var observability = File.ReadAllText(InfrastructurePath("Extensions", "ObservabilityServiceExtensions.cs"));

        Assert.Contains("AccountingOutboxReadinessHealthCheck", healthChecks, StringComparison.Ordinal);
        Assert.Contains("AccountingOutboxProcessor.CriticalFinancialTruthEventTypes", healthChecks, StringComparison.Ordinal);
        Assert.Contains("AccountingOutboxMessage", healthChecks, StringComparison.Ordinal);
        Assert.Contains("RepeatedFailureAttemptThreshold", healthChecks, StringComparison.Ordinal);
        Assert.Contains("HealthCheckResult.Unhealthy", healthChecks, StringComparison.Ordinal);
        Assert.Contains("AddCheck<AccountingOutboxReadinessHealthCheck>(\"accounting_outbox_readiness\", tags: new[] { \"ready\", \"accounting\", \"critical\" })", observability, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubPayoutCompletion_MustLockExternalTransferEvidence()
    {
        var service = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPayoutService.cs"));
        var entity = File.ReadAllText(CorePath("Entities", "CommerceHubPayout.cs"));
        var dbContext = File.ReadAllText(InfrastructurePath("Data", "ApplicationDbContext.cs"));
        var migration = File.ReadAllText(InfrastructurePath("Migrations", "20260424173000_S33_PayoutCompletionEvidenceFingerprint.sql"));
        var financialMigration = File.ReadAllText(InfrastructurePath("Migrations", "20260424190000_S36_FinancialIntegrityBoundaryHardening.sql"));

        Assert.Contains("CompletionEvidenceFingerprint", entity, StringComparison.Ordinal);
        Assert.Contains("CompletedAtUtc", entity, StringComparison.Ordinal);
        Assert.Contains("CompletionEvidenceFingerprint", dbContext, StringComparison.Ordinal);
        Assert.Contains("EnsureRequestedPayoutEvidenceMatchesStored", service, StringComparison.Ordinal);
        Assert.Contains("EnsureExternalPayoutEvidenceIsUniqueAsync", service, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_MUST_BE_PROCESSING_BEFORE_COMPLETION", service, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_EXTERNAL_EVIDENCE_ALREADY_USED", service, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_EXTERNAL_CONFIRMATION_CONFLICT", service, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_COMPLETION_EVIDENCE_TAMPERED", service, StringComparison.Ordinal);
        Assert.Matches(
            @"payout\.CompletionEvidenceFingerprint\s*=\s*BuildCompletionEvidenceFingerprint\(payout\)",
            service);
        Assert.Contains("\"CompletionEvidenceFingerprint\"", migration, StringComparison.Ordinal);
        Assert.Contains("\"CompletedAtUtc\"", migration, StringComparison.Ordinal);
        Assert.Contains("UX_CommerceHubPayouts_Live_ExternalReferenceId", dbContext, StringComparison.Ordinal);
        Assert.Contains("UX_CommerceHubPayouts_Live_BankTransactionId", dbContext, StringComparison.Ordinal);
        Assert.Contains("\"UX_CommerceHubPayouts_Completed_ExternalReferenceId\"", financialMigration, StringComparison.Ordinal);
        Assert.Contains("\"UX_CommerceHubPayouts_Completed_BankTransactionId\"", financialMigration, StringComparison.Ordinal);
    }

    [Fact]
    public void AffiliateCommissionRecording_MustReconcilePriorRefundAndDeliveryState()
    {
        var service = File.ReadAllText(InfrastructurePath("Services", "Affiliate", "AffiliateService.cs"));
        var recordMethod = ExtractMethodBody(service, "RecordCommissionForOrderAsync");

        Assert.NotNull(recordMethod);
        Assert.Contains("LoadCurrentEarnedEntriesForOrderAsync", recordMethod!, StringComparison.Ordinal);
        Assert.Contains("ReconcileRefundedCommissionEntriesAsync", recordMethod!, StringComparison.Ordinal);
        Assert.Contains("ReconcileDeliveredCommissionApprovalsAsync", recordMethod!, StringComparison.Ordinal);
        AssertContainsInOrder(
            recordMethod!,
            "PublishAsync(new AffiliateCommissionEarnedEvent",
            "ReconcileRefundedCommissionEntriesAsync",
            "ReconcileDeliveredCommissionApprovalsAsync",
            "CommitAsync");

        Assert.Contains("item.RefundedAmount > 0m", service, StringComparison.Ordinal);
        Assert.Contains("order.IsPhysicallyDelivered", service, StringComparison.Ordinal);
        Assert.Contains("AffiliateCommissionReversedEvent", service, StringComparison.Ordinal);
        Assert.Contains("AffiliateCommissionApprovedEvent", service, StringComparison.Ordinal);
    }

    [Fact]
    public void AffiliateCommissionRecording_MustNotUseOrderSnapshotAsInlineAuthority()
    {
        var service = File.ReadAllText(InfrastructurePath("Services", "Affiliate", "AffiliateService.cs"));
        var worker = File.ReadAllText(InfrastructurePath("Services", "Affiliate", "AffiliateAttributionReconciliationHostedService.cs"));
        var program = File.ReadAllText(ApiPath("Program.cs"));
        var recordMethod = ExtractMethodBody(service, "RecordCommissionForOrderAsync");

        Assert.NotNull(recordMethod);
        Assert.DoesNotContain("AttributeOrderToAffiliateFromOrderSnapshotAsync", recordMethod!, StringComparison.Ordinal);
        Assert.Contains("Canonical affiliate attribution is missing", recordMethod!, StringComparison.Ordinal);
        Assert.Contains("ReconcileMissingAttributionsAsync", service, StringComparison.Ordinal);
        Assert.Contains("TryRepairMissingAttributionAsync", service, StringComparison.Ordinal);
        Assert.Contains("Commission issuance must never call this directly", service, StringComparison.Ordinal);
        Assert.Contains("AffiliateAttributionReconciliationHostedService", worker, StringComparison.Ordinal);
        Assert.Contains("AddHostedService<PointOfSale.Infrastructure.Services.Affiliate.AffiliateAttributionReconciliationHostedService>", program, StringComparison.Ordinal);
    }

    [Fact]
    public void AffiliateRefundReversal_MustRetryWhenRefundArrivesBeforeEarnedCommission()
    {
        var service = File.ReadAllText(InfrastructurePath("Services", "Affiliate", "AffiliateService.cs"));
        var reverseMethod = ExtractMethodBody(service, "ReverseCommissionForOrderItemAsync");

        Assert.NotNull(reverseMethod);
        Assert.Contains("ShouldRetryMissingEarnedCommissionForRefundAsync", reverseMethod!, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_COMMISSION_NOT_READY_FOR_REFUND_REVERSAL", reverseMethod!, StringComparison.Ordinal);
        Assert.Contains("throw new InvalidOperationException", reverseMethod!, StringComparison.Ordinal);
        Assert.Contains("HasAffiliateCommissionExpectation", service, StringComparison.Ordinal);
        Assert.Contains("IsAffiliateCommissionExpectedForItem", service, StringComparison.Ordinal);
    }

    [Fact]
    public void AffiliateDeliveryApproval_MustRetryWhenDeliveryArrivesBeforeEarnedCommission()
    {
        var service = File.ReadAllText(InfrastructurePath("Services", "Affiliate", "AffiliateService.cs"));
        var approveMethod = ExtractMethodBody(service, "ApproveCommissionForDeliveredOrderAsync");

        Assert.NotNull(approveMethod);
        Assert.Contains("EnsureCommissionReadyForDeliveredAffiliateOrderAsync", approveMethod!, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_COMMISSION_NOT_READY_FOR_DELIVERY_APPROVAL", service, StringComparison.Ordinal);
        Assert.Contains("order is not { IsPhysicallyDelivered: true }", service, StringComparison.Ordinal);
    }

    [Fact]
    public void AffiliatePartialOrderRefundConsumer_MustUseDurableItemRefundAllocations()
    {
        var consumer = File.ReadAllText(InfrastructurePath("Consumers", "Affiliate", "AffiliateRefundConsumers.cs"));

        Assert.DoesNotContain("partial_refund_requires_item_level_event", consumer, StringComparison.Ordinal);
        Assert.Contains("OrderRefunds", consumer, StringComparison.Ordinal);
        Assert.Contains(".Include(item => item.Lines)", consumer, StringComparison.Ordinal);
        Assert.Contains("refund_item_allocation_missing", consumer, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_REFUND_ITEM_ALLOCATION_REQUIRED", consumer, StringComparison.Ordinal);
        Assert.Contains("ReverseCommissionForOrderItemAsync", consumer, StringComparison.Ordinal);
    }

    [Fact]
    public void PaidStorefrontFulfillment_MustBlockUntilCollectionAccountingPosted()
    {
        var fulfillment = File.ReadAllText(InfrastructurePath("Fulfillment", "OrderFulfillmentService.cs"));
        var authority = File.ReadAllText(CorePath("Payments", "OrderOperationalFinancialAuthority.cs"));

        Assert.Contains("RequiresPostedCollectionAccountingBeforeFulfillment", fulfillment, StringComparison.Ordinal);
        Assert.Contains("IsOutboundFulfillmentProgression", fulfillment, StringComparison.Ordinal);
        Assert.Contains("RequiresPostedCollectionAccountingBeforeOutboundProgress", authority, StringComparison.Ordinal);
        Assert.Contains("FinancialPostingStatus.Posted or FinancialPostingStatus.NotRequired", authority, StringComparison.Ordinal);
        Assert.Contains("Cannot advance fulfillment until the payment collection accounting journal is posted", fulfillment, StringComparison.Ordinal);
    }

    [Fact]
    public void FulfillmentDeliveryEvent_MustBePublishedBeforeFulfillmentStateCommit()
    {
        var fulfillment = File.ReadAllText(InfrastructurePath("Fulfillment", "OrderFulfillmentService.cs"));

        AssertContainsInOrder(
            fulfillment,
            "private async Task<OrderFulfillmentResult> TransitionAsync<TEvent>",
            "order.FulfillmentStatus = targetStatus",
            "order.AcceptFulfillmentStatusChange()",
            "var domainEvent = eventFactory(order, oldStatus, targetStatus)",
            "await _eventPublisher.PublishAsync(domainEvent, ct)",
            "await _db.SaveChangesAsync(ct)");
    }

    [Fact]
    public void AffiliateDeliveryApproval_MustConsumeCanonicalFulfillmentDeliveryEvent()
    {
        var consumer = File.ReadAllText(InfrastructurePath("Consumers", "Affiliate", "AffiliateSettlementConsumers.cs"));
        var program = File.ReadAllText(ApiPath("Program.cs"));

        Assert.Contains("IConsumer<OrderDeliveredEvent>", consumer, StringComparison.Ordinal);
        Assert.Contains("IConsumer<FulfillmentDeliveredEvent>", consumer, StringComparison.Ordinal);
        Assert.Contains("nameof(FulfillmentDeliveredEvent)", consumer, StringComparison.Ordinal);
        Assert.Contains("ApproveCommissionForDeliveredOrderAsync", consumer, StringComparison.Ordinal);
        Assert.Contains("evt.EventVersion", consumer, StringComparison.Ordinal);
        Assert.Contains("AddConsumer<PointOfSale.Infrastructure.Consumers.Affiliate.AffiliateOrderDeliveredConsumer>", program, StringComparison.Ordinal);
    }

    [Fact]
    public void AffiliateDeliveryApproval_MustValidateCurrentOrderAuthorityBeforeApprovingMoney()
    {
        var service = File.ReadAllText(InfrastructurePath("Services", "Affiliate", "AffiliateService.cs"));
        var serviceInterface = File.ReadAllText(CorePath("Interfaces", "Affiliate", "IAffiliateService.cs"));
        var approvalMethod = ExtractMethodBody(service, "public async Task ApproveCommissionForDeliveredOrderAsync");

        Assert.Contains("ApproveCommissionForDeliveredOrderAsync", serviceInterface, StringComparison.Ordinal);
        Assert.NotNull(approvalMethod);
        Assert.Contains("LoadAffiliateApprovalOrderAuthorityAsync", approvalMethod!, StringComparison.Ordinal);
        Assert.Contains("EnsureAffiliateDeliveryApprovalAuthority", approvalMethod!, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_DELIVERY_EVENT_TENANT_MISMATCH", service, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_DELIVERY_AUTHORITY_NOT_CONFIRMED", service, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_DELIVERY_PAYMENT_AUTHORITY_NOT_CONFIRMED", service, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_DELIVERY_ACCOUNTING_AUTHORITY_NOT_CONFIRMED", service, StringComparison.Ordinal);
        Assert.Contains("order.IsPhysicallyDelivered", service, StringComparison.Ordinal);
        Assert.Contains("eventVersion <= 0", approvalMethod!, StringComparison.Ordinal);
    }

    [Fact]
    public void WalletDeliveryConsumer_MustFailClosedWhenDeliveryArrivesBeforePaymentRecognition()
    {
        var consumer = File.ReadAllText(InfrastructurePath("Consumers", "OrderDeliveredEventConsumer.cs"));

        Assert.Contains("Wallet delivery arrived before payment authority was recognized", consumer, StringComparison.Ordinal);
        Assert.Contains("Manual payment approval must complete first", consumer, StringComparison.Ordinal);
        Assert.DoesNotContain("Wallet order requires manual approval before financial recognition", consumer, StringComparison.Ordinal);
    }

    [Fact]
    public void ExternalRefundSplitBrain_MustGateCommerceHubMoneyMovementUntilReconciled()
    {
        var refundStatus = File.ReadAllText(CorePath("Enums", "CommerceHubRefundStatus.cs"));
        var refundService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubRefundService.cs"));
        var refundExecution = File.ReadAllText(ApiPath("Services", "CommerceHub", "RefundExecutionService.cs"));
        var gate = File.ReadAllText(ApiPath("Services", "CommerceHub", "Finance", "CommerceHubFinancialReadinessGate.cs"));
        var dbContext = File.ReadAllText(InfrastructurePath("Data", "ApplicationDbContext.cs"));
        var migration = File.ReadAllText(InfrastructurePath("Migrations", "20260503160000_S69_CrossSystemCriticalRiskProductionFixes.sql"));
        var reconciliationMigration = File.ReadAllText(InfrastructurePath("Migrations", "20260507231500_S75_CommerceHubRefundReconciliationRequired.sql"));
        var outwardMethod = ExtractMethodBody(gate, "public async Task EnsureOutwardMoneyMovementAllowedAsync");
        var intakeMethod = ExtractMethodBody(gate, "public async Task EnsureCommerceIntakeAllowedAsync");
        var refundGateMethod = ExtractMethodBody(gate, "EnsureExternalRefundsReconciledAsync");

        Assert.NotNull(outwardMethod);
        Assert.NotNull(intakeMethod);
        Assert.NotNull(refundGateMethod);
        Assert.Contains("EnsureExternalRefundsReconciledAsync", outwardMethod!, StringComparison.Ordinal);
        Assert.Contains("EnsureExternalRefundsReconciledAsync", intakeMethod!, StringComparison.Ordinal);
        Assert.Contains("ExternalRefundProcessingMaxLag", gate, StringComparison.Ordinal);
        Assert.Contains("GatewayStatus == \"execution_unknown\"", refundGateMethod!, StringComparison.Ordinal);
        Assert.Contains("ReconciliationRequired = 4", refundStatus, StringComparison.Ordinal);
        Assert.Contains("CommerceHubRefundStatus.ReconciliationRequired", refundGateMethod!, StringComparison.Ordinal);
        Assert.Contains("CommerceHubRefundStatus.ReconciliationRequired", refundService, StringComparison.Ordinal);
        Assert.Contains("CommerceHubRefundStatus.ReconciliationRequired", refundExecution, StringComparison.Ordinal);
        Assert.Contains("manual_transfer_reconciliation_required", refundExecution, StringComparison.Ordinal);
        Assert.DoesNotContain("manual-failed", refundExecution, StringComparison.Ordinal);
        Assert.Contains("EXTERNAL_REFUND_RECONCILIATION_REQUIRED", refundGateMethod!, StringComparison.Ordinal);
        Assert.Contains("IX_CommerceHubRefunds_ExternalReconciliation", migration, StringComparison.Ordinal);
        Assert.Contains("\"Status\" = 4", reconciliationMigration, StringComparison.Ordinal);
        Assert.Contains(".HasFilter(\"(\\\"Status\\\" = 1 OR \\\"Status\\\" = 4) AND \\\"IsDeleted\\\" = false\")", dbContext, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthoritativeInventoryReservationEvent_MustNotUseNonAuthoritativeCheckoutBase()
    {
        var checkoutEvents = File.ReadAllText(ApiPath("Events", "CheckoutEvents.cs"));
        var authority = File.ReadAllText(CorePath("Events", "IntegrationEventAuthority.cs"));
        var outboxPublisher = File.ReadAllText(InfrastructurePath("Jobs", "OutboxPublisherJob.cs"));

        Assert.Contains("InventoryReservedEvent : IntegrationEvent, IAggregateScopedIntegrationEvent", checkoutEvents, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryReservedEvent : CheckoutEventBase", checkoutEvents, StringComparison.Ordinal);
        Assert.Contains("cannot be both authoritative and non-authoritative", authority, StringComparison.Ordinal);
        Assert.Contains("EnsureOutboxAuthorityScope(message, eventType, integrationEvent, isCriticalOutboxEvent)", outboxPublisher, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubPayoutExecution_MustRequireCommittedHandoffTokenForReconciliation()
    {
        var entity = File.ReadAllText(CorePath("Entities", "CommerceHubPayout.cs"));
        var dto = File.ReadAllText(ApiPath("DTOs", "CommerceHub", "CommerceHubFinanceDtos.cs"));
        var service = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPayoutService.cs"));
        var dbContext = File.ReadAllText(InfrastructurePath("Data", "ApplicationDbContext.cs"));
        var migration = File.ReadAllText(InfrastructurePath("Migrations", "20260504120000_S70_CrossSystemCriticalRiskFinalClosure.sql"));

        var markProcessing = ExtractMethodBody(service, "public async Task<CommerceHubPayoutDto> MarkProcessingAsync");
        var markCompleted = ExtractMethodBody(service, "public async Task<CommerceHubPayoutDto> MarkCompletedAsync");
        var markFailed = ExtractMethodBody(service, "public async Task<CommerceHubPayoutDto> MarkFailedAsync");

        Assert.Contains("ExternalTransferHandoffToken", entity, StringComparison.Ordinal);
        Assert.Contains("ExternalTransferHandoffCommittedAtUtc", entity, StringComparison.Ordinal);
        Assert.Contains("ExternalTransferHandoffToken", dto, StringComparison.Ordinal);
        Assert.Contains("EnsureProcessingHandoffCommitted(payout)", markProcessing!, StringComparison.Ordinal);
        Assert.Contains("EnsureExternalTransferHandoffMatchesPayout(payout, request)", markCompleted!, StringComparison.Ordinal);
        Assert.Contains("EnsureExternalTransferHandoffMatchesPayout(payout, request)", markFailed!, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_EXTERNAL_HANDOFF_TOKEN_REQUIRED", service, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_EXTERNAL_HANDOFF_TOKEN_MISMATCH", service, StringComparison.Ordinal);
        Assert.Contains("CK_CommerceHubPayouts_Processing_HandoffCommitted", dbContext, StringComparison.Ordinal);
        Assert.Contains("UX_CommerceHubPayouts_ExternalTransferHandoffToken", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubStaleProcessingPayouts_MustQuarantineIntoReconciliationRequiredState()
    {
        var status = File.ReadAllText(CorePath("Enums", "CommerceHubPayoutStatus.cs"));
        var worker = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPayoutReconciliationHostedService.cs"));
        var service = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPayoutService.cs"));
        var gate = File.ReadAllText(ApiPath("Services", "CommerceHub", "Finance", "CommerceHubFinancialReadinessGate.cs"));
        var dbContext = File.ReadAllText(InfrastructurePath("Data", "ApplicationDbContext.cs"));
        var program = File.ReadAllText(ApiPath("Program.cs"));
        var migration = File.ReadAllText(InfrastructurePath("Migrations", "20260505130000_S72_CommerceHubPayoutReconciliationRequired.sql"));

        Assert.Contains("ReconciliationRequired = 4", status, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPayoutStatus.ReconciliationRequired", worker, StringComparison.Ordinal);
        Assert.Contains("PayoutReconciliationRequired", worker, StringComparison.Ordinal);
        Assert.Contains("AddHostedService<PointOfSale.API.Services.CommerceHub.CommerceHubPayoutReconciliationHostedService>", program, StringComparison.Ordinal);
        Assert.Contains("IsExternallyLockedPayoutStatus", service, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPayoutStatus.ReconciliationRequired", gate, StringComparison.Ordinal);
        Assert.Contains("\"Status\" = 4", migration, StringComparison.Ordinal);
        Assert.Contains(".HasFilter(\"(\\\"Status\\\" = 1 OR \\\"Status\\\" = 4) AND \\\"IsDeleted\\\" = false\")", dbContext, StringComparison.Ordinal);
        Assert.Contains("CK_CommerceHubPayouts_Processing_HandoffCommitted", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void MarketplaceOutboxTerminalFailure_MustRefreshAndRetryAuthoritativeBackboneEvent()
    {
        var compensation = File.ReadAllText(InfrastructurePath("Eventing", "ProjectionFailureCompensation.cs"));
        var message = File.ReadAllText(CorePath("Entities", "MarketplaceEventOutboxMessage.cs"));

        Assert.Contains("RefreshForAuthoritativeRetry", compensation, StringComparison.Ordinal);
        Assert.Contains("BuildMarketplaceReplayAsync", compensation, StringComparison.Ordinal);
        Assert.Contains("BuildOrderCreatedReplayAsync", compensation, StringComparison.Ordinal);
        Assert.Contains("BuildPaymentCompletedReplayAsync", compensation, StringComparison.Ordinal);
        Assert.Contains("BuildRefundProcessedReplayAsync", compensation, StringComparison.Ordinal);
        Assert.Contains("BuildProductUpdatedReplayAsync", compensation, StringComparison.Ordinal);
        Assert.Contains("RefreshForAuthoritativeRetry", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Snapshot reconciliation will rebuild from SQL", compensation, StringComparison.Ordinal);
    }

    [Fact]
    public void MarketplaceOutboxBatching_MustSelectOnlyHeadOfLineMessagePerAggregate()
    {
        var processor = File.ReadAllText(InfrastructurePath("Eventing", "MarketplaceEventOutboxProcessor.cs"));

        Assert.Contains("!batchDbContext.Set<MarketplaceEventOutboxMessage>()", processor, StringComparison.Ordinal);
        Assert.Contains("Only queue the head-of-line message for each aggregate stream. Global version ordering", processor, StringComparison.Ordinal);
        Assert.Contains("across unrelated aggregates causes backlog amplification without improving correctness.", processor, StringComparison.Ordinal);
        Assert.Contains("other.Version < message.Version", processor, StringComparison.Ordinal);
        Assert.DoesNotContain(".OrderBy(message => message.Version)", processor, StringComparison.Ordinal);
    }

    [Fact]
    public void StorefrontCheckoutStockReservation_MustUseDeterministicVariantLockOrder()
    {
        var checkout = File.ReadAllText(ApiPath("Services", "Checkout", "CheckoutService.cs"));
        var reserveMethod = ExtractMethodBody(checkout, "ValidateAndReserveInventoryAsync");

        Assert.NotNull(reserveMethod);
        Assert.Contains("reservationItemsInAuthoritativeLockOrder", reserveMethod!, StringComparison.Ordinal);
        Assert.Contains(".OrderBy(item => item.VariantId ?? Guid.Empty)", reserveMethod!, StringComparison.Ordinal);
        Assert.Contains("foreach (var item in reservationItemsInAuthoritativeLockOrder)", reserveMethod!, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyManualWalletPaymentBypass_MustBeHardDisabled()
    {
        var paymentServiceInterface = File.ReadAllText(ApiPath("Services", "Payments", "IPaymentService.cs"));
        var paymentService = File.ReadAllText(ApiPath("Services", "Payments", "PaymentService.cs"));

        Assert.Contains("ApproveReviewedPaymentAsync", paymentServiceInterface, StringComparison.Ordinal);
        Assert.Contains("RejectReviewedPaymentAsync", paymentServiceInterface, StringComparison.Ordinal);
        Assert.Contains("Legacy manual wallet confirmation is disabled", paymentServiceInterface, StringComparison.Ordinal);
        Assert.Contains("Legacy manual wallet rejection is disabled", paymentServiceInterface, StringComparison.Ordinal);
        Assert.Contains("LegacyManualPaymentBypassDisabled", paymentService, StringComparison.Ordinal);
        Assert.Contains("CreateLegacyManualPaymentBypassException(nameof(ConfirmManualPaymentAsync))", paymentService, StringComparison.Ordinal);
        Assert.Contains("CreateLegacyManualPaymentBypassException(nameof(RejectManualPaymentAsync))", paymentService, StringComparison.Ordinal);
    }

    [Fact]
    public void StorefrontRefund_MustSynchronouslyReverseExistingAffiliateCommission()
    {
        var lifecycle = File.ReadAllText(InfrastructurePath("Payments", "PaymentLifecycleService.cs"));
        var reconcileMethod = ExtractMethodBody(lifecycle, "ReconcileAffiliateCommissionReversalsForRefundAsync");

        Assert.Contains("ReconcileAffiliateCommissionReversalsForRefundAsync", lifecycle, StringComparison.Ordinal);
        Assert.NotNull(reconcileMethod);
        Assert.Contains("AffiliateCommissionLedger.CreateReversal", reconcileMethod!, StringComparison.Ordinal);
        Assert.Contains("AffiliateCommissionReversedEvent", reconcileMethod!, StringComparison.Ordinal);
        Assert.Contains("_db.AffiliateCommissionLedger.Add(reversal)", reconcileMethod!, StringComparison.Ordinal);
    }

    [Fact]
    public void AffiliatePayouts_MustSettleNetRefundReversedApprovedEntries()
    {
        var payoutService = File.ReadAllText(InfrastructurePath("Services", "Affiliate", "AffiliatePayoutService.cs"));
        var orchestrator = File.ReadAllText(InfrastructurePath("Services", "Affiliate", "AffiliateSettlementOrchestrator.cs"));

        Assert.Contains("ledgerByOrderItem", payoutService, StringComparison.Ordinal);
        Assert.Contains("ApprovedSum", payoutService, StringComparison.Ordinal);
        Assert.Contains("ReversedSum", payoutService, StringComparison.Ordinal);
        Assert.Contains("Math.Max(item.ApprovedSum - item.ReversedSum, 0m)", payoutService, StringComparison.Ordinal);
        Assert.Contains("AffiliateCommissionReversalTotals", orchestrator, StringComparison.Ordinal);
        Assert.Contains("PayableCommissionAmount", orchestrator, StringComparison.Ordinal);
        Assert.Contains("CreatePaidNetOfReversals", orchestrator, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_PAYOUT_REVERSED_AMOUNT_EXCEEDS_APPROVED", orchestrator, StringComparison.Ordinal);
        Assert.DoesNotContain("!reversedOrderItemIds.Contains(e.OrderItemId)", orchestrator, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubFailedPaymentWebhook_MustReleaseReservationsImmediately()
    {
        var payment = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPaymentService.cs"));

        Assert.Contains("ReleaseMarketplaceOrderReservationsAsync", payment, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPaymentFailedWebhook", payment, StringComparison.Ordinal);
        Assert.Contains("Reserved stock was released immediately", payment, StringComparison.Ordinal);
        Assert.DoesNotContain("reserved stock is retained until reservation expiry", payment, StringComparison.Ordinal);
    }

    [Fact]
    public void PosPaymentWebhookConcurrency_MustNotAcknowledgeWithoutDurableCompletion()
    {
        var paymentService = File.ReadAllText(ApiPath("Services", "Payments", "PaymentService.cs"));

        Assert.Contains("TryResolveCommittedWebhookDuplicateAsync", paymentService, StringComparison.Ordinal);
        Assert.Contains("WEBHOOK_CONCURRENCY_RETRY_REQUIRED", paymentService, StringComparison.Ordinal);
        Assert.Contains("await transaction.RollbackAsync(cancellationToken)", paymentService, StringComparison.Ordinal);
        Assert.Contains("Webhook processing concurrency conflict before durable completion", paymentService, StringComparison.Ordinal);
        Assert.DoesNotContain("Message = \"Payment intent already processed by concurrent operation\"", paymentService, StringComparison.Ordinal);
    }

    [Fact]
    public void VariantStockEvents_MustDriveLegacyProductStockAndSearchProjections()
    {
        var productDocument = File.ReadAllText(InfrastructurePath("MongoDB", "Documents", "ProductDocument.cs"));
        var productVariantDocument = File.ReadAllText(InfrastructurePath("MongoDB", "Documents", "ProductVariantDocument.cs"));
        var variantProjection = File.ReadAllText(InfrastructurePath("Consumers", "ProductVariantProjectionConsumer.cs"));
        var stockProjection = File.ReadAllText(InfrastructurePath("Consumers", "ProductStockChangedConsumer.cs"));
        var searchProjection = File.ReadAllText(InfrastructurePath("Consumers", "ProductStockPriceSearchIndexConsumer.cs"));
        var affiliatePolicy = File.ReadAllText(InfrastructurePath("Consumers", "Affiliate", "AffiliateProductPolicyProjectionConsumer.cs"));

        Assert.Contains("LastStockEventId", productDocument, StringComparison.Ordinal);
        Assert.Contains("LastStockUpdatedAtUtc", productDocument, StringComparison.Ordinal);
        Assert.Contains("LastStockEventVersion", productDocument, StringComparison.Ordinal);
        Assert.Contains("LastStockEventId", productVariantDocument, StringComparison.Ordinal);
        Assert.Contains("LastStockUpdatedAtUtc", productVariantDocument, StringComparison.Ordinal);
        Assert.Contains("LastStockEventVersion", productVariantDocument, StringComparison.Ordinal);
        Assert.Contains("BuildFreshVariantStockEventFilter", variantProjection, StringComparison.Ordinal);
        Assert.Contains(".Set(\"Variants.$.LastStockEventVersion\", @event.EventVersion)", variantProjection, StringComparison.Ordinal);
        Assert.Contains(".Set(\"Variants.$.LastEventVersion\", @event.EventVersion)", variantProjection, StringComparison.Ordinal);
        Assert.Contains("IConsumer<ProductVariantStockChangedEvent>", stockProjection, StringComparison.Ordinal);
        Assert.Contains("SynchronizeProductLevelStockFromVariantProjectionAsync", stockProjection, StringComparison.Ordinal);
        Assert.Contains("BuildFreshStockEventFilter", stockProjection, StringComparison.Ordinal);
        Assert.Contains("IConsumer<ProductVariantStockChangedEvent>", searchProjection, StringComparison.Ordinal);
        Assert.Contains("ReindexProductFromMongoAfterVariantStockAsync", searchProjection, StringComparison.Ordinal);
        Assert.Contains("IConsumer<ProductVariantStockChangedEvent>", affiliatePolicy, StringComparison.Ordinal);
        Assert.Contains("EnsureProductVariantStockProjectionReadyAsync", affiliatePolicy, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubPaymentStatus_MustBeOwnedByPaymentLifecycleAuthority()
    {
        var scope = File.ReadAllText(InfrastructurePath("Data", "CommerceHubBoundaryMutationScope.cs"));
        var dbContext = File.ReadAllText(InfrastructurePath("Data", "ApplicationDbContext.cs"));
        var authority = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPaymentLifecycleAuthority.cs"));
        var paymentService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPaymentService.cs"));
        var refundService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubRefundService.cs"));
        var refundExecution = File.ReadAllText(ApiPath("Services", "CommerceHub", "RefundExecutionService.cs"));
        var financialWorkflow = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubFinancialWorkflowService.cs"));

        Assert.Contains("BeginPaymentLifecycle", scope, StringComparison.Ordinal);
        Assert.Contains("IsPaymentLifecycleAuthorized", dbContext, StringComparison.Ordinal);
        Assert.Contains("CommerceHub payment lifecycle authority", dbContext, StringComparison.Ordinal);
        Assert.Contains("MarkOnlinePaymentCollected", authority, StringComparison.Ordinal);
        Assert.Contains("ResetOnlinePaymentToPendingForRetry", authority, StringComparison.Ordinal);
        Assert.Contains("MarkCodSubOrderCollected", authority, StringComparison.Ordinal);
        Assert.Contains("ApplyRefundPaymentState", authority, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPaymentLifecycleAuthority.MarkOnlinePaymentCollected", paymentService, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPaymentLifecycleAuthority.ResetOnlinePaymentToPendingForRetry", paymentService, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPaymentLifecycleAuthority.MarkCodSubOrderCollected", financialWorkflow, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPaymentLifecycleAuthority.ApplyRefundPaymentState", refundService, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPaymentLifecycleAuthority.ApplyRefundPaymentState", refundExecution, StringComparison.Ordinal);

        foreach (var source in new[] { paymentService, refundService, refundExecution, financialWorkflow })
        {
            Assert.DoesNotMatch(@"(marketplaceOrder|subOrder)\.PaymentStatus\s*=", source);
        }
    }

    [Fact]
    public void CmsAndNavigationPublishes_MustStageProjectionEventsBeforeAuthorityCommit()
    {
        var storefrontEvents = File.ReadAllText(CorePath("Events", "CacheInvalidationEvents.cs"));
        var cmsEvents = File.ReadAllText(CorePath("Events", "CmsPageEvents.cs"));
        var adminCms = File.ReadAllText(ApiPath("Services", "Admin", "AdminCmsService.cs"));
        var cmsPageService = File.ReadAllText(ApiPath("Services", "Cms", "CmsPageService.cs"));
        var marketplaceCms = File.ReadAllText(ApiPath("Services", "MarketplaceCms", "MarketplaceCmsPageService.cs"));
        var publishStorefrontPage = ExtractMethodBody(cmsPageService, "public async Task<AdminCmsPageResponse> PublishPageAsync");
        var deleteStorefrontPage = ExtractMethodBody(cmsPageService, "public async Task DeletePageAsync");
        var publishNavigation = ExtractMethodBody(adminCms, "public async Task<AdminNavigationResponse> PublishNavigationAsync");
        var publishMarketplacePage = ExtractMethodBody(marketplaceCms, "public async Task<MarketplaceCmsPageResponse> PublishPageAsync");
        var deleteMarketplacePage = ExtractMethodBody(marketplaceCms, "public async Task DeletePageAsync");

        Assert.Contains("StorefrontCmsPagePublishedEvent : IntegrationEvent, IAggregateScopedIntegrationEvent", storefrontEvents, StringComparison.Ordinal);
        Assert.Contains("StorefrontCmsPageDeletedEvent : IntegrationEvent, IAggregateScopedIntegrationEvent", storefrontEvents, StringComparison.Ordinal);
        Assert.Contains("MarketplaceCmsPagePublishedEvent : IntegrationEvent, INonAuthoritativeIntegrationEvent", storefrontEvents, StringComparison.Ordinal);
        Assert.Contains("MarketplaceCmsPageDeletedEvent : IntegrationEvent, INonAuthoritativeIntegrationEvent", storefrontEvents, StringComparison.Ordinal);
        Assert.Contains("NavigationPublishedEvent : IntegrationEvent, IAggregateScopedIntegrationEvent", cmsEvents, StringComparison.Ordinal);
        Assert.DoesNotContain("EmitNavigationPublishedEventAsync", adminCms, StringComparison.Ordinal);
        Assert.NotNull(publishStorefrontPage);
        Assert.NotNull(deleteStorefrontPage);
        Assert.NotNull(publishNavigation);
        Assert.NotNull(publishMarketplacePage);
        Assert.NotNull(deleteMarketplacePage);
        AssertContainsInOrder(
            publishStorefrontPage!,
            "StageStorefrontCmsPagePublishedEventAsync",
            "SaveChangesAsync(cancellationToken)",
            "CommitAsync(cancellationToken)");
        AssertContainsInOrder(
            deleteStorefrontPage!,
            "StageStorefrontCmsPageDeletedEventAsync",
            "SaveChangesAsync(cancellationToken)");
        AssertContainsInOrder(
            publishNavigation!,
            "StageNavigationPublishedEventAsync",
            "SaveStoreConfigChangesAsync",
            "InvalidateCacheAsync");
        AssertContainsInOrder(
            publishMarketplacePage!,
            "StageMarketplaceCmsPagePublishedEventAsync",
            "SaveChangesAsync(cancellationToken)",
            "CommitAsync(cancellationToken)");
        AssertContainsInOrder(
            deleteMarketplacePage!,
            "StageMarketplaceCmsPageDeletedEventAsync",
            "SaveChangesAsync(cancellationToken)");
    }

    [Fact]
    public void NavigationProjection_MustRejectStaleEventsByVersionAndTimestamp()
    {
        var document = File.ReadAllText(InfrastructurePath("MongoDB", "Documents", "StorefrontNavigationDocument.cs"));
        var consumer = File.ReadAllText(InfrastructurePath("Consumers", "NavigationProjectionConsumer.cs"));
        var consumeMethod = ExtractMethodBody(consumer, "Consume");
        var projectMethod = ExtractMethodBody(consumer, "ProjectNavigationAsync");
        var filterMethod = ExtractMethodBody(consumer, "BuildFreshNavigationEventFilter");

        Assert.Contains("LastEventId", document, StringComparison.Ordinal);
        Assert.Contains("LastEventOccurredAtUtc", document, StringComparison.Ordinal);
        Assert.Contains("LastEventVersion", document, StringComparison.Ordinal);
        Assert.NotNull(consumeMethod);
        Assert.NotNull(projectMethod);
        Assert.NotNull(filterMethod);
        Assert.Contains("ProjectNavigationAsync(@event", consumeMethod!, StringComparison.Ordinal);
        Assert.Contains("navDoc.LastEventId = @event.EventId", projectMethod!, StringComparison.Ordinal);
        Assert.Contains("navDoc.LastEventOccurredAtUtc = @event.OccurredAtUtc", projectMethod!, StringComparison.Ordinal);
        Assert.Contains("navDoc.LastEventVersion = @event.EventVersion", projectMethod!, StringComparison.Ordinal);
        Assert.Contains("BuildFreshNavigationEventFilter", projectMethod!, StringComparison.Ordinal);
        Assert.Contains("ReplaceOneAsync", projectMethod!, StringComparison.Ordinal);
        Assert.Contains("LastEventOccurredAtUtc", filterMethod!, StringComparison.Ordinal);
        Assert.Contains("LastEventVersion", filterMethod!, StringComparison.Ordinal);
        Assert.Contains("LastEventId", filterMethod!, StringComparison.Ordinal);
    }

    private static string ApiPath(params string[] parts) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "PointOfSale.API", Path.Combine(parts)));

    private static string InfrastructurePath(params string[] parts)
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var activePath = Path.Combine(repositoryRoot, "PointOfSale.Infrastructure", Path.Combine(parts));
        if (File.Exists(activePath) || parts.Length == 0 || !string.Equals(parts[0], "Migrations", StringComparison.Ordinal))
        {
            return activePath;
        }

        return Path.Combine(
            repositoryRoot,
            "docs",
            "recovery",
            "archive",
            "RECOVERY-4_PHASE_A",
            "PointOfSale.Infrastructure",
            Path.Combine(parts));
    }

    private static string CorePath(params string[] parts) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "PointOfSale.Core", Path.Combine(parts)));

    private static string CommerceHubPath(params string[] parts) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "PointOfSale.CommerceHub", Path.Combine(parts)));

    private static string WebPath(params string[] parts) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "PointOfSale.Web", Path.Combine(parts)));

    private static void AssertProductMutationPublishesBeforeCommit(string source, string methodName, string publishMarker)
    {
        var method = ExtractMethodBody(source, methodName);

        Assert.NotNull(method);
        AssertContainsInOrder(
            method!,
            "BeginTransactionAsync",
            "SaveChangesAsync",
            publishMarker,
            "SaveChangesAsync",
            "CommitAsync");
    }

    private static void AssertVariantMutationPublishesBeforeCommit(string source, string methodName, string publishMarker)
    {
        var method = ExtractMethodBody(source, methodName);

        Assert.NotNull(method);
        AssertContainsInOrder(
            method!,
            "BeginTransactionAsync",
            "SaveChangesAsync",
            publishMarker,
            "SaveChangesAsync",
            "CommitAsync");
    }

    private static void AssertContainsInOrder(string source, params string[] markers)
    {
        var previousIndex = -1;

        foreach (var marker in markers)
        {
            var index = source.IndexOf(marker, previousIndex + 1, StringComparison.Ordinal);
            Assert.True(index >= 0, $"Expected marker '{marker}' after index {previousIndex}.");
            previousIndex = index;
        }
    }

    private static string? ExtractMethodBody(string source, string methodName)
    {
        var methodPattern = methodName.Contains(' ', StringComparison.Ordinal)
            ? Regex.Escape(methodName) + @"\s*\("
            : $@"\b{Regex.Escape(methodName)}\s*\(";
        foreach (Match match in Regex.Matches(source, methodPattern))
        {
            var openParenIndex = source.IndexOf('(', match.Index);
            if (openParenIndex == -1)
            {
                continue;
            }

            var closeParenIndex = FindMatching(source, openParenIndex, '(', ')');
            if (closeParenIndex == -1)
            {
                continue;
            }

            var openBraceIndex = FindMethodOpeningBrace(source, closeParenIndex + 1);
            if (openBraceIndex == -1)
            {
                continue;
            }

            var closeBraceIndex = FindMatching(source, openBraceIndex, '{', '}');
            if (closeBraceIndex != -1)
            {
                return source.Substring(match.Index, closeBraceIndex - match.Index + 1);
            }
        }

        return null;
    }

    private static int FindMatching(string source, int openIndex, char open, char close)
    {
        var depth = 1;
        for (var i = openIndex + 1; i < source.Length; i++)
        {
            if (source[i] == open)
            {
                depth++;
            }
            else if (source[i] == close)
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private static int FindMethodOpeningBrace(string source, int startIndex)
    {
        for (var i = startIndex; i < source.Length; i++)
        {
            if (char.IsWhiteSpace(source[i]))
            {
                continue;
            }

            return source[i] == '{' ? i : -1;
        }

        return -1;
    }

    /// <summary>
    /// Asserts that a method body contains no try-catch blocks using Roslyn syntax tree analysis.
    /// This is more accurate than raw substring checks which would incorrectly flag words like
    /// "retry", "country", "entry", etc.
    /// </summary>
    private static void AssertMethodHasNoTryCatchBlocks(string source, string methodName)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();
        var method = root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .SingleOrDefault(candidate => candidate.Identifier.Text == methodName);

        Assert.NotNull(method);

        var tryStatements = method!.DescendantNodes()
            .OfType<TryStatementSyntax>()
            .ToList();

        Assert.Empty(tryStatements);
    }
}
