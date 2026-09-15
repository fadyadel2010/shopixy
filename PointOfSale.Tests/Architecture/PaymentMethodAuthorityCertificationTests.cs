namespace PointOfSale.Tests.Architecture;

public sealed class PaymentMethodAuthorityCertificationTests
{
    [Fact]
    public void StorefrontPaymentMethodRuntime_MustKeepFrontendCandidateOnly()
    {
        var checkoutPage = ReadRepoFile("PointOfSale.Storefront/src/pages/CheckoutPage.tsx");
        var paymentStep = ReadRepoFile("PointOfSale.Storefront/src/components/checkout/PaymentStep.tsx");
        var reviewStep = ReadRepoFile("PointOfSale.Storefront/src/components/checkout/ReviewStep.tsx");
        var checkoutStore = ReadRepoFile("PointOfSale.Storefront/src/store/checkoutStore.ts");
        var paymentSessionAuthority = ReadRepoFile("PointOfSale.Storefront/src/features/checkout/paymentSessionAuthority.ts");
        var checkoutTypes = ReadRepoFile("PointOfSale.Storefront/src/types/checkout.ts");

        Assert.Contains("fetchPaymentMethods();", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("selectedPaymentMethodCode", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("refreshPricingPreview", checkoutPage, StringComparison.Ordinal);

        Assert.Contains("selectPaymentMethod(method.code)", paymentStep, StringComparison.Ordinal);
        Assert.Contains("method.isAvailable", paymentStep, StringComparison.Ordinal);
        Assert.Contains("orderAmountForValidation = pricing?.total ?? cartSubtotal", paymentStep, StringComparison.Ordinal);
        Assert.Contains("selectedPaymentMethod.displayName", reviewStep, StringComparison.Ordinal);
        Assert.Contains("selectedPaymentMethod.description", reviewStep, StringComparison.Ordinal);

        Assert.Contains("PAYMENT_METHODS: '/api/storefront/payment-methods'", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("const admittedResponse = admitStorefrontPaymentMethods(response.data);", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("const methods = admittedResponse.methods;", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("selectPaymentMethod: (code) =>", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("const admittedMethod = findAdmittedAvailablePaymentMethod(get().paymentMethods, normalizedCode);", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("selectedPaymentMethodCode: admittedMethod.code", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("paymentMethod: admittedMethod.code", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("paymentMethod: get().paymentSelectionStatus === 'stale'", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("paymentMethod: latestState.paymentMethod", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("expectedPricing: buildExpectedPricing(latestState.pricing)", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("requireStorefrontPaymentSessionAuthority(response.data, latestState.paymentMethod, cart.cartId)", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("paymentMethods.find(m => m.code === selectedPaymentMethodCode)", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("return state.pricing?.paymentFee ?? 0", checkoutStore, StringComparison.Ordinal);

        var partializeStart = checkoutStore.IndexOf("partialize: (state) => ({", StringComparison.Ordinal);
        Assert.True(partializeStart >= 0, "Checkout store must keep an explicit localStorage partialize boundary.");
        var partializeEnd = checkoutStore.IndexOf("}),", partializeStart, StringComparison.Ordinal);
        Assert.True(partializeEnd > partializeStart, "Checkout store partialize boundary must remain inspectable.");
        var localStoragePartialize = checkoutStore[partializeStart..partializeEnd];

        Assert.Contains("paymentMethod: state.paymentMethod", localStoragePartialize, StringComparison.Ordinal);
        Assert.Contains("selectedPaymentMethodCode: state.selectedPaymentMethodCode", localStoragePartialize, StringComparison.Ordinal);
        Assert.DoesNotContain("paymentMethods", localStoragePartialize, StringComparison.Ordinal);
        Assert.DoesNotContain("pricing", localStoragePartialize, StringComparison.Ordinal);
        Assert.DoesNotContain("paymentFee", localStoragePartialize, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("export interface PaymentMethodDto", checkoutTypes, StringComparison.Ordinal);
        Assert.Contains("code: string;", checkoutTypes, StringComparison.Ordinal);
        Assert.Contains("isAvailable: boolean;", checkoutTypes, StringComparison.Ordinal);
        Assert.Contains("paymentMethod: PaymentMethod;", checkoutTypes, StringComparison.Ordinal);
        Assert.Contains("expectedPricing: CheckoutExpectedPricing", checkoutTypes, StringComparison.Ordinal);

        Assert.Contains("const provider = typeof selectedPaymentMethod === 'string'", paymentSessionAuthority, StringComparison.Ordinal);
        Assert.Contains("provider !== 'STRIPE'", paymentSessionAuthority, StringComparison.Ordinal);
        Assert.Contains("provider !== 'PAYMOB'", paymentSessionAuthority, StringComparison.Ordinal);
    }

    [Fact]
    public void StorefrontPaymentMethodDtoAdmission_MustRejectMalformedDuplicateAndUnavailableAuthorityEvidence()
    {
        var checkoutStore = ReadRepoFile("PointOfSale.Storefront/src/store/checkoutStore.ts");

        Assert.Contains("function admitStorefrontPaymentMethods(payload: unknown): { status: CheckoutDependencyStatus; methods: PaymentMethodDto[] }", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("if (!Array.isArray(payload))", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("const seenIds = new Set<string>();", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("const seenCodes = new Set<string>();", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("!UUID_REGEX.test(id)", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("!PAYMENT_METHOD_CODE_REGEX.test(code)", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("!displayName", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("typeof candidate.supportsOnline !== 'boolean'", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("typeof candidate.supportsOffline !== 'boolean'", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("typeof candidate.isAvailable !== 'boolean'", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("(!candidate.supportsOnline && !candidate.supportsOffline)", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("!isFiniteNumber(fee)", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("(feeType !== 'fixed' && feeType !== 'percentage')", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("!Number.isInteger(sortOrder)", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("!isOptionalNonNegativeNumber(candidate.minOrderAmount)", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("candidate.minOrderAmount > candidate.maxOrderAmount", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("seenIds.has(id) || seenCodes.has(code)", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("throw new Error('Payment method response failed admission.')", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("throw new Error('Payment method response contained duplicate identity evidence.')", checkoutStore, StringComparison.Ordinal);
    }

    [Fact]
    public void BackendPaymentMethodAdmission_MustOwnIdentityGatewayTenantCurrencyAndCheckoutCompatibility()
    {
        var storefrontPaymentMethodsController = ReadRepoFile("PointOfSale.API/Controllers/StorefrontPaymentMethodsController.cs");
        var checkoutController = ReadRepoFile("PointOfSale.API/Controllers/CheckoutController.cs");
        var checkoutDtos = ReadRepoFile("PointOfSale.API/DTOs/Checkout/CheckoutDtos.cs");
        var checkoutService = ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var pricingService = ReadRepoFile("PointOfSale.API/Services/Pricing/StorefrontPricingService.cs");
        var validator = ReadRepoFile("PointOfSale.Core/Payments/PaymentMethodConfigurationValidator.cs");

        Assert.Contains("[Route(\"api/storefront/payment-methods\")]", storefrontPaymentMethodsController, StringComparison.Ordinal);
        Assert.Contains("SetNoStoreHeaders();", storefrontPaymentMethodsController, StringComparison.Ordinal);
        Assert.Contains("_tenantContext.TenantId is not Guid tenantId", storefrontPaymentMethodsController, StringComparison.Ordinal);
        Assert.Contains("GetEnabledPaymentMethodsAsync", storefrontPaymentMethodsController, StringComparison.Ordinal);
        Assert.Contains("document.IsConfigurationValid == false", storefrontPaymentMethodsController, StringComparison.Ordinal);
        Assert.Contains("document.IsConfigurationValid == true", storefrontPaymentMethodsController, StringComparison.Ordinal);
        Assert.Contains("IsSupportedStorefrontPaymentMethod(document.Code)", storefrontPaymentMethodsController, StringComparison.Ordinal);
        // T-09: Fawry is never an available storefront payment method.
        Assert.Contains("\"COD\" or \"STRIPE\" or \"PAYMOB\" or \"MANUALWALLET\"", storefrontPaymentMethodsController, StringComparison.Ordinal);
        Assert.DoesNotContain("\"FAWRY\"", storefrontPaymentMethodsController, StringComparison.Ordinal);

        Assert.Contains("[HttpPost(\"pricing\")]", checkoutController, StringComparison.Ordinal);
        Assert.Contains("catch (CheckoutPaymentMethodException ex)", checkoutController, StringComparison.Ordinal);
        Assert.Contains("return BadRequest(new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId))", checkoutController, StringComparison.Ordinal);
        Assert.Contains("public string PaymentMethod { get; set; } = \"card\";", checkoutDtos, StringComparison.Ordinal);
        Assert.Contains("public string? PaymentMethod { get; set; }", checkoutDtos, StringComparison.Ordinal);
        Assert.Contains("public CheckoutExpectedPricingDto ExpectedPricing", checkoutDtos, StringComparison.Ordinal);

        Assert.Contains("ValidatePaymentMethodAsync(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("LoadPaymentMethodAuthorityAsync(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("method.TenantId == tenantId", checkoutService, StringComparison.Ordinal);
        Assert.Contains("!method.IsDeleted", checkoutService, StringComparison.Ordinal);
        Assert.Contains("!paymentMethod.IsEnabled", checkoutService, StringComparison.Ordinal);
        Assert.Contains("PaymentMethodConfigurationValidator.Validate", checkoutService, StringComparison.Ordinal);
        Assert.Contains("throw new CheckoutPaymentMethodException(normalizedCode, \"Payment method is not fully configured\")", checkoutService, StringComparison.Ordinal);
        Assert.Contains("MapAuthoritativePaymentMethodDocument(paymentMethod, configurationValidation)", checkoutService, StringComparison.Ordinal);
        Assert.Contains("lockAuthorityRow: true", checkoutService, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", checkoutService, StringComparison.Ordinal);
        Assert.Contains("WITH (UPDLOCK, ROWLOCK, HOLDLOCK)", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ValidatePaymentMethodOrderAmountLimits(paymentMethod, pricing.FinalTotal", checkoutService, StringComparison.Ordinal);
        Assert.Contains("EnsureExpectedPricingMatches(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("Checkout authority revalidated inside final order transaction", checkoutService, StringComparison.Ordinal);
        Assert.Contains("EnsureCheckoutCurrencyAsync(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ResolveTransactionalCheckoutPaymentProvider(paymentMethod.Code)", checkoutService, StringComparison.Ordinal);
        Assert.Contains("\"STRIPE\" => PaymentProvider.Stripe", checkoutService, StringComparison.Ordinal);
        Assert.Contains("\"PAYMOB\" => PaymentProvider.Paymob", checkoutService, StringComparison.Ordinal);
        Assert.Contains("\"MANUALWALLET\" => PaymentProvider.ManualWallet", checkoutService, StringComparison.Ordinal);
        Assert.Contains("PaymentMethodCode = paymentMethod.Code", checkoutService, StringComparison.Ordinal);
        Assert.Contains("PaymentMethodName = paymentMethod.DisplayName", checkoutService, StringComparison.Ordinal);
        Assert.Contains("PaymentFee = paymentFee", checkoutService, StringComparison.Ordinal);

        Assert.Contains("CalculatePaymentFee(paymentMethod, requestTotals.Subtotal)", pricingService, StringComparison.Ordinal);
        Assert.Contains("\"percentage\" => subtotal * method.Fee / 100m", pricingService, StringComparison.Ordinal);
        Assert.Contains("\"fixed\" => method.Fee", pricingService, StringComparison.Ordinal);

        Assert.Contains("Unsupported payment method", validator, StringComparison.Ordinal);
        Assert.Contains("RequiresConfiguration", validator, StringComparison.Ordinal);
    }

    [Fact]
    public void CacheRecoveryConcurrencyAndCrossSystemPaths_MustReenterBackendAuthority()
    {
        var checkoutStore = ReadRepoFile("PointOfSale.Storefront/src/store/checkoutStore.ts");
        var recoverPage = ReadRepoFile("PointOfSale.Storefront/src/pages/RecoverCheckoutPage.tsx");
        var checkoutSessionController = ReadRepoFile("PointOfSale.API/Controllers/CheckoutSessionController.cs");
        var repository = ReadRepoFile("PointOfSale.Infrastructure/MongoDB/Services/PaymentMethodReadRepository.cs");
        var checkoutService = ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");

        Assert.Contains("checkoutStore.reset();", recoverPage, StringComparison.Ordinal);
        Assert.Contains("const recoveredPaymentMethodCode = session.paymentMethod?.code ?? session.paymentMethodCode ?? null;", recoverPage, StringComparison.Ordinal);
        Assert.Contains("selectedPaymentMethodCode: recoveredPaymentMethodCode", recoverPage, StringComparison.Ordinal);
        Assert.Contains("requiresManualWalletReentry", recoverPage, StringComparison.Ordinal);

        Assert.Contains("PaymentMethodCode = request.PaymentMethodCode", checkoutSessionController, StringComparison.Ordinal);
        Assert.Contains("var recoveredLiveCart = await TryRevalidateRecoveredCartAsync(", checkoutSessionController, StringComparison.Ordinal);
        Assert.Contains("RECOVERY_CART_REVALIDATION_FAILED", checkoutSessionController, StringComparison.Ordinal);
        Assert.Contains("PaymentMethod = !string.IsNullOrWhiteSpace(session.PaymentMethodCode)", checkoutSessionController, StringComparison.Ordinal);
        Assert.Contains("Code = session.PaymentMethodCode.Trim()", checkoutSessionController, StringComparison.Ordinal);
        Assert.Contains("CheckoutToken = null", checkoutSessionController, StringComparison.Ordinal);

        Assert.Contains("Payment method projection miss or stale; falling back to SQL", repository, StringComparison.Ordinal);
        Assert.Contains("IsProjectionCurrent", repository, StringComparison.Ordinal);
        Assert.Contains("Payment method read model unavailable; falling back to SQL", repository, StringComparison.Ordinal);
        Assert.Contains("x.TenantId == tenantId && x.IsEnabled && !x.IsDeleted", repository, StringComparison.Ordinal);
        Assert.Contains("PaymentMethodConfigurationValidator.Validate", repository, StringComparison.Ordinal);
        Assert.Contains("NO SECRETS", repository, StringComparison.Ordinal);

        Assert.Contains("pricingPreviewAbortController", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("pricingPreviewRequestSequence", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("if (state.isSubmitting)", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("await get().refreshPricingPreview();", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("CHECKOUT_IN_PROGRESS", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("CHECKOUT_TOTAL_MISMATCH", checkoutStore, StringComparison.Ordinal);

        Assert.Contains("CheckoutRequestIdempotencyCoordinator", checkoutService, StringComparison.Ordinal);
        Assert.Contains("EnsureExpectedPricingMatches(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("PaymentMethodCode = paymentMethod.Code", checkoutService, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationArtifacts_MustRecordCurrentPaymentMethodAuthorityTruth()
    {
        Assert.Contains(
            "PASS SF-PAY2 Storefront Payment Method Authority Certification",
            ReadRepoFile("docs/architecture/payment-method-authority-certification-findings.md"),
            StringComparison.Ordinal);
        Assert.Contains(
            "Payment method authority: SQL `TenantPaymentMethods` during checkout.",
            ReadRepoFile("docs/architecture/SHOPIXY_PAYMENT_METHODS_FORENSIC_RECOVERY_AUDIT_2026-08-12.md"),
            StringComparison.Ordinal);
        Assert.Contains(
            "does not introduce browser authority over tenant, provider, amount, or payment state",
            ReadRepoFile("docs/architecture/SHOPIXY_PAYMENT_METHODS_P2_IMPLEMENTATION_2026-08-12.md"),
            StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var root = DiscoverSolutionRoot(AppContext.BaseDirectory);
        return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string DiscoverSolutionRoot(string startPath)
    {
        var current = new DirectoryInfo(startPath);
        while (current != null)
        {
            if (current.GetFiles("*.sln").Any() || current.GetDirectories("PointOfSale.API").Any())
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException($"Could not discover solution root from '{startPath}'.");
    }
}
