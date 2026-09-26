namespace PointOfSale.Tests.Architecture;

public sealed class CmsEndToEndAuthorityCertificationTests
{
    [Fact]
    public void CmsEndToEndFrontend_MustRemainPresentationOnlyAcrossLifecycle()
    {
        var app = CertificationSource.ReadRepoFile("PointOfSale.Web/src/App.jsx");
        var frontendService = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/storefront/cms/services/cmsService.js");
        var editor = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/storefront/cms/pages/CmsPageEditor.jsx");
        var list = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/storefront/cms/pages/CmsPagesList.jsx");
        var admission = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/storefront/cms/cmsPageIdentityAuthority.js");

        Assert.Contains("<Route element={<PosPermissionRouteGuard permission=\"storefront.design.manage\" />}>", app, StringComparison.Ordinal);
        Assert.Contains("api.get('/admin/pages', noStoreOptions)", frontendService, StringComparison.Ordinal);
        Assert.Contains("api.get(`/admin/pages/${pageId}/versions`, noStoreOptions)", frontendService, StringComparison.Ordinal);
        Assert.Contains("api.post(`/admin/pages/${pageId}/preview-link`, undefined, noStoreOptions)", frontendService, StringComparison.Ordinal);
        Assert.Contains("'If-Match': `\"${expectedVersion}\"`", frontendService, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage", editor, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sessionStorage", editor, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("const admittedPage = adoptAuthoritativePage(", editor, StringComparison.Ordinal);
        Assert.Contains("adoptAuthoritativePage(published, targetPage.id", editor, StringComparison.Ordinal);
        Assert.Contains("deriveCurrentCmsPageVersionHistoryPresentation(data, currentUser", editor, StringComparison.Ordinal);
        Assert.Contains("deriveCurrentCmsPageIdentityPresentation(rolledBack", editor, StringComparison.Ordinal);
        Assert.Contains("window.open(previewAccess.previewUrl", editor, StringComparison.Ordinal);
        Assert.Contains("deriveCurrentCmsPageListPresentation(data, currentUser)", list, StringComparison.Ordinal);
        Assert.Contains("await loadPages()", list, StringComparison.Ordinal);
        Assert.Contains("deriveCurrentCmsPageVersionHistoryPresentation", admission, StringComparison.Ordinal);
        Assert.Contains("snapshot.version !== entry.version", admission, StringComparison.Ordinal);
    }

    [Fact]
    public void CmsEndToEndBackend_MustOwnPersistenceProjectionRecoveryAndPublicDistribution()
    {
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AdminCmsPagesController.cs");
        var service = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Cms/CmsPageService.cs");
        var readService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Cms/CmsPageReadService.cs");
        var publicController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/StorefrontPagesController.cs");
        var projection = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Consumers/CmsPageProjectionConsumer.cs");
        var seo = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Storefront/SeoService.cs");
        var searchSync = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AdminSearchSyncController.cs");
        var navigationAdmission = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Admin/NavigationAdmissionService.cs");
        var navigationService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Admin/AdminCmsService.cs");

        Assert.Contains("[Authorize(Roles = \"Owner,Admin\")]", controller, StringComparison.Ordinal);
        Assert.Contains("_tenantContext.RequiredTenantId", controller, StringComparison.Ordinal);
        Assert.Contains("CmsVersionPreconditionRequired", controller, StringComparison.Ordinal);
        Assert.Contains("GetExpectedCmsVersion", controller, StringComparison.Ordinal);
        Assert.Contains("BuildCmsPageConflictResponse", controller, StringComparison.Ordinal);

        Assert.Contains("BeginTransactionAsync(IsolationLevel.Serializable", service, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", service, StringComparison.Ordinal);
        Assert.Contains("EnsureExpectedVersion(pageId, expectedVersion", service, StringComparison.Ordinal);
        Assert.Contains("ValidateAndNormalize", service, StringComparison.Ordinal);
        Assert.Contains("NormalizeOptionalSeoText", service, StringComparison.Ordinal);
        Assert.Contains("NormalizeOptionalUrl", service, StringComparison.Ordinal);
        Assert.Contains("StageStorefrontCmsPagePublishedEventAsync", service, StringComparison.Ordinal);
        Assert.Contains("StageStorefrontCmsPageDeletedEventAsync", service, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidatePublishedScopeAsync", service, StringComparison.Ordinal);

        Assert.Contains("TryValidatePreviewToken(", publicController, StringComparison.Ordinal);
        Assert.Contains("GetPreviewPageByIdAsync", publicController, StringComparison.Ordinal);
        Assert.Contains("X-Preview-Mode", publicController, StringComparison.Ordinal);
        Assert.Contains("LoadCurrentPublishedPageBySlugAsync", readService, StringComparison.Ordinal);
        Assert.Contains("item.IsPublished && !item.IsDraft", readService, StringComparison.Ordinal);
        Assert.Contains("IsTenantPubliclyActiveAsync", readService, StringComparison.Ordinal);

        Assert.Contains("ProjectionEventFence.TryAcquireAsync", projection, StringComparison.Ordinal);
        Assert.Contains("TenantLineageAuthority.IsActiveTenantAsync", projection, StringComparison.Ordinal);
        Assert.Contains("BuildFreshPageEventFilter", projection, StringComparison.Ordinal);
        Assert.Contains("FailClosedCmsPageProjectionAsync", projection, StringComparison.Ordinal);
        Assert.Contains("SqlAuthorityOverlayKind.StorefrontCms", projection, StringComparison.Ordinal);

        Assert.Contains("LoadCurrentPublishedCmsPageBySlugAsync", seo, StringComparison.Ordinal);
        Assert.Contains("LoadCurrentPublishedCmsPagesAsync", seo, StringComparison.Ordinal);
        Assert.Contains("Sitemap recomposition starting", seo, StringComparison.Ordinal);
        Assert.Contains("SqlAuthorityOverlayKind.StorefrontCms", searchSync, StringComparison.Ordinal);
        Assert.Contains("DeleteStaleCmsPagesAsync", searchSync, StringComparison.Ordinal);
        Assert.Contains("public async Task<AdminNavigationRequest> AdmitAsync(", navigationAdmission, StringComparison.Ordinal);
        Assert.Contains("NavigationAdmissionMode.Publish", navigationService, StringComparison.Ordinal);
        Assert.Contains("ResolveCmsPage(", navigationAdmission, StringComparison.Ordinal);
        Assert.Contains("NAVIGATION_CMS_PAGE_UNPUBLISHED", navigationAdmission, StringComparison.Ordinal);
    }

    [Fact]
    public void CmsPagePostgresStateTransitions_MustReleaseUniqueSlotsAndDiscardFailedTrackedState()
    {
        var service = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Cms/CmsPageService.cs");
        var publishedSlotRelease = service.IndexOf(
            "if (transaction != null && publishedPages.Count > 0)",
            StringComparison.Ordinal);
        var replacementActivation = service.IndexOf(
            "draftPage.IsPublished = true;",
            StringComparison.Ordinal);

        Assert.True(publishedSlotRelease >= 0 && publishedSlotRelease < replacementActivation);
        Assert.Contains("_dbContext.ChangeTracker.Clear();", service, StringComparison.Ordinal);
    }

    [Fact]
    public void CmsSeedPostgresLock_MustProjectXminConcurrencyToken()
    {
        var seedService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Admin/CmsSeedService.cs");

        Assert.Contains("SELECT sc.*, sc.xmin", seedService, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", seedService, StringComparison.Ordinal);
        Assert.Contains("AddDefaultStorefrontHomepage", seedService, StringComparison.Ordinal);
        Assert.Contains("new StoreConfigPublishedEvent", seedService, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "[\"backgroundImage\"] = CreatePlaceholderImageDataUrl",
            seedService,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationArtifacts_MustRecordSfCmsFinalTruth()
    {
        Assert.Contains(
            "PASS SF-CMS.FINAL CMS End-to-End Authority Certification",
            CertificationSource.ReadRepoFile("docs/architecture/cms-end-to-end-authority-certification-findings.md"),
            StringComparison.Ordinal);

        foreach (var path in new[]
                 {
                     ".ai-context/PROJECT_CONTEXT.md",
                     ".ai-context/ARCHITECTURE_TRUTH.md",
                     ".ai-context/KNOWN_RISKS.md",
                     ".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md",
                     ".ai-context/CODEX_RULES.md"
                 })
        {
            Assert.Contains("PASS SF-CMS.FINAL", CertificationSource.ReadRepoFile(path), StringComparison.Ordinal);
        }
    }
}
