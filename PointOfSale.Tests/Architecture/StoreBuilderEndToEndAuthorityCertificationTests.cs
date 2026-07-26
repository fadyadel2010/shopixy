namespace PointOfSale.Tests.Architecture;

public sealed class StoreBuilderEndToEndAuthorityCertificationTests
{
    [Fact]
    public void BuilderEndToEndFrontend_MustRemainPresentationOnlyAcrossWorkspaceDraftPreviewPublishRollback()
    {
        var app = CertificationSource.ReadRepoFile("PointOfSale.Web/src/App.jsx");
        var sidebar = CertificationSource.ReadRepoFile("PointOfSale.Web/src/components/Sidebar.jsx");
        var builder = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/storefront/pages/StorefrontBuilderPage.jsx");
        var history = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/storefront/pages/VersionHistoryPage.jsx");
        var theme = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/storefront/pages/ThemeEditorPage.jsx");
        var marketplace = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/storefront/pages/ThemeMarketplacePage.jsx");
        var store = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/storefront/store/builderStore.js");
        var service = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/storefront/services/storeConfigService.js");

        Assert.Contains("CommerceHubVendorRouteGuard capability=\"commercehub.storefront.manage\"", app, StringComparison.Ordinal);
        Assert.Contains("capability: 'commercehub.storefront.manage'", sidebar, StringComparison.Ordinal);

        Assert.Contains("deriveCurrentStoreIdentityPresentation(response, currentUser)", builder, StringComparison.Ordinal);
        Assert.Contains("deriveCurrentVendorScopedDto(", builder, StringComparison.Ordinal);
        Assert.Contains("if (!storeConfig)", builder, StringComparison.Ordinal);
        Assert.Contains("setStoreConfig(null)", builder, StringComparison.Ordinal);
        Assert.Contains("admitSections(acceptedSections)", builder, StringComparison.Ordinal);
        Assert.Contains("const rowVersion = requireWorkspaceAdmission()", builder, StringComparison.Ordinal);
        Assert.Contains("buildIfMatchOptions(rowVersion", builder, StringComparison.Ordinal);
        Assert.Contains("const acceptedConfig = admitWorkspaceConfig(response)", builder, StringComparison.Ordinal);
        Assert.Contains("const result = await persistPageLayout(sections)", builder, StringComparison.Ordinal);
        Assert.Contains("storeConfigService.getPreviewConfig({ signal: controller.signal })", builder, StringComparison.Ordinal);
        Assert.Contains("const acceptedConfig = admitWorkspaceConfig(response, result.config)", builder, StringComparison.Ordinal);
        Assert.Contains("await loadPageData(targetPage, templateIdFromUrl)", builder, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage", builder, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sessionStorage", builder, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("admitSections: (sections, options = {})", store, StringComparison.Ordinal);
        Assert.Contains("isDirty: false", store, StringComparison.Ordinal);
        Assert.Contains("generateSectionId()", store, StringComparison.Ordinal);

        Assert.Contains("api.get('/admin/store-config', withNoStoreHeaders(options))", service, StringComparison.Ordinal);
        Assert.Contains("api.put(`/admin/store-config/pages/${pageId}`", service, StringComparison.Ordinal);
        Assert.Contains("api.post(`/admin/store-config/pages/${pageId}/publish`", service, StringComparison.Ordinal);
        Assert.Contains("api.get('/storefront/config/preview', withNoStoreHeaders({", service, StringComparison.Ordinal);
        Assert.Contains("api.get('/admin/store-config/versions', withNoStoreHeaders(options))", service, StringComparison.Ordinal);
        Assert.Contains("api.post(`/admin/store-config/rollback/${version}`", service, StringComparison.Ordinal);

        Assert.Contains("deriveCurrentStoreIdentityPresentation(", history, StringComparison.Ordinal);
        Assert.Contains("await storeConfigService.getStoreConfig()", history, StringComparison.Ordinal);
        Assert.Contains("'If-Match': `\"${rowVersion}\"`", history, StringComparison.Ordinal);
        Assert.Contains("Rollback response authority was not admitted.", history, StringComparison.Ordinal);

        Assert.Contains("deriveCurrentStoreIdentityPresentation(response, user)", theme, StringComparison.Ordinal);
        Assert.Contains("Theme mutation requires accepted backend RowVersion evidence.", theme, StringComparison.Ordinal);
        Assert.Contains("storeConfigService.publishTheme(buildIfMatchOptions(rowVersion))", theme, StringComparison.Ordinal);
        Assert.Contains("deriveCurrentStoreIdentityPresentation(response, user)", marketplace, StringComparison.Ordinal);
        Assert.Contains("Theme activation response was rejected by backend store authority admission.", marketplace, StringComparison.Ordinal);
    }

    [Fact]
    public void BuilderEndToEndBackend_MustOwnAdmissionMutationPersistenceVersionAndProjectionAuthority()
    {
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AdminStoreConfigController.cs");
        var service = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Admin/AdminStoreConfigService.cs");
        var preview = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Storefront/StorefrontPreviewService.cs");
        var mapper = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Storefront/StorefrontDraftConfigMapper.cs");
        var projection = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Consumers/StorefrontConfigProjectionConsumer.cs");
        var lineage = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Reliability/TenantLineageAuthority.cs");

        Assert.Contains("[Authorize(Roles = \"Owner,Admin\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]", controller, StringComparison.Ordinal);
        Assert.Contains("ValidateTenantHeader(traceId)", controller, StringComparison.Ordinal);
        Assert.Contains("var precondition = RequireExpectedRowVersion(traceId);", controller, StringComparison.Ordinal);
        Assert.Contains("StatusCodes.Status428PreconditionRequired", controller, StringComparison.Ordinal);
        Assert.Contains("ApplyConcurrencyHeaders(result.RowVersion)", controller, StringComparison.Ordinal);

        Assert.Contains("GetRequiredStoreConfigForMutationAsync(", service, StringComparison.Ordinal);
        Assert.Contains("ApplyExpectedRowVersion(config, expectedRowVersion)", service, StringComparison.Ordinal);
        Assert.Contains("NormalizeBuilderPageComposition(", service, StringComparison.Ordinal);
        Assert.Contains("NormalizeSectionSettings(sectionObject, sectionType)", service, StringComparison.Ordinal);
        Assert.Contains("SafeRichTextSanitizer.Sanitize", service, StringComparison.Ordinal);
        Assert.Contains("_publishLockService.TryAcquireAsync(tenantId, $\"page:{pageId}\")", service, StringComparison.Ordinal);
        Assert.Contains("CreateAndAppendVersionSnapshot(config, \"Page Published\", actor);", service, StringComparison.Ordinal);
        Assert.Contains("_publishLockService.TryAcquireAsync(tenantId, \"rollback\")", service, StringComparison.Ordinal);
        Assert.Contains("MergeSnapshotLiveWithCurrentDrafts(", service, StringComparison.Ordinal);
        Assert.Contains("StoreConfigPublishAction.Published", service, StringComparison.Ordinal);
        Assert.Contains("StoreConfigPublishAction.RolledBack", service, StringComparison.Ordinal);
        Assert.Contains("new StoreConfigPublishedEvent", service, StringComparison.Ordinal);
        Assert.Contains("new StoreConfigRolledBackEvent", service, StringComparison.Ordinal);
        Assert.Contains("await _dbContext.SaveChangesAsync(cancellationToken);", service, StringComparison.Ordinal);

        Assert.Contains("GetConfigPreview (DRAFT)", preview, StringComparison.Ordinal);
        Assert.Contains("StorefrontDraftConfigMapper.Map(", preview, StringComparison.Ordinal);
        Assert.Contains("TenantId = config.TenantId", mapper, StringComparison.Ordinal);
        Assert.Contains("var selected = useDraftContent ? draft : live;", mapper, StringComparison.Ordinal);

        Assert.Contains("ProjectionEventFence.TryAcquireAsync", projection, StringComparison.Ordinal);
        Assert.Contains("LoadCurrentStoreConfigProjectionAuthorityAsync", projection, StringComparison.Ordinal);
        Assert.Contains("BuildFreshStoreConfigEventFilter", projection, StringComparison.Ordinal);
        Assert.Contains("await db.KeyDeleteAsync(new RedisKey[] { configKey });", projection, StringComparison.Ordinal);
        Assert.Contains("storeConfig.Id == storeConfigId", lineage, StringComparison.Ordinal);
        Assert.Contains("storeConfig.TenantId == tenantId", lineage, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationArtifacts_MustRecordSfAdm2FinalTruth()
    {
        Assert.Contains(
            "PASS SF-ADM2.FINAL Store Builder End-to-End Authority Certification",
            CertificationSource.ReadRepoFile("docs/architecture/store-builder-end-to-end-authority-certification-findings.md"),
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
            Assert.Contains("PASS SF-ADM2.FINAL", CertificationSource.ReadRepoFile(path), StringComparison.Ordinal);
        }
    }
}
