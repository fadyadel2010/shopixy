using PointOfSale.Core.Entities;
using PointOfSale.Core.Pricing;

namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubPriceAuthorityCertificationTests
{
    [Fact]
    public async Task Resolver_CurrentRuntimeBehavior_RemainsVariantEffectiveOnlinePrice()
    {
        var resolver = new CommerceHubPriceAuthority();
        var variant = new ProductVariant
        {
            Price = 100m,
            OnlinePrice = 75m
        };

        var resolution = await resolver.ResolveAsync(variant, overridePrice: 50m);

        Assert.Equal(75m, resolution.FinalPrice);
        Assert.Equal(75m, resolution.BaseVariantPrice);
        Assert.Null(resolution.OverridePrice);
        Assert.Equal(CommerceHubPriceSource.ProductVariantEffectiveOnlinePrice, resolution.PriceSource);
        Assert.True(resolution.AuthorityAccepted);
        Assert.False(CommerceHubPriceAuthority.OverridesEnabled);
    }

    [Fact]
    public void ResolverContract_MustExistAndKeepOverrideActivationDisabled()
    {
        var source = ReadRepoFile("PointOfSale.Core/Pricing/CommerceHubPriceAuthority.cs");
        var listing = ReadRepoFile("PointOfSale.Core/Entities/CommerceHubListing.cs");

        Assert.Contains("ICommerceHubPriceAuthority", source, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPriceResolution", source, StringComparison.Ordinal);
        Assert.Contains("FinalPrice", source, StringComparison.Ordinal);
        Assert.Contains("BaseVariantPrice", source, StringComparison.Ordinal);
        Assert.Contains("OverridePrice", source, StringComparison.Ordinal);
        Assert.Contains("PriceSource", source, StringComparison.Ordinal);
        Assert.Contains("AuthorityAccepted", source, StringComparison.Ordinal);
        Assert.Contains("FailureReason", source, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPriceOverrideRuntimePolicy", source, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPrice = commerceHubPrice", listing, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubPricingPaths_MustUseCommerceHubPriceAuthority()
    {
        var program = ReadRepoFile("PointOfSale.API/Program.cs");
        var handlers = ReadRepoFile("PointOfSale.API/CommerceHub/Handlers/CommerceHubCommandHandlers.cs");
        var cart = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCartService.cs");
        var checkout = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");
        var projection = ReadRepoFile("PointOfSale.Infrastructure/Consumers/CommerceHubProjectionConsumer.cs");
        var overlay = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchSqlAuthorityOverlay.cs");
        var query = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubQueryService.cs");
        var reindex = ReadRepoFile("PointOfSale.Infrastructure/Jobs/CommerceHubReindexService.cs");
        var autoHealing = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubAutoHealingService.cs");

        Assert.Contains("ICommerceHubPriceAuthority", program, StringComparison.Ordinal);
        Assert.Contains("ICommerceHubPriceAuthority", handlers, StringComparison.Ordinal);
        Assert.Contains("_priceAuthority.ResolveAsync", cart, StringComparison.Ordinal);
        Assert.Contains("_priceAuthority.ResolveAsync", checkout, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPriceAuthority", projection, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPriceAuthority", overlay, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", query, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", reindex, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", autoHealing, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationDocumentation_MustRecordPassThirteenThree()
    {
        var certification = ReadRepoFile("docs/architecture/commercehub-price-authority-resolver-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        Assert.Contains("PASS 13.3", certification, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPriceAuthority", certification, StringComparison.Ordinal);
        Assert.Contains("Runtime behavior unchanged", certification, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPrice override remains disabled", certification, StringComparison.Ordinal);
        Assert.Contains("PASS 13.3 CommerceHubPriceAuthority Resolver", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS 13.3 CommerceHubPriceAuthority Resolver Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS 13.3 CommerceHubPriceAuthority Resolver", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS 13.3 CommerceHubPriceAuthority Resolver Freeze", codexRules, StringComparison.Ordinal);
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
