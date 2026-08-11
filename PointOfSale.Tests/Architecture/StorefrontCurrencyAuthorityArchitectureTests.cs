namespace PointOfSale.Tests.Architecture;

public sealed class StorefrontCurrencyAuthorityArchitectureTests
{
    [Fact]
    public void ShippingMethodService_MustValidateProjectedCurrencyWithoutUsdFallback()
    {
        var source = File.ReadAllText(ApiPath("Services", "Storefront", "ShippingMethodService.cs"));

        Assert.DoesNotContain("doc.Currency ?? \"USD\"", source, StringComparison.Ordinal);
        Assert.Contains("TryResolveProjectedCurrency(", source, StringComparison.Ordinal);
        Assert.Contains("RecordCurrencyProjectionMismatch(projection: \"shipping_methods_projection\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ShippingMethodProjectionConsumer_MustProjectTenantCurrencyFromTenantAuthority()
    {
        var source = File.ReadAllText(InfrastructurePath("Consumers", "ShippingMethodProjectionConsumer.cs"));

        Assert.Contains("ResolveTenantCurrencyAsync", source, StringComparison.Ordinal);
        Assert.Contains("Currency = tenantCurrency", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Currency = \"USD\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Currency = \"EGP\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ShippingMethodSqlFallback_MustPreserveTenantCurrencyAuthority()
    {
        var source = File.ReadAllText(InfrastructurePath("MongoDB", "Services", "ShippingMethodReadRepository.cs"));

        Assert.Contains("ResolveTenantCurrencyAsync", source, StringComparison.Ordinal);
        Assert.Contains(".Select(tenant => tenant.Currency)", source, StringComparison.Ordinal);
        Assert.Contains("Currency = tenantCurrency", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Currency = \"USD\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Currency = \"EGP\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AdminShippingMethodProjectionRepair_MustPreserveTenantCurrencyAuthority()
    {
        var source = File.ReadAllText(ApiPath("Controllers", "AdminDataSyncController.cs"));

        Assert.Contains("ResolveTenantCurrencyAsync", source, StringComparison.Ordinal);
        Assert.Contains("BuildShippingMethodUpsert(shippingMethod, tenantCurrency)", source, StringComparison.Ordinal);
        Assert.Contains(".Set(d => d.Currency, tenantCurrency)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Currency = \"USD\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Currency = \"EGP\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StorefrontConfigProjectionConsumer_MustUseTenantCurrencyWithoutEgpFallback()
    {
        var source = File.ReadAllText(InfrastructurePath("Consumers", "StorefrontConfigProjectionConsumer.cs"));

        Assert.DoesNotContain("tenant.Currency ?? \"EGP\"", source, StringComparison.Ordinal);
        Assert.Contains("ResolveTenantCurrency(tenant)", source, StringComparison.Ordinal);
        Assert.Contains("RecordCurrencyProjectionMismatch(\"storefront_config\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CartService_MustReadTenantCurrencyFromSqlWithoutFallback()
    {
        var source = File.ReadAllText(ApiPath("Services", "Cart", "CartService.cs"));

        Assert.DoesNotContain("?? \"EGP\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("?? \"USD\"", source, StringComparison.Ordinal);
        Assert.Contains("_dbContext.Tenants", source, StringComparison.Ordinal);
        Assert.Contains(".Select(tenant => tenant.Currency)", source, StringComparison.Ordinal);
        Assert.Contains("throw new CurrencyNotConfiguredException(tenantId);", source, StringComparison.Ordinal);
    }

    private static string ApiPath(params string[] parts)
    {
        var solutionRoot = DiscoverSolutionRoot(AppContext.BaseDirectory);
        return Path.GetFullPath(Path.Combine(
            new[] { solutionRoot, "PointOfSale.API" }
                .Concat(parts)
                .ToArray()));
    }

    private static string InfrastructurePath(params string[] parts)
    {
        var solutionRoot = DiscoverSolutionRoot(AppContext.BaseDirectory);
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
            if (currentDir.GetFiles("*.sln").Any() ||
                currentDir.GetDirectories("PointOfSale.API").Any())
            {
                return currentDir.FullName;
            }

            currentDir = currentDir.Parent;
        }

        throw new InvalidOperationException($"Could not discover solution root from '{startPath}'.");
    }
}
