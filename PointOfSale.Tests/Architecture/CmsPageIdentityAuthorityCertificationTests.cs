namespace PointOfSale.Tests.Architecture;

public sealed class CmsPageIdentityAuthorityCertificationTests
{
    [Fact]
    public void AdminCmsFrontend_MustAdmitPageIdentityFromCurrentBackendDtoOnly()
    {
        var list = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/storefront/cms/pages/CmsPagesList.jsx");
        var editor = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/storefront/cms/pages/CmsPageEditor.jsx");
        var authority = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/storefront/cms/cmsPageIdentityAuthority.js");
        var service = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/storefront/cms/services/cmsService.js");

        Assert.Contains("deriveCurrentCmsPageListPresentation(data, currentUser)", list, StringComparison.Ordinal);
        Assert.Contains("adoptAuthoritativePage(data, id)", editor, StringComparison.Ordinal);
        Assert.Contains("adoptAuthoritativePage(", editor, StringComparison.Ordinal);
        Assert.Contains("savedPage,", editor, StringComparison.Ordinal);
        Assert.Contains("adoptAuthoritativePage(published, targetPage.id", editor, StringComparison.Ordinal);
        Assert.Contains("expectedPageId", authority, StringComparison.Ordinal);
        Assert.Contains("tenantId !== currentTenantId", authority, StringComparison.Ordinal);
        Assert.Contains("slug !== currentVersion.slug", authority, StringComparison.Ordinal);
        Assert.Contains("seenPageIds", authority, StringComparison.Ordinal);
        Assert.Contains("'Cache-Control': 'no-store'", service, StringComparison.Ordinal);
    }

    [Fact]
    public void AdminCmsBackend_MustKeepPageIdentityTenantAndVersionBackendOwned()
    {
        var dto = CertificationSource.ReadRepoFile("PointOfSale.API/DTOs/Admin/AdminCmsDtos.cs");
        var service = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Cms/CmsPageService.cs");
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AdminCmsPagesController.cs");
        var configuration = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/CmsPageConfiguration.cs");

        Assert.Contains("public Guid TenantId { get; set; }", dto, StringComparison.Ordinal);
        Assert.Contains("Id = current.PageGroupId", service, StringComparison.Ordinal);
        Assert.Contains("TenantId = current.TenantId", service, StringComparison.Ordinal);
        Assert.Contains("var pageGroupId = Guid.NewGuid();", service, StringComparison.Ordinal);
        Assert.Contains("item.PageGroupId == pageId", service, StringComparison.Ordinal);
        Assert.Contains("EnsureExpectedVersion(pageId, request.ExpectedVersion.Value, currentPage.Version);", service, StringComparison.Ordinal);
        Assert.Contains("EnsureExpectedVersion(pageId, expectedVersion, draftPage.Version);", service, StringComparison.Ordinal);
        Assert.Contains("_tenantContext.RequiredTenantId", controller, StringComparison.Ordinal);
        Assert.Contains("GetExpectedCmsVersion", controller, StringComparison.Ordinal);
        Assert.Contains("UQ_CmsPages_PageGroupId_Draft", configuration, StringComparison.Ordinal);
        Assert.Contains("UQ_CmsPages_PageGroupId_Published", configuration, StringComparison.Ordinal);
        Assert.Contains("UQ_CmsPages_Tenant_Draft_Slug", configuration, StringComparison.Ordinal);
        Assert.Contains("UQ_CmsPages_Tenant_Published_Slug", configuration, StringComparison.Ordinal);
    }

    [Fact]
    public void CmsPageIdentityCertification_MustBeRecordedInAuthorityContext()
    {
        var projectContext = CertificationSource.ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = CertificationSource.ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = CertificationSource.ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = CertificationSource.ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var rules = CertificationSource.ReadRepoFile(".ai-context/CODEX_RULES.md");
        var findings = CertificationSource.ReadRepoFile("docs/architecture/cms-page-identity-authority-certification-findings.md");

        Assert.Contains("PASS SF-CMS2 CMS Page Identity Authority Certification", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS SF-CMS2 CMS Page Identity Authority Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS SF-CMS2 CMS page identity runtime certifiable", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS SF-CMS2 CMS Page Identity Authority Hardening", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS SF-CMS2 CMS Page Identity Authority Freeze", rules, StringComparison.Ordinal);
        Assert.Contains("CMS Page Identity is production-certifiable", findings, StringComparison.Ordinal);
    }
}
