namespace PointOfSale.Tests.Architecture;

public sealed class StoreBuilderEntryWorkspaceAuthorityCertificationTests
{
    [Fact]
    public void StoreBuilderRoute_MustEnterThroughPosAdminOwnerAdminAdmission()
    {
        var app = ReadRepoFile("PointOfSale.Web/src/App.jsx");
        var sidebar = ReadRepoFile("PointOfSale.Web/src/components/Sidebar.jsx");

        Assert.Contains("<Route element={<PosRoleRouteGuard roles={['Owner', 'Admin']} />}>", app, StringComparison.Ordinal);
        Assert.Contains("path=\"storefront/builder\"", app, StringComparison.Ordinal);
        Assert.Contains("/storefront/builder?page=home", sidebar, StringComparison.Ordinal);
        Assert.Contains("requiredRoles: ['Owner', 'Admin']", sidebar, StringComparison.Ordinal);
    }

    [Fact]
    public void StoreBuilderWorkspace_MustWithholdRenderingUntilBackendBootstrapAdmission()
    {
        var builder = ReadRepoFile("PointOfSale.Web/src/modules/storefront/pages/StorefrontBuilderPage.jsx");

        Assert.Contains("admitWorkspaceConfig", builder, StringComparison.Ordinal);
        Assert.Contains("deriveCurrentStoreIdentityPresentation(response, currentUser)", builder, StringComparison.Ordinal);
        Assert.Contains("if (!storeConfig)", builder, StringComparison.Ordinal);
        Assert.Contains("Workspace admission failed", builder, StringComparison.Ordinal);
        Assert.Contains("Checking the current store and draft with backend authority.", builder, StringComparison.Ordinal);
        Assert.Contains("setStoreConfig(null)", builder, StringComparison.Ordinal);
    }

    [Fact]
    public void StoreBuilderDraftMutations_MustCarryBackendRowVersionEvidence()
    {
        var builder = ReadRepoFile("PointOfSale.Web/src/modules/storefront/pages/StorefrontBuilderPage.jsx");
        var controller = ReadRepoFile("PointOfSale.API/Controllers/AdminStoreConfigController.cs");

        Assert.Contains("getAcceptedWorkspaceRowVersion", builder, StringComparison.Ordinal);
        Assert.Contains("requireWorkspaceAdmission", builder, StringComparison.Ordinal);
        Assert.Contains("buildIfMatchOptions(rowVersion", builder, StringComparison.Ordinal);
        Assert.Contains("storeConfigService.updatePageLayout(", builder, StringComparison.Ordinal);
        Assert.Contains("admitWorkspaceConfig(response)", builder, StringComparison.Ordinal);
        Assert.Contains("var precondition = RequireExpectedRowVersion(traceId);", controller, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
