namespace PointOfSale.Tests.Architecture;

public sealed class CmsPublicVisibilityCertificationTests
{
    [Fact]
    public void StorefrontCmsPublishedReadsStaySqlAuthoritative()
    {
        var source = ReadRepoFile("PointOfSale.API/Services/Cms/CmsPageReadService.cs");

        Assert.Contains("LoadCurrentPublishedPageBySlugAsync", source, StringComparison.Ordinal);
        Assert.Contains("IsTenantPubliclyActiveAsync", source, StringComparison.Ordinal);
        Assert.Contains("tenant.IsActive", source, StringComparison.Ordinal);
        Assert.Contains("tenant.Status == TenantStatus.Active", source, StringComparison.Ordinal);
        Assert.Contains("item.IsPublished && !item.IsDraft", source, StringComparison.Ordinal);
        Assert.DoesNotContain("var cached = await _cacheService.GetPublishedScopeAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CmsSeoAndSitemapStaySqlAuthoritativeForCmsUrls()
    {
        var source = ReadRepoFile("PointOfSale.API/Services/Storefront/SeoService.cs");

        Assert.Contains("BuildCmsSeoMetadataAsync", source, StringComparison.Ordinal);
        Assert.Contains("LoadCurrentPublishedCmsPageBySlugAsync", source, StringComparison.Ordinal);
        Assert.Contains("LoadCurrentPublishedCmsPagesAsync", source, StringComparison.Ordinal);
        Assert.Contains("if (!await IsTenantPubliclyActiveAsync(tenantId, cancellationToken))", source, StringComparison.Ordinal);
        Assert.Contains("\"cms\" => $\"{basePath}/pages/{slug}\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TryGetFromCacheAsync<SitemapDto>", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CmsProjectionMirrorsOnlyCurrentSqlPublishedAuthority()
    {
        var consumer = ReadRepoFile("PointOfSale.Infrastructure/Consumers/CmsPageProjectionConsumer.cs");

        Assert.Contains("item.IsPublished && !item.IsDraft", consumer, StringComparison.Ordinal);
        Assert.Contains("item.PublishedAt != null", consumer, StringComparison.Ordinal);
        Assert.Contains("TenantLineageAuthority.IsActiveTenantAsync", consumer, StringComparison.Ordinal);
        Assert.Contains("FailClosedCmsPageProjectionAsync", consumer, StringComparison.Ordinal);
        Assert.Contains("ProjectionEventFence.TryAcquireAsync", consumer, StringComparison.Ordinal);
    }

    [Fact]
    public void CmsSearchResultsAreFilteredThroughCurrentSqlAuthority()
    {
        var source = ReadRepoFile("PointOfSale.API/Controllers/StorefrontSearchController.cs");

        Assert.Contains("FilterSearchItemsBySqlAuthorityAsync", source, StringComparison.Ordinal);
        Assert.Contains("page.IsPublished && !page.IsDraft", source, StringComparison.Ordinal);
        Assert.Contains("tenant.IsActive", source, StringComparison.Ordinal);
        Assert.Contains("tenant.Status == TenantStatus.Active", source, StringComparison.Ordinal);
        Assert.Contains("visibleGroups.Contains(item.EntityId)", source, StringComparison.Ordinal);
        Assert.Contains("visibleSlugs.Contains(item.Slug)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CmsSearchRepairIsConvergentSqlFirstRepair()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/AdminSearchSyncController.cs");
        var searchInterface = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/IUnifiedSearchIndexService.cs");
        var searchService = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/UnifiedSearchIndexService.cs");

        Assert.Contains("SqlAuthorityOverlayKind.StorefrontCms", controller, StringComparison.Ordinal);
        Assert.Contains("MapToCmsPageSearchDocument", controller, StringComparison.Ordinal);
        Assert.Contains("tenant.IsActive", controller, StringComparison.Ordinal);
        Assert.Contains("DeleteStaleCmsPagesAsync", controller, StringComparison.Ordinal);
        Assert.Contains("DeleteStaleCmsPagesAsync", searchInterface, StringComparison.Ordinal);
        Assert.Contains("DeleteByQueryAsync<CmsPageSearchDocument>", searchService, StringComparison.Ordinal);
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
