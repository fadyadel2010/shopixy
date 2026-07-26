namespace PointOfSale.Tests.Architecture;

public sealed class OperationalToolingCertificationFreezeTests
{
    [Fact]
    public void AdminSearchRepair_MustStaySqlAuthoritativeAndFailClosed()
    {
        var adminSearchSync = File.ReadAllText(ApiPath("Controllers", "AdminSearchSyncController.cs"));

        Assert.Contains("_tenantContext.RequiredTenantId", adminSearchSync, StringComparison.Ordinal);
        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyAsync", adminSearchSync, StringComparison.Ordinal);
        Assert.Contains("ProductSearchDocumentMapper.MapFromProductDocument", adminSearchSync, StringComparison.Ordinal);
        Assert.Contains("DeleteProductFromSearchAsync", adminSearchSync, StringComparison.Ordinal);
        Assert.Contains("DeleteStaleCategoriesAsync", adminSearchSync, StringComparison.Ordinal);
        Assert.Contains("staleDeletedFromSearch", adminSearchSync, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildProductSyncQuery(", adminSearchSync, StringComparison.Ordinal);
        Assert.DoesNotContain("baseline sync", adminSearchSync, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AdminProductReadModelRepair_CacheInvalidationMustStayBehindReviewedBoundary()
    {
        var adminSync = File.ReadAllText(ApiPath("Controllers", "AdminSyncController.cs"));

        Assert.Contains("IProductSqlAuthorityProjectionWriter", adminSync, StringComparison.Ordinal);
        Assert.Contains("IProductCacheService", adminSync, StringComparison.Ordinal);
        Assert.Contains("InvalidateAllProductsAsync(tenantId, cancellationToken)", adminSync, StringComparison.Ordinal);
        Assert.Contains("InvalidateProductAsync(", adminSync, StringComparison.Ordinal);
        Assert.DoesNotContain("IConnectionMultiplexer", adminSync, StringComparison.Ordinal);
        Assert.DoesNotContain("GetServer(", adminSync, StringComparison.Ordinal);
        Assert.DoesNotContain("KeyDeleteAsync(", adminSync, StringComparison.Ordinal);
    }

    [Fact]
    public void AffiliateRepairPrimitive_MustStaySqlAuthoritativeAndFailClosedOnMissingCurrencyAuthority()
    {
        var script = File.ReadAllText(InfrastructurePath("Scripts", "AF16E_ReindexAffiliateProductsWithStorefrontUrl.cs"));

        Assert.Contains("CurrencyNormalizationHelper.NormalizeCurrencyOrThrow", script, StringComparison.Ordinal);
        Assert.Contains("SqlTruthRebuildGuard.RequireSqlAuthority", script, StringComparison.Ordinal);
        Assert.Contains("SqlTruthRebuildTarget.Affiliate", script, StringComparison.Ordinal);
        Assert.Contains("RemoveStalePublicAffiliateProductAsync", script, StringComparison.Ordinal);
        Assert.Contains("StaleIntelligenceProductsDeleted", script, StringComparison.Ordinal);
        Assert.DoesNotContain("? \"USD\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain(": \"USD\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void AdminMongoSyncRepair_MustDeclareSqlAuthorityAndDeleteStaleDerivativeRows()
    {
        var adminDataSync = File.ReadAllText(ApiPath("Controllers", "AdminDataSyncController.cs"));

        Assert.Contains("SqlTruthRebuildGuard.RequireSqlAuthority", adminDataSync, StringComparison.Ordinal);
        Assert.Contains("SqlAuthorityOverlayKind.Category", adminDataSync, StringComparison.Ordinal);
        Assert.Contains("SqlAuthorityOverlayKind.Shipping", adminDataSync, StringComparison.Ordinal);
        Assert.Contains("DeleteStaleCategoryDocumentsAsync", adminDataSync, StringComparison.Ordinal);
        Assert.Contains("DeleteStaleShippingMethodDocumentsAsync", adminDataSync, StringComparison.Ordinal);
        Assert.Contains("DeleteManyAsync", adminDataSync, StringComparison.Ordinal);
    }

    [Fact]
    public void Pass6Documentation_MustRecordOperationalToolingFindingAndFreeze()
    {
        var findings = File.ReadAllText(DocsPath("architecture", "operational-tooling-certification-findings.md"));
        var runbook = File.ReadAllText(DocsPath("operations", "operational-recovery-runbooks.md"));
        var primitives = File.ReadAllText(DocsPath("architecture", "dangerous-operational-primitives.md"));
        var projectContext = File.ReadAllText(ContextPath("PROJECT_CONTEXT.md"));
        var architectureTruth = File.ReadAllText(ContextPath("ARCHITECTURE_TRUTH.md"));
        var knownRisks = File.ReadAllText(ContextPath("KNOWN_RISKS.md"));
        var rules = File.ReadAllText(ContextPath("CODEX_RULES.md"));

        foreach (var marker in new[]
        {
            "POS-ADMIN-CERTIFICATION-01 PASS 6 Operational Tooling & Dangerous Primitive Certification",
            "AdminSearchSyncController",
            "ProductSearchSqlAuthorityOverlay",
            "ProductSearchDocumentMapper.MapFromProductDocument",
            "AdminSyncController",
            "IProductCacheService",
            "AdminDataSyncController",
            "DeleteStaleCategoryDocumentsAsync",
            "DeleteStaleShippingMethodDocumentsAsync",
            "staleDeleted",
            "DeleteStaleCategoriesAsync",
            "staleDeletedFromSearch",
            "AF16E_ReindexAffiliateProductsWithStorefrontUrl",
            "CurrencyNormalizationHelper.NormalizeCurrencyOrThrow",
            "SqlTruthRebuildGuard.RequireSqlAuthority",
            "baseline operational search repair",
            "one-way Mongo sync",
            "category search sync",
            "Remaining Operational-Risk Debt",
            "Regression Freeze Invariants"
        })
        {
            Assert.Contains(marker, findings, StringComparison.Ordinal);
        }

        Assert.Contains("partial SQL-row search rebuild", runbook, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("one-way Mongo sync", runbook, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("direct Redis key mutation", runbook, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fallback currency", runbook, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PASS 6 Operational Tooling Freeze", primitives, StringComparison.Ordinal);
        Assert.Contains("PASS 6 Operational Tooling & Dangerous Primitive Certification", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS 6 Operational Tooling Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("Admin search repair can no longer rebuild storefront product search visibility", knownRisks, StringComparison.Ordinal);
        Assert.Contains("Admin category search sync now deletes stale Elasticsearch visibility missing from SQL authority", knownRisks, StringComparison.Ordinal);
        Assert.Contains("Admin product rebuild no longer mutates Redis keys directly", knownRisks, StringComparison.Ordinal);
        Assert.Contains("Admin data sync no longer preserves stale category or shipping-method Mongo documents", knownRisks, StringComparison.Ordinal);
        Assert.Contains("Affiliate repair/reindex can no longer inject fallback tenant currency", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS 6 Operational Tooling Freeze", rules, StringComparison.Ordinal);
    }

    private static string ApiPath(params string[] parts) =>
        Path.Combine([DiscoverSolutionRoot(AppContext.BaseDirectory), "PointOfSale.API", .. parts]);

    private static string DocsPath(params string[] parts) =>
        Path.Combine([DiscoverSolutionRoot(AppContext.BaseDirectory), "docs", .. parts]);

    private static string InfrastructurePath(params string[] parts) =>
        Path.Combine([DiscoverSolutionRoot(AppContext.BaseDirectory), "PointOfSale.Infrastructure", .. parts]);

    private static string ContextPath(string fileName) =>
        Path.Combine(DiscoverSolutionRoot(AppContext.BaseDirectory), ".ai-context", fileName);

    private static string DiscoverSolutionRoot(string startPath)
    {
        var currentDir = new DirectoryInfo(startPath);

        while (currentDir != null)
        {
            if (currentDir.GetFiles("*.sln").Any() ||
                currentDir.GetDirectories("PointOfSale.API").Any())
            {
                return currentDir.FullName;
            }

            currentDir = currentDir.Parent;
        }

        throw new InvalidOperationException($"Could not discover solution root from '{startPath}'.");
    }
}
