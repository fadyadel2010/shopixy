namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubSearchDiscoveryAuthorityCertificationTests
{
    [Fact]
    public void RuntimeSearchDiscoveryReads_MustRecomposeCandidatesThroughSqlAuthority()
    {
        var queryService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubQueryService.cs");

        Assert.Contains("SearchSqlAuthorityFirstAsync", queryService, StringComparison.Ordinal);
        Assert.Contains("Total = result.TotalCount", queryService, StringComparison.Ordinal);
        Assert.Contains("TotalCount = totalCount", queryService, StringComparison.Ordinal);
        Assert.Contains(".Skip(PaginationGuard.GetSkip(page, pageSize))", queryService, StringComparison.Ordinal);
        Assert.Contains("MapSqlVisibleFacetsAsync", queryService, StringComparison.Ordinal);
        Assert.Contains("RecomposeVisibleSuggestionProductsAsync", queryService, StringComparison.Ordinal);
        Assert.Contains("RecomposeVisibleAutocompleteProductsAsync", queryService, StringComparison.Ordinal);
        Assert.Contains("FilterLiveSuggestionTermsAsync", queryService, StringComparison.Ordinal);
        Assert.Contains("RevalidatePopularQueriesResponseAsync", queryService, StringComparison.Ordinal);
        Assert.Contains("MapZeroResultSuggestionsAsync", queryService, StringComparison.Ordinal);
    }

    [Fact]
    public void RankingTrendingAndAnalytics_MustStayDerivativeAndSqlVisibleBound()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/CommerceHubController.cs");
        var analytics = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubSearchAnalyticsBackgroundService.cs");
        var readService = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchReadService.cs");

        Assert.Contains("ResolveVisibleSearchAnalyticsAuthorityAsync", controller, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", controller, StringComparison.Ordinal);
        Assert.Contains("return Accepted();", controller, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", analytics, StringComparison.Ordinal);
        Assert.Contains("indexService.DeleteAsync", analytics, StringComparison.Ordinal);
        Assert.Contains("PopularityScore", readService, StringComparison.Ordinal);
        Assert.Contains("SearchSqlAuthorityFirstAsync", ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubQueryService.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void IndexingProjectionAndReindex_MustDeleteWhenSqlAuthorityRejectsVisibility()
    {
        var projectionConsumer = ReadRepoFile("PointOfSale.Infrastructure/Consumers/CommerceHubProjectionConsumer.cs");
        var reindex = ReadRepoFile("PointOfSale.Infrastructure/Jobs/CommerceHubReindexService.cs");
        var indexService = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchIndexService.cs");

        Assert.Contains("ProjectionConsumerEventFence.TryAcquireAsync", projectionConsumer, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", projectionConsumer, StringComparison.Ordinal);
        Assert.Contains("_searchIndexService.DeleteAsync(document.TenantId, document.Id", projectionConsumer, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchDocumentMapper.Map(sqlAuthority)", projectionConsumer, StringComparison.Ordinal);
        Assert.Contains("ReconcileMissingSqlListingsAsync", reindex, StringComparison.Ordinal);
        Assert.Contains("DeleteStaleSearchDocumentsAsync", reindex, StringComparison.Ordinal);
        Assert.Contains("LoadSqlVisibleListingIdentitiesAsync", reindex, StringComparison.Ordinal);
        Assert.Contains("targetCount.Value == publishedCount", reindex, StringComparison.Ordinal);
        Assert.Contains("DeleteStaleAsync", indexService, StringComparison.Ordinal);
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
