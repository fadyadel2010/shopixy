namespace PointOfSale.Tests.Architecture;

public sealed class CrossSystemConvergenceCertificationTests
{
    [Fact]
    public void WholeSystemConvergenceInventoryMustMapSqlMongoRedisAndSearchSurfaces()
    {
        var findings = ReadRepoFile("docs/architecture/cross-system-convergence-certification-findings.md");

        foreach (var marker in new[]
        {
            "PostgreSQL -> Outbox -> Consumers -> MongoDB -> Redis -> Elasticsearch",
            "ProductDocument",
            "CategoryDocument",
            "StorefrontPageDocument",
            "CommerceHubProductDocument",
            "MarketplaceHomepageReadModelDocument",
            "AffiliateProductIntelligence",
            "ProductSearchDocument",
            "CategorySearchDocument",
            "CmsPageSearchDocument",
            "CommerceHubProductSearchDocument",
            "PublicAffiliateProductSearchDocument",
            "marketplace_services"
        })
        {
            Assert.Contains(marker, findings, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ProductCategoryCmsHomepageDiscoveryAndCommerceHubPathsStaySqlFirstAcrossStores()
    {
        var productWriter = ReadRepoFile("PointOfSale.Infrastructure/MongoDB/Services/ProductSqlAuthorityProjectionWriter.cs");
        var productSearchOverlay = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/ProductSearchSqlAuthorityOverlay.cs");
        var productCacheInvalidation = ReadRepoFile("PointOfSale.Infrastructure/Consumers/ProductCacheInvalidationConsumer.cs");
        var adminDataSync = ReadRepoFile("PointOfSale.API/Controllers/AdminDataSyncController.cs");
        var adminSearchSync = ReadRepoFile("PointOfSale.API/Controllers/AdminSearchSyncController.cs");
        var cmsConsumer = ReadRepoFile("PointOfSale.Infrastructure/Consumers/CmsPageProjectionConsumer.cs");
        var homepageRead = ReadRepoFile("PointOfSale.API/Services/Homepage/HomepageSectionReadService.cs");
        var commerceHubQuery = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubQueryService.cs");
        var commerceHubReindex = ReadRepoFile("PointOfSale.Infrastructure/Jobs/CommerceHubReindexService.cs");
        var affiliateDiscovery = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/AffiliateProductDiscoveryService.cs");

        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyAsync", productWriter, StringComparison.Ordinal);
        Assert.Contains("ReviewedVisibilityTombstone", productWriter, StringComparison.Ordinal);
        Assert.Contains("TenantLineageAuthority.IsActiveTenantAsync", productSearchOverlay, StringComparison.Ordinal);
        Assert.Contains("ResolveCurrentSqlProductSlugAsync", productCacheInvalidation, StringComparison.Ordinal);
        Assert.Contains("ProjectionConsumerEventFence.TryAcquireAsync", productCacheInvalidation, StringComparison.Ordinal);

        Assert.Contains("CategoryLineageAuthority.LoadTenantSnapshotAsync", adminDataSync, StringComparison.Ordinal);
        Assert.Contains("DeleteStaleCategoryDocumentsAsync", adminDataSync, StringComparison.Ordinal);
        Assert.Contains("DeleteStaleCategoriesAsync", adminSearchSync, StringComparison.Ordinal);
        Assert.Contains("SqlAuthorityOverlayKind.StorefrontCms", adminSearchSync, StringComparison.Ordinal);
        Assert.Contains("FailClosedCmsPageProjectionAsync", cmsConsumer, StringComparison.Ordinal);

        Assert.Contains("SanitizeDiscoveryReferencesAsync", homepageRead, StringComparison.Ordinal);
        Assert.Contains("RecomposeVisibleSuggestionProductsAsync", commerceHubQuery, StringComparison.Ordinal);
        Assert.Contains("MapSqlVisibleFacetsAsync", commerceHubQuery, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", commerceHubReindex, StringComparison.Ordinal);
        Assert.Contains("CountSqlVisibleCommerceHubListingsAsync", commerceHubReindex, StringComparison.Ordinal);
        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyAsync", affiliateDiscovery, StringComparison.Ordinal);
    }

    [Fact]
    public void DriftRepairStartupFreshnessAndChaosControlsRemainObservableAndRecoverable()
    {
        var healthChecks = ReadRepoFile("PointOfSale.Infrastructure/HealthChecks/ProductionHealthChecks.cs");
        var commerceHubSearchHealth = ReadRepoFile("PointOfSale.API/HealthChecks/CommerceHubSearchSyncHealthCheck.cs");
        var serviceDriftJob = ReadRepoFile("PointOfSale.Infrastructure/Jobs/ElasticsearchDriftReconciliationJob.cs");
        var redisCache = ReadRepoFile("PointOfSale.Infrastructure/Redis/Services/RedisJsonCacheService.cs");
        var telemetry = ReadRepoFile("PointOfSale.Infrastructure/Instrumentation/OperationalConvergenceTelemetry.cs");
        var autoHealing = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubAutoHealingService.cs");
        var projectionFence = ReadRepoFile("PointOfSale.Infrastructure/Reliability/ProjectionEventFence.cs");
        var sqlTruthGuard = ReadRepoFile("PointOfSale.Infrastructure/Reliability/SqlTruthRebuildGuard.cs");

        Assert.Contains("ProjectionDriftHealthCheck", healthChecks, StringComparison.Ordinal);
        Assert.Contains("ProjectionOutboxHealthCheck", healthChecks, StringComparison.Ordinal);
        Assert.Contains("MassTransitBusOutboxReadinessHealthCheck", healthChecks, StringComparison.Ordinal);
        Assert.Contains("WarningLag = TimeSpan.FromMinutes(5)", healthChecks, StringComparison.Ordinal);
        Assert.Contains("CriticalLag = TimeSpan.FromMinutes(15)", healthChecks, StringComparison.Ordinal);
        Assert.Contains("OperationalConvergenceTelemetry.RecordDriftDiagnostic", healthChecks, StringComparison.Ordinal);

        Assert.Contains("CountSqlVisibleCommerceHubListingsAsync", commerceHubSearchHealth, StringComparison.Ordinal);
        Assert.Contains("ratio >= 0.95d", commerceHubSearchHealth, StringComparison.Ordinal);
        Assert.Contains("ratio >= 0.80d", commerceHubSearchHealth, StringComparison.Ordinal);
        Assert.Contains("OperationalConvergenceAuthority.SearchIndex", commerceHubSearchHealth, StringComparison.Ordinal);

        Assert.Contains("SqlTruthRebuildGuard.RequireGlobalSqlAuthority", serviceDriftJob, StringComparison.Ordinal);
        Assert.Contains("extraInEs", serviceDriftJob, StringComparison.Ordinal);
        Assert.Contains("missingInEs", serviceDriftJob, StringComparison.Ordinal);

        Assert.Contains("CacheEnvelope<T>", redisCache, StringComparison.Ordinal);
        Assert.Contains("ShouldBypassForProjectionLag", redisCache, StringComparison.Ordinal);
        Assert.Contains("ProjectionLagDetected", telemetry, StringComparison.Ordinal);
        Assert.Contains("DriftDetected", telemetry, StringComparison.Ordinal);
        Assert.Contains("DriftWithinTolerance", telemetry, StringComparison.Ordinal);

        Assert.Contains("RecoveryIdentity(Guid? TenantId, Guid ListingId)", autoHealing, StringComparison.Ordinal);
        Assert.Contains("ProjectionEventFence", projectionFence, StringComparison.Ordinal);
        Assert.Contains("RecordRebuildDiagnostic", sqlTruthGuard, StringComparison.Ordinal);
    }

    [Fact]
    public void Pass105DocumentationAndGovernanceMustStayUpdated()
    {
        var findings = ReadRepoFile("docs/architecture/cross-system-convergence-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        foreach (var marker in new[]
        {
            "POS-ADMIN-CERTIFICATION-01 PASS 10.5",
            "Convergence Inventory Findings",
            "Product Convergence Findings",
            "Category/CMS Convergence Findings",
            "Homepage/Discovery Convergence Findings",
            "Drift Findings",
            "Repair/Recovery Findings",
            "Startup Findings",
            "Freshness Findings",
            "Drift Detection Findings",
            "Convergence SLO Findings",
            "Chaos Findings",
            "Runtime Hardening Performed",
            "Monitoring Improvements",
            "Governance Updates",
            "Honest Remaining Debt"
        })
        {
            Assert.Contains(marker, findings, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("PASS 10.5 Cross-System Convergence Certification", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS 10.5 Cross-System Convergence Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("Cross-system convergence now has a documented whole-system certification boundary", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS 10.5 Cross-System Convergence Freeze", codexRules, StringComparison.Ordinal);
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
