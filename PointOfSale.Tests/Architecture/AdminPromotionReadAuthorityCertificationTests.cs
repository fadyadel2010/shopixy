namespace PointOfSale.Tests.Architecture;

public sealed class AdminPromotionReadAuthorityCertificationTests
{
    [Fact]
    public void AdminPromotionReadRuntime_MustRemainBackendOwnedAndTenantScoped()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/PromotionsController.cs");
        var page = ReadRepoFile("PointOfSale.Web/src/pages/Promotions.jsx");
        var api = ReadRepoFile("PointOfSale.Web/src/services/adminPromotionsApi.js");
        var authority = ReadRepoFile("PointOfSale.Web/src/auth/adminPromotionReadAuthority.js");

        Assert.Contains("[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]", controller, StringComparison.Ordinal);
        Assert.Contains("Where(p => p.TenantId == tenantId && !p.IsDeleted)", controller, StringComparison.Ordinal);
        Assert.Contains("AsNoTracking()", controller, StringComparison.Ordinal);
        Assert.Contains("ApplyPromotionReadSort(sortBy, sortDirection)", controller, StringComparison.Ordinal);
        Assert.Contains("ResolveTenantCurrencyAsync(tenantId)", controller, StringComparison.Ordinal);
        Assert.Contains("EligibilitySummary = ResolveEligibilitySummary(promotion)", controller, StringComparison.Ordinal);

        Assert.Contains("listAdminPromotions(filter, user)", page, StringComparison.Ordinal);
        Assert.Contains("getAdminPromotion(promotionId, user)", page, StringComparison.Ordinal);
        Assert.Contains("getAdminPromotionStats(user)", page, StringComparison.Ordinal);
        Assert.Contains("formatCurrency(promotion.discountValue, promotion.currency)", page, StringComparison.Ordinal);

        Assert.Contains("'Cache-Control': 'no-store'", api, StringComparison.Ordinal);
        Assert.Contains("requireAdminPromotionListReadAuthority(response.data, currentUser)", api, StringComparison.Ordinal);
        Assert.Contains("requireAdminPromotionReadAuthority(response.data, currentUser, promotionId)", api, StringComparison.Ordinal);
        Assert.Contains("tenantId.toLowerCase() !== userTenantId.toLowerCase()", authority, StringComparison.Ordinal);
        Assert.Contains("Promotion read authority rejected duplicate promotion list evidence", authority, StringComparison.Ordinal);
    }

    [Fact]
    public void AdminPromotionReadAuthorityDocumentation_MustBeFrozen()
    {
        var findings = ReadRepoFile("docs/architecture/admin-promotion-read-authority-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        Assert.Contains("PASS SF-PROMO.READ Admin Promotion Read Authority", findings, StringComparison.Ordinal);
        Assert.Contains("PASS SF-PROMO.READ Admin Promotion Read Authority", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS SF-PROMO.READ Admin Promotion Read Authority Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS SF-PROMO.READ", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS SF-PROMO.READ Admin Promotion Read Authority", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS SF-PROMO.READ Admin Promotion Read Authority Freeze", codexRules, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
