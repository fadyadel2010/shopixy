namespace PointOfSale.Tests.Architecture;

public sealed class MarketplaceGovernanceAuthorityCertificationTests
{
    [Fact]
    public void ListingApprovalAndGovernanceRestore_MustStaySqlAuthorityBound()
    {
        var handlers = ReadRepoFile("PointOfSale.API/CommerceHub/Handlers/CommerceHubCommandHandlers.cs");
        var vendorVisibility = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubVendorVisibilityService.cs");
        var overlay = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchSqlAuthorityOverlay.cs");

        Assert.Contains("EnsureVendorListingAllowedAsync", handlers, StringComparison.Ordinal);
        Assert.Contains("EnsureTenantHasActiveShippingAsync", handlers, StringComparison.Ordinal);
        Assert.Contains("WherePubliclyVisibleLineage", handlers, StringComparison.Ordinal);
        Assert.Contains("CommerceHubListing.Create", handlers, StringComparison.Ordinal);
        Assert.Contains("PublishStatus.Published", vendorVisibility, StringComparison.Ordinal);
        Assert.Contains("product.IsOnlineVisible", vendorVisibility, StringComparison.Ordinal);
        Assert.Contains("CommerceHubVendorAuthority.LoadAsync", vendorVisibility, StringComparison.Ordinal);
        Assert.Contains("vendorAuthority.IsMarketplaceEligible", vendorVisibility, StringComparison.Ordinal);
        Assert.Contains("CommerceHubCategoryLineageAuthority.LoadLiveIdsAsync", vendorVisibility, StringComparison.Ordinal);
        Assert.Contains("ClearGovernanceRestoreState", vendorVisibility, StringComparison.Ordinal);
        Assert.Contains("SqlAuthorityOverlayKind.CommerceHubListing", overlay, StringComparison.Ordinal);
        Assert.Contains("listing.IsPublished", overlay, StringComparison.Ordinal);
        Assert.Contains("item.PublishStatus == PublishStatus.Published", overlay, StringComparison.Ordinal);
    }

    [Fact]
    public void HomepageRecommendationAndDiscoveryGovernance_MustRemainCandidateOnly()
    {
        var homepageManagement = ReadRepoFile("PointOfSale.API/SuperAdmin/Services/SuperAdminHomepageSectionManagementService.cs");
        var homepageRead = ReadRepoFile("PointOfSale.API/Services/Homepage/HomepageSectionReadService.cs");
        var marketplaceRead = ReadRepoFile("PointOfSale.API/Controllers/MarketplaceReadController.cs");
        var queryService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubQueryService.cs");

        Assert.Contains("ValidateReferencesAsync", homepageManagement, StringComparison.Ordinal);
        Assert.Contains("FilterVisibleListingIdsAsync", homepageManagement, StringComparison.Ordinal);
        Assert.Contains("CommerceHubCategoryLineageAuthority.LoadLiveIdsAsync", homepageManagement, StringComparison.Ordinal);
        Assert.Contains("SanitizeDiscoveryReferencesAsync", homepageRead, StringComparison.Ordinal);
        Assert.Contains("LoadLiveCommerceHubListingIdsAsync", homepageRead, StringComparison.Ordinal);
        Assert.Contains("FilterVisibleListingIdsAsync", homepageRead, StringComparison.Ordinal);
        Assert.Contains("FilterPublicReadAsync", marketplaceRead, StringComparison.Ordinal);
        Assert.Contains("FilterHomepageSectionsByVendorVisibilityAsync", marketplaceRead, StringComparison.Ordinal);
        Assert.Contains("RecomposeVisibleListingItemsAsync", queryService, StringComparison.Ordinal);
        Assert.Contains("FilterLiveSuggestionTermsAsync", queryService, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", queryService, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplayRecoveryAndFeaturedPromotionPaths_MustUseDerivativeConvergenceControls()
    {
        var homepageConsumer = ReadRepoFile("PointOfSale.Infrastructure/Consumers/Marketplace/HomepageProjectionConsumer.cs");
        var projection = ReadRepoFile("PointOfSale.Infrastructure/ReadModels/Marketplace/MarketplaceReadModelProjectionServices.cs");
        var reindex = ReadRepoFile("PointOfSale.Infrastructure/Jobs/CommerceHubReindexService.cs");
        var autoHealing = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubAutoHealingService.cs");
        var searchHealth = ReadRepoFile("PointOfSale.API/HealthChecks/CommerceHubSearchSyncHealthCheck.cs");

        Assert.Contains("ProjectionConsumerEventFence.TryAcquireAsync", homepageConsumer, StringComparison.Ordinal);
        Assert.Contains("SanitizeSectionsAsync", projection, StringComparison.Ordinal);
        Assert.Contains("SqlAuthorityOverlayKind.Homepage", projection, StringComparison.Ordinal);
        Assert.Contains("DeleteIfFreshAsync", projection, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", reindex, StringComparison.Ordinal);
        Assert.Contains("PromoteIndexAsync", reindex, StringComparison.Ordinal);
        Assert.Contains("TenantId", autoHealing, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", autoHealing, StringComparison.Ordinal);
        Assert.Contains("LoadSqlVisibleListingIdentitiesAsync", autoHealing, StringComparison.Ordinal);
        Assert.Contains("UserGovernanceProfiles", searchHealth, StringComparison.Ordinal);
        Assert.Contains("FinancialCurrencyGuard.PlatformBaseCurrencyCode", searchHealth, StringComparison.Ordinal);
        Assert.Contains("governanceProfile.IsKycVerified", searchHealth, StringComparison.Ordinal);
        Assert.Contains("governanceProfile.IsMarketplaceApproved", searchHealth, StringComparison.Ordinal);
        Assert.Contains("OperationalConvergenceTelemetry", searchHealth, StringComparison.Ordinal);
    }

    [Fact]
    public void GovernanceAuditabilityAndDocumentation_MustRecordPassElevenThree()
    {
        var userGovernance = ReadRepoFile("PointOfSale.API/Services/Governance/UserGovernanceService.cs");
        var superAdminAudit = ReadRepoFile("PointOfSale.API/SuperAdmin/Infrastructure/SuperAdminOperationalAuditMiddleware.cs");
        var certification = ReadRepoFile("docs/architecture/marketplace-governance-authority-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        Assert.Contains("AppendAuditLogAsync", userGovernance, StringComparison.Ordinal);
        Assert.Contains("listingSnapshot", userGovernance, StringComparison.Ordinal);
        Assert.Contains("cacheInvalidation", userGovernance, StringComparison.Ordinal);
        Assert.Contains("PLATFORM_OPERATION_COMPLETED", superAdminAudit, StringComparison.Ordinal);

        foreach (var marker in new[]
        {
            "POS-ADMIN-CERTIFICATION-01 PASS 11.3",
            "1. Governance Inventory Findings",
            "2. Listing Approval Findings",
            "3. Rejection And Suspension Findings",
            "4. Homepage Governance Findings",
            "5. Recommendation Findings",
            "6. Discovery Findings",
            "7. Vendor Governance Findings",
            "8. Promotion And Featured Content Findings",
            "9. Replay And Recovery Findings",
            "10. Auditability Findings",
            "11. Chaos Findings",
            "Runtime Hardening Performed",
            "Honest Remaining Debt"
        })
        {
            Assert.Contains(marker, certification, StringComparison.Ordinal);
        }

        Assert.Contains("PASS 11.3 Marketplace Governance Authority", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS 11.3 Marketplace Governance Authority Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS 11.3 Marketplace Governance Authority Freeze", codexRules, StringComparison.Ordinal);
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
