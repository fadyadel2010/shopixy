using System;
using System.IO;
using Xunit;

namespace PointOfSale.Tests.Architecture;

public sealed class MarketplaceOperationsConsoleCertificationTests
{
    [Fact]
    public void SuperAdminOperationsConsole_MustExposeDiagnosticsAndSqlDerivativeRepairControls()
    {
        var controller = ReadRepoFile("PointOfSale.API/SuperAdmin/Controllers/SuperAdminMarketplaceOperationsController.cs");

        Assert.Contains("[Route(\"api/super-admin/marketplace-operations\")]", controller, StringComparison.Ordinal);
        Assert.Contains("GetDashboard", controller, StringComparison.Ordinal);
        Assert.Contains("_dbContext.CommerceHubListings", controller, StringComparison.Ordinal);
        Assert.Contains("_mongoDbContext.CommerceHubProducts", controller, StringComparison.Ordinal);
        Assert.Contains("_searchIndexService.GetStatusAsync", controller, StringComparison.Ordinal);
        Assert.Contains("_searchIndexService.VerifyMigrationAsync", controller, StringComparison.Ordinal);
        Assert.Contains("_autoHealingMetrics.GetSnapshot()", controller, StringComparison.Ordinal);
        Assert.Contains("AuthorityOwner", ReadRepoFile("PointOfSale.API/SuperAdmin/DTOs/SuperAdminMarketplaceOperationsDtos.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void ReindexAndCatchUp_MustRouteThroughReviewedSqlAuthoritativeReindexServiceAndAudit()
    {
        var controller = ReadRepoFile("PointOfSale.API/SuperAdmin/Controllers/SuperAdminMarketplaceOperationsController.cs");
        var reindex = ReadRepoFile("PointOfSale.Infrastructure/Jobs/CommerceHubReindexService.cs");

        Assert.Contains("_reindexService.ReindexAsync", controller, StringComparison.Ordinal);
        Assert.Contains("_reindexService.ReindexTenantAsync", controller, StringComparison.Ordinal);
        Assert.Contains("_reindexService.CatchUpReindexAsync", controller, StringComparison.Ordinal);
        Assert.Contains("ValidateReason", controller, StringComparison.Ordinal);
        Assert.Contains("MARKETPLACE_GLOBAL_REINDEX_EXECUTED", controller, StringComparison.Ordinal);
        Assert.Contains("MARKETPLACE_TENANT_REINDEX_EXECUTED", controller, StringComparison.Ordinal);
        Assert.Contains("MARKETPLACE_SEARCH_CATCH_UP_REINDEX_EXECUTED", controller, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", reindex, StringComparison.Ordinal);
        Assert.Contains("DeleteStaleSearchDocumentsAsync", reindex, StringComparison.Ordinal);
        Assert.Contains("LoadSqlVisibleListingIdentitiesAsync", reindex, StringComparison.Ordinal);
    }

    [Fact]
    public void ManualVerification_MustUseAutoHealingQueueWithTenantScope()
    {
        var controller = ReadRepoFile("PointOfSale.API/SuperAdmin/Controllers/SuperAdminMarketplaceOperationsController.cs");
        var autoHealing = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubAutoHealingService.cs");

        Assert.Contains("VerifyListing", controller, StringComparison.Ordinal);
        Assert.Contains("VerifyTenant", controller, StringComparison.Ordinal);
        Assert.Contains("TenantId = listing.TenantId", controller, StringComparison.Ordinal);
        Assert.Contains("TenantId = tenantId", controller, StringComparison.Ordinal);
        Assert.Contains("SUPER_ADMIN_MANUAL_VERIFY", controller, StringComparison.Ordinal);
        Assert.Contains("SUPER_ADMIN_TENANT_VERIFY", controller, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", autoHealing, StringComparison.Ordinal);
        Assert.Contains("document.TenantId != queuedItem.TenantId.Value", autoHealing, StringComparison.Ordinal);
    }

    [Fact]
    public void FrontendOperationsConsole_MustAlignWithBackendOperations()
    {
        var page = ReadRepoFile("PointOfSale.Web/src/super-admin/marketplace-operations/MarketplaceOperationsPage.tsx");
        var service = ReadRepoFile("PointOfSale.Web/src/super-admin/marketplace-operations/marketplaceOperationsService.ts");
        var routes = ReadRepoFile("PointOfSale.Web/src/App.jsx");
        var layout = ReadRepoFile("PointOfSale.Web/src/super-admin/layout/SuperAdminLayout.tsx");

        Assert.Contains("getMarketplaceOperationsDashboard", page, StringComparison.Ordinal);
        Assert.Contains("runGlobalMarketplaceReindex", page, StringComparison.Ordinal);
        Assert.Contains("runTenantMarketplaceReindex", page, StringComparison.Ordinal);
        Assert.Contains("runMarketplaceCatchUpReindex", page, StringComparison.Ordinal);
        Assert.Contains("verifyMarketplaceListing", page, StringComparison.Ordinal);
        Assert.Contains("verifyMarketplaceTenant", page, StringComparison.Ordinal);
        Assert.Contains("/marketplace-operations/dashboard", service, StringComparison.Ordinal);
        Assert.Contains("/marketplace-operations/reindex/global", service, StringComparison.Ordinal);
        Assert.Contains("/marketplace-operations/reindex/tenants/", service, StringComparison.Ordinal);
        Assert.Contains("/marketplace-operations/listings/", service, StringComparison.Ordinal);
        Assert.Contains("marketplace/operations", routes, StringComparison.Ordinal);
        Assert.Contains("Marketplace Operations", layout, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationFindingsAndContext_MustRecordOperationsBoundary()
    {
        var findings = ReadRepoFile("docs/architecture/marketplace-operations-console-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        Assert.Contains("PASS 15.2 Marketplace Operations Certification", findings, StringComparison.Ordinal);
        Assert.Contains("Reindex remains SQL-authoritative", findings, StringComparison.Ordinal);
        Assert.Contains("Auto-healing remains derivative", findings, StringComparison.Ordinal);
        Assert.Contains("PASS 15.2 Marketplace Operations Console Certification", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS 15.2 Marketplace Operations Console Certification Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS 15.2 Marketplace Operations Runtime Risks", knownRisks, StringComparison.Ordinal);
        Assert.Contains("POS-ADMIN-CERTIFICATION-01 PASS 15.2 Marketplace Operations Console Certification Freeze", codexRules, StringComparison.Ordinal);
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
