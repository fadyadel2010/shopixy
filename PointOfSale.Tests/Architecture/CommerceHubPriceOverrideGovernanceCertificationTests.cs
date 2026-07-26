using PointOfSale.Core.Entities;
using PointOfSale.Core.Pricing;

namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubPriceOverrideGovernanceCertificationTests
{
    [Fact]
    public async Task OperationalGovernanceFreeze_MustNotActivateOverridePricing()
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
    public void OperationalGovernanceFreeze_MustKeepAllRuntimeSafetyLocksClosed()
    {
        var resolver = ReadRepoFile("PointOfSale.Core/Pricing/CommerceHubPriceAuthority.cs");
        var listing = ReadRepoFile("PointOfSale.Core/Entities/CommerceHubListing.cs");
        var config = ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/CommerceHubListingConfiguration.cs");
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");
        var projection = ReadRepoFile("PointOfSale.Infrastructure/Consumers/CommerceHubProjectionConsumer.cs");

        Assert.Contains("CommerceHubPriceOverrideRuntimePolicy", resolver, StringComparison.Ordinal);
        Assert.Contains("ValidateCommerceHubPriceOverride(commerceHubPrice);", listing, StringComparison.Ordinal);
        Assert.Contains("GuardCommerceHubPriceAuthority", dbContext, StringComparison.Ordinal);
        Assert.Contains("\\\"CommerceHubPrice\\\" IS NULL OR \\\"CommerceHubPrice\\\" > 0", config, StringComparison.Ordinal);
        Assert.Contains("priceResolution.OverridePrice", projection, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationDocumentation_MustFreezeOperationalModelAndCheckoutBlockerDecision()
    {
        var certification = ReadRepoFile("docs/architecture/commercehub-price-override-governance-rollout-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        foreach (var marker in new[]
        {
            "PASS 13.5",
            "CommerceHubPriceOverrideManage",
            "CommerceHubPriceOverrideAudit",
            "CommerceHubPriceOverrideEmergencyDisable",
            "optional `Draft`, then `Saved`, `Updated`, or `Removed`",
            "global emergency kill switch",
            "Stage 0",
            "ProductVariant.EffectiveOnlinePrice",
            "Checkout failures must be fixed before activation: Yes",
            "Tenant merchants own CommerceHub price override decisions.",
            "Marketplace governance may not approve, reject, create, or edit merchant prices."
        })
        {
            Assert.Contains(marker, certification, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("CommerceHubPriceOverrideApprove", certification, StringComparison.Ordinal);
        Assert.DoesNotContain("PendingApproval", certification, StringComparison.Ordinal);

        Assert.Contains("PASS 13.5 CommerceHub Price Override Governance", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS 13.5 CommerceHub Price Override Governance Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS 13.5", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS 13.5 CommerceHub Price Override Governance", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS 13.5 CommerceHub Price Override Governance Freeze", codexRules, StringComparison.Ordinal);
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
