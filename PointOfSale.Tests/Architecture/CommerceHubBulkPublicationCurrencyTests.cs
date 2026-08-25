namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubBulkPublicationCurrencyTests
{
    [Fact]
    public void BulkPublishUpdateAndOverrideRemoval_MustValidateCurrencyPerRow()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/AdminCommerceHubController.cs");

        Assert.Contains("foreach (var listingId in listingIds)", controller, StringComparison.Ordinal);
        Assert.Contains("\"publish\" => ApplySafeBulkPublish(listing, variant, normalizedProductCurrency, liveCurrentCategoryIds),", controller, StringComparison.Ordinal);
        Assert.Contains("\"remove_override\" => ApplySafeBulkRemoveOverride(", controller, StringComparison.Ordinal);
        Assert.Contains("normalizedProductCurrency,\n                        liveCurrentCategoryIds),", controller.Replace("\r\n", "\n"), StringComparison.Ordinal);
        Assert.Contains("\"update_category\" => ApplySafeBulkCategoryUpdate(listing, variant, request.CategoryId!.Value, normalizedProductCurrency),", controller, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPublicationCurrencyPolicy.EnsureEgpPricedProduct(productCurrency);", controller, StringComparison.Ordinal);
        Assert.Contains("EnsureLiveCurrentCategoryForBulk(listing, liveCurrentCategoryIds);", controller, StringComparison.Ordinal);
        Assert.Contains("catch (Exception ex) when (ex is CommerceHubValidationException or InvalidOperationException or ArgumentException)", controller, StringComparison.Ordinal);
        Assert.Contains("Success = false", controller, StringComparison.Ordinal);
        Assert.Contains("Message = ex.Message", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("Only EGP-denominated tenants are supported for CommerceHub operations.", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void BulkUnpublish_MustRemainAvailableForLegacyNonEgpListingWithdrawal()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/AdminCommerceHubController.cs");

        Assert.Contains("\"unpublish\" => listing.Unpublish(),", controller, StringComparison.Ordinal);
        Assert.Contains("ApplySafeBulkPublish", controller, StringComparison.Ordinal);
        Assert.Contains("ApplySafeBulkCategoryUpdate", controller, StringComparison.Ordinal);
        Assert.Contains("ApplySafeBulkRemoveOverride", controller, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
