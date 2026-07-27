namespace PointOfSale.Tests.Architecture;

public sealed class StoreBuilderThemeAuthorityCertificationTests
{
    [Fact]
    public void BuilderThemeFrontend_MustAdmitBackendConfigAndSubmitRowVersionedThemeIntentOnly()
    {
        var editor = ReadRepoFile("PointOfSale.Web/src/modules/storefront/pages/ThemeEditorPage.jsx");
        var marketplace = ReadRepoFile("PointOfSale.Web/src/modules/storefront/pages/ThemeMarketplacePage.jsx");
        var service = ReadRepoFile("PointOfSale.Web/src/modules/storefront/services/storeConfigService.js");

        Assert.Contains("deriveCurrentStoreIdentityPresentation(response, user)", editor, StringComparison.Ordinal);
        Assert.Contains("setAcceptedRowVersion(rowVersion)", editor, StringComparison.Ordinal);
        Assert.Contains("buildIfMatchOptions(rowVersion)", editor, StringComparison.Ordinal);
        Assert.Contains("storeConfigService.updateThemeOverrides({", editor, StringComparison.Ordinal);
        Assert.Contains("storeConfigService.publishTheme(buildIfMatchOptions(rowVersion))", editor, StringComparison.Ordinal);
        Assert.Contains("deriveCurrentStoreIdentityPresentation(response, user)", marketplace, StringComparison.Ordinal);
        Assert.Contains("storeConfigService.activateTheme(", marketplace, StringComparison.Ordinal);
        Assert.DoesNotContain("storeConfigService.updateStoreConfig({", marketplace, StringComparison.Ordinal);
        Assert.Contains("createThemePreviewLink(themeId)", service, StringComparison.Ordinal);
        Assert.DoesNotContain("previewTheme", marketplace, StringComparison.Ordinal);
    }

    [Fact]
    public void BuilderThemeBackend_MustOwnThemeAcceptanceVersionAndPreviewCapability()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/AdminStoreConfigController.cs");
        var adminService = ReadRepoFile("PointOfSale.API/Services/Admin/AdminStoreConfigService.cs");
        var themeActivationService = ReadRepoFile("PointOfSale.API/Services/Admin/AdminThemeActivationService.cs");
        var previewTokenService = ReadRepoFile("PointOfSale.API/Services/Storefront/StorefrontThemePreviewTokenService.cs");
        var runtimeController = ReadRepoFile("PointOfSale.API/Controllers/StorefrontStoreConfigController.cs");

        Assert.Contains("RequireExpectedRowVersion(traceId)", controller, StringComparison.Ordinal);
        Assert.Contains("UpdateThemeOverrides", controller, StringComparison.Ordinal);
        Assert.Contains("ActivateTheme", controller, StringComparison.Ordinal);
        Assert.Contains("IAdminThemeActivationService", controller, StringComparison.Ordinal);
        Assert.Contains("ActivateThemeAsync", themeActivationService, StringComparison.Ordinal);
        Assert.DoesNotContain("ActivateThemeAsync", adminService, StringComparison.Ordinal);
        Assert.Contains("PublishTheme", controller, StringComparison.Ordinal);
        Assert.Contains("ApplyExpectedRowVersion(config, expectedRowVersion)", adminService, StringComparison.Ordinal);
        Assert.Contains("ValidateThemeOverrideShape", adminService, StringComparison.Ordinal);
        Assert.Contains("config.ThemeLivePresetKey = config.ThemePresetKey", adminService, StringComparison.Ordinal);
        Assert.Contains("StorefrontThemes.IsSupported(themeId)", previewTokenService, StringComparison.Ordinal);
        Assert.Contains("_themePreviewTokenService.TryValidate", runtimeController, StringComparison.Ordinal);
        Assert.Contains("config.StorefrontTheme = previewThemeId", runtimeController, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationArtifacts_MustRecordSfAdm25Truth()
    {
        Assert.Contains(
            "PASS SF-ADM2.5 Builder Theme Authority Certification",
            ReadRepoFile("docs/architecture/store-builder-theme-authority-certification-findings.md"),
            StringComparison.Ordinal);

        foreach (var path in new[]
                 {
                     ".ai-context/PROJECT_CONTEXT.md",
                     ".ai-context/ARCHITECTURE_TRUTH.md",
                     ".ai-context/KNOWN_RISKS.md",
                     ".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md",
                     ".ai-context/CODEX_RULES.md"
                 })
        {
            Assert.Contains("PASS SF-ADM2.5", ReadRepoFile(path), StringComparison.Ordinal);
        }
    }

    private static string ReadRepoFile(string relativePath)
    {
        var root = DiscoverSolutionRoot(AppContext.BaseDirectory);
        return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string DiscoverSolutionRoot(string startPath)
    {
        var current = new DirectoryInfo(startPath);
        while (current != null)
        {
            if (current.GetFiles("*.sln").Any() || current.GetDirectories("PointOfSale.API").Any())
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException($"Could not discover solution root from '{startPath}'.");
    }
}
