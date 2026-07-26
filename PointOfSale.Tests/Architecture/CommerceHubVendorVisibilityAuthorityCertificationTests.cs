using System;
using System.IO;
using Xunit;

namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubVendorVisibilityAuthorityCertificationTests
{
    [Fact]
    public void PublicVendorVisibility_MustBeDerivedFromCurrentBackendAuthority()
    {
        var publicVisibility = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPublicVendorVisibilityService.cs");
        var vendorVisibility = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubVendorVisibilityService.cs");
        var overlay = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchSqlAuthorityOverlay.cs");

        Assert.Contains("CommerceHubVendorAuthority.LoadEligibleVendorIdsAsync", publicVisibility, StringComparison.Ordinal);
        Assert.Contains("GetVendorsSuspendedFromNewOrdersAsync", publicVisibility, StringComparison.Ordinal);
        Assert.Contains("!listing.IsDeleted", publicVisibility, StringComparison.Ordinal);
        Assert.Contains("listing.IsPublished", publicVisibility, StringComparison.Ordinal);

        Assert.Contains("HideByGovernance()", vendorVisibility, StringComparison.Ordinal);
        Assert.Contains("RestoreAfterGovernance()", vendorVisibility, StringComparison.Ordinal);
        Assert.Contains("ClearGovernanceRestoreState()", vendorVisibility, StringComparison.Ordinal);
        Assert.Contains("CommerceHubVendorAuthority.LoadAsync", vendorVisibility, StringComparison.Ordinal);
        Assert.Contains("vendorAuthority.IsMarketplaceEligible", vendorVisibility, StringComparison.Ordinal);
        Assert.Contains("CommerceHubCategoryLineageAuthority.LoadLiveIdsAsync", vendorVisibility, StringComparison.Ordinal);
        Assert.Contains("ScheduleSearchVerification", vendorVisibility, StringComparison.Ordinal);

        Assert.Contains("CommerceHubVendorAuthority.LoadAsync", overlay, StringComparison.Ordinal);
        Assert.Contains("vendorAuthority.IsMarketplaceEligible", overlay, StringComparison.Ordinal);
        Assert.Contains("listing == null || !listing.IsPublished", overlay, StringComparison.Ordinal);
        Assert.Contains("tenant == null || tenant.Currency != FinancialCurrencyGuard.PlatformBaseCurrencyCode", overlay, StringComparison.Ordinal);
        Assert.Contains("product == null", overlay, StringComparison.Ordinal);
        Assert.Contains("variant == null", overlay, StringComparison.Ordinal);
        Assert.Contains("category == null", overlay, StringComparison.Ordinal);
    }

    [Fact]
    public void SearchDiscoveryStorefrontAndListingReads_MustRecomposeThroughSqlAuthority()
    {
        var queryService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubQueryService.cs");
        var controller = ReadRepoFile("PointOfSale.API/Controllers/CommerceHubController.cs");

        Assert.Contains("request.ExcludedTenantIds = await LoadPubliclySuppressedVendorIdsAsync", queryService, StringComparison.Ordinal);
        Assert.Contains("RecomposeVisibleListingItemsAsync(result.Items, cancellationToken)", queryService, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", queryService, StringComparison.Ordinal);
        Assert.Contains("LoadGovernanceHiddenVendorIdsAsync", queryService, StringComparison.Ordinal);
        Assert.Contains("LoadSuspendedVendorIdsAsync", queryService, StringComparison.Ordinal);
        Assert.Contains("IsVendorSuppressedAsync(listing.TenantId, cancellationToken)", queryService, StringComparison.Ordinal);
        Assert.Contains("RevalidateSuggestionsResponseAsync", queryService, StringComparison.Ordinal);
        Assert.Contains("RevalidateAutocompleteResponseAsync", queryService, StringComparison.Ordinal);
        Assert.Contains("RevalidatePopularQueriesResponseAsync", queryService, StringComparison.Ordinal);
        Assert.Contains("FilterLiveSuggestionTermsAsync", queryService, StringComparison.Ordinal);
        Assert.Contains("MapSqlVisibleFacetsAsync", queryService, StringComparison.Ordinal);
        Assert.Contains("BuildVendorExclusionFingerprint", queryService, StringComparison.Ordinal);

        Assert.Contains("ApplyNoStoreHeaders();", controller, StringComparison.Ordinal);
        Assert.Contains("Surrogate-Control", controller, StringComparison.Ordinal);
        Assert.Contains("ResolveVisibleSearchAnalyticsAuthorityAsync", controller, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void FrontendVendorPresentation_MustRemainApiDerivativeAndNonPersistent()
    {
        var productQueries = ReadRepoFile("PointOfSale.CommerceHub/src/products/queries.ts");
        var discoveryQueries = ReadRepoFile("PointOfSale.CommerceHub/src/features/discovery/queries.ts");
        var commerceHubService = ReadRepoFile("PointOfSale.CommerceHub/src/services/commercehub-service.ts");
        var productCard = ReadRepoFile("PointOfSale.CommerceHub/src/commerce/components/ProductCard.tsx");
        var productDetails = ReadRepoFile("PointOfSale.CommerceHub/src/pages/ProductDetailsPage.tsx");
        var homepageResolver = ReadRepoFile("PointOfSale.CommerceHub/src/homepage/resolveHomepageSections.ts");

        Assert.Contains("commerceHubService.getProducts", productQueries, StringComparison.Ordinal);
        Assert.Contains("commerceHubService.getProductById", productQueries, StringComparison.Ordinal);
        Assert.Contains("commerceHubService.search(query, signal)", discoveryQueries, StringComparison.Ordinal);
        Assert.Contains("httpClient.get<CommerceHubProductListResponse>", commerceHubService, StringComparison.Ordinal);
        Assert.Contains("httpClient.get<CommerceHubProductDetail>", commerceHubService, StringComparison.Ordinal);
        Assert.Contains("httpClient.get<CommerceHubSearchResponse>", commerceHubService, StringComparison.Ordinal);

        Assert.DoesNotContain("localStorage", productQueries, StringComparison.Ordinal);
        Assert.DoesNotContain("sessionStorage", productQueries, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage", discoveryQueries, StringComparison.Ordinal);
        Assert.DoesNotContain("sessionStorage", discoveryQueries, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage", commerceHubService, StringComparison.Ordinal);
        Assert.DoesNotContain("sessionStorage", commerceHubService, StringComparison.Ordinal);

        Assert.Contains("product.storeName", productCard, StringComparison.Ordinal);
        Assert.Contains("product.storeName", productDetails, StringComparison.Ordinal);
        Assert.Contains("storeName: product.storeName", homepageResolver, StringComparison.Ordinal);
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
