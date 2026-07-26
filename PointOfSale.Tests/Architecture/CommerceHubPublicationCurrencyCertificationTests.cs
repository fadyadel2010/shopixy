namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubPublicationCurrencyCertificationTests
{
    [Fact]
    public void PublicationSearchProjectionAndReindex_MustEmitEgpOnlyMarketplaceCurrency()
    {
        var handlers = ReadRepoFile("PointOfSale.API/CommerceHub/Handlers/CommerceHubCommandHandlers.cs");
        var listing = ReadRepoFile("PointOfSale.Core/Entities/CommerceHubListing.cs");
        var projection = ReadRepoFile("PointOfSale.Infrastructure/Consumers/CommerceHubProjectionConsumer.cs");
        var overlay = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchSqlAuthorityOverlay.cs");
        var reindex = ReadRepoFile("PointOfSale.Infrastructure/Jobs/CommerceHubReindexService.cs");
        var query = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubQueryService.cs");

        Assert.Contains("CommerceHubListing.Create", handlers, StringComparison.Ordinal);
        Assert.Contains("listing.Publish", handlers, StringComparison.Ordinal);
        Assert.Contains("listing.Update", handlers, StringComparison.Ordinal);
        Assert.DoesNotContain("Currency", listing, StringComparison.Ordinal);

        Assert.Contains("ResolveCurrency()", projection, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", projection, StringComparison.Ordinal);
        Assert.Contains("FinancialCurrencyGuard.PlatformBaseCurrencyCode", overlay, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", reindex, StringComparison.Ordinal);
        Assert.Contains("RecomposeVisibleListingItemsAsync", query, StringComparison.Ordinal);
    }

    [Fact]
    public void CartCheckoutAndOrderSnapshots_MustNotRequireVendorCurrencyAgreement()
    {
        var cart = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCartService.cs");
        var checkout = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");
        var order = ReadRepoFile("PointOfSale.Core/Entities/CommerceHubMarketplaceOrder.cs");
        var subOrder = ReadRepoFile("PointOfSale.Core/Entities/CommerceHubSubOrder.cs");

        Assert.DoesNotContain("MULTI_CURRENCY_NOT_SUPPORTED", cart, StringComparison.Ordinal);
        Assert.DoesNotContain("MULTI_CURRENCY_NOT_SUPPORTED", checkout, StringComparison.Ordinal);
        Assert.Contains("Currency = ResolveCommerceHubAuthorityCurrency()", cart, StringComparison.Ordinal);
        Assert.Contains("var currency = ResolveCommerceHubAuthorityCurrency();", checkout, StringComparison.Ordinal);
        Assert.Contains("Currency = currency", checkout, StringComparison.Ordinal);
        Assert.Contains("public string Currency", order, StringComparison.Ordinal);
        Assert.Contains("public string Currency", subOrder, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
