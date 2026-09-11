using PointOfSale.Core.Entities;
using PointOfSale.Core.Pricing;

namespace PointOfSale.Tests.Architecture;

[Collection("CommerceHubPriceOverrideRuntimePolicy")]
public sealed class CommerceHubPriceOverrideBusinessModelCertificationTests
{
    [Fact]
    public async Task CorrectedBusinessModel_MustKeepOverrideRuntimeDisabled()
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
    public void CorrectedBusinessModel_MustFreezeMerchantPricingAndGovernanceVisibilityOwnership()
    {
        var certification = ReadRepoFile("docs/architecture/commercehub-price-override-business-model-correction-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        foreach (var marker in new[]
        {
            "Tenant merchants own CommerceHub price override decisions.",
            "CommerceHub visibility authority owner: marketplace governance.",
            "Marketplace governance may not approve prices, reject prices, create prices, or edit merchant prices.",
            "`CommerceHubPriceOverrideManage`",
            "`CommerceHubPriceOverrideAudit`",
            "`CommerceHubPriceOverrideEmergencyDisable`",
            "optional `Draft`, then `Saved`, `Updated`, or `Removed`",
            "Audit is oversight and must never become approval.",
            "Overrides remain disabled and no runtime pricing activation occurs."
        })
        {
            Assert.Contains(marker, certification, StringComparison.Ordinal);
        }

        Assert.Contains("PASS 13.5B CommerceHub Price Override Business Model Correction", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS 13.5B CommerceHub Price Override Business Model Correction Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS 13.5B CommerceHub Price Override Business Model Correction", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS 13.5B CommerceHub Price Override Business Model Correction Freeze", codexRules, StringComparison.Ordinal);
    }

    [Fact]
    public void CorrectedBusinessModel_MustRemoveApprovalCentricPricingAssumptions()
    {
        foreach (var relativePath in new[]
        {
            "docs/architecture/commercehub-price-override-persistence-audit-certification-findings.md",
            "docs/architecture/commercehub-price-override-governance-rollout-certification-findings.md",
            "docs/architecture/commercehub-price-override-controlled-activation-certification-findings.md",
            ".ai-context/PROJECT_CONTEXT.md",
            ".ai-context/ARCHITECTURE_TRUTH.md",
            ".ai-context/KNOWN_RISKS.md",
            ".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md",
            ".ai-context/CODEX_RULES.md"
        })
        {
            var source = ReadRepoFile(relativePath);
            Assert.DoesNotContain("CommerceHubPriceOverrideApprove", source, StringComparison.Ordinal);
            Assert.DoesNotContain("PendingApproval", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void RolloutAndRecoveryControls_MustSurviveOwnershipCorrection()
    {
        var certification = ReadRepoFile("docs/architecture/commercehub-price-override-business-model-correction-certification-findings.md");
        var resolver = ReadRepoFile("PointOfSale.Core/Pricing/CommerceHubPriceAuthority.cs");
        var listing = ReadRepoFile("PointOfSale.Core/Entities/CommerceHubListing.cs");
        var config = ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/CommerceHubListingConfiguration.cs");
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        Assert.Contains("default-off global kill switch", certification, StringComparison.Ordinal);
        Assert.Contains("default-empty tenant allowlist", certification, StringComparison.Ordinal);
        Assert.Contains("Current SQL authority still wins.", certification, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPriceOverrideRuntimePolicy", resolver, StringComparison.Ordinal);
        Assert.Contains("ValidateCommerceHubPriceOverride", listing, StringComparison.Ordinal);
        Assert.Contains("\\\"CommerceHubPrice\\\" IS NULL OR \\\"CommerceHubPrice\\\" > 0", config, StringComparison.Ordinal);
        Assert.Contains("GuardCommerceHubPriceAuthority", dbContext, StringComparison.Ordinal);
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
