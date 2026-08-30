namespace PointOfSale.Tests.Architecture;

public sealed class AffiliateFrontendAuthorityCertificationTests
{
    [Fact]
    public void ReferralLinkFrontend_MustCopyBackendIssuedPromotionUrlsOnly()
    {
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AffiliatePromotionLinksController.cs");
        var linksTab = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/affiliate/pages/portal/LinksTab.jsx");
        var productDrawer = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/affiliate/pages/portal/ProductDrawer.jsx");
        var legacyDiscoverTab = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/affiliate/pages/portal/DiscoverTab.jsx");

        Assert.Contains("public string? PromotionUrl { get; set; }", controller, StringComparison.Ordinal);
        Assert.Contains("[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]", controller, StringComparison.Ordinal);
        Assert.Contains("MapToResponseAsync", controller, StringComparison.Ordinal);
        Assert.Contains("_urlResolver.Resolve(tenant).TrimEnd('/')", controller, StringComparison.Ordinal);
        Assert.Contains("p.Id == link.ProductId && p.TenantId == link.TenantId && !p.IsDeleted", controller, StringComparison.Ordinal);
        Assert.Contains("PromotionUrl = promotionUrl", controller, StringComparison.Ordinal);

        Assert.Contains("resolveBackendPromotionUrl", linksTab, StringComparison.Ordinal);
        Assert.Contains("affiliateApi.listPromotionLinks()", linksTab, StringComparison.Ordinal);
        Assert.Contains("affiliateApi.deactivatePromotionLink(link.id)", linksTab, StringComparison.Ordinal);
        Assert.Contains("typeof link.promotionUrl !== 'string'", linksTab, StringComparison.Ordinal);
        Assert.Contains("Copy only the backend-issued URL", linksTab, StringComparison.Ordinal);
        Assert.DoesNotContain("`${baseUrl}${productPath}?ref=${link.referralCode}`", linksTab, StringComparison.Ordinal);

        Assert.Contains("backend-issued promotionUrl", productDrawer, StringComparison.Ordinal);
        Assert.Contains("promoLink?.promotionUrl", productDrawer, StringComparison.Ordinal);
        Assert.DoesNotContain("promoLink?.referralCode || referralCode", productDrawer, StringComparison.Ordinal);
        Assert.DoesNotContain("`${product.storefrontUrl}?ref=${code}`", productDrawer, StringComparison.Ordinal);

        Assert.Contains("affiliateApi.createPromotionLink(product.tenantId, product.productId)", legacyDiscoverTab, StringComparison.Ordinal);
        Assert.Contains("typeof link?.promotionUrl === 'string'", legacyDiscoverTab, StringComparison.Ordinal);
        Assert.DoesNotContain("`${storefrontBaseUrl}/products/${product.slug}?ref=${referralCode}`", legacyDiscoverTab, StringComparison.Ordinal);
    }

    [Fact]
    public void AffiliatePromotionLinkGeneration_MustRemainBackendOwnedAndNoStore()
    {
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AffiliatePromotionLinksController.cs");
        var affiliateApi = CertificationSource.ReadRepoFile("PointOfSale.Web/src/services/affiliateApi.js");
        var linksTab = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/affiliate/pages/portal/LinksTab.jsx");
        var discoverTab = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/affiliate/pages/portal/DiscoverTab.jsx");
        var productDrawer = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/affiliate/pages/portal/ProductDrawer.jsx");

        Assert.Contains("AffiliatePromotionLink.Create(", controller, StringComparison.Ordinal);
        Assert.Contains("GenerateUniqueReferralCodeAsync(ct)", controller, StringComparison.Ordinal);
        Assert.Contains("PromotionUrl = promotionUrl", controller, StringComparison.Ordinal);
        Assert.Contains("MapToResponseAsync(link, requireCurrentSelectionAdmission: true, ct)", controller, StringComparison.Ordinal);
        Assert.Contains("[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]", controller, StringComparison.Ordinal);

        Assert.Contains("createPromotionLink: (tenantId, productId)", affiliateApi, StringComparison.Ordinal);
        Assert.Contains("affiliateAxios.post('/affiliate/promotion-links', { tenantId, productId }, affiliateAuthorityReadConfig)", affiliateApi, StringComparison.Ordinal);
        Assert.Contains("listPromotionLinks: ()", affiliateApi, StringComparison.Ordinal);
        Assert.Contains("affiliateAxios.get('/affiliate/promotion-links', affiliateAuthorityReadConfig)", affiliateApi, StringComparison.Ordinal);
        Assert.Contains("deactivatePromotionLink: (promotionLinkId)", affiliateApi, StringComparison.Ordinal);
        Assert.Contains("affiliateAxios.delete(`/affiliate/promotion-links/${promotionLinkId}`, affiliateAuthorityReadConfig)", affiliateApi, StringComparison.Ordinal);

        Assert.DoesNotContain("?ref=${", linksTab, StringComparison.Ordinal);
        Assert.DoesNotContain("?ref=${", discoverTab, StringComparison.Ordinal);
        Assert.DoesNotContain("?ref=${", productDrawer, StringComparison.Ordinal);
        Assert.Contains("promoLink?.promotionUrl", productDrawer, StringComparison.Ordinal);
        Assert.Contains("typeof link?.promotionUrl === 'string'", discoverTab, StringComparison.Ordinal);
    }

