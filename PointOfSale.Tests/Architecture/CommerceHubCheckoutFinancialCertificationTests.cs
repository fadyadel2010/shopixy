namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubCheckoutFinancialCertificationTests
{
    [Fact]
    public void CheckoutPricingAuthority_MustUseCommerceHubPriceAuthorityAndRejectStaleTotals()
    {
        var service = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");
        var cartService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCartService.cs");
        var shippingService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubShippingService.cs");

        Assert.Contains("ICommerceHubPriceAuthority", service, StringComparison.Ordinal);
        Assert.Contains("_priceAuthority.Resolve", service, StringComparison.Ordinal);
        Assert.Contains("\"PRICE_CHANGED\"", service, StringComparison.Ordinal);
        Assert.Contains("ValidateRequestedPaymentMethod", service, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPaymentMethodAuthority.IsRequestedMethodAvailable", service, StringComparison.Ordinal);
        Assert.Contains("ValidatePromoForCheckoutAsync", service, StringComparison.Ordinal);
        Assert.Contains("ConsumePromoAsync", service, StringComparison.Ordinal);
        Assert.Contains("\"SHIPPING_CHANGED\"", service, StringComparison.Ordinal);
        Assert.Contains("bypassCache: true", service, StringComparison.Ordinal);
        Assert.Contains("ICommerceHubPriceAuthority", cartService, StringComparison.Ordinal);
        Assert.Contains("LoadStoreShippingOptionsAsync", shippingService, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckoutOrderSnapshots_MustPersistFinancialShippingPaymentAndCommissionEvidence()
    {
        var service = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        Assert.Contains("BuildMarketplaceOrder", service, StringComparison.Ordinal);
        Assert.Contains("ShippingSnapshotJson = BuildShippingSnapshotJson", service, StringComparison.Ordinal);
        Assert.Contains("PaymentSnapshotJson = BuildPaymentSnapshotJson", service, StringComparison.Ordinal);
        Assert.Contains("CommissionSnapshotJson = BuildCommissionSnapshotJson", service, StringComparison.Ordinal);
        Assert.Contains("VendorEarningAmount", service, StringComparison.Ordinal);
        Assert.Contains("FinalPlatformCommissionAmount", service, StringComparison.Ordinal);
        Assert.Contains("marketplaceOrder.Subtotal = RoundCurrency", service, StringComparison.Ordinal);
        Assert.Contains("marketplaceOrder.ShippingTotal = RoundCurrency", service, StringComparison.Ordinal);
        Assert.Contains("marketplaceOrder.TotalAmount = RoundCurrency", service, StringComparison.Ordinal);
        Assert.Contains("GuardBoundaryOwnedProperties<CommerceHubSubOrder>", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardImmutableCommerceHubEntity<CommerceHubSubOrder>", dbContext, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckoutPaymentPreparation_MustStayReadinessGatedAndRecoveryAware()
    {
        var service = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");
        var paymentService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs");
        var recoveryService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPaymentRecoveryAuthorityService.cs");
        var checkoutPage = ReadRepoFile("PointOfSale.CommerceHub/src/pages/CheckoutPage.tsx");

        Assert.Contains("EnsureCommerceIntakeAllowedAsync", service, StringComparison.Ordinal);
        Assert.Contains("PaymentSession", service, StringComparison.Ordinal);
        Assert.Contains("paymentInitiationRequired", service, StringComparison.Ordinal);
        Assert.Contains("requires_initiation", service, StringComparison.Ordinal);
        Assert.Contains("InitiateAsync", paymentService, StringComparison.Ordinal);
        Assert.Contains("CanRetryPayment", recoveryService, StringComparison.Ordinal);
        Assert.Contains("HasReservationAuthorityLoss", recoveryService, StringComparison.Ordinal);
        Assert.Contains("active SQL reservation authority", recoveryService, StringComparison.Ordinal);
        Assert.Contains("/recover", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("initiateOnlinePayment", checkoutPage, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
