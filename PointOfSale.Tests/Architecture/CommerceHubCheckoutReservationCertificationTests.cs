namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubCheckoutReservationCertificationTests
{
    [Fact]
    public void CheckoutReservations_MustBeCreatedBeforeOrderCommitAndFailClosedOnShortage()
    {
        var service = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");
        var inventoryService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/InventoryConsistencyService.cs");

        Assert.Contains("InventoryReservationTtl", service, StringComparison.Ordinal);
        Assert.Contains("ReserveOrderItemAsync", service, StringComparison.Ordinal);
        Assert.Contains("InventoryReservationRequest", service, StringComparison.Ordinal);
        Assert.Contains("OrderId: marketplaceOrder.Id", service, StringComparison.Ordinal);
        Assert.Contains("SubOrderId: subOrder.Id", service, StringComparison.Ordinal);
        Assert.Contains("OrderItemId: item.Id", service, StringComparison.Ordinal);
        Assert.Contains("\"OUT_OF_STOCK\"", service, StringComparison.Ordinal);
        Assert.Contains("_dbContext.CommerceHubMarketplaceOrders.Add(marketplaceOrder)", service, StringComparison.Ordinal);
        Assert.Contains("await transaction.CommitAsync", service, StringComparison.Ordinal);
        Assert.Contains("InventoryReservation", inventoryService, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckoutReservations_MustKeepCodUntilFulfillmentAndOnlinePaymentsTimeBound()
    {
        var checkout = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");
        var reconciliation = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubInventoryReconciliationJob.cs");

        Assert.Contains("subOrder.PaymentMethod == CommerceHubPaymentMethod.CashOnDelivery", checkout, StringComparison.Ordinal);
        Assert.Contains("? null", checkout, StringComparison.Ordinal);
        Assert.Contains(": DateTime.UtcNow.Add(InventoryReservationTtl)", checkout, StringComparison.Ordinal);
        Assert.Contains("item.SubOrder.PaymentMethod == CommerceHubPaymentMethod.CashOnDelivery", reconciliation, StringComparison.Ordinal);
        Assert.Contains("item.SubOrder.Status != OrderStatus.Cancelled", reconciliation, StringComparison.Ordinal);
        Assert.Contains("item.SubOrder.Status != OrderStatus.Refunded", reconciliation, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckoutConcurrency_MustKeepCartLocksCustomerLocksAndSerializableBoundary()
    {
        var service = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");
        var cartStore = ReadRepoFile("PointOfSale.CommerceHub/src/store/cart-store.ts");

        Assert.Contains("BuildCustomerCheckoutLockKey", service, StringComparison.Ordinal);
        Assert.Contains("BuildCheckoutLockKey", service, StringComparison.Ordinal);
        Assert.Contains("TryAcquireLockAsync", service, StringComparison.Ordinal);
        Assert.Contains("CreateCheckoutInProgressException", service, StringComparison.Ordinal);
        Assert.Contains("IsolationLevel.Serializable", service, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", service, StringComparison.Ordinal);
        Assert.Contains("request.CartVersion!.Value", service, StringComparison.Ordinal);
        Assert.Contains("\"CART_CHANGED\"", service, StringComparison.Ordinal);
        Assert.Contains("isCheckoutLocked", cartStore, StringComparison.Ordinal);
        Assert.Contains("CART_LOCKED_MESSAGE", cartStore, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckoutFailureAndRecovery_MustUnlockCartRemovePendingIdempotencyAndBlockUnsafePaymentRetry()
    {
        var service = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");
        var recoveryService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPaymentRecoveryAuthorityService.cs");
        var checkoutPage = ReadRepoFile("PointOfSale.CommerceHub/src/pages/CheckoutPage.tsx");

        Assert.Contains("CleanupAfterCheckoutFailureAsync", service, StringComparison.Ordinal);
        Assert.Contains("idempotencyRecordId", service, StringComparison.Ordinal);
        Assert.Contains("lockedCartId", service, StringComparison.Ordinal);
        Assert.Contains("LockedAtUtc = null", service, StringComparison.Ordinal);
        Assert.Contains("CommerceHubCheckoutIdempotency", service, StringComparison.Ordinal);
        Assert.Contains("ReservationAuthorityLostMessage", recoveryService, StringComparison.Ordinal);
        Assert.Contains("HasActiveMarketplaceReservationAuthorityAsync", recoveryService, StringComparison.Ordinal);
        Assert.Contains("persistActivePaymentRecoveryContext", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("clearActivePaymentRecoveryContextIfMatches", checkoutPage, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpiredReservationRecovery_MustReacquireOnlyIntactZeroEvidenceOrderAuthority()
    {
        var inventory = ReadRepoFile("PointOfSale.API/Services/CommerceHub/InventoryConsistencyService.cs");
        var payment = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs");
        var recovery = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPaymentRecoveryAuthorityService.cs");
        var customerQueries = ReadRepoFile("PointOfSale.CommerceHub/src/features/customer/queries.ts");
        var customerOrderPage = ReadRepoFile("PointOfSale.CommerceHub/src/pages/CustomerOrderDetailsPage.tsx");

        Assert.Contains("ReacquireMarketplaceOrderReservationsForPaymentAsync", inventory, StringComparison.Ordinal);
        Assert.Contains("EnsureHistoricalReservationsMatchCurrentOrderAuthority", inventory, StringComparison.Ordinal);
        Assert.Contains("MARKETPLACE_RESERVATION_HISTORY_CHANGED", inventory, StringComparison.Ordinal);
        Assert.Contains("MARKETPLACE_RESERVATION_QUANTITY_CHANGED", inventory, StringComparison.Ordinal);
        Assert.Contains("MARKETPLACE_RESERVATION_REACQUISITION_REQUIRES_NO_PAYMENT_EVIDENCE", inventory, StringComparison.Ordinal);
        Assert.Contains("LoadMarketplaceOrderReservationHistoryForUpdateAsync", inventory, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", inventory, StringComparison.Ordinal);
        Assert.Contains("IsolationLevel.Serializable", inventory, StringComparison.Ordinal);

        var lockIndex = payment.IndexOf("var paymentLockResult = await _lockService.TryAcquireLockAsync(", StringComparison.Ordinal);
        var lockedReloadIndex = payment.IndexOf("await _dbContext.Entry(marketplaceOrder).ReloadAsync(cancellationToken);", lockIndex, StringComparison.Ordinal);
        var readinessPrecheckIndex = payment.IndexOf("CommerceHubPayment.Initiate.ReservationReacquisition", lockedReloadIndex, StringComparison.Ordinal);
        var reacquireIndex = payment.IndexOf("ReacquireMarketplaceOrderReservationsForPaymentAsync(", lockIndex, StringComparison.Ordinal);
        var readinessIndex = payment.IndexOf("EnsureCommerceIntakeAllowedAsync(", reacquireIndex, StringComparison.Ordinal);
        var providerIndex = payment.IndexOf("var session = provider switch", readinessIndex, StringComparison.Ordinal);
        Assert.True(lockedReloadIndex > lockIndex, "Order lifecycle authority must be freshly reloaded under the payment lock.");
        Assert.True(readinessPrecheckIndex < reacquireIndex, "Financial readiness must fail closed before reservation reacquisition mutates inventory.");
        Assert.True(reacquireIndex > lockIndex, "Reservation reacquisition must run under the payment authority lock.");
        Assert.True(readinessIndex > reacquireIndex, "Financial readiness must run after current inventory is proven.");
        Assert.True(providerIndex > readinessIndex, "Provider side effects must remain behind financial readiness.");

        Assert.Contains("RequiresReservationReacquisition", recovery, StringComparison.Ordinal);
        Assert.Contains("!hasAnyPaymentAuthorityEvidence", recovery, StringComparison.Ordinal);
        Assert.Contains("HasMarketplaceReservationReacquisitionAuthorityAsync", recovery, StringComparison.Ordinal);
        Assert.Contains("staleTime: 0", customerQueries, StringComparison.Ordinal);
        Assert.Contains("refetchOnMount: \"always\"", customerQueries, StringComparison.Ordinal);
        Assert.Contains("refetchOnWindowFocus: true", customerQueries, StringComparison.Ordinal);
        Assert.Contains("const refreshedOrder = await orderQuery.refetch()", customerOrderPage, StringComparison.Ordinal);
        Assert.Contains("!currentOrder?.canRetryPayment", customerOrderPage, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckoutMonitoring_MustRetainReviewedLogAndTelemetryAnchors()
    {
        var service = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");
        var controller = ReadRepoFile("PointOfSale.API/Controllers/CommerceHubCheckoutController.cs");
        var shippingService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubShippingService.cs");
        var reservationTelemetry = ReadRepoFile("PointOfSale.Infrastructure/Instrumentation/InventoryReservationTelemetry.cs");

        Assert.Contains("CommerceHub checkout blocked", controller, StringComparison.Ordinal);
        Assert.Contains("CommerceHub checkout failed unexpectedly", controller, StringComparison.Ordinal);
        Assert.Contains("CommerceHub checkout created", service, StringComparison.Ordinal);
        Assert.Contains("CommerceHub checkout replay", service, StringComparison.Ordinal);
        Assert.Contains("CommerceHub promo claim failed", service, StringComparison.Ordinal);
        Assert.Contains("CommerceHub shipping selection refreshed", shippingService, StringComparison.Ordinal);
        Assert.Contains("inventory.reservations.stale_count", reservationTelemetry, StringComparison.Ordinal);
        Assert.Contains("inventory.reservations.commit_release_ratio", reservationTelemetry, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
