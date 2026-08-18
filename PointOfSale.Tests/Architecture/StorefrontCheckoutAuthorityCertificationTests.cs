namespace PointOfSale.Tests.Architecture;

public sealed class StorefrontCheckoutAuthorityCertificationTests
{
    [Fact]
    public void TenantStorefrontCheckout_MustRevalidatePublishedProductAndCategoryAuthority()
    {
        var source = ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");

        Assert.Contains("variant.Product.PublishStatus == PublishStatus.Published", source, StringComparison.Ordinal);
        Assert.Contains("CategoryLineageAuthority.LoadTenantSnapshotAsync", source, StringComparison.Ordinal);
        Assert.Contains("categoryLineage.IsLiveInLineage(authoritativeVariant.CategoryId)", source, StringComparison.Ordinal);
        Assert.Contains("RevalidateCheckoutAuthorityInsideTransactionAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubCheckout_MustRevalidateLiveMarketplaceCategoryAuthority()
    {
        var source = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");

        Assert.Contains("CommerceHubCategoryLineageAuthority.LoadLiveIdsAsync", source, StringComparison.Ordinal);
        Assert.Contains("CATEGORY_UNAVAILABLE", source, StringComparison.Ordinal);
        Assert.Contains("IsolationLevel.Serializable", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckoutSessionAutosave_MustPersistOwnedLiveCartSnapshots()
    {
        var source = ReadRepoFile("PointOfSale.API/Controllers/CheckoutSessionController.cs");

        Assert.Contains("var authoritativeCart = await LoadAuthoritativeCartAsync(", source, StringComparison.Ordinal);
        Assert.Contains("ItemsSnapshotJson = BuildAuthoritativeItemsSnapshotJson(authoritativeCart)", source, StringComparison.Ordinal);
        Assert.Contains("CartTotal = authoritativeCart.GrandTotal", source, StringComparison.Ordinal);
        Assert.Contains("Currency = authoritativeCart.Currency", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ItemsSnapshotJson = request.ItemsSnapshotJson", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CartTotal = request.CartTotal", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Currency = request.Currency", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckoutSessionRecovery_MustFailClosedWithoutLiveCartRevalidation()
    {
        var source = ReadRepoFile("PointOfSale.API/Controllers/CheckoutSessionController.cs");

        Assert.Contains("var recoveredLiveCart = await TryRevalidateRecoveredCartAsync(", source, StringComparison.Ordinal);
        Assert.Contains("if (recoveredLiveCart.Cart == null)", source, StringComparison.Ordinal);
        Assert.Contains("RECOVERY_CART_REVALIDATION_FAILED", source, StringComparison.Ordinal);
        Assert.Contains("var responseCart = MapRecoveredCartItems(recoveredLiveCart.Cart);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("var responseCart = recoveredLiveCart.Cart != null", source, StringComparison.Ordinal);
        Assert.DoesNotContain(": recoveredCart", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckoutConversion_MustRemainServerPricedInventoryBoundAndReplaySafe()
    {
        var storefront = ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var commerceHub = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");

        Assert.Contains("_storefrontPricingService.CalculateCheckoutAsync", storefront, StringComparison.Ordinal);
        Assert.Contains("EnsureExpectedPricingMatches", storefront, StringComparison.Ordinal);
        Assert.Contains("ValidateAndReserveInventoryAsync", storefront, StringComparison.Ordinal);
        Assert.Contains("IsolationLevel.Serializable", storefront, StringComparison.Ordinal);
        Assert.Contains("ActiveCartOrderUniqueIndexName", storefront, StringComparison.Ordinal);

        Assert.Contains("_priceAuthority.ResolveAsync", commerceHub, StringComparison.Ordinal);
        Assert.Contains("_shippingService.CalculateAsync", commerceHub, StringComparison.Ordinal);
        Assert.Contains("ValidateRequestedPaymentMethod", commerceHub, StringComparison.Ordinal);
        Assert.Contains("_inventoryConsistencyService.ReserveOrderItemAsync", commerceHub, StringComparison.Ordinal);
        Assert.Contains("EnsureCheckoutIdempotencyRequestMatches", commerceHub, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationArtifacts_MustRecordP2BTruth()
    {
        Assert.Contains(
            "PASS P2-B Storefront Checkout Authority",
            ReadRepoFile("docs/architecture/storefront-checkout-authority-certification-findings.md"),
            StringComparison.Ordinal);

    }

    private static string ReadRepoFile(string relativePath)
    {
        var root = DiscoverSolutionRoot(AppContext.BaseDirectory);
        return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string DiscoverSolutionRoot(string startPath)
    {
        var current = new DirectoryInfo(startPath);
        while (current != null)
        {
            if (current.GetFiles("*.sln").Any() || current.GetDirectories("PointOfSale.API").Any())
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException($"Could not discover solution root from '{startPath}'.");
    }
}
