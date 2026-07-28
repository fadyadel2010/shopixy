namespace PointOfSale.Tests.Architecture;

public sealed class StoreBuilderPublishAuthorityCertificationTests
{
    [Fact]
    public void BuilderPublishFrontend_MustSubmitIntentOnlyWithAcceptedRowVersion()
    {
        var builder = ReadRepoFile("PointOfSale.Web/src/modules/storefront/pages/StorefrontBuilderPage.jsx");
        var topBar = ReadRepoFile("PointOfSale.Web/src/modules/storefront/components/builder/BuilderTopBar.jsx");
        var modal = ReadRepoFile("PointOfSale.Web/src/modules/storefront/components/builder/PublishModal.jsx");
        var service = ReadRepoFile("PointOfSale.Web/src/modules/storefront/services/storeConfigService.js");
        var store = ReadRepoFile("PointOfSale.Web/src/modules/storefront/store/builderStore.js");

        Assert.Contains("const result = await persistPageLayout(sections)", builder, StringComparison.Ordinal);
        Assert.Contains("const publishRowVersion = getAcceptedWorkspaceRowVersion(result.config) || requireWorkspaceAdmission()", builder, StringComparison.Ordinal);
        Assert.Contains("storeConfigService.publishPage(", builder, StringComparison.Ordinal);
        Assert.Contains("buildIfMatchOptions(publishRowVersion)", builder, StringComparison.Ordinal);
        Assert.Contains("const acceptedConfig = admitWorkspaceConfig(response, result.config)", builder, StringComparison.Ordinal);
        Assert.Contains("setStoreConfig(acceptedConfig)", builder, StringComparison.Ordinal);
        Assert.Contains("markClean()", builder, StringComparison.Ordinal);
        Assert.Contains("onPublish={() => setIsPublishModalOpen(true)}", builder, StringComparison.Ordinal);
        Assert.Contains("onClick={onPublish}", topBar, StringComparison.Ordinal);
        Assert.Contains("onClick={onConfirm}", modal, StringComparison.Ordinal);
        Assert.Contains("async publishPage(pageId, options = {})", service, StringComparison.Ordinal);
        Assert.Contains("api.post(`/admin/store-config/pages/${pageId}/publish`, undefined, options)", service, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage", builder, StringComparison.Ordinal);
        Assert.Contains("setPublishing: (isPublishing) => set({ isPublishing })", store, StringComparison.Ordinal);
    }

    [Fact]
    public void BuilderPublishBackend_MustOwnAdmissionVersionTransactionAndFailureSemantics()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/AdminStoreConfigController.cs");
        var service = ReadRepoFile("PointOfSale.API/Services/Admin/AdminStoreConfigService.cs");
        var errors = ReadRepoFile("PointOfSale.API/DTOs/Admin/AdminStoreConfigDtos.cs");

        Assert.Contains("[Authorize(Roles = \"Owner,Admin\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpPost(\"pages/{pageId}/publish\")]", controller, StringComparison.Ordinal);
        Assert.Contains("var precondition = RequireExpectedRowVersion(traceId);", controller, StringComparison.Ordinal);
        Assert.Contains("expectedRowVersion: precondition.RowVersion", controller, StringComparison.Ordinal);
        Assert.Contains("return Conflict(AdminStoreConfigApiErrorResponse.ConcurrencyConflict(traceId));", controller, StringComparison.Ordinal);
        Assert.Contains("return NotFound(AdminStoreConfigApiErrorResponse.PublishTargetNotFound(ex.Message, traceId));", controller, StringComparison.Ordinal);
        Assert.Contains("return UnprocessableEntity(AdminStoreConfigApiErrorResponse.InvalidConfig(ex.Message, traceId));", controller, StringComparison.Ordinal);
        Assert.Contains("StatusCodes.Status428PreconditionRequired", controller, StringComparison.Ordinal);
        Assert.Contains("AdminStoreConfigApiErrorResponse.PreconditionRequired(traceId)", controller, StringComparison.Ordinal);
        Assert.Contains("PublishTargetNotFound", errors, StringComparison.Ordinal);

        Assert.Contains("_publishLockService.TryAcquireAsync(tenantId, $\"page:{pageId}\")", service, StringComparison.Ordinal);
        Assert.Contains("throw new ConcurrencyConflictException(\"StoreConfigPublish\", tenantId);", service, StringComparison.Ordinal);
        Assert.Contains("var config = await GetRequiredStoreConfigForMutationAsync(", service, StringComparison.Ordinal);
        Assert.Contains("ApplyExpectedRowVersion(config, expectedRowVersion)", service, StringComparison.Ordinal);
        Assert.Contains("throw new KeyNotFoundException($\"Publish target '{pageId}' was not found.\");", service, StringComparison.Ordinal);
        Assert.Contains("throw new ArgumentException($\"No draft content to publish for {pageId}\");", service, StringComparison.Ordinal);
        Assert.Contains("var normalizedDraft = !string.IsNullOrWhiteSpace(templateId)", service, StringComparison.Ordinal);
        Assert.Contains("NormalizeBuilderPageComposition(draftConfig, pageContent, authoritativeTemplateId: templateId)", service, StringComparison.Ordinal);
        Assert.Contains("pageContent[\"draft\"] = normalizedDraft;", service, StringComparison.Ordinal);
        Assert.Contains("pageContent[\"live\"] = System.Text.Json.JsonSerializer.Deserialize<object>(", service, StringComparison.Ordinal);
        Assert.Contains("CreateAndAppendVersionSnapshot(config, \"Page Published\", actor);", service, StringComparison.Ordinal);
        Assert.Contains("await PersistStoreConfigChangeAsync(", service, StringComparison.Ordinal);
        Assert.Contains("StoreConfigPublishAction.Published", service, StringComparison.Ordinal);
        Assert.Contains("await _dbContext.SaveChangesAsync(cancellationToken);", service, StringComparison.Ordinal);
        Assert.Contains("new StoreConfigPublishedEvent", service, StringComparison.Ordinal);
    }

