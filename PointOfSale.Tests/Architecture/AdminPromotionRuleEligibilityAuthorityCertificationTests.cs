namespace PointOfSale.Tests.Architecture;

public sealed class AdminPromotionRuleEligibilityAuthorityCertificationTests
{
    [Fact]
    public void AdminPromotionRuleEligibilityRuntime_MustValidateAndCanonicalizeOnBackend()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/PromotionsController.cs");
        var page = ReadRepoFile("PointOfSale.Web/src/pages/Promotions.jsx");
        var api = ReadRepoFile("PointOfSale.Web/src/services/adminPromotionsApi.js");
        var admission = ReadRepoFile("PointOfSale.Web/src/auth/adminPromotionReadAuthority.js");
        var entity = ReadRepoFile("PointOfSale.Core/Entities/Promotion.cs");
        var scope = ReadRepoFile("PointOfSale.Core/Enums/PromotionScope.cs");

        Assert.Contains("ValidatePromotionRuleAdmissionAsync(tenantId, dto", controller, StringComparison.Ordinal);
        Assert.Contains("Promotion product target must be an active product in the current tenant", controller, StringComparison.Ordinal);
        Assert.Contains("Promotion category target must be a category in the current tenant", controller, StringComparison.Ordinal);
        Assert.Contains("Cart promotions cannot target a product or category", controller, StringComparison.Ordinal);
        Assert.Contains("p.TenantId == tenantId", controller, StringComparison.Ordinal);
        Assert.Contains("!p.IsDeleted", controller, StringComparison.Ordinal);
        Assert.Contains("p.IsActive", controller, StringComparison.Ordinal);
        Assert.Contains("c.TenantId == tenantId", controller, StringComparison.Ordinal);
        Assert.Contains("!c.IsDeleted", controller, StringComparison.Ordinal);
        Assert.Contains("Promotion scope is not supported", controller, StringComparison.Ordinal);
        Assert.Contains("Product = 0", scope, StringComparison.Ordinal);
        Assert.Contains("Category = 1", scope, StringComparison.Ordinal);
        Assert.Contains("Cart = 2", scope, StringComparison.Ordinal);
        Assert.Contains("public Guid? AppliesToId", entity, StringComparison.Ordinal);

        Assert.Contains("createAdminPromotion(data, user)", page, StringComparison.Ordinal);
        Assert.Contains("updateAdminPromotion(editingPromotion, data, user)", page, StringComparison.Ordinal);
        Assert.Contains("requireAdminPromotionReadAuthority(response.data, currentUser)", api, StringComparison.Ordinal);
        Assert.Contains("SCOPES = new Set(['Product', 'Category', 'Cart', 0, 1, 2])", admission, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sessionStorage", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("zustand", page, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AdminPromotionRuleEligibilityAuthorityDocumentation_MustBeFrozen()
    {
        var findings = ReadRepoFile("docs/architecture/admin-promotion-rule-eligibility-authority-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        Assert.Contains("PASS SF-PROMO.RULE Admin Promotion Rule Eligibility Authority", findings, StringComparison.Ordinal);
        Assert.Contains("PASS SF-PROMO.RULE Admin Promotion Rule Eligibility Authority", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS SF-PROMO.RULE Admin Promotion Rule Eligibility Authority Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS SF-PROMO.RULE", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS SF-PROMO.RULE Admin Promotion Rule Eligibility Authority", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS SF-PROMO.RULE Admin Promotion Rule Eligibility Authority Freeze", codexRules, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
