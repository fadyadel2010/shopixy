namespace PointOfSale.Tests.Architecture;

public sealed class StorefrontPaymentMethodSelectionAuthorityCertificationTests
{
    [Fact]
    public void StorefrontPaymentMethodFrontend_MustSubmitCandidatesOnly()
    {
        var checkoutTypes = ReadRepoFile("PointOfSale.Storefront/src/types/checkout.ts");
        var checkoutStore = ReadRepoFile("PointOfSale.Storefront/src/store/checkoutStore.ts");
        var paymentStep = ReadRepoFile("PointOfSale.Storefront/src/components/checkout/PaymentStep.tsx");
        var reviewStep = ReadRepoFile("PointOfSale.Storefront/src/components/checkout/ReviewStep.tsx");

        Assert.Contains("export interface PaymentMethodDto", checkoutTypes, StringComparison.Ordinal);
        Assert.Contains("code: string;", checkoutTypes, StringComparison.Ordinal);
        Assert.Contains("fee: number;", checkoutTypes, StringComparison.Ordinal);
        Assert.Contains("feeType: 'fixed' | 'percentage';", checkoutTypes, StringComparison.Ordinal);
        Assert.Contains("isAvailable: boolean;", checkoutTypes, StringComparison.Ordinal);
        Assert.Contains("paymentMethod: PaymentMethod;", checkoutTypes, StringComparison.Ordinal);
        Assert.Contains("expectedPricing: CheckoutExpectedPricing", checkoutTypes, StringComparison.Ordinal);

        Assert.Contains("PAYMENT_METHODS: '/api/storefront/payment-methods'", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("PRICING: '/api/storefront/checkout/pricing'", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("const response = await httpClient.get<PaymentMethodDto[]>", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("availableMethodCodes", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("paymentSelectionStatus: nextPaymentSelectionStatus", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("selectPaymentMethod: (code) =>", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("const admittedMethod = findAdmittedAvailablePaymentMethod(get().paymentMethods, normalizedCode);", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("selectedPaymentMethodCode: admittedMethod.code", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("paymentMethod: admittedMethod.code", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("paymentSelectionStatus === 'stale'", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("paymentMethod: get().paymentSelectionStatus === 'stale'", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("paymentMethod: latestState.paymentMethod", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("expectedPricing: buildExpectedPricing(latestState.pricing)", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("return state.pricing?.paymentFee ?? 0", checkoutStore, StringComparison.Ordinal);

        Assert.Contains("method.isAvailable", paymentStep, StringComparison.Ordinal);
        Assert.Contains("method.fee > 0", paymentStep, StringComparison.Ordinal);
        Assert.Contains("formatPrice(method.fee)", paymentStep, StringComparison.Ordinal);
        Assert.Contains("selectPaymentMethod(method.code)", paymentStep, StringComparison.Ordinal);
        Assert.Contains("orderAmountForValidation = pricing?.total ?? cartSubtotal", paymentStep, StringComparison.Ordinal);

        Assert.Contains("selectSelectedPaymentMethod", reviewStep, StringComparison.Ordinal);
        Assert.Contains("selectedPaymentMethod.displayName", reviewStep, StringComparison.Ordinal);
        Assert.Contains("selectedPaymentMethod.description", reviewStep, StringComparison.Ordinal);
        Assert.Contains("pricing.paymentFee > 0", reviewStep, StringComparison.Ordinal);
        Assert.Contains("formatMoney(pricing.paymentFee, currency)", reviewStep, StringComparison.Ordinal);

        Assert.DoesNotContain("crypto.randomUUID()", paymentStep, StringComparison.Ordinal);
        Assert.DoesNotContain("checkout.payment.stripe.title", reviewStep, StringComparison.Ordinal);
        Assert.DoesNotContain("checkout.payment.paymob.title", reviewStep, StringComparison.Ordinal);
    }

    [Fact]
    public void StorefrontPaymentMethodRecovery_MustRemainCandidateUntilBackendReadAndAdmission()
    {
        var checkoutStore = ReadRepoFile("PointOfSale.Storefront/src/store/checkoutStore.ts");
        var recoverPage = ReadRepoFile("PointOfSale.Storefront/src/pages/RecoverCheckoutPage.tsx");
        var checkoutSessionController = ReadRepoFile("PointOfSale.API/Controllers/CheckoutSessionController.cs");

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

        Assert.Contains("checkoutStore.reset();", recoverPage, StringComparison.Ordinal);
        Assert.Contains("const recoveredPaymentMethodCode = session.paymentMethod?.code ?? session.paymentMethodCode ?? null;", recoverPage, StringComparison.Ordinal);
        Assert.Contains("selectedPaymentMethodCode: recoveredPaymentMethodCode", recoverPage, StringComparison.Ordinal);
        Assert.Contains("requiresManualWalletReentry", recoverPage, StringComparison.Ordinal);

        Assert.Contains("var recoveredLiveCart = await TryRevalidateRecoveredCartAsync(", checkoutSessionController, StringComparison.Ordinal);
        Assert.Contains("RECOVERY_CART_REVALIDATION_FAILED", checkoutSessionController, StringComparison.Ordinal);
        Assert.Contains("PaymentMethod = !string.IsNullOrWhiteSpace(session.PaymentMethodCode)", checkoutSessionController, StringComparison.Ordinal);
        Assert.Contains("Code = session.PaymentMethodCode.Trim()", checkoutSessionController, StringComparison.Ordinal);
    }

    [Fact]
    public void BackendStorefrontPaymentMethodList_MustDeriveFromTenantPaymentAuthority()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/StorefrontPaymentMethodsController.cs");
        var repository = ReadRepoFile("PointOfSale.Infrastructure/MongoDB/Services/PaymentMethodReadRepository.cs");
        var validator = ReadRepoFile("PointOfSale.Core/Payments/PaymentMethodConfigurationValidator.cs");

        Assert.Contains("[Route(\"api/storefront/payment-methods\")]", controller, StringComparison.Ordinal);
        Assert.Contains("_tenantContext.TenantId is not Guid tenantId", controller, StringComparison.Ordinal);
        Assert.Contains("GetEnabledPaymentMethodsAsync", controller, StringComparison.Ordinal);
        Assert.Contains("document.IsConfigurationValid == false", controller, StringComparison.Ordinal);
        Assert.Contains("document.IsConfigurationValid == true", controller, StringComparison.Ordinal);
        Assert.Contains("IsSupportedStorefrontPaymentMethod(document.Code)", controller, StringComparison.Ordinal);
        Assert.Contains("Fee = doc.Fee", controller, StringComparison.Ordinal);
        Assert.Contains("FeeType = doc.FeeType", controller, StringComparison.Ordinal);
        Assert.Contains("IsAvailable = true", controller, StringComparison.Ordinal);

        Assert.Contains("x.TenantId == tenantId && x.IsEnabled && !x.IsDeleted", repository, StringComparison.Ordinal);
        Assert.Contains("OrderBy(x => x.SortOrder)", repository, StringComparison.Ordinal);
        Assert.Contains("PaymentMethodConfigurationValidator.Validate", repository, StringComparison.Ordinal);
        Assert.Contains("RequiresConfiguration", validator, StringComparison.Ordinal);
    }

    [Fact]
    public void BackendCheckoutPaymentAdmission_MustRevalidateSqlFeesLimitsAndSnapshot()
    {
        var checkoutController = ReadRepoFile("PointOfSale.API/Controllers/CheckoutController.cs");
        var checkoutDtos = ReadRepoFile("PointOfSale.API/DTOs/Checkout/CheckoutDtos.cs");
        var checkoutService = ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var pricingService = ReadRepoFile("PointOfSale.API/Services/Pricing/StorefrontPricingService.cs");

        Assert.Contains("[HttpPost(\"pricing\")]", checkoutController, StringComparison.Ordinal);
        Assert.Contains("_checkoutService.PreviewPricingAsync(", checkoutController, StringComparison.Ordinal);
        Assert.Contains("catch (CheckoutPaymentMethodException ex)", checkoutController, StringComparison.Ordinal);
        Assert.Contains("return Conflict(new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId)", checkoutController, StringComparison.Ordinal);

        Assert.Contains("public string PaymentMethod { get; set; } = \"card\";", checkoutDtos, StringComparison.Ordinal);
        Assert.Contains("public string? PaymentMethod { get; set; }", checkoutDtos, StringComparison.Ordinal);
        Assert.Contains("public decimal PaymentFee { get; set; }", checkoutDtos, StringComparison.Ordinal);

        Assert.Contains("ValidatePaymentMethodAsync(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("LoadPaymentMethodAuthorityAsync(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("method.TenantId == tenantId", checkoutService, StringComparison.Ordinal);
        Assert.Contains("!method.IsDeleted", checkoutService, StringComparison.Ordinal);
        Assert.Contains("PaymentMethodConfigurationValidator.Validate", checkoutService, StringComparison.Ordinal);
        Assert.Contains("MapAuthoritativePaymentMethodDocument", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ValidatePaymentMethodOrderAmountLimits(paymentMethod, pricing.FinalTotal", checkoutService, StringComparison.Ordinal);
        Assert.Contains("lockAuthorityRow: true", checkoutService, StringComparison.Ordinal);
        Assert.Contains("EnsureExpectedPricingMatches(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("Checkout authority revalidated inside final order transaction", checkoutService, StringComparison.Ordinal);
        Assert.Contains("PaymentMethodCode = paymentMethod.Code", checkoutService, StringComparison.Ordinal);
        Assert.Contains("PaymentMethodName = paymentMethod.DisplayName", checkoutService, StringComparison.Ordinal);
        Assert.Contains("PaymentFee = paymentFee", checkoutService, StringComparison.Ordinal);

        Assert.Contains("var paymentFee = paymentMethod == null", pricingService, StringComparison.Ordinal);
        Assert.Contains("CalculatePaymentFee(paymentMethod, requestTotals.Subtotal)", pricingService, StringComparison.Ordinal);
        Assert.Contains("\"percentage\" => subtotal * method.Fee / 100m", pricingService, StringComparison.Ordinal);
        Assert.Contains("\"fixed\" => method.Fee", pricingService, StringComparison.Ordinal);
        Assert.Contains("PaymentFee = paymentFee", pricingService, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationArtifacts_MustRecordSfPa54Truth()
    {
        Assert.Contains(
            "PASS SF-PA5.4 Payment Method Selection Authority Certification",
            ReadRepoFile("docs/architecture/storefront-payment-method-selection-authority-certification-findings.md"),
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
