namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubCartInventoryCertificationTests
{
    [Fact]
    public void CartInventory_MustRevalidateListingProductVariantAndStockAuthority()
    {
        var cartService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCartService.cs");
        var checkoutService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");

        Assert.Contains("SanitizeAsync", cartService, StringComparison.Ordinal);
        Assert.Contains("WherePubliclyVisibleLineage()", cartService, StringComparison.Ordinal);
        Assert.Contains("listing.IsPublished", cartService, StringComparison.Ordinal);
        Assert.Contains("!product.IsDeleted", cartService, StringComparison.Ordinal);
        Assert.Contains("product.PublishStatus == PublishStatus.Published", cartService, StringComparison.Ordinal);
        Assert.Contains("ValidateLineIntegrity", cartService, StringComparison.Ordinal);
        Assert.Contains("GetAvailableQuantityAsync", cartService, StringComparison.Ordinal);
        Assert.Contains("MaxQuantity", cartService, StringComparison.Ordinal);
        Assert.Contains("IsInStock", cartService, StringComparison.Ordinal);
        Assert.Contains("InventoryNotice", cartService, StringComparison.Ordinal);
        Assert.Contains("LoadCommerceHubListingsForCheckoutAuthorityAsync", checkoutService, StringComparison.Ordinal);
        Assert.Contains("LoadProductVariantsForCheckoutAuthorityAsync", checkoutService, StringComparison.Ordinal);
        Assert.Contains("LoadProductsForCheckoutAuthorityAsync", checkoutService, StringComparison.Ordinal);
        Assert.Contains("GetAvailableQuantityAsync", checkoutService, StringComparison.Ordinal);
        Assert.Contains("\"OUT_OF_STOCK\"", checkoutService, StringComparison.Ordinal);
    }

    [Fact]
    public void CartCheckout_MustLockPersistedCartAndRejectStaleClientSnapshots()
    {
        var checkoutService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");
        var cartService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCartService.cs");

        Assert.Contains("BuildCustomerCheckoutLockKey", checkoutService, StringComparison.Ordinal);
        Assert.Contains("BuildCheckoutLockKey", checkoutService, StringComparison.Ordinal);
        Assert.Contains("LockCartForCheckoutAsync", checkoutService, StringComparison.Ordinal);
        Assert.Contains("EnsureLockedCartMatchesCheckoutRequest", checkoutService, StringComparison.Ordinal);
        Assert.Contains("request.CartVersion!.Value", checkoutService, StringComparison.Ordinal);
        Assert.Contains("IsolationLevel.Serializable", checkoutService, StringComparison.Ordinal);
        Assert.Contains("CleanupAfterCheckoutFailureAsync", checkoutService, StringComparison.Ordinal);
        Assert.Contains("CreateCheckoutInProgressException", checkoutService, StringComparison.Ordinal);
        Assert.Contains("IsCartLockExpired", cartService, StringComparison.Ordinal);
        Assert.Contains("EnsureCartIsMutable", cartService, StringComparison.Ordinal);
    }

    [Fact]
    public void CartSanitization_MustRemoveUnavailableItemsWithNotices()
    {
        // PASS 16.3 HARDENING: Cart sanitization must issue distinct notices for each
        // category of item removal so customers understand why items left their cart.
        var cartService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCartService.cs");

        Assert.Contains("\"STORE_REMOVED\"", cartService, StringComparison.Ordinal);
        Assert.Contains("\"LISTING_REMOVED\"", cartService, StringComparison.Ordinal);
        Assert.Contains("\"PRODUCT_REMOVED\"", cartService, StringComparison.Ordinal);
        Assert.Contains("\"VARIANT_REMOVED\"", cartService, StringComparison.Ordinal);
        Assert.Contains("\"ITEM_REMOVED_MISMATCH\"", cartService, StringComparison.Ordinal);
        Assert.Contains("\"ITEM_REMOVED_UNAVAILABLE\"", cartService, StringComparison.Ordinal);
        Assert.Contains("\"ITEM_REMOVED_OUT_OF_STOCK\"", cartService, StringComparison.Ordinal);
        Assert.Contains("\"QUANTITY_ADJUSTED\"", cartService, StringComparison.Ordinal);
        Assert.Contains("\"PRICE_CHANGED\"", cartService, StringComparison.Ordinal);

        // Sanitization must enforce full publication visibility
        Assert.Contains("!product.IsOnlineVisible", cartService, StringComparison.Ordinal);
        Assert.Contains("product.PublishStatus != PublishStatus.Published", cartService, StringComparison.Ordinal);
    }

    [Fact]
    public void CartInventoryReservation_MustOccurAtCheckoutOnly()
    {
        // PASS 16.3: Inventory reservation must NOT occur at cart add/save/sanitize time.
        // Reservation is checkout authority only via the serializable transaction.
        var checkoutService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");

        Assert.Contains("ReserveOrderItemAsync", checkoutService, StringComparison.Ordinal);
        Assert.Contains("InventoryReservationTtl", checkoutService, StringComparison.Ordinal);
        Assert.Contains("InventoryEventIdFactory.Create", checkoutService, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
