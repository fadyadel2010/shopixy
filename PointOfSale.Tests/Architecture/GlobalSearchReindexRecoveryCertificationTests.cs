namespace PointOfSale.Tests.Architecture;

public sealed class GlobalSearchReindexRecoveryCertificationTests
{
    [Fact]
    public void OperationalReindexRepairRecoveryAndHealingMustStaySqlAuthoritative()
    {
        var commerceHubReindex = ReadRepoFile("PointOfSale.Infrastructure/Jobs/CommerceHubReindexService.cs");
        var searchIndex = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchIndexService.cs");
        var autoHealing = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubAutoHealingService.cs");
        var adminSearch = ReadRepoFile("PointOfSale.API/Controllers/AdminSearchSyncController.cs");
        var tenantReindex = ReadRepoFile("PointOfSale.API/Controllers/SearchReindexController.cs");
        var serviceDrift = ReadRepoFile("PointOfSale.Infrastructure/Jobs/ElasticsearchDriftReconciliationJob.cs");

        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", commerceHubReindex, StringComparison.Ordinal);
        Assert.Contains("LoadSqlVisibleListingIdentitiesAsync", commerceHubReindex, StringComparison.Ordinal);
        Assert.Contains("DeleteStaleSearchDocumentsAsync", commerceHubReindex, StringComparison.Ordinal);
        Assert.Contains("DeleteStaleAsync", searchIndex, StringComparison.Ordinal);
        Assert.Contains("targetCount.Value == publishedCount", commerceHubReindex, StringComparison.Ordinal);
        Assert.Contains("PromoteIndexAsync", commerceHubReindex, StringComparison.Ordinal);

        Assert.Contains("RecoveryIdentity(Guid? TenantId, Guid ListingId)", autoHealing, StringComparison.Ordinal);
        Assert.Contains("EnqueueStartupRecoveryAsync", autoHealing, StringComparison.Ordinal);
        Assert.Contains("RunBackgroundReconciliationAsync", autoHealing, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", autoHealing, StringComparison.Ordinal);

        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyAsync", adminSearch, StringComparison.Ordinal);
        Assert.Contains("DeleteStaleCategoriesAsync", adminSearch, StringComparison.Ordinal);
        Assert.Contains("DeleteStaleCmsPagesAsync", adminSearch, StringComparison.Ordinal);
        Assert.Contains("GLOBAL_SEARCH_CONTROL_FORBIDDEN", tenantReindex, StringComparison.Ordinal);
        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyAsync", tenantReindex, StringComparison.Ordinal);

        Assert.Contains("SqlTruthRebuildGuard.RequireGlobalSqlAuthority", serviceDrift, StringComparison.Ordinal);
        Assert.Contains("LoadLiveServiceSearchAuthorityAsync", serviceDrift, StringComparison.Ordinal);
        Assert.Contains("extraInEs", serviceDrift, StringComparison.Ordinal);
        Assert.Contains("missingInEs", serviceDrift, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplayRecoveryStartupAndAuditabilityMustRemainObservableAndTenantSafe()
    {
        var projectionCompensation = ReadRepoFile("PointOfSale.Infrastructure/Eventing/ProjectionFailureCompensation.cs");
        var marketplaceOutbox = ReadRepoFile("PointOfSale.Infrastructure/Eventing/MarketplaceProjectionOutboxProcessor.cs");
        var stockOutbox = ReadRepoFile("PointOfSale.Infrastructure/Eventing/StockSyncOutboxProcessor.cs");
        var hangfire = ReadRepoFile("PointOfSale.API/Jobs/HangfireInitializationHostedService.cs");
        var autoHealing = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubAutoHealingService.cs");
        var platformAudit = ReadRepoFile("PointOfSale.API/SuperAdmin/Infrastructure/SuperAdminOperationalAuditMiddleware.cs");

        Assert.Contains("BuildMarketplaceReplayAsync", projectionCompensation, StringComparison.Ordinal);
        Assert.Contains("AuthoritativeReplay", projectionCompensation, StringComparison.Ordinal);
        Assert.Contains("BuildProductUpdatedReplayAsync", projectionCompensation, StringComparison.Ordinal);
        Assert.Contains("_db.Products", projectionCompensation, StringComparison.Ordinal);
        Assert.Contains("ProjectionCompensationReplay", projectionCompensation, StringComparison.Ordinal);

        Assert.Contains("OperationalConvergenceTelemetry.RecordReplayDecision", marketplaceOutbox, StringComparison.Ordinal);
        Assert.Contains("OperationalConvergenceTelemetry.RecordReplayDecision", stockOutbox, StringComparison.Ordinal);
        Assert.Contains("ClaimedUntilUtc", marketplaceOutbox, StringComparison.Ordinal);

        Assert.Contains("CommerceHubAutoHealingService", autoHealing, StringComparison.Ordinal);
        Assert.Contains("StartupRecoveryReason", autoHealing, StringComparison.Ordinal);
        Assert.Contains("EnqueueDocumentsByTenant", autoHealing, StringComparison.Ordinal);
        Assert.Contains("Filter.Eq(x => x.TenantId, item.TenantId)", autoHealing, StringComparison.Ordinal);

        Assert.Contains("CommerceHubReindexService", hangfire, StringComparison.Ordinal);
        Assert.Contains("ElasticsearchDriftReconciliationJob", hangfire, StringComparison.Ordinal);
        Assert.Contains("commercehub-search-reconciliation", hangfire, StringComparison.Ordinal);
        Assert.Contains("PLATFORM_OPERATION_COMPLETED", platformAudit, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationDocumentationAndGovernanceMustRecordPassElevenFour()
    {
        var findings = ReadRepoFile("docs/architecture/global-search-reindex-recovery-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");

        foreach (var marker in new[]
        {
            "POS-ADMIN-CERTIFICATION-01 PASS 11.4",
            "1. Operational Inventory Findings",
            "2. Reindex Findings",
            "3. Search Repair Findings",
            "4. Recovery Findings",
            "5. Replay Findings",
            "6. Auto-Healing Findings",
            "7. Startup Findings",
            "8. Governance-Preservation Findings",
            "9. Auditability Findings",
            "10. Tenant-Boundary Findings",
            "11. Chaos Findings",
            "12. Runtime Hardening Performed",
            "13. Monitoring Improvements",
            "14. Governance Updates",
            "15. Honest Remaining Debt"
        })
        {
            Assert.Contains(marker, findings, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("PASS 11.4 Global Search, Reindex & Recovery", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS 11.4 Global Search / Reindex / Recovery Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("Global search, reindex, and recovery authority now has a documented certification boundary", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS 11.4 Global Search / Reindex / Recovery Freeze", codexRules, StringComparison.Ordinal);
        Assert.Contains("PASS 11.4 Global Search, Reindex & Recovery", hardening, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string FindRepoRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root could not be found.");
    }
}
