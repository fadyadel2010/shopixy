namespace PointOfSale.Tests.Architecture;

public sealed class StorefrontAuthoringIntegratedCertificationTests
{
    [Fact]
    public void StoreConfigSurfacesShareOneRowVersionButKeepSeparatePayloadDomains()
    {
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AdminStoreConfigController.cs");
        var service = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Admin/AdminStoreConfigService.cs");
        var activation = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Admin/AdminThemeActivationService.cs");

        Assert.Contains("[HttpPut(\"pages/{pageId}\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpPut(\"theme/overrides\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpPost(\"theme/activate\")]", controller, StringComparison.Ordinal);
        Assert.Contains("RequireExpectedRowVersion", controller, StringComparison.Ordinal);
        Assert.Contains("pages", service, StringComparison.Ordinal);
        Assert.Contains("ThemeDraftOverridesJson", service, StringComparison.Ordinal);
        Assert.Contains("root[\"storefrontTheme\"]", activation, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicStoreConfigIsLiveSqlWhilePreviewUsesDraftAuthority()
    {
        var publicController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/StorefrontStoreConfigController.cs");
        var preview = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Storefront/StorefrontPreviewService.cs");
        var mapper = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Storefront/StorefrontDraftConfigMapper.cs");

        Assert.Contains("_dbContext.StoreConfigs", publicController, StringComparison.Ordinal);
        Assert.Contains("StorefrontDraftConfigMapper.MapLive", publicController, StringComparison.Ordinal);
        Assert.Contains("ExtractDraftNavigation", preview, StringComparison.Ordinal);
        Assert.Contains("ThemeDraftOverridesJson", mapper, StringComparison.Ordinal);
        Assert.Contains("ThemeLiveOverridesJson", mapper, StringComparison.Ordinal);
    }

    [Fact]
    public void CmsPagesOwnIndependentVersionedSqlAuthority()
    {
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AdminCmsPagesController.cs");
        var service = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Cms/CmsPageService.cs");

        Assert.Contains("ExpectedVersion", controller, StringComparison.Ordinal);
        Assert.Contains("GetExpectedCmsVersion", controller, StringComparison.Ordinal);
        Assert.Contains("PageGroupId", service, StringComparison.Ordinal);
        Assert.Contains("IsPublished", service, StringComparison.Ordinal);
        Assert.DoesNotContain("StoreConfig", service, StringComparison.Ordinal);
    }

    [Fact]
    public void NavigationAndDedicatedFooterHaveIndependentPersistenceAndConcurrency()
    {
        var navigation = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Admin/AdminCmsService.cs");
        var footer = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Admin/FooterAuthoringService.cs");

        Assert.Contains("ApplyExpectedRowVersion", navigation, StringComparison.Ordinal);
        Assert.Contains("CustomizationJson", navigation, StringComparison.Ordinal);
        Assert.Contains("ExpectedRevision", footer, StringComparison.Ordinal);
        Assert.Contains("_dbContext.StorefrontFooterSections", footer, StringComparison.Ordinal);
        Assert.DoesNotContain("_dbContext.StoreConfigs", footer, StringComparison.Ordinal);
    }

    [Fact]
    public void NavigationAndFooterCanonicallyResolveStableCmsPageIdentity()
    {
        var navigation = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Admin/NavigationAdmissionService.cs");
        var navigationRuntime = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/StorefrontNavigationController.cs");
        var footerEntity = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/StorefrontFooterLink.cs");
        var footerRuntime = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Storefront/StorefrontFooterReadService.cs");

        Assert.Contains("TargetId", navigation, StringComparison.Ordinal);
        Assert.Contains("PageGroupId", navigationRuntime, StringComparison.Ordinal);
        Assert.Contains("CmsPageGroupId", footerEntity, StringComparison.Ordinal);
        Assert.Contains("CmsPageSlug", footerEntity, StringComparison.Ordinal);
        Assert.Contains("pagesByGroupId.TryGetValue", footerRuntime, StringComparison.Ordinal);
    }

    [Fact]
    public void StorefrontRuntimeComposesOneHeaderHomepageAndDedicatedFooterFromSeparateStores()
    {
        var layout = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/storefront/runtime/layout/LayoutRuntime.tsx");
        var home = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/pages/HomePage.tsx");
        var shell = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/storefront/layout/StorefrontLayout.tsx");

        Assert.Contains("useNavigationStore", layout, StringComparison.Ordinal);
        Assert.Contains("useFooterStore", layout, StringComparison.Ordinal);
        Assert.Contains("config.pages?.home?.sections", home, StringComparison.Ordinal);
        Assert.Contains("<HeaderRuntime", shell, StringComparison.Ordinal);
        Assert.Contains("<FooterRuntime", shell, StringComparison.Ordinal);
    }
}
