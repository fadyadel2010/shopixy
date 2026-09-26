namespace PointOfSale.Tests.Architecture;

public sealed class StorefrontNavigationAuthorityCertificationTests
{
    [Fact]
    public void PublicNavigationIsRecomposedFromCurrentTenantSqlAuthority()
    {
        var navigation = ReadRepoFile("PointOfSale.API/Controllers/StorefrontNavigationController.cs");

        Assert.Contains("_dbContext.StoreConfigs", navigation, StringComparison.Ordinal);
        Assert.Contains("tenant.Status == TenantStatus.Active", navigation, StringComparison.Ordinal);
        Assert.Contains("authority.StorefrontStatus", navigation, StringComparison.Ordinal);
        Assert.Contains("navigation.TryGetProperty(\"live\"", navigation, StringComparison.Ordinal);
        Assert.Contains("CategoryLineageAuthority.LoadTenantSnapshotAsync", navigation, StringComparison.Ordinal);
        Assert.Contains("_dbContext.CmsPages", navigation, StringComparison.Ordinal);
        Assert.Contains("page.IsPublished && !page.IsDraft", navigation, StringComparison.Ordinal);
        Assert.Contains("IsSafeInternalPath", navigation, StringComparison.Ordinal);
        Assert.DoesNotContain("IMongoCollection", navigation, StringComparison.Ordinal);
        Assert.DoesNotContain("_redis", navigation, StringComparison.Ordinal);
        Assert.DoesNotContain("GetDefaultNavigation", navigation, StringComparison.Ordinal);
    }

    [Fact]
    public void NavigationWritesAndPublicationAreVersionAndTargetBound()
    {
        var service = ReadRepoFile("PointOfSale.API/Services/Admin/AdminCmsService.cs");
        var admission = ReadRepoFile("PointOfSale.API/Services/Admin/NavigationAdmissionService.cs");
        var storage = ReadRepoFile("PointOfSale.API/Services/Admin/NavigationStorageAdapter.cs");
        var controller = ReadRepoFile("PointOfSale.API/Controllers/AdminCmsPagesController.cs");

        Assert.Contains("ApplyExpectedRowVersion", service, StringComparison.Ordinal);
        Assert.Contains("NavigationAdmissionMode.Draft", service, StringComparison.Ordinal);
        Assert.Contains("NavigationAdmissionMode.Publish", service, StringComparison.Ordinal);
        Assert.Contains("public async Task<AdminNavigationRequest> AdmitAsync(", admission, StringComparison.Ordinal);
        Assert.Contains("ResolveCategory(", admission, StringComparison.Ordinal);
        Assert.Contains("ResolveProduct(", admission, StringComparison.Ordinal);
        Assert.Contains("ResolveCmsPage(", admission, StringComparison.Ordinal);
        Assert.Contains("public static Dictionary<string, object> ToStorage(", storage, StringComparison.Ordinal);
        Assert.Contains("JsonElement.DeepEquals", service, StringComparison.Ordinal);
        Assert.Contains("Status428PreconditionRequired", controller, StringComparison.Ordinal);
        Assert.Contains("NavigationAdmissionException", controller, StringComparison.Ordinal);
        Assert.Contains("If-Match", controller, StringComparison.Ordinal);
        Assert.Contains("[Authorize(Roles = \"Owner,Admin\")]", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void FooterNavigationIsCurrentSqlOnlyAndReplayProtected()
    {
        var readService = ReadRepoFile("PointOfSale.API/Services/Storefront/StorefrontFooterReadService.cs");
        var writeService = ReadRepoFile("PointOfSale.API/Services/Admin/FooterAuthoringService.cs");
        var validator = ReadRepoFile("PointOfSale.API/Services/Admin/FooterAuthoringValidator.cs");
        var controller = ReadRepoFile("PointOfSale.API/Controllers/AdminStorefrontFooterController.cs");

        Assert.Contains("_dbContext.StorefrontFooterSections", readService, StringComparison.Ordinal);
        Assert.Contains("page.IsPublished && !page.IsDraft", readService, StringComparison.Ordinal);
        Assert.Contains("page.PublishedAt != null", readService, StringComparison.Ordinal);
        Assert.DoesNotContain("IMongoCollection", readService, StringComparison.Ordinal);
        Assert.Contains("ExpectedRevision", writeService, StringComparison.Ordinal);
        Assert.Contains("FOOTER_CONCURRENCY_CONFLICT", writeService, StringComparison.Ordinal);
        Assert.Contains("BeginTransactionAsync(IsolationLevel.Serializable", writeService, StringComparison.Ordinal);
        Assert.Contains("page.IsPublished && !page.IsDraft", writeService, StringComparison.Ordinal);
        Assert.Contains("FooterAuthoringValidator", validator, StringComparison.Ordinal);
        Assert.Contains("FooterAuthoringException", controller, StringComparison.Ordinal);
        Assert.Contains("[Authorize(Roles = \"Owner,Admin\")]", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectionAndStorefrontConsumersRemainDerivative()
    {
        var projection = ReadRepoFile("PointOfSale.Infrastructure/Consumers/StorefrontConfigProjectionConsumer.cs");
        var navigationStore = ReadRepoFile("PointOfSale.Storefront/src/stores/navigationStore.ts");
        var layoutRuntime = ReadRepoFile("PointOfSale.Storefront/src/storefront/runtime/layout/LayoutRuntime.tsx");
        var header = ReadRepoFile("PointOfSale.Storefront/src/components/layout/Header.tsx");

        Assert.Contains("ProjectionEventFence.TryAcquireAsync", projection, StringComparison.Ordinal);
        Assert.Contains("BuildFreshStoreConfigEventFilter", projection, StringComparison.Ordinal);
        Assert.Contains("DerivativeProjectionMutationGuard.RequireReviewedMutation", projection, StringComparison.Ordinal);
        Assert.Contains("Empty navigation - NO fallback content", navigationStore, StringComparison.Ordinal);
        Assert.Contains("headerLinks.map", layoutRuntime, StringComparison.Ordinal);
        Assert.Contains("NavigationSlot", header, StringComparison.Ordinal);
        Assert.DoesNotContain("Fallback: Loading state or basic navigation", header, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationDocumentationFreezesNavigationAuthority()
    {
        var findings = ReadRepoFile("docs/architecture/storefront-navigation-authority-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var rules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        Assert.Contains("P2-GD Storefront Navigation Authority", findings, StringComparison.Ordinal);
        Assert.Contains("production-certifiable", findings, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("P2-GD Storefront Navigation Authority", projectContext, StringComparison.Ordinal);
        Assert.Contains("P2-GD Storefront Navigation Authority", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("P2-GD Storefront Navigation Authority", knownRisks, StringComparison.Ordinal);
        Assert.Contains("P2-GD Storefront Navigation Authority", hardening, StringComparison.Ordinal);
        Assert.Contains("P2-GD Storefront Navigation Authority Freeze", rules, StringComparison.Ordinal);
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
