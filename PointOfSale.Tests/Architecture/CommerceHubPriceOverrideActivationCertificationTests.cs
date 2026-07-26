using PointOfSale.Core.Entities;
using PointOfSale.Core.Pricing;

namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubPriceOverrideActivationCertificationTests
{
    [Fact]
    public async Task ActivationGate_MustRemainClosedUntilRequiredControlsExist()
    {
        var resolver = new CommerceHubPriceAuthority();
        var variant = new ProductVariant
        {
            Price = 100m,
            OnlinePrice = 80m
        };

        var resolution = await resolver.ResolveAsync(variant, overridePrice: 50m);

        Assert.False(CommerceHubPriceAuthority.OverridesEnabled);
        Assert.Equal(80m, resolution.FinalPrice);
        Assert.Null(resolution.OverridePrice);
        Assert.Equal(CommerceHubPriceSource.ProductVariantEffectiveOnlinePrice, resolution.PriceSource);
    }

    [Fact]
    public void PartialActivation_MustRemainBlockedAcrossWriteAndDerivativePaths()
    {
        var resolver = ReadRepoFile("PointOfSale.Core/Pricing/CommerceHubPriceAuthority.cs");
        var listing = ReadRepoFile("PointOfSale.Core/Entities/CommerceHubListing.cs");
        var config = ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/CommerceHubListingConfiguration.cs");
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");
        var projection = ReadRepoFile("PointOfSale.Infrastructure/Consumers/CommerceHubProjectionConsumer.cs");
        var overlay = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchSqlAuthorityOverlay.cs");

        Assert.Contains("CommerceHubPriceOverrideRuntimePolicy", resolver, StringComparison.Ordinal);
        Assert.Contains("ValidateCommerceHubPriceOverride(commerceHubPrice);", listing, StringComparison.Ordinal);
        Assert.Contains("GuardCommerceHubPriceAuthority", dbContext, StringComparison.Ordinal);
        Assert.Contains("\\\"CommerceHubPrice\\\" IS NULL OR \\\"CommerceHubPrice\\\" > 0", config, StringComparison.Ordinal);
        Assert.Contains("priceResolution.OverridePrice", projection, StringComparison.Ordinal);
        Assert.Contains("listing.CommerceHubPrice", overlay, StringComparison.Ordinal);
    }

    [Fact]
    public void CartAndCheckout_MustKeepUsingCentralPriceAuthority()
    {
        var cart = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCartService.cs");
        var checkout = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");

        Assert.Contains("_priceAuthority.ResolveAsync(variant, listing, cancellationToken)", cart, StringComparison.Ordinal);
        Assert.Contains("\"PRICE_CHANGED\"", cart, StringComparison.Ordinal);
        Assert.Contains("_priceAuthority.ResolveAsync(variant, listing, cancellationToken)", checkout, StringComparison.Ordinal);
        Assert.Contains("\"PRICE_CHANGED\"", checkout, StringComparison.Ordinal);
        Assert.Contains("IsolationLevel.Serializable", checkout, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationDocumentation_MustRecordPassThirteenSixStopDecision()
    {
        var certification = ReadRepoFile("docs/architecture/commercehub-price-override-controlled-activation-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        Assert.Contains("PASS 13.6", certification, StringComparison.Ordinal);
        Assert.Contains("activation is not allowed", certification, StringComparison.Ordinal);
        Assert.Contains("PASS 13.6 CommerceHub Price Override Controlled Activation", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS 13.6 CommerceHub Price Override Controlled Activation Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS 13.6", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS 13.6 CommerceHub Price Override Controlled Activation", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS 13.6 CommerceHub Price Override Controlled Activation Freeze", codexRules, StringComparison.Ordinal);
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
