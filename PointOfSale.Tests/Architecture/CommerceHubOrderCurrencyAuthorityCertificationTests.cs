namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubOrderCurrencyAuthorityCertificationTests
{
    [Fact]
    public void CommerceHubOrderRuntime_MustPersistAndExposeEgpOnly()
    {
        var checkout = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");
        var cart = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCartService.cs");
        var customerOrders = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCustomerOrderService.cs");
        var orderController = ReadRepoFile("PointOfSale.API/Controllers/CommerceHubOrdersController.cs");

        Assert.Contains("var currency = ResolveCommerceHubAuthorityCurrency();", checkout, StringComparison.Ordinal);
        Assert.Contains("Currency = currency", checkout, StringComparison.Ordinal);
        Assert.Contains("FinancialCurrencyGuard.PlatformBaseCurrencyCode", cart, StringComparison.Ordinal);
        Assert.Contains("!order.SubOrders.Any(subOrder => subOrder.Currency != FinancialCurrencyGuard.PlatformBaseCurrencyCode)", customerOrders, StringComparison.Ordinal);
        Assert.Contains("IsEgpOrderProjection(document)", orderController, StringComparison.Ordinal);
        Assert.Contains("!order.SubOrders.Any(subOrder => subOrder.Currency != FinancialCurrencyGuard.PlatformBaseCurrencyCode)", orderController, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubOrderDatabaseBoundary_MustRejectNewNonEgpRows()
    {
        var marketplaceOrderConfiguration = ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/CommerceHubMarketplaceOrderConfiguration.cs");
        var subOrderConfiguration = ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/CommerceHubSubOrderConfiguration.cs");
        var migration = ReadRepoFile("PointOfSale.Infrastructure/Migrations/20260723214642_Baseline_Recovery3.cs");

        Assert.Contains("CK_CommerceHubMarketplaceOrders_Currency_EGP", marketplaceOrderConfiguration, StringComparison.Ordinal);
        Assert.Contains("CK_CommerceHubSubOrders_Currency_EGP", subOrderConfiguration, StringComparison.Ordinal);
        Assert.Contains("CK_CommerceHubMarketplaceOrders_Currency_EGP", migration, StringComparison.Ordinal);
        Assert.Contains("CK_CommerceHubSubOrders_Currency_EGP", migration, StringComparison.Ordinal);
        Assert.Contains("CommerceHubMarketplaceOrders", migration, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSubOrders", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubFrontend_MustWithholdNonEgpCartCheckoutAndOrderEvidence()
    {
        var financialAuthority = ReadRepoFile("PointOfSale.CommerceHub/src/features/orderFinancialAuthority.ts");
        var cartService = ReadRepoFile("PointOfSale.CommerceHub/src/services/cart-service.ts");
        var checkoutAuthority = ReadRepoFile("PointOfSale.CommerceHub/src/features/checkout/orderCreationAuthority.ts");
        var orderIdentity = ReadRepoFile("PointOfSale.CommerceHub/src/features/customer/orderIdentityAuthority.ts");

        Assert.Contains("requireCommerceHubCartStateCurrencyAuthority", financialAuthority, StringComparison.Ordinal);
        Assert.Contains("rejected non-EGP currency evidence", financialAuthority, StringComparison.Ordinal);
        Assert.Contains("requireCommerceHubCartStateCurrencyAuthority(response.data)", cartService, StringComparison.Ordinal);
        Assert.Contains("requireCommerceHubCartPricingAuthority(response.data)", cartService, StringComparison.Ordinal);
        Assert.Contains("isCommerceHubAuthorityCurrency(response.currency)", checkoutAuthority, StringComparison.Ordinal);
        Assert.Contains("isCommerceHubAuthorityCurrency(typeof order.currency", orderIdentity, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
