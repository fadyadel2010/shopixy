namespace PointOfSale.Tests.Architecture;

public sealed class AffiliateCurrencyProjectionArchitectureTests
{
    [Fact]
    public void AffiliateProductIntelligenceProjectionConsumer_MustResolveTenantCurrency_And_HandleCurrencyEvents()
    {
        var source = File.ReadAllText(InfrastructurePath("Consumers", "Affiliate", "AffiliateProductIntelligenceProjectionConsumer.cs"));

        Assert.DoesNotContain("Currency, \"EGP\"", source, StringComparison.Ordinal);
        Assert.Contains("IConsumer<TenantCurrencyChangedEvent>", source, StringComparison.Ordinal);
        Assert.Contains("ResolveTenantCurrencyAsync", source, StringComparison.Ordinal);
        Assert.Contains("NormalizeCurrency(", source, StringComparison.Ordinal);
        Assert.Contains("var currency = await ResolveTenantCurrencyAsync(tenantId, ct);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("context.Message.Currency", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AffiliateProductPolicyProjectionConsumer_MustProjectTenantCurrency_WithoutHardcodingEgp()
    {
        var source = File.ReadAllText(InfrastructurePath("Consumers", "Affiliate", "AffiliateProductPolicyProjectionConsumer.cs"));

        Assert.DoesNotContain("Currency = \"EGP\"", source, StringComparison.Ordinal);
        Assert.Contains("IConsumer<TenantCurrencyChangedEvent>", source, StringComparison.Ordinal);
        Assert.Contains("ResolveTenantCurrencyAsync", source, StringComparison.Ordinal);
        Assert.Contains("NormalizeCurrency(", source, StringComparison.Ordinal);
        Assert.Contains("tenantProfile.Currency", source, StringComparison.Ordinal);
        Assert.Contains("UpdateManyAsync(filter, update, cancellationToken: ct)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AffiliateReindexScript_MustBackfillCurrencyForExistingAffiliateDocuments()
    {
        var scriptSource = File.ReadAllText(InfrastructurePath("Scripts", "AF16E_ReindexAffiliateProductsWithStorefrontUrl.cs"));

        Assert.Contains("CurrencyNormalizationHelper.NormalizeCurrencyOrThrow", scriptSource, StringComparison.Ordinal);
        Assert.Contains("SqlTruthRebuildGuard.RequireSqlAuthority", scriptSource, StringComparison.Ordinal);
        Assert.Contains("SqlTruthRebuildTarget.Affiliate", scriptSource, StringComparison.Ordinal);
        Assert.Contains("tenantProjectionMap[tenant.Id] = (", scriptSource, StringComparison.Ordinal);
        Assert.DoesNotContain("? \"USD\"", scriptSource, StringComparison.Ordinal);
        Assert.DoesNotContain(": \"USD\"", scriptSource, StringComparison.Ordinal);
        Assert.Contains("product.Currency = projection.Currency;", scriptSource, StringComparison.Ordinal);
        Assert.Contains("_mongoDbContext.AffiliateProductIntelligence", scriptSource, StringComparison.Ordinal);
        Assert.Contains("UpdatedIntelligenceProducts", scriptSource, StringComparison.Ordinal);
        Assert.Contains("LoadAffiliateVisibilityAuthorityAsync", scriptSource, StringComparison.Ordinal);
        Assert.Contains("StalePublicProductsDeleted", scriptSource, StringComparison.Ordinal);
        Assert.Contains("StaleIntelligenceProductsDeleted", scriptSource, StringComparison.Ordinal);
        Assert.Contains("Removing affiliate product because current tenant storefront/currency authority is missing", scriptSource, StringComparison.Ordinal);
    }

    private static string InfrastructurePath(params string[] parts)
    {
        // Check for environment variable first (useful for CI/CD)
        var solutionRoot = Environment.GetEnvironmentVariable("TEST_SOLUTION_ROOT");
        
        if (string.IsNullOrEmpty(solutionRoot))
        {
            // Dynamically discover solution root by walking up from test output directory
            solutionRoot = DiscoverSolutionRoot(AppContext.BaseDirectory);
        }

        return Path.GetFullPath(Path.Combine(
            new[] { solutionRoot, "PointOfSale.Infrastructure" }
                .Concat(parts)
                .ToArray()));
    }

    private static string DiscoverSolutionRoot(string startPath)
    {
        var currentDir = new DirectoryInfo(startPath);

        while (currentDir != null)
        {
            // Look for solution markers: .sln file or PointOfSale.Infrastructure directory
            if (currentDir.GetFiles("*.sln").Any() ||
                currentDir.GetDirectories("PointOfSale.Infrastructure").Any())
            {
                return currentDir.FullName;
            }

            currentDir = currentDir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not discover solution root from '{startPath}'. " +
            "Ensure tests run from within the solution directory structure, " +
            "or set the TEST_SOLUTION_ROOT environment variable.");
    }
}
