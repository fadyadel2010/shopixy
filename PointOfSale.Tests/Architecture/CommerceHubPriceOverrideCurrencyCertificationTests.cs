namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubPriceOverrideCurrencyCertificationTests
{
    [Fact]
    public void PriceOverrides_MustRemainAmountOnlyUnderEgpCommerceHubCurrencyAuthority()
    {
        var listing = ReadRepoFile("PointOfSale.Core/Entities/CommerceHubListing.cs");
        var priceAuthority = ReadRepoFile("PointOfSale.Core/Pricing/CommerceHubPriceAuthority.cs");
        var handlers = ReadRepoFile("PointOfSale.API/CommerceHub/Handlers/CommerceHubCommandHandlers.cs");
        var overlay = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchSqlAuthorityOverlay.cs");
        var projection = ReadRepoFile("PointOfSale.Infrastructure/Consumers/CommerceHubProjectionConsumer.cs");

        Assert.Contains("decimal? CommerceHubPrice", listing, StringComparison.Ordinal);
        Assert.Contains("ValidateCommerceHubPriceOverride", listing, StringComparison.Ordinal);
        Assert.DoesNotContain("CommerceHubPriceCurrency", listing, StringComparison.Ordinal);
        Assert.DoesNotContain("OverrideCurrency", listing, StringComparison.Ordinal);

        Assert.Contains("ResolveAsync(tenantId, variant.EffectiveOnlinePrice, listing.CommerceHubPrice", overlay, StringComparison.Ordinal);
        Assert.Contains("FinancialCurrencyGuard.PlatformBaseCurrencyCode", overlay, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPriceOverrideRuntimePolicy.IsEnabledForTenant", priceAuthority, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPriceOverrideSavedEvent", handlers, StringComparison.Ordinal);
        Assert.Contains("priceResolution.OverridePrice", projection, StringComparison.Ordinal);
        Assert.Contains("ResolveCurrency()", projection, StringComparison.Ordinal);
    }

    [Fact]
    public void OverridePersistence_MustNotCreateCurrencyBypassFields()
    {
        var migrations = string.Join(
            Environment.NewLine,
            Directory.GetFiles(RepoRoot(), "*.cs", SearchOption.AllDirectories)
                .Where(path => path.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Select(File.ReadAllText));

        Assert.DoesNotContain("CommerceHubPriceCurrency", migrations, StringComparison.Ordinal);
        Assert.DoesNotContain("CommerceHubPriceOverrideCurrency", migrations, StringComparison.Ordinal);
        Assert.DoesNotContain("OverrideCurrency", migrations, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
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
