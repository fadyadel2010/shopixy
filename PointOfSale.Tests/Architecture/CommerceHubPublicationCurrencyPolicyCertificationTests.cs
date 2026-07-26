namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubPublicationCurrencyPolicyCertificationTests
{
    [Fact]
    public void CommerceHubPublicationBoundary_MustRejectNonEgpProductPriceAuthority()
    {
        var handlers = ReadRepoFile("PointOfSale.API/CommerceHub/Handlers/CommerceHubCommandHandlers.cs");
        var controller = ReadRepoFile("PointOfSale.API/Controllers/AdminCommerceHubController.cs");

        Assert.Contains("internal static class CommerceHubPublicationCurrencyPolicy", handlers, StringComparison.Ordinal);
        Assert.Contains("FinancialCurrencyGuard.PlatformBaseCurrencyCode", handlers, StringComparison.Ordinal);
        Assert.Contains("CommerceHub currently supports EGP products only.", handlers, StringComparison.Ordinal);
        Assert.Contains("This product uses {normalizedCurrency} and cannot be published.", handlers, StringComparison.Ordinal);

        Assert.Contains("CommerceHubPublicationCurrencyPolicy.EnsureEgpPricedProduct(tenant.Currency)", handlers, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPublicationCurrencyPolicy.EnsureEgpPricedProduct(productCurrency)", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("Only EGP-denominated tenants can publish or update CommerceHub listings.", handlers, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplayRecoveryAndReindex_MustKeepNonEgpListingsOutOfLiveSearchAuthority()
    {
        var overlay = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchSqlAuthorityOverlay.cs");
        var reindex = ReadRepoFile("PointOfSale.Infrastructure/Jobs/CommerceHubReindexService.cs");
        var autoHealing = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubAutoHealingService.cs");

        Assert.Contains("tenant.Currency != FinancialCurrencyGuard.PlatformBaseCurrencyCode", overlay, StringComparison.Ordinal);
        Assert.Contains("return null;", overlay, StringComparison.Ordinal);
        Assert.Contains("item.tenant.Currency == FinancialCurrencyGuard.PlatformBaseCurrencyCode", reindex, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", reindex, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", autoHealing, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
