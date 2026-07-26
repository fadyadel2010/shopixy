namespace PointOfSale.Tests.Architecture;

public sealed class MongoProjectionConvergenceCertificationTests
{
    [Fact]
    public void MongoProjectionDocumentsExposeFreshnessAndSqlAuthorityMarkers()
    {
        var product = ReadRepoFile("PointOfSale.Infrastructure/MongoDB/Documents/ProductDocument.cs");
        var category = ReadRepoFile("PointOfSale.Infrastructure/MongoDB/Documents/CategoryDocument.cs");
        var cms = ReadRepoFile("PointOfSale.Infrastructure/MongoDB/Documents/StorefrontPageDocument.cs");
        var commerceHubProduct = ReadRepoFile("PointOfSale.Infrastructure/MongoDB/Documents/CommerceHubProductDocument.cs");
        var marketplaceProduct = ReadRepoFile("PointOfSale.Infrastructure/MongoDB/Documents/Marketplace/MarketplaceProductReadModelDocument.cs");
        var marketplaceOrder = ReadRepoFile("PointOfSale.Infrastructure/MongoDB/Documents/Marketplace/MarketplaceOrderReadModelDocument.cs");
        var marketplaceHomepage = ReadRepoFile("PointOfSale.Infrastructure/MongoDB/Documents/Marketplace/MarketplaceHomepageReadModelDocument.cs");
        var affiliateIntelligence = ReadRepoFile("PointOfSale.Infrastructure/MongoDB/Documents/Affiliate/AffiliateProductIntelligenceDocument.cs");

        Assert.Contains("DefaultUnit", product, StringComparison.Ordinal);
        Assert.Contains("Units", product, StringComparison.Ordinal);
        Assert.Contains("LastEventVersion", product, StringComparison.Ordinal);
        Assert.Contains("LastMediaEventVersion", product, StringComparison.Ordinal);
        Assert.Contains("LastStockEventVersion", product, StringComparison.Ordinal);
        Assert.Contains("DeletedVariantTombstones", product, StringComparison.Ordinal);
        Assert.Contains("SyncedAtUtc", category, StringComparison.Ordinal);

        Assert.Contains("LastEventId", cms, StringComparison.Ordinal);
        Assert.Contains("LastEventOccurredAtUtc", cms, StringComparison.Ordinal);
        Assert.Contains("LastEventVersion", cms, StringComparison.Ordinal);

        Assert.Contains("LastListingEventVersion", commerceHubProduct, StringComparison.Ordinal);
        Assert.Contains("LastProductProjectionEventVersion", commerceHubProduct, StringComparison.Ordinal);
        Assert.Contains("LastProductLifecycleEventVersion", commerceHubProduct, StringComparison.Ordinal);
        Assert.Contains("LastStoreMetadataEventVersion", commerceHubProduct, StringComparison.Ordinal);
        Assert.Contains("SearchSyncStatus", commerceHubProduct, StringComparison.Ordinal);

        Assert.Contains("LastEntityMutationVersion", marketplaceProduct, StringComparison.Ordinal);
        Assert.Contains("ProjectionUpdatedAtUtc", marketplaceProduct, StringComparison.Ordinal);
        Assert.Contains("LastEntityMutationVersion", marketplaceOrder, StringComparison.Ordinal);
        Assert.Contains("ProjectionUpdatedAtUtc", marketplaceOrder, StringComparison.Ordinal);
        Assert.Contains("LastEventOccurredAtUtc", marketplaceHomepage, StringComparison.Ordinal);
        Assert.Contains("LastEventVersion", marketplaceHomepage, StringComparison.Ordinal);

        Assert.Contains("ProcessedEventIds", affiliateIntelligence, StringComparison.Ordinal);
        Assert.Contains("LastProductEventVersion", affiliateIntelligence, StringComparison.Ordinal);
        Assert.Contains("LastStockEventVersion", affiliateIntelligence, StringComparison.Ordinal);
        Assert.Contains("LastPolicyEventVersion", affiliateIntelligence, StringComparison.Ordinal);
        Assert.Contains("LastStoreProfileEventVersion", affiliateIntelligence, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductCategoryCmsAndHomepageMongoWritesStaySqlDerivedAndStaleDeleteCapable()
    {
        var productCreated = ReadRepoFile("PointOfSale.Infrastructure/Consumers/ProductCreatedEventConsumer.cs");
        var productUpdated = ReadRepoFile("PointOfSale.Infrastructure/Consumers/ProductUpdatedEventConsumer.cs");
        var productLifecycle = ReadRepoFile("PointOfSale.Infrastructure/Consumers/ProductVisibilityLifecycleProjectionConsumer.cs");
        var productWriter = ReadRepoFile("PointOfSale.Infrastructure/MongoDB/Services/ProductSqlAuthorityProjectionWriter.cs");
        var mutationBoundary = ReadRepoFile("PointOfSale.Infrastructure/MongoDB/Services/StorefrontProductProjectionMutationBoundary.cs");
        var adminDataSync = ReadRepoFile("PointOfSale.API/Controllers/AdminDataSyncController.cs");
        var cmsConsumer = ReadRepoFile("PointOfSale.Infrastructure/Consumers/CmsPageProjectionConsumer.cs");
        var marketplaceProjection = ReadRepoFile("PointOfSale.Infrastructure/ReadModels/Marketplace/MarketplaceReadModelProjectionServices.cs");

        Assert.Contains("IProductSqlAuthorityProjectionWriter", productCreated, StringComparison.Ordinal);
        Assert.Contains("IProductSqlAuthorityProjectionWriter", productUpdated, StringComparison.Ordinal);
        Assert.Contains("IProductSqlAuthorityProjectionWriter", productLifecycle, StringComparison.Ordinal);
        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyAsync", productWriter, StringComparison.Ordinal);
        Assert.Contains("ReviewedVisibilityTombstone", productWriter, StringComparison.Ordinal);
        Assert.Contains("ReviewedFullDocumentUpsert", mutationBoundary, StringComparison.Ordinal);
        Assert.Contains("TombstoneIfFreshAsync", mutationBoundary, StringComparison.Ordinal);

        Assert.Contains("CategoryLineageAuthority.LoadTenantSnapshotAsync", adminDataSync, StringComparison.Ordinal);
        Assert.Contains("DeleteStaleCategoryDocumentsAsync", adminDataSync, StringComparison.Ordinal);
        Assert.Contains("DeleteStaleShippingMethodDocumentsAsync", adminDataSync, StringComparison.Ordinal);
        Assert.Contains("SqlTruthRebuildGuard.RequireSqlAuthority", adminDataSync, StringComparison.Ordinal);

        Assert.Contains("ProjectionEventFence.TryAcquireAsync", cmsConsumer, StringComparison.Ordinal);
        Assert.Contains("TenantLineageAuthority.IsActiveTenantAsync", cmsConsumer, StringComparison.Ordinal);
        Assert.Contains("FailClosedCmsPageProjectionAsync", cmsConsumer, StringComparison.Ordinal);
        Assert.Contains("TombstoneProjectionIfFreshAsync", cmsConsumer, StringComparison.Ordinal);
        Assert.Contains("DerivativeProjectionMutationGuard.RequireReviewedMutation", cmsConsumer, StringComparison.Ordinal);

        Assert.Contains("SanitizeSectionsAsync", marketplaceProjection, StringComparison.Ordinal);
        Assert.Contains("SanitizeProjectedSectionsAsync", marketplaceProjection, StringComparison.Ordinal);
        Assert.Contains("LoadLiveListingIdsAsync", marketplaceProjection, StringComparison.Ordinal);
        Assert.Contains("CommerceHubCategoryLineageAuthority.LoadLiveIdsAsync", marketplaceProjection, StringComparison.Ordinal);
        Assert.Contains("DeleteIfFreshAsync", marketplaceProjection, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubAffiliateAndMarketplaceMongoProjectionsConvergeThroughSqlAuthority()
    {
        var commerceHubConsumer = ReadRepoFile("PointOfSale.Infrastructure/Consumers/CommerceHubProjectionConsumer.cs");
        var marketplaceProductConsumer = ReadRepoFile("PointOfSale.Infrastructure/Consumers/Marketplace/ProductReadModelConsumer.cs");
        var marketplaceProjection = ReadRepoFile("PointOfSale.Infrastructure/ReadModels/Marketplace/MarketplaceReadModelProjectionServices.cs");
        var marketplaceProductRepository = ReadRepoFile("PointOfSale.Infrastructure/MongoDB/Services/Marketplace/MarketplaceProductReadRepository.cs");
        var marketplaceHomepageRepository = ReadRepoFile("PointOfSale.Infrastructure/MongoDB/Services/Marketplace/MarketplaceHomepageReadRepository.cs");
        var affiliateIntelligence = ReadRepoFile("PointOfSale.Infrastructure/Consumers/Affiliate/AffiliateProductIntelligenceProjectionConsumer.cs");

        Assert.Contains("ProjectionConsumerEventFence", commerceHubConsumer, StringComparison.Ordinal);
        Assert.Contains("CommerceHubCategoryLineageAuthority", commerceHubConsumer, StringComparison.Ordinal);
        Assert.Contains("WherePubliclyVisibleLineage", commerceHubConsumer, StringComparison.Ordinal);
        Assert.Contains("WhereCurrentInventoryStockOwner", commerceHubConsumer, StringComparison.Ordinal);

        Assert.Contains("RebuildAsync", marketplaceProductConsumer, StringComparison.Ordinal);
        Assert.Contains("TenantLineageAuthority.IsActiveTenantAsync", marketplaceProjection, StringComparison.Ordinal);
        Assert.Contains("SqlTruthRebuildGuard.RequireSqlAuthority", marketplaceProjection, StringComparison.Ordinal);
        Assert.Contains("ProductVariantLifecycleAuthority.IsPubliclyVisibleLineage", marketplaceProjection, StringComparison.Ordinal);
        Assert.Contains("ProductVariantInventoryLineageAuthority.FilterCurrentInventoryStockOwnersAsync", marketplaceProjection, StringComparison.Ordinal);
        Assert.Contains("LoadLiveMarketplaceListingAsync", marketplaceProjection, StringComparison.Ordinal);

        Assert.Contains("LastEntityMutationVersion", marketplaceProductRepository, StringComparison.Ordinal);
        Assert.Contains("DeleteIfFreshAsync", marketplaceProductRepository, StringComparison.Ordinal);
        Assert.Contains("LastEventOccurredAtUtc", marketplaceHomepageRepository, StringComparison.Ordinal);
        Assert.Contains("DeleteIfFreshAsync", marketplaceHomepageRepository, StringComparison.Ordinal);

        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyAsync", affiliateIntelligence, StringComparison.Ordinal);
        Assert.Contains("IsProductVisibleInSqlAsync", affiliateIntelligence, StringComparison.Ordinal);
        Assert.Contains("DeleteProductIntelligenceIfFreshAsync", affiliateIntelligence, StringComparison.Ordinal);
        Assert.Contains("BuildFreshProductEventFilter", affiliateIntelligence, StringComparison.Ordinal);
        Assert.Contains("BuildFreshStockEventFilter", affiliateIntelligence, StringComparison.Ordinal);
        Assert.Contains("CurrencyNormalizationHelper.NormalizeCurrencyOrThrow", affiliateIntelligence, StringComparison.Ordinal);
    }

    [Fact]
    public void MongoProjectionDriftRecoveryAndGovernanceRemainDocumentedAndObservable()
    {
        var healthChecks = ReadRepoFile("PointOfSale.Infrastructure/HealthChecks/ProductionHealthChecks.cs");
        var telemetry = ReadRepoFile("PointOfSale.Infrastructure/Instrumentation/OperationalConvergenceTelemetry.cs");
        var findings = ReadRepoFile("docs/architecture/mongo-projection-convergence-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        Assert.Contains("OperationalConvergenceTelemetry.RecordDriftDiagnostic", healthChecks, StringComparison.Ordinal);
        Assert.Contains("ProjectionLagDetected", telemetry, StringComparison.Ordinal);
        Assert.Contains("ProjectionLagWithinTolerance", telemetry, StringComparison.Ordinal);
        Assert.Contains("OutboxReadinessCheckFailed", telemetry, StringComparison.Ordinal);
        Assert.Contains("DriftDetected", telemetry, StringComparison.Ordinal);

        foreach (var marker in new[]
        {
            "POS-ADMIN-CERTIFICATION-01 PASS 10.3",
            "Projection Inventory Findings",
            "Projection Consumer Findings",
            "Projection Freshness Findings",
            "Product Projection Findings",
            "Category/CMS Findings",
            "Homepage/Discovery Findings",
            "Rebuild Findings",
            "Repair Findings",
            "Drift Detection Findings",
            "Cross-System Convergence Findings",
            "Startup/Recovery Findings",
            "Chaos Findings",
            "Runtime Hardening Performed",
            "Monitoring Improvements",
            "Governance Updates",
            "Honest Remaining Debt"
        })
        {
            Assert.Contains(marker, findings, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("PASS 10.3 Mongo Projection Convergence", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS 10.3 Mongo Projection Convergence Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("Mongo projection convergence now has a documented SQL-derived certification boundary", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS 10.3 Mongo Projection Convergence Freeze", codexRules, StringComparison.Ordinal);
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
