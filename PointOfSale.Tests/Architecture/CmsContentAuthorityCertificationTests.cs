namespace PointOfSale.Tests.Architecture;

public sealed class CmsContentAuthorityCertificationTests
{
    [Fact]
    public void AdminCmsBackend_MustKeepAcceptedContentBackendOwned()
    {
        var validator = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Cms/CmsPageContentValidator.cs");
        var service = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Cms/CmsPageService.cs");
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AdminCmsPagesController.cs");

        Assert.Contains("SafeRichTextSanitizer.Sanitize", validator, StringComparison.Ordinal);
        Assert.Contains("SupportedBlockTypes", validator, StringComparison.Ordinal);
        Assert.Contains("\"text\"", validator, StringComparison.Ordinal);
        Assert.Contains("\"image\"", validator, StringComparison.Ordinal);
        Assert.Contains("\"faq\"", validator, StringComparison.Ordinal);
        Assert.Contains("Block type '{type}' is not supported.", validator, StringComparison.Ordinal);
        Assert.Contains("NormalizeSafeUrl(imageUrl, \"Image block URL\")", validator, StringComparison.Ordinal);
        Assert.Contains("NormalizeOptionalUrl(request.Seo.OpenGraphImageUrl", service, StringComparison.Ordinal);
        Assert.Contains("ValidateAndNormalize(request.ContentType, request.Content, request.SchemaVersion)", service, StringComparison.Ordinal);
        Assert.Contains("EnsureExpectedVersion(pageId, request.ExpectedVersion.Value, currentPage.Version);", service, StringComparison.Ordinal);
        Assert.Contains("EnsureExpectedVersion(pageId, expectedVersion, draftPage.Version);", service, StringComparison.Ordinal);
        Assert.Contains("_tenantContext.RequiredTenantId", controller, StringComparison.Ordinal);
        Assert.Contains("CmsVersionPreconditionRequired", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void AdminCmsFrontend_MustTreatContentAsCandidateUntilBackendAdmission()
    {
        var editor = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/storefront/cms/pages/CmsPageEditor.jsx");
        var authority = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/storefront/cms/cmsPageIdentityAuthority.js");
        var service = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/storefront/cms/services/cmsService.js");

        Assert.Contains("const payload = toRequestPayload(form)", editor, StringComparison.Ordinal);
        Assert.Contains("payload.expectedVersion = expectedVersionOverride ?? page?.draft?.version ?? page?.published?.version", editor, StringComparison.Ordinal);
        Assert.Contains("const admittedPage = adoptAuthoritativePage(", editor, StringComparison.Ordinal);
        Assert.Contains("const acceptedForm = fromPageResponse(admittedPage)", editor, StringComparison.Ordinal);
        Assert.Contains("setForm(acceptedForm)", editor, StringComparison.Ordinal);
        Assert.Contains("adoptAuthoritativePage(published, targetPage.id", editor, StringComparison.Ordinal);
        Assert.Contains("contentType: workingVersion.contentType", editor, StringComparison.Ordinal);
        Assert.Contains("CMS_CONTENT_TYPES", authority, StringComparison.Ordinal);
        Assert.Contains("'Cache-Control': 'no-store'", service, StringComparison.Ordinal);
        Assert.Contains("'If-Match': `\"${expectedVersion}\"`", service, StringComparison.Ordinal);
    }

    [Fact]
    public void CmsContentCertification_MustBeRecordedInAuthorityContext()
    {
        var projectContext = CertificationSource.ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = CertificationSource.ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = CertificationSource.ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = CertificationSource.ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var rules = CertificationSource.ReadRepoFile(".ai-context/CODEX_RULES.md");
        var findings = CertificationSource.ReadRepoFile("docs/architecture/cms-content-authority-certification-findings.md");

        Assert.Contains("PASS SF-CMS3 CMS Content Authority Certification", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS SF-CMS3 CMS Content Authority Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS SF-CMS3 CMS content runtime certifiable", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS SF-CMS3 CMS Content Authority Hardening", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS SF-CMS3 CMS Content Authority Freeze", rules, StringComparison.Ordinal);
        Assert.Contains("CMS Content is production-certifiable", findings, StringComparison.Ordinal);
    }
}
