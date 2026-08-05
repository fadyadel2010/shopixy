namespace PointOfSale.Tests.Architecture;

public sealed class StorefrontRecoveryRebuildCertificationTests
{
    [Fact]
    public void StorefrontProductProjectionRebuildAndSearchRepairStaySqlAuthorityBound()
    {
        var adminSync = ReadRepoFile("PointOfSale.API/Controllers/AdminSyncController.cs");
        var searchReindex = ReadRepoFile("PointOfSale.API/Controllers/SearchReindexController.cs");
        var adminSearch = ReadRepoFile("PointOfSale.API/Controllers/AdminSearchSyncController.cs");
        var productWriter = ReadRepoFile("PointOfSale.Infrastructure/MongoDB/Services/ProductSqlAuthorityProjectionWriter.cs");

        Assert.Contains("IProductSqlAuthorityProjectionWriter", adminSync, StringComparison.Ordinal);
        Assert.Contains("ApplySqlProductRebuildAuthorityAsync", adminSync, StringComparison.Ordinal);
        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyAsync", searchReindex, StringComparison.Ordinal);
        Assert.Contains("_indexService.DeleteProductAsync(tenantId, productId", searchReindex, StringComparison.Ordinal);
        Assert.Contains("ProductSearchDocumentMapper.MapFromProductDocument", searchReindex, StringComparison.Ordinal);
        Assert.Contains("SqlAuthorityOverlayKind.StorefrontCms", adminSearch, StringComparison.Ordinal);
        Assert.Contains("DeleteStaleCmsPagesAsync", adminSearch, StringComparison.Ordinal);
        Assert.Contains("CategoryLineageAuthority", adminSearch, StringComparison.Ordinal);
        Assert.Contains("DeleteStaleCategoriesAsync", adminSearch, StringComparison.Ordinal);
        Assert.Contains("ReviewedVisibilityTombstone", productWriter, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubRecoveryReindexAndStartupHealingStaySqlTenantAuthorityBound()
    {
        var reindex = ReadRepoFile("PointOfSale.Infrastructure/Jobs/CommerceHubReindexService.cs");
        var autoHealing = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubAutoHealingService.cs");
        var analytics = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubSearchAnalyticsBackgroundService.cs");

        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", reindex, StringComparison.Ordinal);
        Assert.Contains("ReconcileMissingSqlListingsAsync", reindex, StringComparison.Ordinal);
        Assert.Contains("CountSqlVisibleCommerceHubListingsAsync", reindex, StringComparison.Ordinal);
        Assert.Contains("targetCount.Value == publishedCount", reindex, StringComparison.Ordinal);
        Assert.Contains("CommerceHubCategoryLineageAuthority.LoadLiveIdsAsync", reindex, StringComparison.Ordinal);

        Assert.Contains("private readonly record struct RecoveryIdentity(Guid? TenantId, Guid ListingId)", autoHealing, StringComparison.Ordinal);
        Assert.Contains("EnqueueDocumentsByTenant", autoHealing, StringComparison.Ordinal);
        Assert.Contains(".GroupBy(document => new RecoveryIdentity(document.TenantId, document.Id))", autoHealing, StringComparison.Ordinal);
        Assert.Contains("Filter.Eq(x => x.TenantId, item.TenantId)", autoHealing, StringComparison.Ordinal);
        Assert.Contains("new RecoveryIdentity(queuedItem.TenantId, queuedItem.ListingId)", autoHealing, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", autoHealing, StringComparison.Ordinal);
        Assert.Contains("indexService.DeleteAsync(", autoHealing, StringComparison.Ordinal);

        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", analytics, StringComparison.Ordinal);
        Assert.Contains("indexService.DeleteAsync", analytics, StringComparison.Ordinal);
    }

    [Fact]
    public void CacheReplayRepairAndDeletionConvergenceStayDerivativeOnly()
    {
        var redisInvalidation = ReadRepoFile("PointOfSale.API/Consumers/RedisInvalidationConsumer.cs");
        var productCache = ReadRepoFile("PointOfSale.Infrastructure/Redis/Services/StorefrontProductCacheInvalidator.cs");
        var discountRepair = ReadRepoFile("PointOfSale.API/Services/ReadModels/DiscountReadModelRepairService.cs");
        var adminDataSync = ReadRepoFile("PointOfSale.API/Controllers/AdminDataSyncController.cs");

        Assert.Contains("ProjectionEventFence.TryAcquireAsync", redisInvalidation, StringComparison.Ordinal);
        Assert.Contains("RunIntegrationInvalidationWithFenceAsync", redisInvalidation, StringComparison.Ordinal);
        Assert.Contains("RunMarketplaceInvalidationWithFenceAsync", redisInvalidation, StringComparison.Ordinal);
        Assert.Contains("CacheKeyBuilder.ProductsPatternKey", productCache, StringComparison.Ordinal);
        Assert.Contains("CacheKeyBuilder.ProductByIdKey", productCache, StringComparison.Ordinal);
        Assert.Contains("AdvanceInvalidationGenerationAsync", productCache, StringComparison.Ordinal);

        Assert.Contains("SqlTruthRebuildGuard.RequireSqlAuthority", discountRepair, StringComparison.Ordinal);
        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyAsync", discountRepair, StringComparison.Ordinal);
        Assert.Contains("_productSearchIndexService.DeleteProductAsync", discountRepair, StringComparison.Ordinal);
        Assert.Contains("DeleteManyAsync(staleFilter", discountRepair, StringComparison.Ordinal);

        Assert.Contains("SqlTruthRebuildGuard.RequireSqlAuthority", adminDataSync, StringComparison.Ordinal);
        Assert.Contains("DeleteManyAsync(staleFilter", adminDataSync, StringComparison.Ordinal);
        Assert.Contains("CategoryLineageAuthority", adminDataSync, StringComparison.Ordinal);
    }

    [Fact]
    public void Pass96DocumentationRecordsRecoveryGuaranteesDebtAndGovernance()
    {
        var findings = ReadRepoFile("docs/architecture/storefront-recovery-rebuild-certification-findings.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        foreach (var marker in new[]
        {
            "POS-ADMIN-CERTIFICATION-01 PASS 9.6",
            "Projection Rebuild Certification",
            "Search Rebuild Certification",
            "Cache Rehydration Certification",
            "Startup Bootstrap Certification",
            "Replay Certification",
            "Repair Tool Certification",
            "Deletion Convergence Certification",
            "CommerceHub Recovery Certification",
            "Tenant Isolation Certification",
            "Recovery Resurrection Chaos Certification",
            "Remaining Debt"
        })
        {
            Assert.Contains(marker, findings, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("PASS 9.6 Storefront Recovery / Rebuild Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS 9.6 Storefront Recovery / Rebuild Freeze", codexRules, StringComparison.Ordinal);
        Assert.Contains("Storefront recovery, rebuild, repair, startup, and cache rehydration paths now have a documented SQL-authoritative certification boundary", knownRisks, StringComparison.Ordinal);
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
