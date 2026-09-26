namespace PointOfSale.Tests.Architecture;

public sealed class CmsVersionHistoryRollbackAuthorityCertificationTests
{
    [Fact]
    public void CmsVersionHistoryRollback_MustRemainBackendOwnedAndPreconditioned()
    {
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AdminCmsPagesController.cs");
        var service = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Cms/CmsPageService.cs");
        var frontendService = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/storefront/cms/services/cmsService.js");
        var editor = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/storefront/cms/pages/CmsPageEditor.jsx");
        var admission = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/storefront/cms/cmsPageIdentityAuthority.js");

        Assert.Contains("[Authorize(Roles = \"Owner,Admin\")]", controller, StringComparison.Ordinal);
        Assert.Contains("GetCmsPageVersionHistory", controller, StringComparison.Ordinal);
        Assert.Contains("RollbackCmsPage", controller, StringComparison.Ordinal);
        Assert.Contains("[ResponseCache(NoStore = true", controller, StringComparison.Ordinal);
        Assert.Contains("GetExpectedCmsVersion", controller, StringComparison.Ordinal);
        Assert.Contains("CmsVersionPreconditionRequired", controller, StringComparison.Ordinal);
        Assert.Contains("BuildCmsPageConflictResponse", controller, StringComparison.Ordinal);

        Assert.Contains("GetVersionHistoryAsync", service, StringComparison.Ordinal);
        Assert.Contains("RollbackPageAsync", service, StringComparison.Ordinal);
        Assert.Contains("LoadPageGroupHistoryForMutationAsync", service, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", service, StringComparison.Ordinal);
        Assert.Contains("EnsureExpectedVersion(pageId, expectedVersion, currentPage.Version);", service, StringComparison.Ordinal);
        Assert.Contains("snapshot = pages", service, StringComparison.Ordinal);
        Assert.Contains("Version = pages.Max(item => item.Version) + 1", service, StringComparison.Ordinal);
        Assert.Contains("IsPublished = true", service, StringComparison.Ordinal);
        Assert.Contains("StageStorefrontCmsPagePublishedEventAsync", service, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidatePublishedScopeAsync", service, StringComparison.Ordinal);

        Assert.Contains("async getVersionHistory(pageId)", frontendService, StringComparison.Ordinal);
        Assert.Contains("/admin/pages/${pageId}/versions", frontendService, StringComparison.Ordinal);
        Assert.Contains("async rollbackPage(pageId, version, expectedVersion, reason = '')", frontendService, StringComparison.Ordinal);
        Assert.Contains("'If-Match': `\"${expectedVersion}\"`", frontendService, StringComparison.Ordinal);
        Assert.DoesNotContain("snapshot:", frontendService, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("publishedVersion:", frontendService, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("deriveCurrentCmsPageVersionHistoryPresentation", admission, StringComparison.Ordinal);
        Assert.Contains("snapshot.version !== entry.version", admission, StringComparison.Ordinal);
        Assert.Contains("deriveCurrentCmsPageVersionHistoryPresentation(data, currentUser", editor, StringComparison.Ordinal);
        Assert.Contains("cmsService.rollbackPage(", editor, StringComparison.Ordinal);
        Assert.Contains("deriveCurrentCmsPageIdentityPresentation(rolledBack", editor, StringComparison.Ordinal);
        Assert.Contains("Rollback requires current backend version authority.", editor, StringComparison.Ordinal);
    }

    [Fact]
    public void CmsVersionHistoryRollbackCertification_MustBeRecordedInAuthorityContext()
    {
        var projectContext = CertificationSource.ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = CertificationSource.ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = CertificationSource.ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = CertificationSource.ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var rules = CertificationSource.ReadRepoFile(".ai-context/CODEX_RULES.md");
        var findings = CertificationSource.ReadRepoFile("docs/architecture/cms-version-history-rollback-authority-certification-findings.md");

        Assert.Contains("PASS SF-CMS7 CMS Version History & Rollback Authority Certification", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS SF-CMS7 CMS Version History & Rollback Authority Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS SF-CMS7 CMS version history rollback runtime certifiable", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS SF-CMS7 CMS Version History & Rollback Authority Hardening", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS SF-CMS7 CMS Version History & Rollback Authority Freeze", rules, StringComparison.Ordinal);
        Assert.Contains("CMS Version History & Rollback is production-certifiable", findings, StringComparison.Ordinal);
    }
}
