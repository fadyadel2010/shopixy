namespace PointOfSale.Tests.Architecture;

public sealed class FrontendCurrencyAuthorityCertificationTests
{
    [Fact]
    public void CommerceHubPublicationFrontend_MustFailClosedWhenCurrencyEligibilityIsMissing()
    {
        var settingsPage = CertificationSource.ReadRepoFile("PointOfSale.Web/src/pages/CommerceHubSettings.jsx");

        Assert.Contains("variant.isCommerceHubCurrencyEligible !== true", settingsPage, StringComparison.Ordinal);
        Assert.Contains("formatProductCurrencyPrice(formatCurrency, variant.price || 0, variant.productCurrency)", settingsPage, StringComparison.Ordinal);
        Assert.Contains("return 'Currency unavailable'", settingsPage, StringComparison.Ordinal);
        Assert.DoesNotContain("variant.productCurrency || 'EGP'", settingsPage, StringComparison.Ordinal);
        Assert.DoesNotContain("variant.isCommerceHubCurrencyEligible === false", settingsPage, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubCustomerAndLegacyPublicFrontend_MustNotInjectFallbackCurrencyLabels()
    {
        var commerceHubCurrencyAuthority = CertificationSource.ReadRepoFile(
            "PointOfSale.CommerceHub/src/utils/commerceHubCurrencyAuthority.ts");
        var webCurrencyAuthority = CertificationSource.ReadRepoFile(
            "PointOfSale.Web/src/utils/commerceHubCurrencyAuthority.js");
        var productDetails = CertificationSource.ReadRepoFile(
            "PointOfSale.CommerceHub/src/pages/ProductDetailsPage.tsx");
        var payments = CertificationSource.ReadRepoFile(
            "PointOfSale.Web/src/pages/CommerceHubPayments.jsx");
        var earnings = CertificationSource.ReadRepoFile(
            "PointOfSale.Web/src/pages/CommerceHubEarnings.jsx");
        var marketplaceProduct = CertificationSource.ReadRepoFile(
            "PointOfSale.Web/src/marketplace-cms/MarketplaceProductPublicPage.jsx");
        var marketplaceProducts = CertificationSource.ReadRepoFile(
            "PointOfSale.Web/src/marketplace-cms/MarketplaceProductsPublicPage.jsx");

        Assert.Contains("normalizeCommerceHubCurrency(currency) === COMMERCEHUB_AUTHORITY_CURRENCY", commerceHubCurrencyAuthority, StringComparison.Ordinal);
        Assert.DoesNotContain("currency || COMMERCEHUB_AUTHORITY_CURRENCY", commerceHubCurrencyAuthority, StringComparison.Ordinal);
        Assert.Contains("normalizeCommerceHubCurrency(currency) === COMMERCEHUB_AUTHORITY_CURRENCY", webCurrencyAuthority, StringComparison.Ordinal);
        Assert.DoesNotContain("currency || COMMERCEHUB_AUTHORITY_CURRENCY", webCurrencyAuthority, StringComparison.Ordinal);

        Assert.Contains("formatCurrency(selectedVariant.effectivePrice, product?.currency)", productDetails, StringComparison.Ordinal);
        Assert.Contains("`From ${formatCurrency(startingPrice, product?.currency)}`", productDetails, StringComparison.Ordinal);
        Assert.DoesNotContain("product?.currency ?? \"EGP\"", productDetails, StringComparison.Ordinal);

        Assert.Contains("function formatCurrency(value, currency)", payments, StringComparison.Ordinal);
        Assert.Contains("resolveCommerceHubDisplayCurrency(currency)", payments, StringComparison.Ordinal);
        Assert.DoesNotContain("dashboard?.currency || 'EGP'", payments, StringComparison.Ordinal);

        Assert.Contains("function formatCurrency(value, currency)", earnings, StringComparison.Ordinal);
        Assert.Contains("resolveCommerceHubDisplayCurrency(currency)", earnings, StringComparison.Ordinal);
        Assert.Contains("const currency = dashboard?.currency", earnings, StringComparison.Ordinal);
        Assert.DoesNotContain("dashboard?.currency || 'EGP'", earnings, StringComparison.Ordinal);

        foreach (var source in new[] { marketplaceProduct, marketplaceProducts })
        {
            Assert.Contains("formatBackendCurrency(formatCurrency", source, StringComparison.Ordinal);
            Assert.Contains("Currency unavailable", source, StringComparison.Ordinal);
            Assert.DoesNotContain("product.currency || 'EGP'", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AffiliateProductFrontend_MustNotInjectFallbackCurrencyLabels()
    {
        var productCard = CertificationSource.ReadRepoFile(
            "PointOfSale.Web/src/modules/affiliate/pages/portal/ProductCard.jsx");
        var productDrawer = CertificationSource.ReadRepoFile(
            "PointOfSale.Web/src/modules/affiliate/pages/portal/ProductDrawer.jsx");
        var affiliateLanding = CertificationSource.ReadRepoFile(
            "PointOfSale.Web/src/modules/affiliate/pages/AffiliateLanding.jsx");
        var storefrontLanding = CertificationSource.ReadRepoFile(
            "PointOfSale.Storefront/src/pages/AffiliateLandingPage.tsx");

        foreach (var source in new[] { productCard, productDrawer, affiliateLanding })
        {
            Assert.Contains("resolveProductCurrency", source, StringComparison.Ordinal);
            Assert.Contains("Currency unavailable", source, StringComparison.Ordinal);
            Assert.DoesNotContain("product?.currency || 'EGP'", source, StringComparison.Ordinal);
            Assert.DoesNotContain("product.currency || 'EGP'", source, StringComparison.Ordinal);
        }

        Assert.Contains("data?.product.currency || data?.tenant.currency || '').trim().toUpperCase()", storefrontLanding, StringComparison.Ordinal);
        Assert.Contains("return 'Currency unavailable';", storefrontLanding, StringComparison.Ordinal);
        Assert.DoesNotContain("|| 'USD'", storefrontLanding, StringComparison.Ordinal);
        Assert.DoesNotContain("`${currency} ${price.toFixed(2)}`", storefrontLanding, StringComparison.Ordinal);
    }

    [Fact]
    public void PosWishlistAnalytics_MustUseBackendTenantCurrency()
    {
        var wishlistService = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/Storefront/StorefrontWishlistService.cs");
        var wishlistCard = CertificationSource.ReadRepoFile(
            "PointOfSale.Web/src/components/WishlistAnalyticsCard.jsx");

        Assert.Contains("public string Currency { get; set; } = \"\";", wishlistService, StringComparison.Ordinal);
        Assert.Contains(".Select(t => t.Currency ?? \"\")", wishlistService, StringComparison.Ordinal);
        Assert.Contains("Currency = tenantCurrency", wishlistService, StringComparison.Ordinal);

        Assert.Contains("const { formatCurrency } = useSettings()", wishlistCard, StringComparison.Ordinal);
        Assert.Contains("formatCurrency(product.price, currency)", wishlistCard, StringComparison.Ordinal);
        Assert.Contains("Currency unavailable", wishlistCard, StringComparison.Ordinal);
        Assert.DoesNotContain("currency: 'USD'", wishlistCard, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationDocumentation_MustRecordPassF5FrontendCurrencyAuthority()
    {
        var certification = CertificationSource.ReadRepoFile(
            "docs/architecture/frontend-currency-authority-certification-findings.md");

        foreach (var marker in new[]
        {
            "PASS F5",
            "## CommerceHub Findings",
            "## Publication Findings",
            "## Affiliate Findings",
            "## Financial Findings",
            "## Cache Findings",
            "## DevTools Findings",
            "## Legacy DTO Findings",
            "## Runtime Hardening Performed",
            "## Regression Freezes Added",
            "## Required Answers"
        })
        {
            Assert.Contains(marker, certification, StringComparison.Ordinal);
        }
    }
}
