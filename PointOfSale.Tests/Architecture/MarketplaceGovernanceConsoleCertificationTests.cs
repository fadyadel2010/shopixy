using System;
using System.IO;
using Xunit;

namespace PointOfSale.Tests.Architecture;

public sealed class MarketplaceGovernanceConsoleCertificationTests
{
    [Fact]
    public void SuperAdminGovernanceConsole_MustExposeSqlBackedInventoryDashboardAndSearch()
    {
        var controller = ReadRepoFile("PointOfSale.API/SuperAdmin/Controllers/SuperAdminMarketplaceGovernanceController.cs");

        Assert.Contains("[Route(\"api/super-admin/marketplace-governance\")]", controller, StringComparison.Ordinal);
        Assert.Contains("public async Task<IActionResult> GetDashboard", controller, StringComparison.Ordinal);
        Assert.Contains("public async Task<IActionResult> SearchListings", controller, StringComparison.Ordinal);
        Assert.Contains("_dbContext.CommerceHubListings", controller, StringComparison.Ordinal);
        Assert.Contains("_dbContext.UserGovernanceProfiles", controller, StringComparison.Ordinal);
        Assert.Contains("_dbContext.Tenants", controller, StringComparison.Ordinal);
        Assert.Contains("BuildListingGovernanceQuery", controller, StringComparison.Ordinal);
        Assert.Contains("IgnoreQueryFilters()", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void ListingGovernanceActions_MustOnlyMutateVisibilityAndMustAudit()
    {
        var controller = ReadRepoFile("PointOfSale.API/SuperAdmin/Controllers/SuperAdminMarketplaceGovernanceController.cs");

        Assert.Contains("listing.HideByGovernance()", controller, StringComparison.Ordinal);
        Assert.Contains("listing.RestoreAfterGovernance()", controller, StringComparison.Ordinal);
        Assert.Contains("GetRestoreAuthorityFailureAsync", controller, StringComparison.Ordinal);
        Assert.Contains("vendorAuthority.IsMarketplaceEligible", controller, StringComparison.Ordinal);
        Assert.Contains("CommerceHubListingUnpublishedEvent", controller, StringComparison.Ordinal);
        Assert.Contains("CommerceHubListingUpdatedEvent", controller, StringComparison.Ordinal);
        Assert.Contains("EnqueueListingVerification", controller, StringComparison.Ordinal);
        Assert.Contains("MARKETPLACE_LISTING_SUPPRESSED", controller, StringComparison.Ordinal);
        Assert.Contains("MARKETPLACE_LISTING_RESTORED", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("CommerceHubPrice =", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void VendorGovernance_MustNotMutateCoreTenantLifecycleAuthority()
    {
        var controller = ReadRepoFile("PointOfSale.API/SuperAdmin/Controllers/SuperAdminMarketplaceGovernanceController.cs");

        Assert.Contains("ApplyVendorGovernanceAsync", controller, StringComparison.Ordinal);
        Assert.Contains("UserGovernanceRole.Vendor", controller, StringComparison.Ordinal);
        Assert.Contains("_userGovernanceService.ApplyActionAsync", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("tenant.SetLifecycleStatus(", controller, StringComparison.Ordinal);
        Assert.Contains("MARKETPLACE_VENDOR_SUSPENDED", controller, StringComparison.Ordinal);
        Assert.Contains("MARKETPLACE_VENDOR_RESTORED", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void FrontendConsole_MustAlignWithBackendGovernanceAuthority()
    {
        var page = ReadRepoFile("PointOfSale.Web/src/super-admin/marketplace-governance/MarketplaceGovernancePage.tsx");
        var service = ReadRepoFile("PointOfSale.Web/src/super-admin/marketplace-governance/marketplaceGovernanceService.ts");
        var routes = ReadRepoFile("PointOfSale.Web/src/App.jsx");
        var layout = ReadRepoFile("PointOfSale.Web/src/super-admin/layout/SuperAdminLayout.tsx");

        Assert.Contains("getMarketplaceGovernanceDashboard", page, StringComparison.Ordinal);
        Assert.Contains("getMarketplaceGovernanceListings", page, StringComparison.Ordinal);
        Assert.Contains("suppressMarketplaceListing", page, StringComparison.Ordinal);
        Assert.Contains("restoreMarketplaceListing", page, StringComparison.Ordinal);
        Assert.Contains("suspendMarketplaceVendor", page, StringComparison.Ordinal);
        Assert.Contains("restoreMarketplaceVendor", page, StringComparison.Ordinal);
        Assert.Contains("/marketplace-governance/dashboard", service, StringComparison.Ordinal);
        Assert.Contains("/marketplace-governance/listings", service, StringComparison.Ordinal);
        Assert.Contains("marketplace/governance", routes, StringComparison.Ordinal);
        Assert.Contains("Marketplace Governance", layout, StringComparison.Ordinal);
    }

    [Fact]
    public void GovernanceFilters_MustMatchSqlAuthoritySemantics()
    {
        var controller = ReadRepoFile("PointOfSale.API/SuperAdmin/Controllers/SuperAdminMarketplaceGovernanceController.cs");

        Assert.Contains("\"published\" => query.Where(row => row.IsPublished && !row.IsSuppressed && !row.IsOrphaned)", controller, StringComparison.Ordinal);
        Assert.Contains("\"suppressed\" => query.Where(row => row.IsSuppressed)", controller, StringComparison.Ordinal);
        Assert.Contains("\"orphaned\" => query.Where(row => row.IsOrphaned)", controller, StringComparison.Ordinal);
        Assert.Contains("\"vendor_suspended\" => query.Where(row =>", controller, StringComparison.Ordinal);
        Assert.Contains("\"tenant_suspended\" => query.Where(row => row.TenantStatus == TenantStatus.Suspended.ToString())", controller, StringComparison.Ordinal);
        Assert.Contains("request.OverrideEnabled.HasValue", controller, StringComparison.Ordinal);
        Assert.Contains("request.VendorStatus.HasValue", controller, StringComparison.Ordinal);
        Assert.Contains("request.TenantStatus.HasValue", controller, StringComparison.Ordinal);
        Assert.Contains("let vendorIneligible = profile == null", controller, StringComparison.Ordinal);
        Assert.Contains("let governanceHidden = vendorIneligible || tenantInactive", controller, StringComparison.Ordinal);
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
