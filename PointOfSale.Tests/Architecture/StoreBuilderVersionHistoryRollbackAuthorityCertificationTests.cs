namespace PointOfSale.Tests.Architecture;

public sealed class StoreBuilderVersionHistoryRollbackAuthorityCertificationTests
{
    [Fact]
    public void BuilderVersionHistoryFrontend_MustPresentBackendVersionsAndSubmitRollbackIntentOnly()
    {
        var builder = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/storefront/pages/StorefrontBuilderPage.jsx");
        var history = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/storefront/pages/VersionHistoryPage.jsx");
        var modal = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/storefront/components/builder/RollbackModal.jsx");
        var service = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/storefront/services/storeConfigService.js");

        Assert.Contains("storeConfigService.getVersionHistory()", builder, StringComparison.Ordinal);
        Assert.Contains("storeConfigService.getVersionHistory()", history, StringComparison.Ordinal);
        Assert.Contains("api.get('/admin/store-config/versions', withNoStoreHeaders(options))", service, StringComparison.Ordinal);

        Assert.Contains("const rowVersion = requireWorkspaceAdmission()", builder, StringComparison.Ordinal);
        Assert.Contains("buildIfMatchOptions(rowVersion)", builder, StringComparison.Ordinal);
        Assert.Contains("await loadPageData(targetPage, templateIdFromUrl)", builder, StringComparison.Ordinal);

        Assert.Contains("deriveCurrentStoreIdentityPresentation(", history, StringComparison.Ordinal);
        Assert.Contains("await storeConfigService.getStoreConfig()", history, StringComparison.Ordinal);
        Assert.Contains("Rollback requires current backend RowVersion authority.", history, StringComparison.Ordinal);
        Assert.Contains("'If-Match': `\"${rowVersion}\"`", history, StringComparison.Ordinal);
        Assert.Contains("Rollback response authority was not admitted.", history, StringComparison.Ordinal);
        Assert.Contains("await loadVersions()", history, StringComparison.Ordinal);

        Assert.Contains("onConfirm(selectedVersion.version, reason)", modal, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage", history, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sessionStorage", history, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuilderRollbackBackend_MustOwnVersionResolutionSnapshotTransactionAndConcurrency()
    {
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AdminStoreConfigController.cs");
        var service = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Admin/AdminStoreConfigService.cs");

        Assert.Contains("[Authorize(Roles = \"Owner,Admin\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"versions\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpPost(\"rollback/{version}\")]", controller, StringComparison.Ordinal);
        Assert.Contains("ValidateTenantHeader(traceId)", controller, StringComparison.Ordinal);
        Assert.Contains("var precondition = RequireExpectedRowVersion(traceId);", controller, StringComparison.Ordinal);
        Assert.Contains("expectedRowVersion: precondition.RowVersion", controller, StringComparison.Ordinal);
        Assert.Contains("StatusCodes.Status428PreconditionRequired", controller, StringComparison.Ordinal);
        Assert.Contains("return Conflict(AdminStoreConfigApiErrorResponse.ConcurrencyConflict(traceId));", controller, StringComparison.Ordinal);
        Assert.Contains("Code = \"ROLLBACK_REJECTED\"", controller, StringComparison.Ordinal);

        Assert.Contains("GetVersionHistoryAsync(", service, StringComparison.Ordinal);
        Assert.Contains(".AsNoTracking()", service, StringComparison.Ordinal);
        Assert.Contains("IsCurrent = s.Version == config.LiveVersion", service, StringComparison.Ordinal);
        Assert.Contains("_publishLockService.TryAcquireAsync(tenantId, \"rollback\")", service, StringComparison.Ordinal);
        Assert.Contains("GetRequiredStoreConfigForMutationAsync(", service, StringComparison.Ordinal);
        Assert.Contains("ApplyExpectedRowVersion(config, expectedRowVersion)", service, StringComparison.Ordinal);
        Assert.Contains("versions.FirstOrDefault(v => v.Version == versionNumber)", service, StringComparison.Ordinal);
        Assert.Contains("ParseCustomizationRootStrict(targetVersion.CustomizationJson)", service, StringComparison.Ordinal);
        Assert.Contains("ValidateRollbackThemeAuthorityAsync(targetVersion, cancellationToken)", service, StringComparison.Ordinal);
        Assert.Contains("ExtractHistoricalLiveState(snapshotCustomization)", service, StringComparison.Ordinal);
        Assert.Contains("MergeSnapshotLiveWithCurrentDrafts(", service, StringComparison.Ordinal);
        Assert.Contains("PreserveDraftBranches(currentRoot, mergedRoot)", service, StringComparison.Ordinal);
        Assert.Contains("CreateAndAppendVersionSnapshot(", service, StringComparison.Ordinal);
        Assert.Contains("restoredFromVersion: versionNumber", service, StringComparison.Ordinal);
        Assert.Contains("StoreConfigPublishAction.RolledBack", service, StringComparison.Ordinal);
        Assert.Contains("new StoreConfigRolledBackEvent", service, StringComparison.Ordinal);
        Assert.Contains("await _dbContext.SaveChangesAsync(cancellationToken);", service, StringComparison.Ordinal);
        Assert.DoesNotContain("config.LiveVersion = versionNumber", service, StringComparison.Ordinal);
    }

    [Fact]
    public void BuilderRollbackProjectionRecovery_MustRecomposeFromCurrentSqlAndInvalidateDerivativeCaches()
    {
        var projection = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Consumers/StorefrontConfigProjectionConsumer.cs");
        var lineage = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Reliability/TenantLineageAuthority.cs");

        Assert.Contains("IConsumer<StoreConfigRolledBackEvent>", projection, StringComparison.Ordinal);
        Assert.Contains("ProjectionEventFence.TryAcquireAsync", projection, StringComparison.Ordinal);
        Assert.Contains("LoadCurrentStoreConfigProjectionAuthorityAsync", projection, StringComparison.Ordinal);
        Assert.Contains("MapToMongoDocument(storeConfig, tenant, themePreset, tenantCurrency, smartOffers)", projection, StringComparison.Ordinal);
        Assert.Contains("BuildFreshStoreConfigEventFilter", projection, StringComparison.Ordinal);
        Assert.Contains("await db.KeyDeleteAsync(new RedisKey[] { configKey });", projection, StringComparison.Ordinal);
        Assert.Contains("from storeConfig in dbContext.StoreConfigs.AsNoTracking().IgnoreQueryFilters()", lineage, StringComparison.Ordinal);
        Assert.Contains("storeConfig.Id == storeConfigId", lineage, StringComparison.Ordinal);
        Assert.Contains("storeConfig.TenantId == tenantId", lineage, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationArtifacts_MustRecordSfAdm29Truth()
    {
        Assert.Contains(
            "PASS SF-ADM2.9 Builder Version History & Rollback Authority Certification",
            CertificationSource.ReadRepoFile("docs/architecture/store-builder-version-history-rollback-authority-certification-findings.md"),
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
            Assert.Contains("PASS SF-ADM2.9", CertificationSource.ReadRepoFile(path), StringComparison.Ordinal);
        }
    }
}
