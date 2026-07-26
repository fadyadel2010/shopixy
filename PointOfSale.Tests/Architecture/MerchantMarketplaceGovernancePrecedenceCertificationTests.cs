namespace PointOfSale.Tests.Architecture;

public sealed class MerchantMarketplaceGovernancePrecedenceCertificationTests
{
    [Fact]
    public void MerchantPublicationAndListingUpdates_MustRemainGovernanceSubordinate()
    {
        var handlers = ReadRepoFile("PointOfSale.API/CommerceHub/Handlers/CommerceHubCommandHandlers.cs");
        var listing = ReadRepoFile("PointOfSale.Core/Entities/CommerceHubListing.cs");
        var vendorVisibility = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubVendorVisibilityService.cs");

        Assert.Contains("EnsureVendorListingAllowedAsync", handlers, StringComparison.Ordinal);
        Assert.Contains("EnsureLiveCommerceHubCategoryAsync", handlers, StringComparison.Ordinal);
        Assert.Contains("WherePubliclyVisibleLineage", handlers, StringComparison.Ordinal);
        Assert.Contains("HideByGovernance", listing, StringComparison.Ordinal);
        Assert.Contains("RestorePublicationOnActivate", listing, StringComparison.Ordinal);
        Assert.Contains("ClearGovernanceRestoreState", vendorVisibility, StringComparison.Ordinal);
        Assert.Contains("RestoreAfterGovernance", vendorVisibility, StringComparison.Ordinal);
        Assert.Contains("CommerceHubCategoryLineageAuthority.LoadLiveIdsAsync", vendorVisibility, StringComparison.Ordinal);
    }

    [Fact]
    public void SearchReindexAndRecovery_MustSuppressCurrentVendorGovernance()
    {
        var overlay = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchSqlAuthorityOverlay.cs");
        var vendorAuthority = ReadRepoFile("PointOfSale.Infrastructure/Reliability/CommerceHubVendorAuthority.cs");
        var reindex = ReadRepoFile("PointOfSale.Infrastructure/Jobs/CommerceHubReindexService.cs");
        var autoHealing = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubAutoHealingService.cs");
        var query = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubQueryService.cs");

        Assert.Contains("CommerceHubVendorAuthority.LoadAsync", overlay, StringComparison.Ordinal);
        Assert.Contains("vendorAuthority.IsMarketplaceEligible", overlay, StringComparison.Ordinal);
        Assert.Contains("UserGovernanceRole.Vendor", vendorAuthority, StringComparison.Ordinal);
        Assert.Contains("GovernanceStatus == UserGovernanceStatus.Active", vendorAuthority, StringComparison.Ordinal);
        Assert.Contains("IsKycVerified", vendorAuthority, StringComparison.Ordinal);
        Assert.Contains("item.governanceProfile != null", reindex, StringComparison.Ordinal);
        Assert.Contains("item.governanceProfile.IsKycVerified", reindex, StringComparison.Ordinal);
        Assert.Contains("item.governanceProfile.IsMarketplaceApproved", reindex, StringComparison.Ordinal);
        Assert.Contains("DeleteStaleSearchDocumentsAsync", reindex, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", autoHealing, StringComparison.Ordinal);
        Assert.Contains("LoadGovernanceHiddenVendorIdsAsync", query, StringComparison.Ordinal);
        Assert.Contains("LoadSuspendedVendorIdsAsync", query, StringComparison.Ordinal);
    }

    [Fact]
    public void GovernancePrecedenceCertificationDocumentation_MustRecordPassTwelveSix()
    {
        var certification = ReadRepoFile("docs/architecture/merchant-marketplace-governance-precedence-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        foreach (var marker in new[]
        {
            "POS-ADMIN-CERTIFICATION-01 PASS 12.6",
            "1. Governance Inventory Findings",
            "2. Merchant-vs-Governance Findings",
            "3. Suppression Findings",
            "4. Vendor Governance Findings",
            "5. Category/Product Governance Findings",
            "6. Search/Discovery Governance Findings",
            "7. Replay/Recovery Governance Findings",
            "8. Auditability Findings",
            "9. Chaos Findings",
            "10. Runtime Hardening Performed",
            "11. Monitoring Improvements",
            "12. Governance Updates",
            "13. Honest Remaining Debt"
        })
        {
            Assert.Contains(marker, certification, StringComparison.Ordinal);
        }

        Assert.Contains("PASS 12.6 Merchant vs Marketplace Governance Precedence Certification", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS 12.6 Merchant vs Marketplace Governance Precedence Certification Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS 12.6", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS 12.6 Merchant vs Marketplace Governance Precedence Certification", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS 12.6 Merchant vs Marketplace Governance Precedence Certification Freeze", codexRules, StringComparison.Ordinal);
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
