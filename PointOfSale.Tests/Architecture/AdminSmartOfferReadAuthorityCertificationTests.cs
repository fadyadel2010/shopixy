namespace PointOfSale.Tests.Architecture;

public sealed class AdminSmartOfferReadAuthorityCertificationTests
{
    [Fact]
    public void AdminSmartOfferReadRuntime_MustRemainBackendOwnedAndTenantScoped()
    {
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AdminSmartOffersController.cs");
        var dto = CertificationSource.ReadRepoFile("PointOfSale.API/DTOs/SmartOffers/SmartOfferDtos.cs");
        var page = CertificationSource.ReadRepoFile("PointOfSale.Web/src/pages/SmartOffersPage.jsx");
        var api = CertificationSource.ReadRepoFile("PointOfSale.Web/src/services/adminSmartOffersApi.js");
        var authority = CertificationSource.ReadRepoFile("PointOfSale.Web/src/auth/adminSmartOfferReadAuthority.js");

        Assert.Contains("[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]", controller, StringComparison.Ordinal);
        Assert.Contains("Where(x => x.TenantId == tenantId)", controller, StringComparison.Ordinal);
        Assert.Contains("AsNoTracking()", controller, StringComparison.Ordinal);
        Assert.Contains("ApplySmartOfferReadSort(query, sortBy, sortDirection)", controller, StringComparison.Ordinal);
        Assert.Contains("ResolveTenantCurrencyAsync(tenantId, cancellationToken)", controller, StringComparison.Ordinal);
        Assert.Contains("ResolveEligibilitySummary(entity, generatedCoupon)", controller, StringComparison.Ordinal);

        Assert.Contains("public Guid TenantId { get; set; }", dto, StringComparison.Ordinal);
        Assert.Contains("public string Currency { get; set; }", dto, StringComparison.Ordinal);
        Assert.Contains("public int UsageCount { get; set; }", dto, StringComparison.Ordinal);
        Assert.Contains("public string EligibilitySummary { get; set; }", dto, StringComparison.Ordinal);

        Assert.Contains("listAdminSmartOffers(filter, user)", page, StringComparison.Ordinal);
        Assert.Contains("getAdminSmartOffer(offerId, user)", page, StringComparison.Ordinal);
        Assert.Contains("getAdminSmartOfferStats(user)", page, StringComparison.Ordinal);
        Assert.Contains("setOffers(admittedOffers)", page, StringComparison.Ordinal);

        Assert.Contains("'Cache-Control': 'no-store'", api, StringComparison.Ordinal);
        Assert.Contains("requireAdminSmartOfferListReadAuthority(response.data, currentUser)", api, StringComparison.Ordinal);
        Assert.Contains("requireAdminSmartOfferReadAuthority(response.data, currentUser, offerId)", api, StringComparison.Ordinal);
        Assert.Contains("tenantId.toLowerCase() !== userTenantId.toLowerCase()", authority, StringComparison.Ordinal);
        Assert.Contains("Smart Offer read authority rejected duplicate list evidence", authority, StringComparison.Ordinal);
    }

    [Fact]
    public void AdminSmartOfferReadAuthorityDocumentation_MustBeFrozen()
    {
        var findings = CertificationSource.ReadRepoFile("docs/architecture/admin-smart-offer-read-authority-certification-findings.md");
        var projectContext = CertificationSource.ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = CertificationSource.ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = CertificationSource.ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = CertificationSource.ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = CertificationSource.ReadRepoFile(".ai-context/CODEX_RULES.md");

        Assert.Contains("PASS SF-SMO.READ Admin Smart Offer Read Authority", findings, StringComparison.Ordinal);
        Assert.Contains("PASS SF-SMO.READ Admin Smart Offer Read Authority", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS SF-SMO.READ Admin Smart Offer Read Authority Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS SF-SMO.READ", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS SF-SMO.READ Admin Smart Offer Read Authority", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS SF-SMO.READ Admin Smart Offer Read Authority Freeze", codexRules, StringComparison.Ordinal);
    }
}
