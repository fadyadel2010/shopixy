namespace PointOfSale.Tests.Architecture;

public sealed class CmsSeoAuthorityCertificationTests
{
    [Fact]
    public void AdminCmsSeo_MustRemainBackendOwnedAndAdmitted()
    {
        var service = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Cms/CmsPageService.cs");
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AdminCmsPagesController.cs");
        var frontendAuthority = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/storefront/cms/cmsPageIdentityAuthority.js");
        var frontendTests = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/storefront/cms/cmsPageIdentityAuthority.test.ts");
        var editor = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/storefront/cms/pages/CmsPageEditor.jsx");

        Assert.Contains("NormalizeOptionalSeoText(request.Seo.MetaTitle", service, StringComparison.Ordinal);
        Assert.Contains("NormalizeOptionalSeoText(request.Seo.MetaDescription", service, StringComparison.Ordinal);
        Assert.Contains("NormalizeOptionalSeoText(request.Seo.MetaKeywords", service, StringComparison.Ordinal);
        Assert.Contains("NormalizeOptionalUrl(request.Seo.OpenGraphImageUrl", service, StringComparison.Ordinal);
        Assert.Contains("EnsureExpectedVersion(pageId, request.ExpectedVersion.Value, currentPage.Version);", service, StringComparison.Ordinal);
        Assert.Contains("EnsureExpectedVersion(pageId, expectedVersion, draftPage.Version);", service, StringComparison.Ordinal);
        Assert.Contains("_tenantContext.RequiredTenantId", controller, StringComparison.Ordinal);
        Assert.Contains("CmsVersionPreconditionRequired", controller, StringComparison.Ordinal);
        Assert.Contains("admitCmsPageSeo", frontendAuthority, StringComparison.Ordinal);
        Assert.Contains("normalizeOptionalSeoUrl", frontendAuthority, StringComparison.Ordinal);
        Assert.Contains("canonicalUrl", frontendTests, StringComparison.Ordinal);
        Assert.Contains("robots", frontendTests, StringComparison.Ordinal);
        Assert.Contains("structuredData", frontendTests, StringComparison.Ordinal);
        Assert.Contains("const acceptedForm = fromPageResponse(admittedPage)", editor, StringComparison.Ordinal);
        Assert.Contains("setForm(acceptedForm)", editor, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicCmsSeoProjection_MustRecomposeFromCurrentBackendAuthority()
    {
        var seoService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Storefront/SeoService.cs");
        var readService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Cms/CmsPageReadService.cs");
        var projection = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Consumers/CmsPageProjectionConsumer.cs");

        Assert.Contains("BuildCmsSeoMetadataAsync", seoService, StringComparison.Ordinal);
        Assert.Contains("LoadCurrentPublishedCmsPageBySlugAsync", seoService, StringComparison.Ordinal);
        Assert.Contains("LoadCurrentPublishedCmsPagesAsync", seoService, StringComparison.Ordinal);
        Assert.Contains("\"cms\" => $\"{basePath}/pages/{slug}\"", seoService, StringComparison.Ordinal);
        Assert.DoesNotContain("TryGetFromCacheAsync<SitemapDto>", seoService, StringComparison.Ordinal);
        Assert.Contains("LoadCurrentPublishedPageBySlugAsync", readService, StringComparison.Ordinal);
        Assert.Contains("MapPageWithSafeFallback(page)", readService, StringComparison.Ordinal);
        Assert.Contains("item.IsPublished && !item.IsDraft", projection, StringComparison.Ordinal);
        Assert.Contains("MapToDocument", projection, StringComparison.Ordinal);
        Assert.Contains("MetaTitle = page.MetaTitle ?? page.Title", projection, StringComparison.Ordinal);
    }

    [Fact]
    public void CmsSeoCertification_MustBeRecordedInAuthorityContext()
    {
        var projectContext = CertificationSource.ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = CertificationSource.ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = CertificationSource.ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = CertificationSource.ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var rules = CertificationSource.ReadRepoFile(".ai-context/CODEX_RULES.md");
        var findings = CertificationSource.ReadRepoFile("docs/architecture/cms-seo-authority-certification-findings.md");

        Assert.Contains("PASS SF-CMS4 CMS SEO Authority Certification", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS SF-CMS4 CMS SEO Authority Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS SF-CMS4 CMS SEO runtime certifiable", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS SF-CMS4 CMS SEO Authority Hardening", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS SF-CMS4 CMS SEO Authority Freeze", rules, StringComparison.Ordinal);
        Assert.Contains("CMS SEO is production-certifiable", findings, StringComparison.Ordinal);
    }
}
