namespace PointOfSale.Tests.Architecture;

public sealed class NavigationCategoryStructureCertificationTests
{
    [Fact]
    public void StorefrontCategoryReadsStaySqlLineageAuthoritative()
    {
        var source = ReadRepoFile("PointOfSale.API/Services/Storefront/StorefrontCategoryService.cs");

        Assert.Contains("CategoryLineageAuthority.LoadTenantSnapshotAsync", source, StringComparison.Ordinal);
        Assert.Contains("TenantLineageAuthority.IsActiveTenantAsync", source, StringComparison.Ordinal);
        Assert.Contains("BuildSqlCategoryTreeAsync", source, StringComparison.Ordinal);
        Assert.Contains("LoadSqlCategoryBySlugAsync", source, StringComparison.Ordinal);
        Assert.Contains("lineage.IsLiveInLineage(row.CategoryId)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("var cachedTree = await TryGetTreeFromCacheAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("document = await _repository.FindBySlugAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CategorySeoBreadcrumbsAndSitemapStaySqlLineageAuthoritative()
    {
        var source = ReadRepoFile("PointOfSale.API/Services/Storefront/SeoService.cs");

        Assert.Contains("LoadCurrentCategoriesAsync", source, StringComparison.Ordinal);
        Assert.Contains("LoadCurrentCategoryBySlugAsync", source, StringComparison.Ordinal);
        Assert.Contains("IsCurrentCategorySlugAsync", source, StringComparison.Ordinal);
        Assert.Contains("CategoryLineageAuthority.LoadTenantSnapshotAsync", source, StringComparison.Ordinal);
        Assert.Contains("lineage.IsLiveInLineage(row.CategoryId)", source, StringComparison.Ordinal);
        Assert.Contains("Cached category slug resolution rejected by current SQL lineage authority", source, StringComparison.Ordinal);
        Assert.DoesNotContain("var categories = await _categoryRepository.GetAllCategoriesAsync(tenantId, cancellationToken);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("var category = await _categoryRepository.FindBySlugAsync(tenantId, slug, cancellationToken);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StorefrontNavigationFiltersCategoryLinksThroughSqlLineage()
    {
        var source = ReadRepoFile("PointOfSale.API/Controllers/StorefrontNavigationController.cs");

        Assert.Contains("_dbContext.StoreConfigs", source, StringComparison.Ordinal);
        Assert.Contains("tenant.Status == TenantStatus.Active", source, StringComparison.Ordinal);
        Assert.Contains("authority.StorefrontStatus", source, StringComparison.Ordinal);
        Assert.Contains("CategoryLineageAuthority.LoadTenantSnapshotAsync", source, StringComparison.Ordinal);
        Assert.Contains("lineage.IsLiveInLineage(row.CategoryId)", source, StringComparison.Ordinal);
        Assert.Contains("FilterNavigationMenu", source, StringComparison.Ordinal);
        Assert.Contains("IsLiveCategoryNavigationLink", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IMongoCollection", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_redis", source, StringComparison.Ordinal);
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