    [Fact]
    public void AffiliateFinancialFrontend_MustUseBackendReadsAndBackendPayoutSubmission()
    {
        var affiliateApi = CertificationSource.ReadRepoFile("PointOfSale.Web/src/services/affiliateApi.js");
        var payoutsTab = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/affiliate/pages/portal/PayoutsTab.jsx");
        var payoutService = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliatePayoutService.cs");

        Assert.Contains("affiliateAxios.get('/affiliate/dashboard', affiliateAuthorityReadConfig)", affiliateApi, StringComparison.Ordinal);
        Assert.Contains("affiliateAxios.get('/affiliate/orders'", affiliateApi, StringComparison.Ordinal);
        Assert.Contains("affiliateAxios.get('/affiliate/earnings-summary', affiliateAuthorityReadConfig)", affiliateApi, StringComparison.Ordinal);
        Assert.Contains("affiliateAxios.get('/affiliate/payouts'", affiliateApi, StringComparison.Ordinal);
        Assert.Contains("affiliateAxios.post('/affiliate/payouts/request', { amount }, affiliateAuthorityReadConfig)", affiliateApi, StringComparison.Ordinal);

        Assert.Contains("await affiliateApi.requestPayout(numAmount)", payoutsTab, StringComparison.Ordinal);
        Assert.Contains("await loadPayouts(1)", payoutsTab, StringComparison.Ordinal);
        Assert.DoesNotContain("setData({", payoutsTab, StringComparison.Ordinal);

        Assert.Contains("var rawAvailable =", payoutService, StringComparison.Ordinal);
        Assert.Contains("Guards MUST use RawAvailable to prevent over-withdrawal.", payoutService, StringComparison.Ordinal);
    }

    [Fact]
    public void AffiliateFrontendSessionCache_MustRemainProfileCacheOnly()
    {
        var authApi = CertificationSource.ReadRepoFile("PointOfSale.Web/src/services/affiliateAuthApi.js");
        var portal = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/affiliate/pages/AffiliatePortal.jsx");

        Assert.Contains("Uses HttpOnly cookies for session tokens", authApi, StringComparison.Ordinal);
        Assert.Contains("JavaScript only keeps non-sensitive profile metadata", authApi, StringComparison.Ordinal);
        Assert.Contains("LEGACY_TOKEN_KEYS", authApi, StringComparison.Ordinal);
        Assert.Contains("register: async (data) =>", authApi, StringComparison.Ordinal);
        Assert.DoesNotContain("register: async (data) => {\r\n        const response = await affiliateAxios.post('/affiliate/auth/register', data)\r\n        cacheAffiliateProfile(response.data)", authApi, StringComparison.Ordinal);

        Assert.Contains("affiliateAuthApi.restoreSession()", portal, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_SESSION_REVALIDATION_INTERVAL_MS", portal, StringComparison.Ordinal);
        Assert.Contains("window.addEventListener('storage', handleStorage)", portal, StringComparison.Ordinal);
        Assert.Contains("navigate('/affiliate/login', { replace: true })", portal, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationDocumentation_MustRecordPassF3FrontendAffiliateAuthority()
    {
        var certification = CertificationSource.ReadRepoFile("docs/architecture/frontend-affiliate-authority-certification-findings.md");

        foreach (var marker in new[]
        {
            "PASS F3",
            "## Attribution Findings",
            "## Referral-Link Findings",
            "## Commission Findings",
            "## Balance Findings",
            "## Withdrawal Findings",
            "## Payment-Method Findings",
            "## POS-Admin Findings",
            "## Cache Findings",
            "## DevTools Findings",
            "## Cross-Domain Findings",
            "## Runtime Hardening Performed",
            "## Regression Freezes Added",
            "## Architecture Updates",
            "## Remaining Debt",
            "## Required Answers"
        })
        {
            Assert.Contains(marker, certification, StringComparison.Ordinal);
        }
    }
}
