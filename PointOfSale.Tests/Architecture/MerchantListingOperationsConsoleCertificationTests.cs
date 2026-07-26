using System;
using System.IO;
using Xunit;

namespace PointOfSale.Tests.Architecture;

public sealed class MerchantListingOperationsConsoleCertificationTests
{
    [Fact]
    public void ListingSearchAndFilters_MustRemainSqlAuthoritativeAndTenantBound()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/AdminCommerceHubController.cs");

        // Server-side listing search must cover the minimum dimensions
        Assert.Contains("row.ProductName.ToLower().Contains(search)", controller, StringComparison.Ordinal);
        Assert.Contains("row.VariantName != null && row.VariantName.ToLower().Contains(search)", controller, StringComparison.Ordinal);
        Assert.Contains("row.Sku != null && row.Sku.ToLower().Contains(search)", controller, StringComparison.Ordinal);
        Assert.Contains("row.Barcode != null && row.Barcode.ToLower().Contains(search)", controller, StringComparison.Ordinal);
        Assert.Contains("row.CategoryName != null && row.CategoryName.ToLower().Contains(search)", controller, StringComparison.Ordinal);

        // Strict tenant scoping must be preserved
        Assert.Contains("BuildTenantListingOperationsQuery(_tenantContext.RequiredTenantId)", controller, StringComparison.Ordinal);

        // Operational filters must be SQL-backed and match governance authority semantics
        Assert.Contains("request.CategoryId.HasValue", controller, StringComparison.Ordinal);
        Assert.Contains("request.OverrideEnabled.HasValue", controller, StringComparison.Ordinal);
        Assert.Contains("request.Status", controller, StringComparison.Ordinal);
        Assert.Contains("request.InventoryStatus", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void ListingKpisAndNotifications_MustDeriveFromSqlAuthority()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/AdminCommerceHubController.cs");

        // KPIs must represent true SQL authority, not client approximations
        Assert.Contains("public async Task<ActionResult<CommerceHubListingKpisDto>> GetListingKpis", controller, StringComparison.Ordinal);
        Assert.Contains("TotalListings = await rows.CountAsync", controller, StringComparison.Ordinal);
        Assert.Contains("PublishedListings = await rows.CountAsync", controller, StringComparison.Ordinal);
        Assert.Contains("UnpublishedListings = await rows.CountAsync", controller, StringComparison.Ordinal);
        Assert.Contains("SuppressedListings = await rows.CountAsync", controller, StringComparison.Ordinal);
        Assert.Contains("OrphanedListings = await rows.CountAsync", controller, StringComparison.Ordinal);

        // Notifications must be actionable, derived from current SQL state, and prevent spam
        Assert.Contains("public async Task<ActionResult<List<CommerceHubListingNotificationDto>>> GetListingNotifications", controller, StringComparison.Ordinal);
        Assert.Contains("row.IsSuppressed ? \"listing_suppressed\" : \"listing_orphaned\"", controller, StringComparison.Ordinal);
        Assert.Contains("Listing is governance-hidden:", controller, StringComparison.Ordinal);
        Assert.Contains("Listing needs cleanup:", controller, StringComparison.Ordinal);
        Assert.Contains("Take(25)", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void FrontendConsoleControls_MustAlignWithBackendEndpoints()
    {
        var frontend = ReadRepoFile("PointOfSale.Web/src/pages/CommerceHubSettings.jsx");

        // UI must align with the backend console API routes
        Assert.Contains("api.get('/admin/commercehub/listings'", frontend, StringComparison.Ordinal);
        Assert.Contains("api.get('/admin/commercehub/listings/kpis')", frontend, StringComparison.Ordinal);
        Assert.Contains("api.get('/admin/commercehub/listings/analytics')", frontend, StringComparison.Ordinal);
        Assert.Contains("api.get('/admin/commercehub/listings/notifications')", frontend, StringComparison.Ordinal);
        Assert.Contains("api.post('/admin/commercehub/listings/bulk'", frontend, StringComparison.Ordinal);

        // Filters rendered in JSX must match controller params
        Assert.Contains("listingFilters", frontend, StringComparison.Ordinal);
        Assert.Contains("status", frontend, StringComparison.Ordinal);
        Assert.Contains("inventoryStatus", frontend, StringComparison.Ordinal);
        Assert.Contains("overrideEnabled", frontend, StringComparison.Ordinal);
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
