namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubCurrencyAuthorityCertificationTests
{
    [Fact]
    public void CommerceHubRuntime_MustUseEgpPlatformAuthority_NotTenantCurrency()
    {
        var cart = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCartService.cs");
        var checkout = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");
        var overlay = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchSqlAuthorityOverlay.cs");
        var projection = ReadRepoFile("PointOfSale.Infrastructure/Consumers/CommerceHubProjectionConsumer.cs");

        Assert.Contains("FinancialCurrencyGuard.PlatformBaseCurrencyCode", cart, StringComparison.Ordinal);
        Assert.Contains("FinancialCurrencyGuard.PlatformBaseCurrencyCode", checkout, StringComparison.Ordinal);
        Assert.Contains("FinancialCurrencyGuard.PlatformBaseCurrencyCode", overlay, StringComparison.Ordinal);
        Assert.Contains("FinancialCurrencyGuard.PlatformBaseCurrencyCode", projection, StringComparison.Ordinal);

        Assert.DoesNotContain(".Select(tenant => NormalizeCurrency(tenant.Currency))", cart, StringComparison.Ordinal);
        Assert.DoesNotContain("Currency = NormalizeCurrency(store.Currency)", cart, StringComparison.Ordinal);
        Assert.DoesNotContain("tenant.Currency!.Trim().ToUpperInvariant()", checkout, StringComparison.Ordinal);
        Assert.DoesNotContain("document.Currency = string.IsNullOrWhiteSpace(tenant.Currency)", overlay, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveCurrentSqlTenantCurrencyAsync", projection, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubCurrencyEventReplay_MustNotRewriteMarketplaceDocumentsToTenantCurrency()
    {
        var projection = ReadRepoFile("PointOfSale.Infrastructure/Consumers/CommerceHubProjectionConsumer.cs");

        Assert.Contains("private static string ResolveCurrency()", projection, StringComparison.Ordinal);
        Assert.Contains("var normalizedCurrency = ResolveCurrency();", projection, StringComparison.Ordinal);
        Assert.Contains(".Set(x => x.Currency, normalizedCurrency)", projection, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrencyNormalizationHelper.NormalizeCurrencyOrThrow", projection, StringComparison.Ordinal);
        Assert.DoesNotContain("storefrontConfig?.Currency, existing?.Currency", projection, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubFinancialPaths_MustKeepEgpOnlyGuardrails()
    {
        var currencyGuard = ReadRepoFile("PointOfSale.Core/Financial/FinancialCurrencyGuard.cs");
        var vendorPayments = ReadRepoFile("PointOfSale.API/Services/CommerceHub/Finance/CommerceHubVendorPaymentsService.cs");
        var vendorFinancialControl = ReadRepoFile("PointOfSale.API/Services/CommerceHub/Finance/CommerceHubVendorFinancialControlService.cs");
        var vendorPayoutRead = ReadRepoFile("PointOfSale.API/Services/CommerceHub/Finance/CommerceHubVendorPayoutReadService.cs");
        var superAdminPayouts = ReadRepoFile("PointOfSale.API/SuperAdmin/Services/SuperAdminPayoutsService.cs");

        Assert.Contains("public const string PlatformBaseCurrencyCode = \"EGP\";", currencyGuard, StringComparison.Ordinal);
        Assert.Contains("RequireEgpCurrency", currencyGuard, StringComparison.Ordinal);
        Assert.Contains("FinancialCurrencyGuard.RequireEgpCurrency", vendorPayments, StringComparison.Ordinal);
        Assert.Contains("NormalizeCurrency(FinancialCurrencyGuard.PlatformBaseCurrencyCode)", vendorPayments, StringComparison.Ordinal);
        Assert.DoesNotContain("NormalizeCurrency(vendor.Currency)", vendorPayments, StringComparison.Ordinal);
        Assert.Contains("Only EGP is supported for CommerceHub vendor repayments.", vendorPayments, StringComparison.Ordinal);
        Assert.Contains("private const string DefaultCurrency = FinancialCurrencyGuard.PlatformBaseCurrencyCode;", vendorFinancialControl, StringComparison.Ordinal);
        Assert.Contains("FinancialCurrencyGuard.RequireEgpCurrency", vendorFinancialControl, StringComparison.Ordinal);
        Assert.DoesNotContain("currency ?? vendor.Currency", vendorFinancialControl, StringComparison.Ordinal);
        Assert.Contains("Currency = FinancialCurrencyGuard.PlatformBaseCurrencyCode", vendorPayoutRead, StringComparison.Ordinal);
        Assert.DoesNotContain("Currency = tenant.Currency", vendorPayoutRead, StringComparison.Ordinal);
        Assert.Contains("FinancialCurrencyGuard.RequireEgpCurrency", superAdminPayouts, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubCurrencyZone_MustEnforceFailClosedBoundaryAtRuntime()
    {
        var handlers = ReadRepoFile("PointOfSale.API/CommerceHub/Handlers/CommerceHubCommandHandlers.cs");
        var controller = ReadRepoFile("PointOfSale.API/Controllers/AdminCommerceHubController.cs");
        var cart = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCartService.cs");
        var checkout = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");
        var overlay = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchSqlAuthorityOverlay.cs");

        // Assert PublishToCommerceHubHandler and UpdateCommerceHubListingHandler fail-closed currency checks
        Assert.Contains("CommerceHubPublicationCurrencyPolicy.EnsureEgpPricedProduct(tenant.Currency)", handlers, StringComparison.Ordinal);
        Assert.Contains("CommerceHub currently supports EGP products only.", handlers, StringComparison.Ordinal);

        // Assert bulk operations check per row instead of request-wide tenant rejection
        Assert.Contains("CommerceHubPublicationCurrencyPolicy.EnsureEgpPricedProduct(productCurrency)", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("Only EGP-denominated tenants are supported for CommerceHub operations.", controller, StringComparison.Ordinal);

        // Assert cart service EGP currency zone checks
        Assert.Contains("STORE_CURRENCY_INVALID", cart, StringComparison.Ordinal);

        // Assert checkout service EGP currency zone checks
        Assert.Contains("STORE_CURRENCY_INVALID", checkout, StringComparison.Ordinal);

        // Assert Elasticsearch overlay EGP currency boundary check
        Assert.Contains("tenant.Currency != FinancialCurrencyGuard.PlatformBaseCurrencyCode", overlay, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
