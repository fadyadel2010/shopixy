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
