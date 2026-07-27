namespace PointOfSale.Tests.Architecture;

public sealed class StorefrontThemeAuthorityCertificationTests
{
    [Fact]
    public void PublicRendering_MustUseLiveSqlThemeState()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/StorefrontStoreConfigController.cs");
        var service = ReadRepoFile("PointOfSale.API/Services/Admin/AdminStoreConfigService.cs");
        var projection = ReadRepoFile("PointOfSale.Infrastructure/Consumers/StorefrontConfigProjectionConsumer.cs");

        Assert.Contains("ThemeLivePresetKey", controller, StringComparison.Ordinal);
        Assert.Contains("config.ThemeLivePresetKey = config.ThemePresetKey", service, StringComparison.Ordinal);
        Assert.Contains("ThemeLivePresetKey", projection, StringComparison.Ordinal);
        Assert.DoesNotContain("storeConfig.ThemePresetKey ??", projection, StringComparison.Ordinal);
    }

    [Fact]
    public void ThemePreview_MustRequireServerIssuedTenantBoundCapability()
    {
        var resolver = ReadRepoFile("PointOfSale.Storefront/src/storefront/themes/ThemeResolver.ts");
        var storefrontApi = ReadRepoFile("PointOfSale.Storefront/src/services/api/storefrontApi.ts");
        var controller = ReadRepoFile("PointOfSale.API/Controllers/StorefrontStoreConfigController.cs");
        var marketplace = ReadRepoFile("PointOfSale.Web/src/modules/storefront/pages/ThemeMarketplacePage.jsx");

        Assert.DoesNotContain("previewTheme", resolver, StringComparison.Ordinal);
        Assert.Contains("themePreviewToken", storefrontApi, StringComparison.Ordinal);
        Assert.Contains("_themePreviewTokenService.TryValidate", controller, StringComparison.Ordinal);
        Assert.Contains("createThemePreviewLink", marketplace, StringComparison.Ordinal);
    }

    [Fact]
    public void ThemeCustomization_MustRemainPresentationOnly()
    {
        var service = ReadRepoFile("PointOfSale.API/Services/Admin/AdminStoreConfigService.cs");
        var catalog = ReadRepoFile("PointOfSale.Core/Constants/StorefrontThemes.cs");

        Assert.Contains("ValidateThemeOverrideShape", service, StringComparison.Ordinal);
        Assert.Contains("allowedRootKeys", service, StringComparison.Ordinal);
        Assert.DoesNotContain("\"currency\",", service, StringComparison.Ordinal);
        Assert.DoesNotContain("\"featureFlags\",", service, StringComparison.Ordinal);
        Assert.Contains("Supported", catalog, StringComparison.Ordinal);
    }

    [Fact]
    public void ThemeRegistry_MustBeManifestBackedAndRejectAmbiguousRegistration()
    {
        var backendCatalog = ReadRepoFile("PointOfSale.Core/Constants/StorefrontThemes.cs");
        var runtimeRegistry = ReadRepoFile("PointOfSale.Storefront/src/storefront/themes/ThemeRegistry.ts");
        var marketplaceRegistry = ReadRepoFile("PointOfSale.Web/src/modules/storefront/themes/themeMarketplaceRegistry.js");

        Assert.Contains("StorefrontThemeDefinition", backendCatalog, StringComparison.Ordinal);
        Assert.Contains("Capabilities", backendCatalog, StringComparison.Ordinal);
        Assert.Contains("Duplicate storefront theme id", backendCatalog, StringComparison.Ordinal);
        Assert.Contains("Exactly one default storefront theme", backendCatalog, StringComparison.Ordinal);

        Assert.Contains("capabilities", runtimeRegistry, StringComparison.Ordinal);
        Assert.Contains("validateThemeRegistry", runtimeRegistry, StringComparison.Ordinal);
        Assert.Contains("Duplicate or empty theme id", runtimeRegistry, StringComparison.Ordinal);

        Assert.Contains("capabilities", marketplaceRegistry, StringComparison.Ordinal);
        Assert.Contains("validateThemeMarketplaceRegistry", marketplaceRegistry, StringComparison.Ordinal);
        Assert.Contains("Duplicate or empty theme id", marketplaceRegistry, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationArtifacts_MustRecordP2GcTruth()
    {
        Assert.Contains(
            "PASS P2-GC Storefront Theme Authority",
            ReadRepoFile("docs/architecture/storefront-theme-authority-certification-findings.md"),
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
            Assert.Contains("PASS P2-GC", ReadRepoFile(path), StringComparison.Ordinal);
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
