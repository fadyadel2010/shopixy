namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubProductDiscoveryCertificationTests
{
    [Fact]
    public void DiscoverySurfaces_MustStayInventoriedAndDocumented()
    {
        var certification = ReadRepoFile("docs/architecture/commercehub-product-discovery-certification-findings.md");

        foreach (var marker in new[]
        {
            "Scope: PASS 16.2",
            "## Discovery Inventory",
            "## Homepage Findings",
            "## Search Findings",
            "## Category Findings",
            "## Filter Findings",
            "## Product Card Findings",
            "## Product Details Findings",
            "## Recommendation Findings",
            "## Frontend UX Findings",
            "## Inventory/Pricing Findings",
            "## SEO/Deep-Link Findings",
            "## Auditability Findings",
            "## Chaos Findings",
            "## Monitoring Findings",
            "## Runtime Hardening Performed",
            "## Architecture Updates",
            "## Remaining Debt",
            "## Required Answers"
        })
        {
            Assert.Contains(marker, certification, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CommerceHubHomepageAndRecommendations_MustRemainSqlFiltered()
    {
        var marketplaceRead = ReadRepoFile("PointOfSale.API/Controllers/MarketplaceReadController.cs");
        var homepageProjection = ReadRepoFile("PointOfSale.Infrastructure/ReadModels/Marketplace/MarketplaceReadModelProjectionServices.cs");
        var productDetailsPage = ReadRepoFile("PointOfSale.CommerceHub/src/pages/ProductDetailsPage.tsx");

        Assert.Contains("FilterPublicReadAsync", marketplaceRead, StringComparison.Ordinal);
        Assert.Contains("FilterHomepageSectionsByVendorVisibilityAsync", marketplaceRead, StringComparison.Ordinal);
        Assert.Contains("FilterVisibleListingIdsAsync", marketplaceRead, StringComparison.Ordinal);
        Assert.Contains("TenantLineageAuthority.IsActiveTenantAsync", homepageProjection, StringComparison.Ordinal);
        Assert.Contains("LoadLiveListingIdsAsync", homepageProjection, StringComparison.Ordinal);
        Assert.Contains("CommerceHubCategoryLineageAuthority.LoadLiveIdsAsync", homepageProjection, StringComparison.Ordinal);
        Assert.Contains("productsQueryOptions({", productDetailsPage, StringComparison.Ordinal);
        Assert.Contains("featuredProductsQueryOptions(8)", productDetailsPage, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductCardsAndDetails_MustRenderBackendAuthorityWithoutClientVisibilityOwnership()
    {
        var productCard = ReadRepoFile("PointOfSale.CommerceHub/src/commerce/components/ProductCard.tsx");
        var productDetailsPage = ReadRepoFile("PointOfSale.CommerceHub/src/pages/ProductDetailsPage.tsx");
        var queryService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubQueryService.cs");

        Assert.Contains("formatCurrency(product.price, product.currency)", productCard, StringComparison.Ordinal);
        Assert.Contains("product.inStock ? \"In stock\" : \"Out of stock\"", productCard, StringComparison.Ordinal);
        Assert.Contains("to={`/product/${product.id}`}", productCard, StringComparison.Ordinal);
        Assert.Contains("useProductDetail(id)", productDetailsPage, StringComparison.Ordinal);
        Assert.Contains("sanitizePriceSnapshot", productDetailsPage, StringComparison.Ordinal);
        Assert.Contains("GetProductByIdAsync", queryService, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", queryService, StringComparison.Ordinal);
        Assert.Contains("return MapDetail(sqlAuthority, product);", queryService, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductDiscoveryCertificationDocumentation_MustRecordPassSixteenTwo()
    {
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        Assert.Contains("PASS 16.2 Product Discovery Certification", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS 16.2 Product Discovery Certification Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS 16.2", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS 16.2 Product Discovery Certification", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS 16.2 Product Discovery Certification Freeze", codexRules, StringComparison.Ordinal);
    }

    [Fact]
    public void HomepageListingVisibility_MustRejectDeletedAndUnpublishedListings()
    {
        var vendorVisibility = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPublicVendorVisibilityService.cs");

        // FilterVisibleListingIdsAsync must check listing publication and deletion status,
        // not just vendor governance suppression, to prevent ghost listing IDs in homepage sections.
        Assert.Contains("!listing.IsDeleted", vendorVisibility, StringComparison.Ordinal);
        Assert.Contains("listing.IsPublished", vendorVisibility, StringComparison.Ordinal);
    }

    [Fact]
    public void PopularQueries_MustRevalidateCachedTermsBeforeReturning()
    {
        var queryService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubQueryService.cs");

        // Popular queries must revalidate cached terms through SQL liveness checks
        // on cache hit, symmetrical with suggestions and autocomplete revalidation.
        Assert.Contains("RevalidatePopularQueriesResponseAsync", queryService, StringComparison.Ordinal);
        Assert.Contains("FilterLiveSuggestionTermsAsync", queryService, StringComparison.Ordinal);
    }

    [Fact]
    public void StorefrontDiscoveryFacets_MustBeRebuiltFromSqlVisibleItems()
    {
        var discoveryService = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/ProductDiscoveryService.cs");

        // Storefront discovery facets must be derived from SQL-authority-recomposed items,
        // not from raw Elasticsearch aggregations that may include invisible products.
        Assert.Contains("BuildFacetsFromVisibleItems", discoveryService, StringComparison.Ordinal);
        Assert.Contains("RecomposeDiscoveryItemsFromSqlAuthorityAsync", discoveryService, StringComparison.Ordinal);
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
