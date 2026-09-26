namespace PointOfSale.Tests.Architecture;

public sealed class CmsPublishAuthorityCertificationTests
{
    [Fact]
    public void AdminCmsPublish_MustRemainBackendOwnedAndVersionPreconditioned()
    {
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AdminCmsPagesController.cs");
        var service = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Cms/CmsPageService.cs");
        var frontendService = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/storefront/cms/services/cmsService.js");
        var editor = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/storefront/cms/pages/CmsPageEditor.jsx");
        var list = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/storefront/cms/pages/CmsPagesList.jsx");
        var admission = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/storefront/cms/cmsPageIdentityAuthority.js");

        Assert.Contains("[Authorize(Roles = \"Owner,Admin\")]", controller, StringComparison.Ordinal);
        Assert.Contains("_tenantContext.RequiredTenantId", controller, StringComparison.Ordinal);
        Assert.Contains("GetExpectedCmsVersion", controller, StringComparison.Ordinal);
        Assert.Contains("CmsVersionPreconditionRequired", controller, StringComparison.Ordinal);
        Assert.Contains("PublishCmsPageAsync", controller, StringComparison.Ordinal);
        Assert.Contains("BuildCmsPageConflictResponse", controller, StringComparison.Ordinal);

        Assert.Contains("BeginTransactionAsync(IsolationLevel.Serializable", service, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", service, StringComparison.Ordinal);
        Assert.Contains("EnsureExpectedVersion(pageId, expectedVersion, draftPage.Version);", service, StringComparison.Ordinal);
        Assert.Contains("draftPage.IsDraft = false;", service, StringComparison.Ordinal);
        Assert.Contains("draftPage.IsPublished = true;", service, StringComparison.Ordinal);
        Assert.Contains("draftPage.PublishedAt = now;", service, StringComparison.Ordinal);
        Assert.Contains("StageStorefrontCmsPagePublishedEventAsync", service, StringComparison.Ordinal);
        Assert.Contains("IdempotencyKey = $\"STOREFRONT_CMS_PUBLISHED", service, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidatePublishedScopeAsync", service, StringComparison.Ordinal);

        Assert.Contains("async publishPage(pageId, expectedVersion)", frontendService, StringComparison.Ordinal);
        Assert.Contains("'If-Match': `\"${expectedVersion}\"`", frontendService, StringComparison.Ordinal);
        Assert.DoesNotContain("publishedAtUtc:", frontendService, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("publishedVersion:", frontendService, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("const expectedVersion = targetPage.draft?.version", editor, StringComparison.Ordinal);
        Assert.Contains("adoptAuthoritativePage(published, targetPage.id", editor, StringComparison.Ordinal);
        Assert.DoesNotContain("cmsService.publishPage", list, StringComparison.Ordinal);
        Assert.Contains("The list never publishes content.", list, StringComparison.Ordinal);
        Assert.Contains("const published = page.published == null ? null : admitCmsPageVersion(page.published)", admission, StringComparison.Ordinal);
    }

    [Fact]
    public void PublishedCmsVisibility_MustReenterSqlBeforeProjectionSearchSeoAndSitemap()
    {
        var readService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Cms/CmsPageReadService.cs");
        var projection = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Consumers/CmsPageProjectionConsumer.cs");
        var seo = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Storefront/SeoService.cs");
        var searchSync = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AdminSearchSyncController.cs");
        var storefrontSearch = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/StorefrontSearchController.cs");

        Assert.Contains("LoadCurrentPublishedPageBySlugAsync", readService, StringComparison.Ordinal);
        Assert.Contains("item.IsPublished && !item.IsDraft", readService, StringComparison.Ordinal);
        Assert.Contains("item.PublishedAt != null", readService, StringComparison.Ordinal);
        Assert.Contains("IsTenantPubliclyActiveAsync", readService, StringComparison.Ordinal);
        Assert.DoesNotContain("GetPublishedScopeAsync", readService, StringComparison.Ordinal);

        Assert.Contains("ProjectionEventFence.TryAcquireAsync", projection, StringComparison.Ordinal);
        Assert.Contains("item.IsPublished && !item.IsDraft", projection, StringComparison.Ordinal);
        Assert.Contains("TenantLineageAuthority.IsActiveTenantAsync", projection, StringComparison.Ordinal);
        Assert.Contains("BuildFreshPageEventFilter", projection, StringComparison.Ordinal);
        Assert.Contains("FailClosedCmsPageProjectionAsync", projection, StringComparison.Ordinal);

        Assert.Contains("BuildCmsSeoMetadataAsync", seo, StringComparison.Ordinal);
        Assert.Contains("LoadCurrentPublishedCmsPageBySlugAsync", seo, StringComparison.Ordinal);
        Assert.Contains("LoadCurrentPublishedCmsPagesAsync", seo, StringComparison.Ordinal);
        Assert.Contains("Sitemap recomposition starting", seo, StringComparison.Ordinal);
        Assert.DoesNotContain("TryGetFromCacheAsync<SitemapDto>", seo, StringComparison.Ordinal);

        Assert.Contains("SqlAuthorityOverlayKind.StorefrontCms", searchSync, StringComparison.Ordinal);
        Assert.Contains("page.IsPublished && !page.IsDraft", searchSync, StringComparison.Ordinal);
        Assert.Contains("DeleteStaleCmsPagesAsync", searchSync, StringComparison.Ordinal);
        Assert.Contains("page.IsPublished && !page.IsDraft", storefrontSearch, StringComparison.Ordinal);
        Assert.Contains("visibleGroups.Contains(item.EntityId)", storefrontSearch, StringComparison.Ordinal);
    }

    [Fact]
    public void CmsPublishCertification_MustBeRecordedInAuthorityContext()
    {
        var projectContext = CertificationSource.ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = CertificationSource.ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = CertificationSource.ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = CertificationSource.ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var rules = CertificationSource.ReadRepoFile(".ai-context/CODEX_RULES.md");
        var findings = CertificationSource.ReadRepoFile("docs/architecture/cms-publish-authority-certification-findings.md");

        Assert.Contains("PASS SF-CMS5 CMS Publish Authority Certification", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS SF-CMS5 CMS Publish Authority Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS SF-CMS5 CMS publish runtime certifiable", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS SF-CMS5 CMS Publish Authority Hardening", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS SF-CMS5 CMS Publish Authority Freeze", rules, StringComparison.Ordinal);
        Assert.Contains("CMS Publish is production-certifiable", findings, StringComparison.Ordinal);
    }
}
