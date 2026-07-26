namespace PointOfSale.Tests.Architecture;

public sealed class StoreBuilderDraftAuthorityCertificationTests
{
    [Fact]
    public void StoreBuilderDraftRuntime_MustStartFromBackendAdmittedWorkspace()
    {
        var builder = ReadRepoFile("PointOfSale.Web/src/modules/storefront/pages/StorefrontBuilderPage.jsx");

        Assert.Contains("admitWorkspaceConfig", builder, StringComparison.Ordinal);
        Assert.Contains("deriveCurrentStoreIdentityPresentation(response, currentUser)", builder, StringComparison.Ordinal);
        Assert.Contains("getAcceptedWorkspaceRowVersion(acceptedAdminConfig)", builder, StringComparison.Ordinal);
        Assert.Contains("setStoreConfig(null)", builder, StringComparison.Ordinal);
        Assert.Contains("admitSections([])", builder, StringComparison.Ordinal);
        Assert.Contains("if (!storeConfig)", builder, StringComparison.Ordinal);
    }

    [Fact]
    public void StoreBuilderSaveAndAutosave_MustSubmitIntentOnlyWithIfMatch()
    {
        var builder = ReadRepoFile("PointOfSale.Web/src/modules/storefront/pages/StorefrontBuilderPage.jsx");
        var service = ReadRepoFile("PointOfSale.Web/src/modules/storefront/services/storeConfigService.js");
        var controller = ReadRepoFile("PointOfSale.API/Controllers/AdminStoreConfigController.cs");

        Assert.Contains("const rowVersion = requireWorkspaceAdmission()", builder, StringComparison.Ordinal);
        Assert.Contains("buildIfMatchOptions(rowVersion", builder, StringComparison.Ordinal);
        Assert.Contains("storeConfigService.updatePageLayout(", builder, StringComparison.Ordinal);
        Assert.Contains("void persistPageLayout(sectionsToSave)", builder, StringComparison.Ordinal);
        Assert.Contains("async updatePageLayout(pageId, pageConfig, options = {})", service, StringComparison.Ordinal);
        Assert.Contains("var precondition = RequireExpectedRowVersion(traceId);", controller, StringComparison.Ordinal);
        Assert.Contains("return Conflict(AdminStoreConfigApiErrorResponse.ConcurrencyConflict(traceId));", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void StoreBuilderAcceptedDraft_MustBeginOnlyAfterBackendPersistenceAndDtoAdmission()
    {
        var builder = ReadRepoFile("PointOfSale.Web/src/modules/storefront/pages/StorefrontBuilderPage.jsx");
        var adminService = ReadRepoFile("PointOfSale.API/Services/Admin/AdminStoreConfigService.cs");

        Assert.Contains("const acceptedConfig = admitWorkspaceConfig(response)", builder, StringComparison.Ordinal);
        Assert.Contains("setStoreConfig(acceptedConfig)", builder, StringComparison.Ordinal);
        Assert.Contains("markClean()", builder, StringComparison.Ordinal);
        Assert.Contains("pageContent[\"draft\"] = NormalizeBuilderPageComposition(pageConfig, pageContent)", adminService, StringComparison.Ordinal);
        Assert.Contains("await SaveDraftStoreConfigAsync(config, traceId, cancellationToken);", adminService, StringComparison.Ordinal);
        Assert.Contains("return await MapToResponseAsync(config, cancellationToken);", adminService, StringComparison.Ordinal);
        Assert.Contains("Property(x => x.RowVersion)", adminService, StringComparison.Ordinal);
        Assert.Contains("OriginalValue = expectedRowVersion.Value", adminService, StringComparison.Ordinal);
    }

    [Fact]
    public void StoreBuilderPreview_MustUseAuthenticatedDraftWithoutLiveFallback()
    {
        var builder = ReadRepoFile("PointOfSale.Web/src/modules/storefront/pages/StorefrontBuilderPage.jsx");
        var preview = ReadRepoFile("PointOfSale.Web/src/modules/storefront/components/builder/PreviewModal.jsx");
        var service = ReadRepoFile("PointOfSale.Web/src/modules/storefront/services/storeConfigService.js");

        Assert.Contains("const result = await persistPageLayout(sections)", builder, StringComparison.Ordinal);
        Assert.Contains("if (!result.saved)", builder, StringComparison.Ordinal);
        Assert.Contains("storeConfigService.getPreviewConfig", builder, StringComparison.Ordinal);
        Assert.Contains("Authenticated draft preview that renders backend preview data locally.", preview, StringComparison.Ordinal);
        Assert.Contains("This preview uses authenticated draft data only and will never fall back to live content.", preview, StringComparison.Ordinal);
        Assert.Contains("Live content will not be substituted automatically.", preview, StringComparison.Ordinal);
        Assert.Contains("/storefront/config/preview", service, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
