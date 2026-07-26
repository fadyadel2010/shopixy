namespace PointOfSale.Tests.Architecture;

public sealed class StorefrontContentAuthorityCertificationTests
{
    [Fact]
    public void PublicContentSeoSearchAndFooterRemainCurrentSqlPublishedOnly()
    {
        var readService = ReadRepoFile("PointOfSale.API/Services/Cms/CmsPageReadService.cs");
        var seoService = ReadRepoFile("PointOfSale.API/Services/Storefront/SeoService.cs");
        var searchController = ReadRepoFile("PointOfSale.API/Controllers/StorefrontSearchController.cs");
        var footerService = ReadRepoFile("PointOfSale.API/Services/Storefront/StorefrontFooterReadService.cs");

        Assert.Contains("item.IsPublished && !item.IsDraft", readService, StringComparison.Ordinal);
        Assert.Contains("LoadCurrentPublishedCmsPageBySlugAsync", seoService, StringComparison.Ordinal);
        Assert.Contains("page.IsPublished && !page.IsDraft", searchController, StringComparison.Ordinal);
        Assert.Contains("_dbContext.StorefrontFooterSections", footerService, StringComparison.Ordinal);
        Assert.Contains("LoadPublishedPagesAsync", footerService, StringComparison.Ordinal);
        Assert.DoesNotContain("IMongoCollection", footerService, StringComparison.Ordinal);
        Assert.DoesNotContain("var cached = await _cacheService.GetPublishedScopeAsync", readService, StringComparison.Ordinal);
    }

    [Fact]
    public void DraftWritePublishDeleteAndPreviewAreVersionBound()
    {
        var service = ReadRepoFile("PointOfSale.API/Services/Cms/CmsPageService.cs");
        var controller = ReadRepoFile("PointOfSale.API/Controllers/AdminCmsPagesController.cs");
        var previewTokens = ReadRepoFile("PointOfSale.API/Services/Cms/CmsPagePreviewTokenService.cs");

        Assert.Contains("ExpectedVersion", service, StringComparison.Ordinal);
        Assert.Contains("IsolationLevel.Serializable", service, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", service, StringComparison.Ordinal);
        Assert.Contains("pages.Max(item => item.Version) + 1", service, StringComparison.Ordinal);
        Assert.Contains("EnsureExpectedVersion", service, StringComparison.Ordinal);
        Assert.Contains("GetExpectedCmsVersion", controller, StringComparison.Ordinal);
        Assert.Contains("Status428PreconditionRequired", controller, StringComparison.Ordinal);
        Assert.Contains("PageVersion", previewTokens, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectionReplayRecomposesSqlAndFailsClosed()
    {
        var consumer = ReadRepoFile("PointOfSale.Infrastructure/Consumers/CmsPageProjectionConsumer.cs");

        Assert.Contains("ProjectionEventFence.TryAcquireAsync", consumer, StringComparison.Ordinal);
        Assert.Contains("item.IsPublished && !item.IsDraft", consumer, StringComparison.Ordinal);
        Assert.Contains("TenantLineageAuthority.IsActiveTenantAsync", consumer, StringComparison.Ordinal);
        Assert.Contains("FailClosedCmsPageProjectionAsync", consumer, StringComparison.Ordinal);
        Assert.Contains("BuildFreshPageEventFilter", consumer, StringComparison.Ordinal);
        Assert.Contains("DerivativeProjectionMutationGuard.RequireReviewedMutation", consumer, StringComparison.Ordinal);
    }

    [Fact]
    public void ContentIdentityAndTenantOwnershipStaySqlScoped()
    {
        var service = ReadRepoFile("PointOfSale.API/Services/Cms/CmsPageService.cs");
        var configuration = ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/CmsPageConfiguration.cs");
        var controller = ReadRepoFile("PointOfSale.API/Controllers/AdminCmsPagesController.cs");

        Assert.Contains("item.TenantId == tenantId", service, StringComparison.Ordinal);
        Assert.Contains("EnsureSlugAvailableAsync", service, StringComparison.Ordinal);
        Assert.Contains("UQ_CmsPages_PageGroupId_Version", configuration, StringComparison.Ordinal);
        Assert.Contains("[Authorize(Roles = \"Owner,Admin\")]", controller, StringComparison.Ordinal);
        Assert.Contains("_tenantContext.RequiredTenantId", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalizedPresentationCannotBypassPagePublication()
    {
        var footerService = ReadRepoFile("PointOfSale.API/Services/Storefront/StorefrontFooterReadService.cs");
        var pageDto = ReadRepoFile("PointOfSale.API/DTOs/Storefront/CmsPageDtos.cs");

        Assert.Contains("LoadPublishedPagesAsync", footerService, StringComparison.Ordinal);
        Assert.Contains("page.IsPublished && !page.IsDraft", footerService, StringComparison.Ordinal);
        Assert.DoesNotContain("Language", pageDto, StringComparison.OrdinalIgnoreCase);
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
