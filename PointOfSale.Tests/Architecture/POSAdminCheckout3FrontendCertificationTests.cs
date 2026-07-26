namespace PointOfSale.Tests.Architecture;

public sealed class POSAdminCheckout3FrontendCertificationTests
{
    [Fact]
    public void FrontendRoutesAndScreens_MustExposeCertifiedCheckoutRecoveryPendingAndSuccessSurfaces()
    {
        var router = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/app/router.tsx");
        var checkoutPage = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/pages/CheckoutPage.tsx");
        var pendingPage = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/pages/CheckoutPendingPage.tsx");
        var recoveryPage = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/pages/RecoverCheckoutPage.tsx");
        var success = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/components/checkout/CheckoutSuccess.tsx");

        Assert.Contains("path: '/checkout'", router, StringComparison.Ordinal);
        Assert.Contains("path: '/checkout/pending'", router, StringComparison.Ordinal);
        Assert.Contains("path: '/checkout/success'", router, StringComparison.Ordinal);
        Assert.Contains("LegacyCheckoutSuccessRedirect", router, StringComparison.Ordinal);
        Assert.Contains("path: '/recover-checkout/:token'", router, StringComparison.Ordinal);

        Assert.Contains("ShippingStep", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("PaymentStep", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("ReviewStep", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("CheckoutAutosaveBanner", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("OrderSummary", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("buildPendingCheckoutUrl", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("paymentStatusApi.getCanonicalPaymentStatus", pendingPage, StringComparison.Ordinal);
        Assert.Contains("recoverCheckout(token)", recoveryPage, StringComparison.Ordinal);
        Assert.Contains("CheckoutSuccess", success, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckoutStore_MustConsumeOnlyCertifiedStorefrontCheckoutApisForCheckoutSubmissionAndRecovery()
    {
        var store = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/store/checkoutStore.ts");
        var recoveryPage = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/pages/RecoverCheckoutPage.tsx");

        Assert.Contains("CHECKOUT: '/api/storefront/checkout'", store, StringComparison.Ordinal);
        Assert.Contains("PRICING: '/api/storefront/checkout/pricing'", store, StringComparison.Ordinal);
        Assert.Contains("SHIPPING_METHODS: '/api/storefront/shipping-methods'", store, StringComparison.Ordinal);
        Assert.Contains("PAYMENT_METHODS: '/api/storefront/payment-methods'", store, StringComparison.Ordinal);
        Assert.Contains("CHECKOUT_SESSION: '/api/storefront/checkout-session'", store, StringComparison.Ordinal);
        Assert.Contains("CHECKOUT_SESSION_UPDATE", store, StringComparison.Ordinal);
        Assert.Contains("httpClient.post<CheckoutResponse>(", store, StringComparison.Ordinal);
        Assert.Contains("CHECKOUT_ENDPOINTS.CHECKOUT", store, StringComparison.Ordinal);
        Assert.Contains("expectedPricing: buildExpectedPricing(latestState.pricing)", store, StringComparison.Ordinal);

        Assert.DoesNotContain("/api/payments/cod/confirm", store, StringComparison.Ordinal);
        Assert.DoesNotContain("COD_CONFIRM", store, StringComparison.Ordinal);
        Assert.DoesNotContain("confirmCodPayment", store, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/commercehub", store, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("affiliate checkout", store, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("recoverCheckout(token)", recoveryPage, StringComparison.Ordinal);
    }

    [Fact]
    public void ShippingUi_MustRenderBackendMethodsDynamicallyWithoutProviderHardcodingOrCalculations()
    {
        var store = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/store/checkoutStore.ts");
        var shippingStep = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/components/checkout/ShippingStep.tsx");
        var orderSummary = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/components/checkout/OrderSummary.tsx");
        var review = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/components/checkout/ReviewStep.tsx");

        Assert.Contains("fetchShippingMethods", store, StringComparison.Ordinal);
        Assert.Contains("admitStorefrontShippingMethods(response.data)", store, StringComparison.Ordinal);
        Assert.Contains("'Cache-Control': 'no-store'", store, StringComparison.Ordinal);
        Assert.Contains("shippingMethods.map", shippingStep, StringComparison.Ordinal);
        Assert.Contains("name={method.name}", shippingStep, StringComparison.Ordinal);
        Assert.Contains("description={method.description}", shippingStep, StringComparison.Ordinal);
        Assert.Contains("price={method.price}", shippingStep, StringComparison.Ordinal);
        Assert.Contains("shippingSelectionStatus === 'stale'", store, StringComparison.Ordinal);
        Assert.Contains("STALE_SHIPPING_SELECTION", store, StringComparison.Ordinal);
        Assert.Contains("pricing.shipping", orderSummary, StringComparison.Ordinal);
        Assert.Contains("pricing.shipping", review, StringComparison.Ordinal);

        Assert.DoesNotContain("FedEx", shippingStep, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DHL", shippingStep, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bosta", shippingStep, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Aramex", shippingStep, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Mylerz", shippingStep, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ShippingCalculationService", shippingStep, StringComparison.Ordinal);
        Assert.DoesNotContain("pricing.shipping =", orderSummary, StringComparison.Ordinal);
        Assert.DoesNotContain("pricing.shipping =", review, StringComparison.Ordinal);
    }

    [Fact]
    public void PaymentUi_MustRenderBackendMethodsAndDelegateProviderHandoffWithoutSecrets()
    {
        var store = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/store/checkoutStore.ts");
        var paymentStep = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/components/checkout/PaymentStep.tsx");
        var paymentAuthority = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/features/checkout/paymentSessionAuthority.ts");
        var checkoutPage = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/pages/CheckoutPage.tsx");

        Assert.Contains("fetchPaymentMethods", store, StringComparison.Ordinal);
        Assert.Contains("admitStorefrontPaymentMethods(response.data)", store, StringComparison.Ordinal);
        Assert.Contains("paymentMethods.map", paymentStep, StringComparison.Ordinal);
        Assert.Contains("method.displayName", paymentStep, StringComparison.Ordinal);
        Assert.Contains("method.description", paymentStep, StringComparison.Ordinal);
        Assert.Contains("method.isAvailable", paymentStep, StringComparison.Ordinal);
        Assert.Contains("paymentSelectionStatus === 'stale'", store, StringComparison.Ordinal);
        Assert.Contains("STALE_PAYMENT_SELECTION", store, StringComparison.Ordinal);
        Assert.Contains("requireStorefrontPaymentSessionAuthority(response.data", store, StringComparison.Ordinal);
        Assert.Contains("StripePaymentForm", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("PaymobPaymentForm", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("Checkout responses are provider-handoff evidence", paymentAuthority, StringComparison.Ordinal);
        Assert.Contains("throw new Error('Backend checkout response mixed provider payment-session artifacts.'", paymentAuthority, StringComparison.Ordinal);

        Assert.DoesNotContain("client_secret", paymentStep, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("paymentKey =", paymentStep, StringComparison.Ordinal);
        Assert.DoesNotContain("markPaid", store, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PaymentLifecycleService", store, StringComparison.Ordinal);
    }

    [Fact]
    public void NavigationAutosaveRecoveryAndDuplicateSubmitProtection_MustRemainFrontendPresentationOnly()
    {
        var store = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/store/checkoutStore.ts");
        var checkoutPage = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/pages/CheckoutPage.tsx");
        var pendingPage = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/pages/CheckoutPendingPage.tsx");
        var recoveryPage = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/pages/RecoverCheckoutPage.tsx");

        Assert.Contains("goToNextStep", store, StringComparison.Ordinal);
        Assert.Contains("if (!isStepValid(step))", store, StringComparison.Ordinal);
        Assert.Contains("goToPreviousStep", store, StringComparison.Ordinal);
        Assert.Contains("goToStep", store, StringComparison.Ordinal);
        Assert.Contains("if (state.isSubmitting)", store, StringComparison.Ordinal);
        Assert.Contains("return false", store, StringComparison.Ordinal);
        Assert.Contains("scheduleCheckoutAutosave", store, StringComparison.Ordinal);
        Assert.Contains("performCheckoutAutosave", store, StringComparison.Ordinal);
        Assert.Contains("retryAutosaveCheckoutSession", store, StringComparison.Ordinal);
        Assert.Contains("partialize: (state) =>", store, StringComparison.Ordinal);
        Assert.Contains("shippingMethodId: state.shippingMethodId", store, StringComparison.Ordinal);
        Assert.Contains("selectedPaymentMethodCode: state.selectedPaymentMethodCode", store, StringComparison.Ordinal);
        Assert.Contains("step: state.step", store, StringComparison.Ordinal);
        Assert.Contains("writeSensitiveCheckoutSessionStorage", store, StringComparison.Ordinal);
        Assert.Contains("clearSensitiveCheckoutSessionStorage", store, StringComparison.Ordinal);
        Assert.Contains("CheckoutAutosaveBanner", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("setError(", recoveryPage, StringComparison.Ordinal);
        Assert.Contains("requiresReview", recoveryPage, StringComparison.Ordinal);
        Assert.Contains("cartChangedDuringRecovery", recoveryPage, StringComparison.Ordinal);
        Assert.Contains("handleManualRefresh", pendingPage, StringComparison.Ordinal);
        Assert.Contains("MAX_POLL_ATTEMPTS", pendingPage, StringComparison.Ordinal);
    }

    [Fact]
    public void ReviewSummaryResponsiveAndAccessibility_MustUseBackendEvidenceAndCertifiedUxAffordances()
    {
        var checkoutPage = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/pages/CheckoutPage.tsx");
        var stepper = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/components/checkout/CheckoutStepper.tsx");
        var review = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/components/checkout/ReviewStep.tsx");
        var orderSummary = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/components/checkout/OrderSummary.tsx");
        var shipping = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/components/checkout/ShippingStep.tsx");

        Assert.Contains("lg:grid-cols-3", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("lg:sticky lg:top-24", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Checkout steps\"", stepper, StringComparison.Ordinal);
        Assert.Contains("aria-current={status === 'current' ? 'step' : undefined}", stepper, StringComparison.Ordinal);
        Assert.Contains("aria-expanded={isExpanded}", orderSummary, StringComparison.Ordinal);
        Assert.Contains("aria-controls=\"order-summary-content\"", orderSummary, StringComparison.Ordinal);
        Assert.Contains("role=\"alert\"", review, StringComparison.Ordinal);
        Assert.Contains("aria-live=\"assertive\"", review, StringComparison.Ordinal);
        Assert.Contains("aria-live=\"polite\"", review, StringComparison.Ordinal);
        Assert.Contains("aria-invalid={hasError}", shipping, StringComparison.Ordinal);
        Assert.Contains("prefers-reduced-motion", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("heading as HTMLElement).focus()", checkoutPage, StringComparison.Ordinal);

        Assert.Contains("SummarySection", review, StringComparison.Ordinal);
        Assert.Contains("onEdit={() => handleEdit('shipping')}", review, StringComparison.Ordinal);
        Assert.Contains("onEdit={() => handleEdit('payment')}", review, StringComparison.Ordinal);
        Assert.Contains("pricing.subtotal", review, StringComparison.Ordinal);
        Assert.Contains("pricing.shipping", review, StringComparison.Ordinal);
        Assert.Contains("pricing.tax", review, StringComparison.Ordinal);
        Assert.Contains("pricing.total", review, StringComparison.Ordinal);
        Assert.Contains("Retry totals", review, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationDocument_MustExistForPOSAdminCheckout3Frontend()
    {
        var document = CertificationSource.ReadRepoFile("docs/architecture/POSADMIN_CHECKOUT_3_FRONTEND.md");

        Assert.Contains("# POSADMIN-CHECKOUT-3", document, StringComparison.Ordinal);
        Assert.Contains("Frontend Certified: YES", document, StringComparison.Ordinal);
        Assert.Contains("API Consumption Certified: YES", document, StringComparison.Ordinal);
        Assert.Contains("FedEx rendered dynamically? **Yes.**", document, StringComparison.Ordinal);
        Assert.Contains("Business logic inside React? **No.**", document, StringComparison.Ordinal);
    }
}
