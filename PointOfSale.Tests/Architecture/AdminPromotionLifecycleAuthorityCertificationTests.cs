namespace PointOfSale.Tests.Architecture;

public sealed class AdminPromotionLifecycleAuthorityCertificationTests
{
    [Fact]
    public void AdminPromotionLifecycleMutations_MustRequireBackendRowVersionAndIfMatch()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/PromotionsController.cs");
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");
        var entity = ReadRepoFile("PointOfSale.Core/Entities/Promotion.cs");

        Assert.Contains("public uint RowVersion", entity, StringComparison.Ordinal);
        Assert.Contains("public string RowVersion", controller, StringComparison.Ordinal);
        Assert.Contains("RowVersion = GetPromotionRowVersion(promotion).ToString", controller, StringComparison.Ordinal);
        Assert.Contains("ParseRequiredPromotionIfMatch", controller, StringComparison.Ordinal);
        Assert.Contains("Promotion lifecycle mutations require current RowVersion evidence", controller, StringComparison.Ordinal);
        Assert.Contains("If-Match must contain the current promotion RowVersion", controller, StringComparison.Ordinal);
        Assert.Contains("currentRowVersion != expectedRowVersion.RowVersion", controller, StringComparison.Ordinal);
        Assert.Contains("Property(p => p.RowVersion).OriginalValue = expectedRowVersion.RowVersion.Value", controller, StringComparison.Ordinal);
        Assert.Contains("LoadPromotionForLifecycleMutationAsync", controller, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", controller, StringComparison.Ordinal);
        Assert.Contains("DbUpdateConcurrencyException", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("_dbContext.Promotions.FindAsync(id)", controller, StringComparison.Ordinal);
        Assert.Contains("p.TenantId == tenantId && !p.IsDeleted", controller, StringComparison.Ordinal);
        Assert.Contains("entity.Property(p => p.RowVersion)", dbContext, StringComparison.Ordinal);
        Assert.Contains("ChangeTracker.Entries<Promotion>()", dbContext, StringComparison.Ordinal);
    }

    [Fact]
    public void AdminPromotionFrontend_MustSubmitLifecycleIntentOnlyWithAcceptedRowVersion()
    {
        var page = ReadRepoFile("PointOfSale.Web/src/pages/Promotions.jsx");
        var service = ReadRepoFile("PointOfSale.Web/src/services/adminPromotionsApi.js");
        var admission = ReadRepoFile("PointOfSale.Web/src/auth/adminPromotionReadAuthority.js");

        Assert.Contains("!rowVersion", admission, StringComparison.Ordinal);
        Assert.Contains("Number(rowVersion) <= 0", admission, StringComparison.Ordinal);
        Assert.Contains("mutationOptionsFromPromotion", service, StringComparison.Ordinal);
        Assert.Contains("Promotion lifecycle mutation requires accepted backend RowVersion evidence", service, StringComparison.Ordinal);
        Assert.Contains("'If-Match': `\"${rowVersion}\"`", service, StringComparison.Ordinal);
        Assert.Contains("createAdminPromotion(data, user)", page, StringComparison.Ordinal);
        Assert.Contains("updateAdminPromotion(editingPromotion, data, user)", page, StringComparison.Ordinal);
        Assert.Contains("toggleAdminPromotion(promotion, user)", page, StringComparison.Ordinal);
        Assert.Contains("archiveAdminPromotion(promotion)", page, StringComparison.Ordinal);
        Assert.DoesNotContain("api.put(`/promotions/${editingPromotion.id}`", page, StringComparison.Ordinal);
        Assert.DoesNotContain("api.patch(`/promotions/${id}/toggle`", page, StringComparison.Ordinal);
        Assert.DoesNotContain("api.delete(`/promotions/${id}`", page, StringComparison.Ordinal);
    }

    [Fact]
    public void AdminPromotionLifecycleAuthorityDocumentation_MustBeFrozen()
    {
        var findings = ReadRepoFile("docs/architecture/admin-promotion-lifecycle-authority-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        Assert.Contains("PASS SF-PROMO.LIFECYCLE Admin Promotion Lifecycle Authority", findings, StringComparison.Ordinal);
        Assert.Contains("PASS SF-PROMO.LIFECYCLE Admin Promotion Lifecycle Authority", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS SF-PROMO.LIFECYCLE Admin Promotion Lifecycle Authority Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS SF-PROMO.LIFECYCLE", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS SF-PROMO.LIFECYCLE Admin Promotion Lifecycle Authority", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS SF-PROMO.LIFECYCLE Admin Promotion Lifecycle Authority Freeze", codexRules, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
