namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubCatalogCurrencyAuthorityCertificationTests
{
    [Fact]
    public void CatalogRuntime_MustRecomposeAndProjectOnlyEgpCurrency()
    {
        var overlay = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchSqlAuthorityOverlay.cs");
        var projection = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Consumers/CommerceHubProjectionConsumer.cs");
        var query = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubQueryService.cs");

        Assert.Contains("tenant.Currency != FinancialCurrencyGuard.PlatformBaseCurrencyCode", overlay, StringComparison.Ordinal);
        Assert.Contains("document.Currency = FinancialCurrencyGuard.PlatformBaseCurrencyCode", overlay, StringComparison.Ordinal);
        Assert.Contains("private static string ResolveCurrency()", projection, StringComparison.Ordinal);
        Assert.Contains("PointOfSale.Core.Financial.FinancialCurrencyGuard.PlatformBaseCurrencyCode", projection, StringComparison.Ordinal);
        Assert.Contains("RecomposeVisibleListingItemsAsync", query, StringComparison.Ordinal);
        Assert.Contains("RecomposeVisibleSuggestionProductsAsync", query, StringComparison.Ordinal);
        Assert.Contains("RecomposeVisibleAutocompleteProductsAsync", query, StringComparison.Ordinal);
    }

    [Fact]
    public void CatalogClient_MustRejectNonEgpDtoEvidenceBeforeCacheOrPresentation()
    {
        var authority = CertificationSource.ReadRepoFile("PointOfSale.CommerceHub/src/utils/commerceHubCurrencyAuthority.ts");
        var service = CertificationSource.ReadRepoFile("PointOfSale.CommerceHub/src/services/commercehub-service.ts");
        var formatter = CertificationSource.ReadRepoFile("PointOfSale.CommerceHub/src/utils/formatCurrency.ts");
        var productCard = CertificationSource.ReadRepoFile("PointOfSale.CommerceHub/src/commerce/components/ProductCard.tsx");
        var detail = CertificationSource.ReadRepoFile("PointOfSale.CommerceHub/src/pages/ProductDetailsPage.tsx");

        Assert.Contains("requireCommerceHubCatalogCurrency", authority, StringComparison.Ordinal);
        Assert.Contains("rejected non-EGP currency evidence", authority, StringComparison.Ordinal);
        Assert.Contains("requireCatalogCurrencies(response.data.items)", service, StringComparison.Ordinal);
        Assert.Contains("requireCatalogCurrency(response.data)", service, StringComparison.Ordinal);
        Assert.Contains("requireCatalogSearchResponseCurrency(response.data)", service, StringComparison.Ordinal);
        Assert.Contains("requireCatalogCurrencies(response.data.products)", service, StringComparison.Ordinal);
        Assert.Contains("resolveCommerceHubDisplayCurrency(currency)", formatter, StringComparison.Ordinal);
        Assert.DoesNotContain(">{product.currency}</p>", productCard, StringComparison.Ordinal);
        Assert.Contains("isCommerceHubAuthorityCurrency(product?.currency)", detail, StringComparison.Ordinal);
        Assert.Contains("did not provide CommerceHub EGP currency authority", detail, StringComparison.Ordinal);
    }
}
