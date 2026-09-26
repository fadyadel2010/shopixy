namespace PointOfSale.Tests.Architecture;

public sealed class CmsPreviewAuthorityCertificationTests
{
    [Fact]
    public void CmsPublicPreview_MustRemainBackendTokenAndDraftResolutionOwned()
    {
        var storefrontController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/StorefrontPagesController.cs");
        var cachePolicy = CertificationSource.ReadRepoFile("PointOfSale.API/Middleware/StorefrontHttpCachePolicyMiddleware.cs");
        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");
        var adminController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AdminCmsPagesController.cs");
        var tokenService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Cms/CmsPagePreviewTokenService.cs");
        var readService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Cms/CmsPageReadService.cs");
        var adminService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Admin/AdminCmsService.cs");
        var frontendService = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/storefront/cms/services/cmsService.js");
        var editor = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/storefront/cms/pages/CmsPageEditor.jsx");

        Assert.Contains("TryValidatePreviewToken(", storefrontController, StringComparison.Ordinal);
        Assert.Contains("tenantId!.Value", storefrontController, StringComparison.Ordinal);
        Assert.Contains("normalizedSlug", storefrontController, StringComparison.Ordinal);
        Assert.Contains("GetPreviewPageByIdAsync", storefrontController, StringComparison.Ordinal);
        Assert.Contains("pageVersion", storefrontController, StringComparison.Ordinal);
        Assert.Contains("return Forbid();", storefrontController, StringComparison.Ordinal);
        Assert.Contains("X-Preview-Mode", storefrontController, StringComparison.Ordinal);
        Assert.Contains("private, no-store, no-cache, max-age=0, must-revalidate", cachePolicy, StringComparison.Ordinal);
        Assert.Contains("UseMiddleware<PointOfSale.API.Middleware.StorefrontHttpCachePolicyMiddleware>", program, StringComparison.Ordinal);
        Assert.DoesNotContain("IsAuthorizedPreviewRequest", storefrontController, StringComparison.Ordinal);
        Assert.DoesNotContain("GetPagePreviewAsync", storefrontController, StringComparison.Ordinal);

        Assert.Contains("[Authorize(Roles = \"Owner,Admin\")]", adminController, StringComparison.Ordinal);
        Assert.Contains("CreateCmsPagePreviewLink", adminController, StringComparison.Ordinal);
        Assert.Contains("[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]", adminController, StringComparison.Ordinal);
        Assert.Contains("CreateCmsPagePreviewAccessAsync", adminService, StringComparison.Ordinal);
        Assert.Contains("var version = page.Draft ?? page.Published;", adminService, StringComparison.Ordinal);
        Assert.Contains("CreatePreviewToken(", adminService, StringComparison.Ordinal);

        Assert.Contains("TenantId", tokenService, StringComparison.Ordinal);
        Assert.Contains("PageGroupId", tokenService, StringComparison.Ordinal);
        Assert.Contains("PageVersion", tokenService, StringComparison.Ordinal);
        Assert.Contains("ExpiresAtUtc", tokenService, StringComparison.Ordinal);
        Assert.Contains("payload.ExpiresAtUtc <= DateTime.UtcNow", tokenService, StringComparison.Ordinal);
        Assert.Contains("payload.TenantId != tenantId", tokenService, StringComparison.Ordinal);
        Assert.Contains("payload.PageVersion < 1", tokenService, StringComparison.Ordinal);
        Assert.Contains("payload.Slug", tokenService, StringComparison.Ordinal);

        Assert.Contains("Where(item => item.TenantId == tenantId)", readService, StringComparison.Ordinal);
        Assert.Contains("Where(item => item.PageGroupId == pageGroupId)", readService, StringComparison.Ordinal);
        Assert.Contains("query = query.Where(item => item.Version == pageVersion.Value);", readService, StringComparison.Ordinal);
        Assert.Contains("!item.IsDeleted && (item.IsDraft || item.IsPublished)", readService, StringComparison.Ordinal);

        Assert.Contains("admitCmsPreviewAccess", frontendService, StringComparison.Ordinal);
        Assert.Contains("api.post(`/admin/pages/${pageId}/preview-link`, undefined, noStoreOptions)", frontendService, StringComparison.Ordinal);
        Assert.Contains("const previewAccess = await cmsService.createPreviewLink(pageId)", editor, StringComparison.Ordinal);
        Assert.Contains("window.open(previewAccess.previewUrl", editor, StringComparison.Ordinal);
    }

    [Fact]
    public void CmsPreviewCertification_MustBeRecordedInAuthorityContext()
    {
        var projectContext = CertificationSource.ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = CertificationSource.ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = CertificationSource.ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = CertificationSource.ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var rules = CertificationSource.ReadRepoFile(".ai-context/CODEX_RULES.md");
        var findings = CertificationSource.ReadRepoFile("docs/architecture/cms-preview-authority-certification-findings.md");

        Assert.Contains("PASS SF-CMS6 CMS Preview Authority Certification", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS SF-CMS6 CMS Preview Authority Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS SF-CMS6 CMS preview runtime certifiable", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS SF-CMS6 CMS Preview Authority Hardening", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS SF-CMS6 CMS Preview Authority Freeze", rules, StringComparison.Ordinal);
        Assert.Contains("CMS Preview is production-certifiable", findings, StringComparison.Ordinal);
    }
}
