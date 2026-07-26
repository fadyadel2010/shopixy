namespace PointOfSale.Tests.Architecture;

public sealed class AdminSmartOfferLifecycleAuthorityCertificationTests
{
    [Fact]
    public void AdminSmartOfferLifecycleMutations_MustRequireBackendRowVersionAndIfMatch()
    {
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AdminSmartOffersController.cs");
        var dto = CertificationSource.ReadRepoFile("PointOfSale.API/DTOs/SmartOffers/SmartOfferDtos.cs");
        var entity = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/SmartOffer.cs");
        var configuration = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/SmartOfferConfiguration.cs");
        var dbContext = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        Assert.Contains("public uint RowVersion", entity, StringComparison.Ordinal);
        Assert.Contains("public string RowVersion", dto, StringComparison.Ordinal);
        Assert.Contains("RowVersion = GetSmartOfferRowVersion(entity).ToString", controller, StringComparison.Ordinal);
        Assert.Contains("ParseRequiredSmartOfferIfMatch", controller, StringComparison.Ordinal);
        Assert.Contains("Smart Offer lifecycle mutations require current RowVersion evidence", controller, StringComparison.Ordinal);
        Assert.Contains("If-Match must contain the current smart offer RowVersion", controller, StringComparison.Ordinal);
        Assert.Contains("currentRowVersion != expectedRowVersion.RowVersion", controller, StringComparison.Ordinal);
        Assert.Contains("Property(x => x.RowVersion).OriginalValue = expectedRowVersion.RowVersion.Value", controller, StringComparison.Ordinal);
        Assert.Contains("LoadSmartOfferForLifecycleMutationAsync", controller, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", controller, StringComparison.Ordinal);
        Assert.Contains("DbUpdateConcurrencyException", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("_dbContext.SmartOffers.FirstOrDefaultAsync(x => x.Id == id", controller, StringComparison.Ordinal);
        Assert.Contains("x.TenantId == tenantId", controller, StringComparison.Ordinal);
        Assert.Contains("builder.Property(x => x.RowVersion)", configuration, StringComparison.Ordinal);
        Assert.Contains("ChangeTracker.Entries<SmartOffer>()", dbContext, StringComparison.Ordinal);
    }

    [Fact]
    public void AdminSmartOfferFrontend_MustSubmitLifecycleIntentOnlyWithAcceptedRowVersion()
    {
        var page = CertificationSource.ReadRepoFile("PointOfSale.Web/src/pages/SmartOffersPage.jsx");
        var service = CertificationSource.ReadRepoFile("PointOfSale.Web/src/services/adminSmartOffersApi.js");
        var admission = CertificationSource.ReadRepoFile("PointOfSale.Web/src/auth/adminSmartOfferReadAuthority.js");

        Assert.Contains("!rowVersion", admission, StringComparison.Ordinal);
        Assert.Contains("Number(rowVersion) <= 0", admission, StringComparison.Ordinal);
        Assert.Contains("mutationOptionsFromSmartOffer", service, StringComparison.Ordinal);
        Assert.Contains("Smart Offer lifecycle mutation requires accepted backend RowVersion evidence", service, StringComparison.Ordinal);
        Assert.Contains("'If-Match': `\"${rowVersion}\"`", service, StringComparison.Ordinal);
        Assert.Contains("createAdminSmartOffer(payload, user)", page, StringComparison.Ordinal);
        Assert.Contains("updateAdminSmartOffer(editingOffer, payload, user)", page, StringComparison.Ordinal);
        Assert.Contains("updateAdminSmartOffer(offer, {", page, StringComparison.Ordinal);
        Assert.Contains("archiveAdminSmartOffer(offer)", page, StringComparison.Ordinal);
        Assert.DoesNotContain("api.put(`/admin/smart-offers/${editingOffer.id}`", page, StringComparison.Ordinal);
        Assert.DoesNotContain("api.put(`/admin/smart-offers/${offer.id}`", page, StringComparison.Ordinal);
        Assert.DoesNotContain("api.delete(`/admin/smart-offers/${offer.id}`", page, StringComparison.Ordinal);
    }

    [Fact]
    public void AdminSmartOfferLifecycleAuthorityDocumentation_MustBeFrozen()
    {
        var findings = CertificationSource.ReadRepoFile("docs/architecture/admin-smart-offer-lifecycle-authority-certification-findings.md");
        var projectContext = CertificationSource.ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = CertificationSource.ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = CertificationSource.ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = CertificationSource.ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = CertificationSource.ReadRepoFile(".ai-context/CODEX_RULES.md");

        Assert.Contains("PASS SF-SMO.LIFECYCLE Admin Smart Offer Lifecycle Authority", findings, StringComparison.Ordinal);
        Assert.Contains("PASS SF-SMO.LIFECYCLE Admin Smart Offer Lifecycle Authority", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS SF-SMO.LIFECYCLE Admin Smart Offer Lifecycle Authority Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS SF-SMO.LIFECYCLE", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS SF-SMO.LIFECYCLE Admin Smart Offer Lifecycle Authority", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS SF-SMO.LIFECYCLE Admin Smart Offer Lifecycle Authority Freeze", codexRules, StringComparison.Ordinal);
    }
}
