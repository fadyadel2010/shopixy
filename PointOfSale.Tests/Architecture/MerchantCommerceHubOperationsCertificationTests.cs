namespace PointOfSale.Tests.Architecture;

public sealed class MerchantCommerceHubOperationsCertificationTests
{
    [Fact]
    public void ListingManagement_MustKeepActionableMerchantFailuresVisible()
    {
        var frontend = ReadRepoFile("PointOfSale.Web/src/pages/CommerceHubSettings.jsx");
        var controller = ReadRepoFile("PointOfSale.API/Controllers/AdminCommerceHubController.cs");
        var handlers = ReadRepoFile("PointOfSale.API/CommerceHub/Handlers/CommerceHubCommandHandlers.cs");

        Assert.Contains("getCommerceHubErrorMessage", frontend, StringComparison.Ordinal);
        Assert.Contains("error?.response?.data?.message || error?.message || fallbackMessage", frontend, StringComparison.Ordinal);
        Assert.Contains("Configure at least one active shipping method before publishing products to CommerceHub.", handlers, StringComparison.Ordinal);
        Assert.Contains("CommerceHub price overrides are disabled for this tenant.", handlers, StringComparison.Ordinal);
        Assert.Contains("CommerceHub category is not currently available for listing publication.", handlers, StringComparison.Ordinal);
        Assert.Contains("[HttpPost(\"publish\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpPut(\"update\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpDelete(\"unpublish\")]", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void MerchantOperationsInventory_MustFreezeSupportedOperationalSurfaces()
    {
        var frontend = ReadRepoFile("PointOfSale.Web/src/pages/CommerceHubSettings.jsx");
        var finance = ReadRepoFile("PointOfSale.Web/src/pages/CommerceHubFinance.jsx");
        var controller = ReadRepoFile("PointOfSale.API/Controllers/AdminCommerceHubController.cs");
        var dtos = ReadRepoFile("PointOfSale.API/DTOs/CommerceHub/CommerceHubAdminDtos.cs");

        Assert.Contains("fetchAllPagedArray('/admin/commercehub/products'", frontend, StringComparison.Ordinal);
        Assert.Contains("handlePublish", frontend, StringComparison.Ordinal);
        Assert.Contains("handleUnpublish", frontend, StringComparison.Ordinal);
        Assert.Contains("priceOverrideMutationVersion", frontend, StringComparison.Ordinal);
        Assert.Contains("/admin/commercehub/listings", frontend, StringComparison.Ordinal);
        Assert.Contains("Listing operations console", frontend, StringComparison.Ordinal);
        Assert.Contains("listingFilters", frontend, StringComparison.Ordinal);
        Assert.Contains("handleBulkOperation", frontend, StringComparison.Ordinal);
        Assert.Contains("Suppressed", frontend, StringComparison.Ordinal);
        Assert.Contains("Orphaned", frontend, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"products\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"listings\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"listings/kpis\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"listings/analytics\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"listings/notifications\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpPost(\"listings/bulk\")]", controller, StringComparison.Ordinal);
        Assert.Contains("BuildTenantListingOperationsQuery", controller, StringComparison.Ordinal);
        Assert.Contains("RestorePublicationOnActivate", controller, StringComparison.Ordinal);
        Assert.Contains("CommerceHubCategoryLineageAuthority.IsLiveAsync", controller, StringComparison.Ordinal);
        Assert.Contains("CommerceHubListingOperationsRowDto", dtos, StringComparison.Ordinal);
        Assert.Contains("CommerceHubListingKpisDto", dtos, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBulkListingOperationOutcomeDto", dtos, StringComparison.Ordinal);
        Assert.Contains("X-Total-Count", controller, StringComparison.Ordinal);
        Assert.Contains("getCommerceHubFinanceDashboard", finance, StringComparison.Ordinal);
        Assert.Contains("getCommerceHubFinanceStatement", finance, StringComparison.Ordinal);
        Assert.Contains("downloadCommerceHubFinanceStatementCsv", finance, StringComparison.Ordinal);
    }

    [Fact]
    public void OperationsCertificationDocumentation_MustRecordPassFourteenThree()
    {
        var certification = ReadRepoFile("docs/architecture/merchant-commercehub-operations-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        foreach (var marker in new[]
        {
            "POS-ADMIN-CERTIFICATION-01 PASS 14.3",
            "1. Listing Search Findings",
            "2. Operational Filter Findings",
            "3. Suppression Visibility Findings",
            "4. Orphan Visibility Findings",
            "5. Bulk Operation Findings",
            "6. KPI Findings",
            "7. Analytics Findings",
            "8. Notification Findings",
            "9. Frontend / Backend Alignment Findings",
            "10. Performance Findings",
            "11. Governance Findings",
            "12. Chaos Findings",
            "13. Monitoring Findings",
            "14. Runtime Hardening Performed",
            "15. Regression Freeze Added",
            "16. Architecture Updates",
            "17. Remaining Debt",
            "merchant operations console"
        })
        {
            Assert.Contains(marker, certification, StringComparison.Ordinal);
        }

        Assert.Contains("PASS 14.3 Merchant Listing Operations Console Certification", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS 14.3 Merchant Listing Operations Console Certification Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS 14.3", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS 14.3 Merchant Listing Operations Console Certification", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS 14.3 Merchant Listing Operations Console Certification Freeze", codexRules, StringComparison.Ordinal);
        Assert.Contains("PASS 14.2 Merchant CommerceHub Operations Certification", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS 14.2 Merchant CommerceHub Operations Certification Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS 14.2 Merchant CommerceHub Operations Certification", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS 14.2 Merchant CommerceHub Operations Certification Freeze", codexRules, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
            {
                return File.ReadAllText(Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root could not be found.");
    }
}