    [Fact]
    public void BuilderPublishProjectionRecovery_MustRecomposeFromSqlAndFenceStaleEvents()
    {
        var publicController = ReadRepoFile("PointOfSale.API/Controllers/StorefrontStoreConfigController.cs");
        var mapper = ReadRepoFile("PointOfSale.API/Services/Storefront/StorefrontDraftConfigMapper.cs");
        var projection = ReadRepoFile("PointOfSale.Infrastructure/Consumers/StorefrontConfigProjectionConsumer.cs");
        var lineage = ReadRepoFile("PointOfSale.Infrastructure/Reliability/TenantLineageAuthority.cs");

        Assert.Contains("_dbContext.StoreConfigs", publicController, StringComparison.Ordinal);
        Assert.Contains("StorefrontDraftConfigMapper.MapLive", publicController, StringComparison.Ordinal);
        Assert.Contains("useDraftContent: false", mapper, StringComparison.Ordinal);
        Assert.Contains("var selected = useDraftContent ? draft : live;", mapper, StringComparison.Ordinal);

        Assert.Contains("IConsumer<StoreConfigPublishedEvent>", projection, StringComparison.Ordinal);
        Assert.Contains("ProjectionEventFence.TryAcquireAsync", projection, StringComparison.Ordinal);
        Assert.Contains("LoadCurrentStoreConfigProjectionAuthorityAsync", projection, StringComparison.Ordinal);
        Assert.Contains("BuildFreshStoreConfigEventFilter", projection, StringComparison.Ordinal);
        Assert.Contains("filter.Ne(x => x.LastEventId, eventId)", projection, StringComparison.Ordinal);
        Assert.Contains("filter.Lt(x => x.LastEventVersion, eventVersion)", projection, StringComparison.Ordinal);
        Assert.Contains("await db.KeyDeleteAsync(new RedisKey[] { configKey });", projection, StringComparison.Ordinal);
        Assert.Contains("from storeConfig in dbContext.StoreConfigs.AsNoTracking().IgnoreQueryFilters()", lineage, StringComparison.Ordinal);
        Assert.Contains("storeConfig.Id == storeConfigId", lineage, StringComparison.Ordinal);
        Assert.Contains("storeConfig.TenantId == tenantId", lineage, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationArtifacts_MustRecordSfAdm27Truth()
    {
        Assert.Contains(
            "PASS SF-ADM2.7 Builder Publish Authority Certification",
            ReadRepoFile("docs/architecture/store-builder-publish-authority-certification-findings.md"),
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
            Assert.Contains("PASS SF-ADM2.7", ReadRepoFile(path), StringComparison.Ordinal);
        }
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
