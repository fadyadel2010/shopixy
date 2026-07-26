using System;
using System.IO;
using Xunit;

namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubCatalogAuthorityCertificationTests
{
    [Fact]
    public void ListingMutation_MustRemainSqlTenantVendorProductAndCategoryAuthoritative()
    {
        var handlers = ReadRepoFile("PointOfSale.API/CommerceHub/Handlers/CommerceHubCommandHandlers.cs");
        var controller = ReadRepoFile("PointOfSale.API/Controllers/AdminCommerceHubController.cs");
        var configuration = ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/CommerceHubListingConfiguration.cs");

        Assert.Contains("CommerceHubTenantIsolationGuard.EnsureTenantAccess", handlers, StringComparison.Ordinal);
        Assert.Contains("EnsureVendorListingAllowedAsync", handlers, StringComparison.Ordinal);
        Assert.Contains("EnsureTenantHasActiveShippingAsync", handlers, StringComparison.Ordinal);
        Assert.Contains("EnsureLiveCommerceHubCategoryAsync", handlers, StringComparison.Ordinal);
        Assert.Contains("WherePubliclyVisibleLineage", handlers, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPublicationCurrencyPolicy.EnsureEgpPricedProduct", handlers, StringComparison.Ordinal);
        Assert.Contains("UQ_CommerceHubListings_TenantId_VariantId", configuration, StringComparison.Ordinal);

        Assert.Contains("_userGovernanceService.EnsureVendorListingAllowedAsync", controller, StringComparison.Ordinal);
        Assert.Contains("TenantHasActiveShippingAsync", controller, StringComparison.Ordinal);
        Assert.Contains("CommerceHubCategoryLineageAuthority.LoadLiveIdsAsync", controller, StringComparison.Ordinal);
        Assert.Contains("EnsureLiveCurrentCategoryForBulk", controller, StringComparison.Ordinal);
        Assert.Contains("variant.ProductId != listing.ProductId", controller, StringComparison.Ordinal);
        Assert.Contains("variant.SemanticLifecycleState != ProductVariantSemanticLifecycleState.Current", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void VisibilitySearchAndRecovery_MustRemainDerivativeSqlOverlayOnly()
    {
        var overlay = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchSqlAuthorityOverlay.cs");
        var query = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubQueryService.cs");
        var projection = ReadRepoFile("PointOfSale.Infrastructure/Consumers/CommerceHubProjectionConsumer.cs");
        var reindex = ReadRepoFile("PointOfSale.Infrastructure/Jobs/CommerceHubReindexService.cs");

        Assert.Contains("listing == null || !listing.IsPublished", overlay, StringComparison.Ordinal);
        Assert.Contains("tenant == null || tenant.Currency != FinancialCurrencyGuard.PlatformBaseCurrencyCode", overlay, StringComparison.Ordinal);
        Assert.Contains("CommerceHubVendorAuthority.LoadAsync", overlay, StringComparison.Ordinal);
        Assert.Contains("!vendorAuthority.IsMarketplaceEligible", overlay, StringComparison.Ordinal);
        Assert.Contains("PublishStatus.Published", overlay, StringComparison.Ordinal);
        Assert.Contains("WherePubliclyVisibleLineage", overlay, StringComparison.Ordinal);
        Assert.Contains("CommerceHubCategoryLineageAuthority.LoadLiveSnapshotAsync", overlay, StringComparison.Ordinal);
        Assert.Contains("WhereCurrentInventoryStockOwner", overlay, StringComparison.Ordinal);
        Assert.Contains("new CommerceHubPriceAuthority()", overlay, StringComparison.Ordinal);

        Assert.Contains("RecomposeVisibleListingItemsAsync", query, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", query, StringComparison.Ordinal);
        Assert.Contains("LoadPubliclySuppressedVendorIdsAsync", query, StringComparison.Ordinal);
        Assert.Contains("ProjectionConsumerEventFence.TryAcquireAsync", projection, StringComparison.Ordinal);
        Assert.Contains("LoadSqlCommerceHubListingAuthorityAsync", projection, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", projection, StringComparison.Ordinal);
        Assert.Contains("DeleteStaleSearchDocumentsAsync", reindex, StringComparison.Ordinal);
        Assert.Contains("LoadSqlVisibleListingIdentitiesAsync", reindex, StringComparison.Ordinal);
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
