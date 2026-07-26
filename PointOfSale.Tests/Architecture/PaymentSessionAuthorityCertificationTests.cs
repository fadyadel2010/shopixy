namespace PointOfSale.Tests.Architecture;

public sealed class PaymentSessionAuthorityCertificationTests
{
    [Fact]
    public void StorefrontPaymentHandoffs_MustBeValidatedAsTransientBackendEvidence()
    {
        var boundary = ReadRepoFile("PointOfSale.Storefront/src/features/checkout/paymentSessionAuthority.ts");
        var checkoutStore = ReadRepoFile("PointOfSale.Storefront/src/store/checkoutStore.ts");

        Assert.Contains("requireStorefrontPaymentSessionAuthority", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("response.data, latestState.paymentMethod, cart.cartId", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("'Cache-Control': 'no-store'", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("mixed provider payment-session artifacts", boundary, StringComparison.Ordinal);
        Assert.Contains("paymentIntentId", boundary, StringComparison.Ordinal);
        Assert.Contains("checkoutToken", boundary, StringComparison.Ordinal);
        Assert.Contains("expiresAtUtc", boundary, StringComparison.Ordinal);
        Assert.Contains("cartId !== expectedCart", boundary, StringComparison.Ordinal);
        Assert.Contains("isFutureIsoDate", boundary, StringComparison.Ordinal);
        Assert.Contains("Terminal payment response unexpectedly included reusable provider payment-session artifacts.", boundary, StringComparison.Ordinal);
        Assert.Contains("complete Stripe handoff", boundary, StringComparison.Ordinal);
        Assert.Contains("complete Paymob handoff", boundary, StringComparison.Ordinal);
        Assert.Contains("url.protocol === 'https:'", boundary, StringComparison.Ordinal);
        Assert.Contains("never durable frontend", boundary, StringComparison.Ordinal);

        var checkoutDto = ReadRepoFile("PointOfSale.API/DTOs/Checkout/CheckoutDtos.cs");
        var checkoutService = ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var checkoutController = ReadRepoFile("PointOfSale.API/Controllers/CheckoutController.cs");
        var stripeService = ReadRepoFile("PointOfSale.API/Services/Payments/Stripe/StripePaymentService.cs");
        var paymobService = ReadRepoFile("PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs");

        Assert.Contains("public Guid? PaymentIntentId", checkoutDto, StringComparison.Ordinal);
        Assert.Contains("public string PaymentStatus", checkoutDto, StringComparison.Ordinal);
        Assert.Contains("ResolvePaymentIntentExpirationAsync", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ShouldIssueCheckoutToken", checkoutService, StringComparison.Ordinal);
        Assert.Contains("PAYMOB", checkoutService, StringComparison.Ordinal);
        Assert.Contains("SetNoStoreHeaders();", checkoutController, StringComparison.Ordinal);
        Assert.Contains("StorefrontPaymentSessionTtl", stripeService, StringComparison.Ordinal);
        Assert.Contains("StorefrontPaymentSessionTtl", paymobService, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomerPaymentSessions_MustRemainCurrentBackendHandoffPresentation()
    {
        var authority = ReadRepoFile("PointOfSale.CommerceHub/src/features/checkout/orderCreationAuthority.ts");
        var recovery = ReadRepoFile("PointOfSale.CommerceHub/src/pages/OrderPaymentRecoveryPage.tsx");
        var context = ReadRepoFile("PointOfSale.CommerceHub/src/features/checkout/activePaymentRecoveryContext.ts");

        Assert.Contains("requirePaymentInitiationAuthority", authority, StringComparison.Ordinal);
        Assert.Contains("response.marketplaceOrderId !== expectedOrderId", authority, StringComparison.Ordinal);
        Assert.Contains("requirePaymentAmountAuthority", authority, StringComparison.Ordinal);
        Assert.Contains("refreshedOrder = await orderQuery.refetch()", recovery, StringComparison.Ordinal);
        Assert.Contains("!currentOrder?.canRetryPayment", recovery, StringComparison.Ordinal);
        Assert.Contains("return null;", context, StringComparison.Ordinal);
        Assert.Contains("Mutation/list data does not create a durable recovery identity.", context, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderReturnsStorageAndCanonicalStatus_MustNotCreatePaymentAuthority()
    {
        var paymob = ReadRepoFile("PointOfSale.Storefront/src/components/checkout/PaymobPaymentForm.tsx");
        var status = ReadRepoFile("PointOfSale.Storefront/src/features/checkout/paymentFlow.ts");
        var checkoutContext = ReadRepoFile("PointOfSale.Storefront/src/features/checkout/activeCheckoutOrderContext.ts");

        Assert.Contains("Provider redirect values are evidence only", paymob, StringComparison.Ordinal);
        Assert.Contains("window.history.replaceState", paymob, StringComparison.Ordinal);
        Assert.Contains("resolveStripeClientOutcome", status, StringComparison.Ordinal);
        Assert.Contains("return 'pending';", status, StringComparison.Ordinal);
        Assert.Contains("can never be restored from", checkoutContext, StringComparison.Ordinal);
        Assert.Contains("return null;", checkoutContext, StringComparison.Ordinal);
        Assert.Contains("return null;", checkoutContext, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory!.FullName, relativePath));
    }
}
