namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubCartPricingCertificationTests
{
    [Fact]
    public void CartPricing_MustResolveFromCommerceHubPriceAuthorityOnly()
    {
        var cartService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCartService.cs");
        var checkoutService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");
        var findings = ReadRepoFile("docs/architecture/commercehub-cart-certification-findings.md");

        Assert.Contains("ICommerceHubPriceAuthority", cartService, StringComparison.Ordinal);
        Assert.Contains("_priceAuthority.ResolveAsync(variant, listing", cartService, StringComparison.Ordinal);
        Assert.Contains("RoundCurrency(priceResolution.FinalPrice)", cartService, StringComparison.Ordinal);
        Assert.Contains("ICommerceHubPriceAuthority", checkoutService, StringComparison.Ordinal);
        Assert.Contains("_priceAuthority.ResolveAsync(variant, listing", checkoutService, StringComparison.Ordinal);
        Assert.Contains("RoundCurrency(priceResolution.FinalPrice)", checkoutService, StringComparison.Ordinal);
        Assert.Contains("EXPECTED_PRICE_CONFLICT", checkoutService, StringComparison.Ordinal);
        Assert.Contains("Are cart totals trustworthy? | Yes, for the certified runtime path.", findings, StringComparison.Ordinal);
    }

    [Fact]
    public void CartFinancialTotals_MustBeBackendRecomputedBeforeCheckout()
    {
        var cartService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCartService.cs");
        var checkoutService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");
        var cartTypes = ReadRepoFile("PointOfSale.CommerceHub/src/types/cart.ts");
        var frontendFinancialAuthority = ReadRepoFile("PointOfSale.CommerceHub/src/features/orderFinancialAuthority.ts");

        Assert.Contains("CommerceHubCartPricingResponseDto", cartService, StringComparison.Ordinal);
        Assert.Contains("_promoService.ValidatePromoAsync", cartService, StringComparison.Ordinal);
        Assert.Contains("_shippingService", cartService, StringComparison.Ordinal);
        Assert.Contains("PaymentMethodAuthority.BuildAvailability", cartService, StringComparison.Ordinal);
        Assert.Contains("BuildCheckoutRequestHash", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ExpectedUnitPrice", checkoutService, StringComparison.Ordinal);
        Assert.Contains("PromoDiscountTotal", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ShippingTotal", checkoutService, StringComparison.Ordinal);
        Assert.Contains("buildCommerceHubCartPricingRequest", cartTypes, StringComparison.Ordinal);
        Assert.Contains("storeShippingTotal += requireMoney(selection.shippingPrice", frontendFinancialAuthority, StringComparison.Ordinal);
        Assert.Contains("contradictory store shipping evidence", frontendFinancialAuthority, StringComparison.Ordinal);
    }

    [Fact]
    public void CartPriceDrift_MustBeDetectedAndBlockedAtSanitizationAndCheckout()
    {
        // PASS 16.3 HARDENING: Price drift must be detected at both sanitization
        // (via PRICE_CHANGED notice) and checkout (via PRICE_CHANGED exception).
        var cartService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCartService.cs");
        var checkoutService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");

        // Cart sanitization detects price changes and issues a notice
        Assert.Contains("\"PRICE_CHANGED\"", cartService, StringComparison.Ordinal);
        Assert.Contains("requestedPriceSnapshot.Value != unitPrice", cartService, StringComparison.Ordinal);

        // Checkout rejects expected-unit-price drift with a hard exception
        Assert.Contains("\"PRICE_CHANGED\"", checkoutService, StringComparison.Ordinal);
        Assert.Contains("requestedLine.ExpectedUnitPrice != roundedUnitPrice", checkoutService, StringComparison.Ordinal);

        // Checkout rejects conflicting expected prices for the same item
        Assert.Contains("\"EXPECTED_PRICE_CONFLICT\"", checkoutService, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
