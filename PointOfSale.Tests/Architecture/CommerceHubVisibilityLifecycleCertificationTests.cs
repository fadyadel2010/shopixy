using System;
using System.IO;
using Xunit;

namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubVisibilityLifecycleCertificationTests
{
    [Fact]
    public void VisibilityLifecycle_MustStaySqlAuthoritativeAndResistResurrection()
    {
        var overlay = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchSqlAuthorityOverlay.cs");
        var query = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubQueryService.cs");
        var cart = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCartService.cs");
        var checkout = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");

        // ── SQL Authority Overlay: central visibility rules ──
        // Unpublished or missing listings must be rejected
        Assert.Contains("listing == null || !listing.IsPublished", overlay, StringComparison.Ordinal);
        // Variant lineage must be publicly visible
        Assert.Contains(".WherePubliclyVisibleLineage(tenantId)", overlay, StringComparison.Ordinal);
        // Category lineage must be loaded from live SQL snapshot
        Assert.Contains("CommerceHubCategoryLineageAuthority.LoadLiveSnapshotAsync", overlay, StringComparison.Ordinal);
        // Stock must be resolved through SQL-authoritative owner filter
        Assert.Contains(".WhereCurrentInventoryStockOwner(tenantId)", overlay, StringComparison.Ordinal);
        // Pricing must flow through CommerceHubPriceAuthority
        Assert.Contains("CommerceHubPriceAuthority", overlay, StringComparison.Ordinal);
        // Vendor governance suppression must be checked
        Assert.Contains("CommerceHubVendorAuthority.LoadAsync", overlay, StringComparison.Ordinal);
        Assert.Contains("vendorAuthority.IsMarketplaceEligible", overlay, StringComparison.Ordinal);
        // SQL truth rebuild guard must enforce authority scope
        Assert.Contains("SqlTruthRebuildGuard.RequireSqlAuthority", overlay, StringComparison.Ordinal);

        // ── Query Service: read surfaces must recompose through SQL authority ──
        Assert.Contains("RecomposeVisibleListingItemsAsync", query, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", query, StringComparison.Ordinal);

        // ── Cart/Checkout: transactional paths must validate lineage and price authority ──
        Assert.Contains(".WherePubliclyVisibleLineage()", cart, StringComparison.Ordinal);
        Assert.Contains(".WherePubliclyVisibleLineage()", checkout, StringComparison.Ordinal);
        Assert.Contains("_priceAuthority.ResolveAsync(variant, listing, cancellationToken)", cart, StringComparison.Ordinal);
        Assert.Contains("_priceAuthority.ResolveAsync(variant, listing, cancellationToken)", checkout, StringComparison.Ordinal);
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
