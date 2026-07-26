namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubMarketplaceCheckoutUxNavigationCertificationTests
{
    [Fact]
    public void MarketplaceCheckoutUx_MustPreserveCertifiedCheckoutJourneyAndRoutes()
    {
        var router = ReadRepoFile("PointOfSale.CommerceHub/src/router/index.tsx");
        var checkoutPage = ReadRepoFile("PointOfSale.CommerceHub/src/pages/CheckoutPage.tsx");

        Assert.Contains("path: \"cart\"", router, StringComparison.Ordinal);
        Assert.Contains("path: \"checkout\"", router, StringComparison.Ordinal);
        Assert.Contains("path: \"orders\"", router, StringComparison.Ordinal);
        Assert.Contains("path: \"orders/:id\"", router, StringComparison.Ordinal);
        Assert.Contains("path: \"orders/:id/recover\"", router, StringComparison.Ordinal);

        Assert.DoesNotContain("MarketplaceCheckoutPage", router, StringComparison.Ordinal);
        Assert.DoesNotContain("MarketplacePaymentPage", router, StringComparison.Ordinal);
        Assert.DoesNotContain("MarketplaceShippingPage", router, StringComparison.Ordinal);
        Assert.DoesNotContain("MarketplaceReviewPage", router, StringComparison.Ordinal);
        Assert.DoesNotContain("MarketplacePendingPage", router, StringComparison.Ordinal);

        Assert.Contains("saveCart({ persistIfAvailableOnly: false })", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("cartService.priceCart(buildCommerceHubCartPricingRequest(authoritativePayload, [], appliedPromoCode))", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("checkoutService.placeOrder(request, requestIdempotencyKey)", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("persistActivePaymentRecoveryContext", checkoutPage, StringComparison.Ordinal);

        var orderDetails = ReadRepoFile("PointOfSale.CommerceHub/src/pages/CustomerOrderDetailsPage.tsx");
        Assert.Contains("Vendor shipments, item states, and payment context", orderDetails, StringComparison.Ordinal);
        Assert.Contains("Order activity", orderDetails, StringComparison.Ordinal);
        Assert.Contains("order.timeline.map", orderDetails, StringComparison.Ordinal);
    }

    [Fact]
    public void MerchantGroupingAndSplitPreviewUx_MustShowStoreItemsShippingSubtotalsAndMarketplaceTotal()
    {
        var storeGroup = ReadRepoFile("PointOfSale.CommerceHub/src/cart/StoreGroup.tsx");
        var cartSummary = ReadRepoFile("PointOfSale.CommerceHub/src/cart/CartSummary.tsx");
        var checkoutSummary = ReadRepoFile("PointOfSale.CommerceHub/src/checkout/CheckoutSummary.tsx");

        Assert.Contains("aria-expanded={isExpanded}", storeGroup, StringComparison.Ordinal);
        Assert.Contains("aria-controls={`store-group-${storeId}`}", storeGroup, StringComparison.Ordinal);
        Assert.Contains("{store.storeName}", storeGroup, StringComparison.Ordinal);
        Assert.Contains("Store subtotal", storeGroup, StringComparison.Ordinal);
        Assert.Contains("Collapse", storeGroup, StringComparison.Ordinal);
        Assert.Contains("Expand", storeGroup, StringComparison.Ordinal);

        Assert.Contains("cart.stores.map((store)", checkoutSummary, StringComparison.Ordinal);
        Assert.Contains("{store.storeName}", checkoutSummary, StringComparison.Ordinal);
        Assert.Contains("Store subtotal", checkoutSummary, StringComparison.Ordinal);
        Assert.Contains("Store shipping", checkoutSummary, StringComparison.Ordinal);
        Assert.Contains("Store total", checkoutSummary, StringComparison.Ordinal);
        Assert.Contains("storeFinancialValue(store.subtotal, store.currency)", checkoutSummary, StringComparison.Ordinal);
        Assert.Contains("storeFinancialValue(store.shipping, store.currency)", checkoutSummary, StringComparison.Ordinal);
        Assert.Contains("storeFinancialValue(store.total, store.currency)", checkoutSummary, StringComparison.Ordinal);
        Assert.Contains("storeFinancialValue(itemTotal, store.currency)", checkoutSummary, StringComparison.Ordinal);

        Assert.Contains("Subtotal", cartSummary, StringComparison.Ordinal);
        Assert.Contains("Shipping", cartSummary, StringComparison.Ordinal);
        Assert.Contains("Grand total", cartSummary, StringComparison.Ordinal);
        Assert.Contains("xl:sticky xl:top-28", cartSummary, StringComparison.Ordinal);
        Assert.Contains("fixed inset-x-0 bottom-0", cartSummary, StringComparison.Ordinal);
        Assert.Contains("disabled={isCheckoutDisabled}", cartSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void ShippingAndPaymentUx_MustReuseBackendIssuedMethodsOnly()
    {
        var checkoutPage = ReadRepoFile("PointOfSale.CommerceHub/src/pages/CheckoutPage.tsx");
        var paymentSelector = ReadRepoFile("PointOfSale.CommerceHub/src/checkout/PaymentMethodSelector.tsx");
        var cartPricing = ReadRepoFile("PointOfSale.CommerceHub/src/cart/useCommerceHubCartPricing.ts");
        var endpoints = ReadRepoFile("PointOfSale.CommerceHub/src/api/endpoints.ts");

        Assert.Contains("paymentMethods = cartPricingQuery.data?.paymentMethods ?? []", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("authoritativePricing.paymentMethods.find", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("authoritativePricing.storeSelections", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("authoritativePricing.shippingTotal", checkoutPage, StringComparison.Ordinal);

        Assert.Contains("paymentMethods.filter(pm => optionMetadata[pm.code])", paymentSelector, StringComparison.Ordinal);
        Assert.Contains("!paymentMethod.isAvailable", paymentSelector, StringComparison.Ordinal);
        Assert.Contains("paymentMethod.unavailableReason", paymentSelector, StringComparison.Ordinal);

        Assert.Contains("cartService.priceCart(request)", cartPricing, StringComparison.Ordinal);
        Assert.Contains("cartService.priceCart(authoritativeRequest)", cartPricing, StringComparison.Ordinal);
        Assert.Contains("buildCommerceHubCartPricingRequest(authoritativePayload, shippingSelections, promoCode)", cartPricing, StringComparison.Ordinal);
        Assert.Contains("cartPricing: \"/api/commercehub/cart/price\"", endpoints, StringComparison.Ordinal);
        Assert.DoesNotContain("FedEx", checkoutPage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FedEx", paymentSelector, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MarketplaceCheckoutUxCertificationDocumentation_MustBeFrozen()
    {
        var certification = ReadRepoFile("docs/architecture/COMMERCEHUB_MARKETPLACE_CHECKOUT_2_5_UX_NAVIGATION_CERTIFICATION.md");

        foreach (var marker in new[]
        {
            "## Executive Summary",
            "## Journey",
            "## Navigation",
            "## Merchant Grouping UX",
            "## Split Preview UX",
            "## Shipping UX",
            "## Payment UX",
            "## Recovery UX",
            "## Responsive UX",
            "## Accessibility",
            "## Certification Decision",
            "Marketplace UX Certified? **Yes.**",
            "Checkout UX Reused? **Yes.**",
            "Split Preview UX Certified? **Yes.**"
        })
        {
            Assert.Contains(marker, certification, StringComparison.Ordinal);
        }
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
