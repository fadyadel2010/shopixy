namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubMarketplaceCheckoutRuntimeCertificationTests
{
    [Fact]
    public void MarketplaceRuntime_MustEnterExistingCheckoutTransactionForDurableEffects()
    {
        var checkout = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");

        Assert.Contains("public class CommerceHubCheckoutService : ICommerceHubCheckoutService", checkout, StringComparison.Ordinal);
        Assert.Contains("IsolationLevel.Serializable", checkout, StringComparison.Ordinal);
        Assert.Contains("BuildCheckoutRequestHash(customerId, request)", checkout, StringComparison.Ordinal);
        Assert.Contains("EnsureCheckoutIdempotencyRequestMatches", checkout, StringComparison.Ordinal);
        Assert.Contains("LockCartForCheckoutAsync", checkout, StringComparison.Ordinal);
        Assert.Contains("LoadCommerceHubListingsForCheckoutAuthorityAsync", checkout, StringComparison.Ordinal);
        Assert.Contains("LoadProductVariantsForCheckoutAuthorityAsync", checkout, StringComparison.Ordinal);
        Assert.Contains("LoadProductsForCheckoutAuthorityAsync", checkout, StringComparison.Ordinal);
        Assert.Contains("LoadTenantsForCheckoutAuthorityAsync", checkout, StringComparison.Ordinal);
        Assert.Contains("BuildMarketplaceOrder", checkout, StringComparison.Ordinal);
        Assert.Contains("_dbContext.CommerceHubMarketplaceOrders.Add(marketplaceOrder)", checkout, StringComparison.Ordinal);
    }

    [Fact]
    public void MerchantGroupingAndPartitionPlanning_MustRemainCheckoutOwnedAndStoreScoped()
    {
        var checkout = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        Assert.Contains("GroupBy(line => line.StoreId)", checkout, StringComparison.Ordinal);
        Assert.Contains("StoreId = group.Key", checkout, StringComparison.Ordinal);
        Assert.Contains("MarketplaceOrderId = marketplaceOrder.Id", checkout, StringComparison.Ordinal);
        Assert.Contains("listing.TenantId != requestedLine.StoreId", checkout, StringComparison.Ordinal);
        Assert.Contains("product.TenantId != requestedLine.StoreId", checkout, StringComparison.Ordinal);
        Assert.Contains("variant.TenantId != requestedLine.StoreId", checkout, StringComparison.Ordinal);
        Assert.Contains("UX_CommerceHubSubOrders_MarketplaceOrderId_StoreId_Live", dbContext, StringComparison.Ordinal);
    }

    [Fact]
    public void SplitPlanning_MustReuseShippingPaymentInventoryOrderAndSettlementAuthorities()
    {
        var checkout = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");
        var shipping = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubShippingService.cs");
        var payment = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs");
        var paymentLifecycle = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPaymentLifecycleAuthority.cs");
        var financialWorkflow = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubFinancialWorkflowService.cs");

        Assert.Contains("using var commerceHubBoundary = CommerceHubBoundaryMutationScope.BeginOrderLifecycle();", checkout, StringComparison.Ordinal);
        Assert.Contains("using var shippingAssignmentBoundary = CommerceHubBoundaryMutationScope.BeginShippingAssignment();", checkout, StringComparison.Ordinal);
        Assert.Contains("using var financialBoundary = CommerceHubBoundaryMutationScope.BeginFinancialWorkflow();", checkout, StringComparison.Ordinal);
        Assert.Contains("using var paymentLifecycleBoundary = CommerceHubBoundaryMutationScope.BeginPaymentLifecycle();", checkout, StringComparison.Ordinal);
        Assert.Contains("ReserveOrderItemAsync", checkout, StringComparison.Ordinal);
        Assert.Contains("Shipping = shippingSelection.ShippingPrice", checkout, StringComparison.Ordinal);
        Assert.Contains("marketplaceOrder.ShippingTotal = RoundCurrency(marketplaceOrder.SubOrders.Sum(subOrder => subOrder.Shipping))", checkout, StringComparison.Ordinal);
        Assert.Contains("method.TenantId == storeId", shipping, StringComparison.Ordinal);
        Assert.Contains("bypassCache", shipping, StringComparison.Ordinal);
        Assert.Contains("using var commerceHubBoundary = CommerceHubBoundaryMutationScope.BeginPayment();", payment, StringComparison.Ordinal);
        Assert.Contains("using var boundary = CommerceHubBoundaryMutationScope.BeginPaymentLifecycle();", paymentLifecycle, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginFinancialWorkflow", financialWorkflow, StringComparison.Ordinal);
    }

    [Fact]
    public void MarketplaceRuntime_MustNotIntroduceDuplicateCheckoutPaymentShippingOrOrderRuntime()
    {
        var csharpSources = EnumerateProductionFiles("*.cs")
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var combinedSources = string.Join('\n', csharpSources.Select(File.ReadAllText));

        Assert.DoesNotContain("class MarketplaceCheckoutService", combinedSources, StringComparison.Ordinal);
        Assert.DoesNotContain("class MarketplaceCheckoutController", combinedSources, StringComparison.Ordinal);
        Assert.DoesNotContain("class MarketplacePaymentService", combinedSources, StringComparison.Ordinal);
        Assert.DoesNotContain("class MarketplaceShippingService", combinedSources, StringComparison.Ordinal);
        Assert.DoesNotContain("class MarketplaceOrderRuntime", combinedSources, StringComparison.Ordinal);
        Assert.DoesNotContain("class MarketplaceShipmentService", combinedSources, StringComparison.Ordinal);
    }

    [Fact]
    public void MarketplaceRuntimeCertificationDocumentation_MustBeFrozen()
    {
        var certification = ReadRepoFile("docs/architecture/COMMERCEHUB_MARKETPLACE_CHECKOUT_1_RUNTIME.md");

        foreach (var marker in new[]
        {
            "## Executive Summary",
            "## Runtime Overview",
            "## Authority Matrix",
            "## Reuse Matrix",
            "## Marketplace Runtime Pipeline",
            "## Merchant Partition Pipeline",
            "## Split Planning Pipeline",
            "## Failure Matrix",
            "## Security Review",
            "## Regression Coverage",
            "## Certification Decision",
            "Marketplace owns Checkout? **No.**",
            "Marketplace owns Payment? **No.**",
            "Marketplace owns Shipment? **No.**",
            "Marketplace owns Inventory? **No.**"
        })
        {
            Assert.Contains(marker, certification, StringComparison.Ordinal);
        }
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);

    private static IEnumerable<string> EnumerateProductionFiles(string pattern)
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        return new[]
            {
                Path.Combine(root, "PointOfSale.API"),
                Path.Combine(root, "PointOfSale.Core"),
                Path.Combine(root, "PointOfSale.Infrastructure")
            }
            .Where(Directory.Exists)
            .SelectMany(path => Directory.EnumerateFiles(path, pattern, SearchOption.AllDirectories));
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
