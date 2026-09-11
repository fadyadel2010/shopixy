namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubMarketplaceCheckoutFrontendCertificationTests
{
    [Fact]
    public void MarketplaceFrontendRoutes_MustEnterCertifiedCheckoutWithoutDuplicateCheckoutPages()
    {
        var router = ReadRepoFile("PointOfSale.CommerceHub/src/router/index.tsx");
        var checkoutPage = ReadRepoFile("PointOfSale.CommerceHub/src/pages/CheckoutPage.tsx");
        var endpoints = ReadRepoFile("PointOfSale.CommerceHub/src/api/endpoints.ts");

        Assert.Contains("path: \"cart\"", router, StringComparison.Ordinal);
        Assert.Contains("element: <CartPage />", router, StringComparison.Ordinal);
        Assert.Contains("path: \"checkout\"", router, StringComparison.Ordinal);
        Assert.Contains("element: <CheckoutPage />", router, StringComparison.Ordinal);
        Assert.Contains("path: \"orders/:id/recover\"", router, StringComparison.Ordinal);
        Assert.Contains("path: \"orders\"", router, StringComparison.Ordinal);
        Assert.Contains("path: \"orders/:id\"", router, StringComparison.Ordinal);

        Assert.Contains("import { CheckoutForm, CheckoutSummary, PaymentMethodSelector } from \"@/checkout\";", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("<CheckoutForm customer={customer}", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("<PaymentMethodSelector", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("<CheckoutSummary", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("checkoutService.placeOrder(request, requestIdempotencyKey)", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("persistActivePaymentRecoveryContext", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("readActivePaymentRecoveryContext", checkoutPage, StringComparison.Ordinal);

        Assert.Contains("checkout: \"/api/commercehub/checkout\"", endpoints, StringComparison.Ordinal);
        Assert.Contains("cartPricing: \"/api/commercehub/cart/price\"", endpoints, StringComparison.Ordinal);
        Assert.Contains("paymentInitiate: \"/api/commercehub/payments/initiate\"", endpoints, StringComparison.Ordinal);
        Assert.DoesNotContain("marketplaceCheckout", endpoints, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/api/marketplace/checkout", endpoints, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/api/marketplace/shipping", endpoints, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/api/marketplace/payment", endpoints, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MarketplaceFrontend_MustNotCreateReplacementCheckoutShippingPaymentReviewOrSummaryComponents()
    {
        var frontendFiles = EnumerateCommerceHubSourceFiles()
            .Select(path => Path.GetRelativePath(RepoRoot(), path).Replace('\\', '/'))
            .ToArray();

        foreach (var forbiddenFileName in new[]
        {
            "MarketplaceCheckoutPage.tsx",
            "MarketplaceShippingStep.tsx",
            "MarketplacePaymentStep.tsx",
            "MarketplaceReviewStep.tsx",
            "MarketplaceOrderSummary.tsx",
            "MarketplacePendingPage.tsx",
            "MarketplaceTrackingPage.tsx",
            "ShippingStep.tsx",
            "ReviewStep.tsx",
            "OrderSummary.tsx"
        })
        {
            Assert.DoesNotContain(frontendFiles, path => path.EndsWith(forbiddenFileName, StringComparison.OrdinalIgnoreCase));
        }

        Assert.Contains(frontendFiles, path => path.EndsWith("PointOfSale.CommerceHub/src/pages/CheckoutPage.tsx", StringComparison.Ordinal));
        Assert.Contains(frontendFiles, path => path.EndsWith("PointOfSale.CommerceHub/src/checkout/CheckoutForm.tsx", StringComparison.Ordinal));
        Assert.Contains(frontendFiles, path => path.EndsWith("PointOfSale.CommerceHub/src/checkout/CheckoutSummary.tsx", StringComparison.Ordinal));
        Assert.Contains(frontendFiles, path => path.EndsWith("PointOfSale.CommerceHub/src/checkout/PaymentMethodSelector.tsx", StringComparison.Ordinal));
    }

    [Fact]
    public void MerchantGroupingAndSplitPreview_MustRemainPresentationOnly()
    {
        var cartPage = ReadRepoFile("PointOfSale.CommerceHub/src/pages/CartPage.tsx");
        var storeGroup = ReadRepoFile("PointOfSale.CommerceHub/src/cart/StoreGroup.tsx");
        var checkoutSummary = ReadRepoFile("PointOfSale.CommerceHub/src/checkout/CheckoutSummary.tsx");

        Assert.Contains("storeIds.map((storeId)", cartPage, StringComparison.Ordinal);
        Assert.Contains("<StoreGroup key={storeId} storeId={storeId} />", cartPage, StringComparison.Ordinal);

        Assert.Contains("aria-expanded={isExpanded}", storeGroup, StringComparison.Ordinal);
        Assert.Contains("aria-controls={`store-group-${storeId}`}", storeGroup, StringComparison.Ordinal);
        Assert.Contains("{store.storeName}", storeGroup, StringComparison.Ordinal);
        Assert.Contains("Store subtotal", storeGroup, StringComparison.Ordinal);
        Assert.Contains("Remove store", storeGroup, StringComparison.Ordinal);
        Assert.Contains("disabled={isCartLocked}", storeGroup, StringComparison.Ordinal);

        Assert.Contains("cart.stores.map((store)", checkoutSummary, StringComparison.Ordinal);
        Assert.Contains("{store.storeName}", checkoutSummary, StringComparison.Ordinal);
        Assert.Contains("Store subtotal", checkoutSummary, StringComparison.Ordinal);
        Assert.Contains("Store shipping", checkoutSummary, StringComparison.Ordinal);
        Assert.Contains("Store total", checkoutSummary, StringComparison.Ordinal);
        Assert.Contains("storeFinancialValue(store.subtotal, store.currency)", checkoutSummary, StringComparison.Ordinal);
        Assert.Contains("storeFinancialValue(store.shipping, store.currency)", checkoutSummary, StringComparison.Ordinal);
        Assert.Contains("storeFinancialValue(store.total, store.currency)", checkoutSummary, StringComparison.Ordinal);
        Assert.Contains("financialSummary && value !== undefined", checkoutSummary, StringComparison.Ordinal);

        Assert.DoesNotContain("fetch(", storeGroup, StringComparison.Ordinal);
        Assert.DoesNotContain("httpClient.", storeGroup, StringComparison.Ordinal);
        Assert.DoesNotContain("checkoutService.", checkoutSummary, StringComparison.Ordinal);
        Assert.DoesNotContain("cartService.", checkoutSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void ShippingAndPaymentFrontend_MustRenderBackendIssuedEvidenceWithoutMarketplaceAuthority()
    {
        var checkoutPage = ReadRepoFile("PointOfSale.CommerceHub/src/pages/CheckoutPage.tsx");
        var cartPricingHook = ReadRepoFile("PointOfSale.CommerceHub/src/cart/useCommerceHubCartPricing.ts");
        var paymentSelector = ReadRepoFile("PointOfSale.CommerceHub/src/checkout/PaymentMethodSelector.tsx");
        var checkoutService = ReadRepoFile("PointOfSale.CommerceHub/src/services/checkout-service.ts");
        var cartService = ReadRepoFile("PointOfSale.CommerceHub/src/services/cart-service.ts");
        var combinedFrontend = string.Join('\n', EnumerateCommerceHubSourceFiles().Select(File.ReadAllText));

        Assert.Contains("useCommerceHubCartPricing(checkoutPayload, [], appliedPromoCode)", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("paymentMethods = cartPricingQuery.data?.paymentMethods ?? []", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("authoritativePricing.paymentMethods.find", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("authoritativePricing.shippingTotal", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("authoritativePricing.storeSelections", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("buildCommerceHubCheckoutRequest", checkoutPage, StringComparison.Ordinal);

        Assert.Contains("cartService.priceCart(request)", cartPricingHook, StringComparison.Ordinal);
        Assert.Contains("cartService.priceCart(authoritativeRequest)", cartPricingHook, StringComparison.Ordinal);
        Assert.Contains("requireCommerceHubCartPricingAuthority(response.data)", cartService, StringComparison.Ordinal);
        Assert.Contains("requireCheckoutOrderCreationAuthority(response.data)", checkoutService, StringComparison.Ordinal);
        Assert.Contains("requirePaymentInitiationAuthority(response.data", checkoutService, StringComparison.Ordinal);

        Assert.Contains("paymentMethod.isAvailable && optionMetadata[paymentMethod.code]", paymentSelector, StringComparison.Ordinal);
        Assert.DoesNotContain("paymentMethod.unavailableReason", paymentSelector, StringComparison.Ordinal);
        Assert.Contains("pricing?.storeSelections.map", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("shippingByStore.get(store.storeId)", checkoutPage, StringComparison.Ordinal);
        Assert.DoesNotContain("checkoutService.initiatePayment", paymentSelector, StringComparison.Ordinal);
        Assert.DoesNotContain("stripe.confirmPayment", paymentSelector, StringComparison.Ordinal);

        Assert.DoesNotContain("FedEx", combinedFrontend, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("calculateShipping", combinedFrontend, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MarketplaceShipping", combinedFrontend, StringComparison.Ordinal);
    }

    [Fact]
    public void MarketplaceFrontendCertificationDocumentation_MustBeFrozen()
    {
        var certification = ReadRepoFile("docs/architecture/COMMERCEHUB_MARKETPLACE_CHECKOUT_3_FRONTEND.md");

        foreach (var marker in new[]
        {
            "## Executive Summary",
            "## Frontend Review",
            "## Component Reuse",
            "## Routing Review",
            "## Merchant Group Review",
            "## Split Preview Review",
            "## Shipping UI",
            "## Payment UI",
            "## Responsive UX",
            "## Accessibility",
            "## Regression Coverage",
            "## Certification Decision",
            "Marketplace Frontend Certified? **Yes.**",
            "Checkout Frontend Reused? **Yes.**",
            "Dynamic Rendering Certified? **Yes.**"
        })
        {
            Assert.Contains(marker, certification, StringComparison.Ordinal);
        }
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);

    private static IEnumerable<string> EnumerateCommerceHubSourceFiles()
    {
        var sourceRoot = Path.Combine(RepoRoot(), "PointOfSale.CommerceHub", "src");
        return Directory.EnumerateFiles(sourceRoot, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase));
    }

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
