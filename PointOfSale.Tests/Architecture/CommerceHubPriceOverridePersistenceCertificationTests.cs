using PointOfSale.Core.Entities;
using PointOfSale.Core.Pricing;

namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubPriceOverridePersistenceCertificationTests
{
    [Fact]
    public async Task OverridePersistence_RemainsInactiveAtRuntime()
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
        Assert.Equal(80m, resolution.BaseVariantPrice);
        Assert.Null(resolution.OverridePrice);
        Assert.Equal(CommerceHubPriceSource.ProductVariantEffectiveOnlinePrice, resolution.PriceSource);
    }

    [Fact]
    public void PersistenceModel_MustKeepOverrideStorageDisabledAndAuditReady()
    {
        var entity = ReadRepoFile("PointOfSale.Core/Entities/CommerceHubListing.cs");
        var config = ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/CommerceHubListingConfiguration.cs");
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");
        var migration = ReadRepoFile("PointOfSale.Infrastructure/Migrations/20260531134000_CommerceHubPriceOverridePersistenceAudit.cs");

        Assert.Contains("CommerceHubPrice { get; private set; }", entity, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPriceOverrideUpdatedAtUtc", entity, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPriceOverrideUpdatedBy", entity, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPriceOverrideReason", entity, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPriceOverrideMutationVersion", entity, StringComparison.Ordinal);
        Assert.Contains("ValidateCommerceHubPriceOverride", entity, StringComparison.Ordinal);

        Assert.Contains("CK_CommerceHubListings_CommerceHubPrice_Positive", config, StringComparison.Ordinal);
        Assert.Contains("\\\"CommerceHubPrice\\\" IS NULL OR \\\"CommerceHubPrice\\\" > 0", config, StringComparison.Ordinal);
        Assert.Contains("HasMaxLength(1024)", config, StringComparison.Ordinal);
        Assert.Contains("GuardCommerceHubPriceAuthority", dbContext, StringComparison.Ordinal);

        Assert.Contains("ADD COLUMN IF NOT EXISTS \"CommerceHubPriceOverrideUpdatedAtUtc\"", migration, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS \"CommerceHubPriceOverrideUpdatedBy\"", migration, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS \"CommerceHubPriceOverrideReason\"", migration, StringComparison.Ordinal);
        Assert.Contains("UPDATE \"CommerceHubListings\"\n                SET \"CommerceHubPrice\" = NULL", migration, StringComparison.Ordinal);
        Assert.Contains("IX_CommerceHubListings_PriceOverrideAudit", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectionAndSearchMetadata_MustExposeFutureStateWithoutActivatingOverridePricing()
    {
        var mongoDocument = ReadRepoFile("PointOfSale.Infrastructure/MongoDB/Documents/CommerceHubProductDocument.cs");
        var searchDocument = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Documents/CommerceHubProductSearchDocument.cs");
        var mapper = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Documents/CommerceHubSearchDocumentMapper.cs");
        var overlay = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchSqlAuthorityOverlay.cs");
        var indexService = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchIndexService.cs");
        var projection = ReadRepoFile("PointOfSale.Infrastructure/Consumers/CommerceHubProjectionConsumer.cs");

        Assert.Contains("public string PriceSource", mongoDocument, StringComparison.Ordinal);
        Assert.Contains("public bool OverridePresent", mongoDocument, StringComparison.Ordinal);
        Assert.Contains("public string PriceSource", searchDocument, StringComparison.Ordinal);
        Assert.Contains("public bool OverridePresent", searchDocument, StringComparison.Ordinal);
        Assert.Contains("PriceSource = string.IsNullOrWhiteSpace(document.PriceSource)", mapper, StringComparison.Ordinal);
        Assert.Contains("OverridePresent = document.OverridePresent", mapper, StringComparison.Ordinal);
        Assert.Contains("document.PriceSource = priceResolution.PriceSource.ToString();", overlay, StringComparison.Ordinal);
        Assert.Contains("document.OverridePresent = priceResolution.OverridePrice.HasValue;", overlay, StringComparison.Ordinal);
        Assert.Contains("\"priceSource\"", indexService, StringComparison.Ordinal);
        Assert.Contains("\"overridePresent\"", indexService, StringComparison.Ordinal);
        Assert.Contains(".Set(x => x.PriceSource, priceResolution.PriceSource.ToString())", projection, StringComparison.Ordinal);
        Assert.Contains(".Set(x => x.OverridePresent, priceResolution.OverridePrice.HasValue)", projection, StringComparison.Ordinal);
        Assert.Contains("document.CommerceHubPriceOverride = priceResolution.OverridePrice;", overlay, StringComparison.Ordinal);
    }

    [Fact]
    public void CartCheckoutAndResolver_MustNotUseOverridePriceForCustomerVisiblePricing()
    {
        var cart = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCartService.cs");
        var checkout = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");
        var resolver = ReadRepoFile("PointOfSale.Core/Pricing/CommerceHubPriceAuthority.cs");

        Assert.Contains("_priceAuthority.ResolveAsync", cart, StringComparison.Ordinal);
        Assert.Contains("_priceAuthority.ResolveAsync", checkout, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPriceOverrideRuntimePolicy", resolver, StringComparison.Ordinal);
        Assert.DoesNotContain("CommerceHubPrice ??", cart, StringComparison.Ordinal);
        Assert.DoesNotContain("CommerceHubPrice ??", checkout, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationDocumentation_MustRecordPassThirteenFour()
    {
        var certification = ReadRepoFile("docs/architecture/commercehub-price-override-persistence-audit-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        foreach (var marker in new[]
        {
            "PASS 13.4",
            "1. Persistence Findings",
            "2. Auditability Findings",
            "3. Governance Findings",
            "4. Migration Findings",
            "5. Replay And Recovery Findings",
            "6. Search And Projection Findings",
            "7. Cart And Checkout Findings",
            "8. Operational Findings",
            "9. Runtime Hardening Performed",
            "10. Regression Freeze Added",
            "11. Architecture Updates",
            "12. Remaining Debt"
        })
        {
            Assert.Contains(marker, certification, StringComparison.Ordinal);
        }

        Assert.Contains("PASS 13.4 CommerceHub Price Override Persistence", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS 13.4 CommerceHub Price Override Persistence Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS 13.4", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS 13.4 CommerceHub Price Override Persistence", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS 13.4 CommerceHub Price Override Persistence Freeze", codexRules, StringComparison.Ordinal);
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
