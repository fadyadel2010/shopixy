namespace PointOfSale.Tests.Architecture;

public sealed class MarketplaceCmsSeoAuthorityCertificationTests
{
    [Fact]
    public void MarketplaceCmsMutationsRequireSqlVersionAuthorityAndImmutableDrafts()
    {
        var service = ReadRepoFile("PointOfSale.API/Services/MarketplaceCms/MarketplaceCmsPageService.cs");
        var controller = ReadRepoFile("PointOfSale.API/SuperAdmin/Controllers/SuperAdminMarketplaceCmsPagesController.cs");
        var dto = ReadRepoFile("PointOfSale.API/SuperAdmin/DTOs/MarketplaceCmsDtos.cs");

        Assert.Contains("ExpectedVersion", dto, StringComparison.Ordinal);
        Assert.Contains("BeginTransactionAsync(IsolationLevel.Serializable", service, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", service, StringComparison.Ordinal);
        Assert.Contains("EnsureExpectedVersion(pageId, request.ExpectedVersion.Value", service, StringComparison.Ordinal);
        Assert.Contains("existingDraft.IsDraft = false", service, StringComparison.Ordinal);
        Assert.Contains("Version = pages.Max(item => item.Version) + 1", service, StringComparison.Ordinal);
        Assert.Contains("EnsureExpectedVersion(pageId, expectedVersion, draftPage.Version)", service, StringComparison.Ordinal);
        Assert.Contains("EnsureExpectedVersion(pageId, expectedVersion, GetCurrentEditablePage(pages).Version)", service, StringComparison.Ordinal);
        Assert.Contains("ReadExpectedVersion", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void MarketplaceCmsPreviewIsExactVersionCapabilityOnly()
    {
        var tokenService = ReadRepoFile("PointOfSale.API/Services/MarketplaceCms/MarketplaceCmsPagePreviewTokenService.cs");
        var readService = ReadRepoFile("PointOfSale.API/Services/MarketplaceCms/MarketplaceCmsPageReadService.cs");
        var publicController = ReadRepoFile("PointOfSale.API/Controllers/MarketplaceCmsPagesController.cs");
        var superAdminController = ReadRepoFile("PointOfSale.API/SuperAdmin/Controllers/SuperAdminMarketplaceCmsPagesController.cs");

        Assert.Contains("public int PageVersion", tokenService, StringComparison.Ordinal);
        Assert.Contains("payload.PageVersion < 1", tokenService, StringComparison.Ordinal);
        Assert.Contains("out int pageVersion", tokenService, StringComparison.Ordinal);
        Assert.Contains("CreatePreviewToken(page.Id, version.Slug, version.Version)", superAdminController, StringComparison.Ordinal);
        Assert.Contains("query = query.Where(item => item.Version == pageVersion.Value)", readService, StringComparison.Ordinal);
        Assert.Contains("GetPreviewPageByIdAsync(", publicController, StringComparison.Ordinal);
        Assert.Contains("pageVersion", publicController, StringComparison.Ordinal);
        Assert.Contains("Cache-Control", publicController, StringComparison.Ordinal);
    }

    [Fact]
    public void MarketplaceCmsPublicRenderingSeoAndSitemapsDeriveFromPublishedSqlTruth()
    {
        var marketplaceRead = ReadRepoFile("PointOfSale.API/Services/MarketplaceCms/MarketplaceCmsPageReadService.cs");
        var seo = ReadRepoFile("PointOfSale.API/Services/Storefront/SeoService.cs");

        Assert.Contains("LoadCurrentPublishedPageBySlugAsync", marketplaceRead, StringComparison.Ordinal);
        Assert.Contains("item.IsPublished && !item.IsDraft", marketplaceRead, StringComparison.Ordinal);
        Assert.Contains("item.PublishedAt != null", marketplaceRead, StringComparison.Ordinal);
        Assert.Contains("FilterCachedPagesByCurrentSqlAuthorityAsync", marketplaceRead, StringComparison.Ordinal);
        Assert.Contains("LoadCurrentPublishedCmsPageBySlugAsync", seo, StringComparison.Ordinal);
        Assert.Contains("LoadCurrentPublishedCmsPagesAsync", seo, StringComparison.Ordinal);
        Assert.Contains("IsCurrentPublishedCmsPageAsync", seo, StringComparison.Ordinal);
        Assert.Contains("IsTenantPubliclyActiveAsync", seo, StringComparison.Ordinal);
        Assert.Contains("BuildCanonicalUrl(baseUrl, \"cms\", page.Slug)", seo, StringComparison.Ordinal);
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
