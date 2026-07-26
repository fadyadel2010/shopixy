namespace PointOfSale.Tests.Architecture;

public sealed class StorefrontEndToEndAuthorityCertificationTests
{
    [Fact]
    public void StorefrontRuntimePipeline_MustReenterBackendAuthorityAtEveryBoundary()
    {
        var bootstrap = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/bootstrap/bootstrap.ts");
        var storefrontApi = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/services/api/storefrontApi.ts");
        var cartStore = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/store/cartStore.ts");
        var checkoutStore = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/store/checkoutStore.ts");
        var recoverCartPage = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/pages/RecoverCartPage.tsx");
        var recoverCheckoutPage = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/pages/RecoverCheckoutPage.tsx");
        var confirmationPage = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/pages/OrderConfirmationPage.tsx");

        Assert.Contains("resolveTenant()", bootstrap, StringComparison.Ordinal);
        Assert.Contains("initializeSession()", bootstrap, StringComparison.Ordinal);
        Assert.Contains("const config = await fetchStoreConfig();", bootstrap, StringComparison.Ordinal);
        Assert.Contains("store.setConfig(config);", bootstrap, StringComparison.Ordinal);

        Assert.Contains("fetchProductBySlug", storefrontApi, StringComparison.Ordinal);
        Assert.Contains("resolveProductVariant", storefrontApi, StringComparison.Ordinal);
        Assert.Contains("fetchCart", storefrontApi, StringComparison.Ordinal);
        Assert.Contains("recoverCheckout", storefrontApi, StringComparison.Ordinal);
        Assert.Contains("recoverCart", storefrontApi, StringComparison.Ordinal);

        Assert.Contains("await fetchCart();", cartStore, StringComparison.Ordinal);
        Assert.Contains("await addCartItem({ productId, quantity, variantId })", cartStore, StringComparison.Ordinal);
        Assert.Contains("detectCartStateMismatch(cart)", cartStore, StringComparison.Ordinal);
        Assert.Contains("resolveCartFetchFailure(", cartStore, StringComparison.Ordinal);
        Assert.Contains("restoreRecoveredCart: (cart, message) =>", cartStore, StringComparison.Ordinal);

        Assert.Contains("refreshPricingPreview: async () =>", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("CHECKOUT_ENDPOINTS.PRICING", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("await useCartStore.getState().fetchCart();", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("await get().refreshPricingPreview();", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("expectedPricing: buildExpectedPricing(latestState.pricing)", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("httpClient.post<CheckoutResponse>", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("requireStorefrontPaymentSessionAuthority(response.data, latestState.paymentMethod, cart.cartId)", checkoutStore, StringComparison.Ordinal);

        Assert.Contains("const response = await recoverCart(token);", recoverCartPage, StringComparison.Ordinal);
        Assert.Contains("isRecoveredCartSnapshot(response.cartSnapshot)", recoverCartPage, StringComparison.Ordinal);
        Assert.Contains("restoreRecoveredCart(response.cartSnapshot, response.message)", recoverCartPage, StringComparison.Ordinal);

        Assert.Contains("const session = await recoverCheckout(token);", recoverCheckoutPage, StringComparison.Ordinal);
        Assert.Contains("buildRecoveredCart(session)", recoverCheckoutPage, StringComparison.Ordinal);
        Assert.Contains("requiresReview", recoverCheckoutPage, StringComparison.Ordinal);
        Assert.Contains("restoreRecoveredCart(recoveredCart)", recoverCheckoutPage, StringComparison.Ordinal);

        Assert.Contains("paymentStatusApi.getCanonicalPaymentStatus", confirmationPage, StringComparison.Ordinal);
        Assert.Contains("resolveBackendPaymentScreen(paymentStatus.status)", confirmationPage, StringComparison.Ordinal);
        Assert.Contains("paymentStatus?.status !== 'PAID'", confirmationPage, StringComparison.Ordinal);
        Assert.Contains("finalizeSuccessfulCheckout(orderId)", confirmationPage, StringComparison.Ordinal);
    }

    [Fact]
    public void BackendAuthorityChain_MustOwnAdmissionSnapshotRecoveryAndConfirmation()
    {
        var configController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/StorefrontStoreConfigController.cs");
        var productsController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/StorefrontProductsController.cs");
        var productService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Storefront/StorefrontProductService.cs");
        var cartController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/StorefrontCartController.cs");
        var cartRecoveryController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/StorefrontCartRecoveryController.cs");
        var checkoutSessionController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/CheckoutSessionController.cs");
        var checkoutController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/CheckoutController.cs");
        var checkoutService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var ordersReadController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/StorefrontOrdersReadController.cs");
        var stripePaymentsController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/StorefrontStripePaymentsController.cs");
        var paymentStatusApi = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/services/api/paymentStatusApi.ts");
        var paymentFlow = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/features/checkout/paymentFlow.ts");

        Assert.Contains("_dbContext.StoreConfigs", configController, StringComparison.Ordinal);
        Assert.Contains("_dbContext.Tenants", configController, StringComparison.Ordinal);
        Assert.Contains("StorefrontDraftConfigMapper.MapLive", configController, StringComparison.Ordinal);

        Assert.Contains("ResolveProductVariantAsync", productsController, StringComparison.Ordinal);
        Assert.Contains("ApplyNoStoreHeaders", productsController, StringComparison.Ordinal);
        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyAsync", productService, StringComparison.Ordinal);
        Assert.Contains("RecomposeCachedProductDetailFromSqlAuthorityAsync", productService, StringComparison.Ordinal);
        Assert.Contains("StorefrontPricingPresentationMapper.Build", productService, StringComparison.Ordinal);
        Assert.Contains("StorefrontAvailabilityPresentationMapper.Build", productService, StringComparison.Ordinal);

        Assert.Contains("ValidateHeadersAsync(traceId)", cartController, StringComparison.Ordinal);
        Assert.Contains("_cartService.AddItemAsync", cartController, StringComparison.Ordinal);
        Assert.Contains("RevalidateCouponForResponseAsync", cartController, StringComparison.Ordinal);
        Assert.Contains("_liveCartService.GetCartAsync", cartRecoveryController, StringComparison.Ordinal);
        Assert.Contains("stored snapshot is not cart authority", cartRecoveryController, StringComparison.Ordinal);
        Assert.Contains("live_cart_unavailable", cartRecoveryController, StringComparison.Ordinal);

        Assert.Contains("BuildAuthoritativeItemsSnapshotJson(authoritativeCart)", checkoutSessionController, StringComparison.Ordinal);
        Assert.Contains("var recoveredLiveCart = await TryRevalidateRecoveredCartAsync(", checkoutSessionController, StringComparison.Ordinal);
        Assert.Contains("RECOVERY_CART_REVALIDATION_FAILED", checkoutSessionController, StringComparison.Ordinal);
        Assert.Contains("var responseCart = MapRecoveredCartItems(recoveredLiveCart.Cart);", checkoutSessionController, StringComparison.Ordinal);

        Assert.Contains("_checkoutService.PreviewPricingAsync(", checkoutController, StringComparison.Ordinal);
        Assert.Contains("_checkoutService.ProcessCheckoutAsync(", checkoutController, StringComparison.Ordinal);
        Assert.Contains("ApplyGuestOrderAccessCookies(response);", checkoutController, StringComparison.Ordinal);

        Assert.Contains("EnsureStorefrontAcceptingOrdersAsync(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("GetCartSnapshotAsync(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("RecalculatePricesAsync(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ValidateShippingMethodAsync(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ValidatePaymentMethodAsync(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("EnsureExpectedPricingMatches(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("IsolationLevel.Serializable", checkoutService, StringComparison.Ordinal);
        Assert.Contains("RevalidateCheckoutAuthorityInsideTransactionAsync(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ValidateAndReserveInventoryAsync(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("_dbContext.Orders.Add(order);", checkoutService, StringComparison.Ordinal);
        Assert.Contains("PricingSnapshotJson", checkoutService, StringComparison.Ordinal);

        Assert.Contains("GuestOrderAccessCookie", ordersReadController, StringComparison.Ordinal);
        Assert.Contains("GetPaymentStatus", stripePaymentsController, StringComparison.Ordinal);
        Assert.Contains("GuestOrderAccessCookie.ReadCheckoutToken(Request)", stripePaymentsController, StringComparison.Ordinal);
        Assert.Contains("paymentStatusApi", paymentStatusApi, StringComparison.Ordinal);
        Assert.Contains("requireCanonicalPaymentStatusForOrder(orderId, response.data)", paymentStatusApi, StringComparison.Ordinal);
        Assert.Contains("Backend payment status did not prove the requested payment identity.", paymentFlow, StringComparison.Ordinal);
        Assert.Contains("resolveBackendPaymentScreen(status", paymentFlow, StringComparison.Ordinal);
    }

    [Fact]
    public void StateNavigationAndRecovery_MustRemainPresentationOrCandidateOnly()
    {
        var checkoutStore = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/store/checkoutStore.ts");
        var activeOrderContext = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/features/checkout/activeCheckoutOrderContext.ts");
        var paymentSessionAuthority = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/features/checkout/paymentSessionAuthority.ts");
        var cartStateResolver = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/store/cartStateResolver.ts");

        Assert.Contains("partialize: (state) => ({", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("shippingMethodId: state.shippingMethodId", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("paymentMethod: state.paymentMethod", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("selectedPaymentMethodCode: state.selectedPaymentMethodCode", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("step: state.step", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("window.sessionStorage", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("address: cloneShippingAddress", checkoutStore, StringComparison.Ordinal);
        Assert.DoesNotContain("orderResult:", Slice(checkoutStore, "partialize: (state) => ({", "}),"), StringComparison.Ordinal);
        Assert.DoesNotContain("pricing:", Slice(checkoutStore, "partialize: (state) => ({", "}),"), StringComparison.Ordinal);

        Assert.Contains("return null;", activeOrderContext, StringComparison.Ordinal);
        Assert.Contains("Deliberately no-op", activeOrderContext, StringComparison.Ordinal);
        Assert.Contains("Checkout responses are provider-handoff evidence, never durable frontend", paymentSessionAuthority, StringComparison.Ordinal);
        Assert.Contains("throw new Error('Backend checkout response did not prove valid payment-session presentation evidence.')", paymentSessionAuthority, StringComparison.Ordinal);

        Assert.Contains("nextCart: emptyCart", cartStateResolver, StringComparison.Ordinal);
        Assert.Contains("preserveLastValidCart: false", cartStateResolver, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationArtifacts_MustRecordSfFinalTruth()
    {
        Assert.Contains(
            "PASS SF-FINAL Storefront End-to-End Authority Certification",
            CertificationSource.ReadRepoFile("docs/architecture/storefront-final-end-to-end-authority-certification-findings.md"),
            StringComparison.Ordinal);

        foreach (var path in new[]
                 {
                     ".ai-context/PROJECT_CONTEXT.md",
                     ".ai-context/ARCHITECTURE_TRUTH.md",
                     ".ai-context/KNOWN_RISKS.md",
                     ".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md",
                     ".ai-context/CODEX_RULES.md"
                 })
        {
            Assert.Contains("PASS SF-FINAL", CertificationSource.ReadRepoFile(path), StringComparison.Ordinal);
        }
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing start marker: {startMarker}");
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"Missing end marker: {endMarker}");
        return source[start..end];
    }
}
