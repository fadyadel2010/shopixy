namespace PointOfSale.Tests.Architecture;

public sealed class StorefrontCheckoutPricingTotalsAuthorityCertificationTests
{
    [Fact]
    public void StorefrontCheckoutPricingFrontend_MustRenderBackendPricingOnly()
    {
        var checkoutTypes = ReadRepoFile("PointOfSale.Storefront/src/types/checkout.ts");
        var checkoutStore = ReadRepoFile("PointOfSale.Storefront/src/store/checkoutStore.ts");
        var checkoutPage = ReadRepoFile("PointOfSale.Storefront/src/pages/CheckoutPage.tsx");
        var orderSummary = ReadRepoFile("PointOfSale.Storefront/src/components/checkout/OrderSummary.tsx");
        var reviewStep = ReadRepoFile("PointOfSale.Storefront/src/components/checkout/ReviewStep.tsx");

        Assert.Contains("export interface CheckoutPricingBreakdown", checkoutTypes, StringComparison.Ordinal);
        Assert.Contains("subtotal: number;", checkoutTypes, StringComparison.Ordinal);
        Assert.Contains("shipping: number;", checkoutTypes, StringComparison.Ordinal);
        Assert.Contains("tax: number;", checkoutTypes, StringComparison.Ordinal);
        Assert.Contains("discount: number;", checkoutTypes, StringComparison.Ordinal);
        Assert.Contains("total: number;", checkoutTypes, StringComparison.Ordinal);
        Assert.Contains("couponDiscount: number;", checkoutTypes, StringComparison.Ordinal);
        Assert.Contains("promotionDiscount: number;", checkoutTypes, StringComparison.Ordinal);
        Assert.Contains("cartPromotionDiscount: number;", checkoutTypes, StringComparison.Ordinal);
        Assert.Contains("paymentFee: number;", checkoutTypes, StringComparison.Ordinal);

        Assert.Contains("PRICING: '/api/storefront/checkout/pricing'", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("const response = await httpClient.post<unknown>", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("admitCheckoutPricingPreview(response.data)", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("pricing: admittedPricing", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("expectedPricing: buildExpectedPricing(latestState.pricing)", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("if (latestState.pricingLoading || !latestState.pricing || latestState.pricingError)", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("await get().refreshPricingPreview();", checkoutStore, StringComparison.Ordinal);

        Assert.Contains("void refreshPricingPreview();", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("cart?.lastUpdatedUtc", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("shippingMethodId", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("selectedPaymentMethodCode", checkoutPage, StringComparison.Ordinal);

        Assert.Contains("Checkout displays ONLY server-quoted pricing", orderSummary, StringComparison.Ordinal);
        Assert.Contains("const pricing = useCheckoutStore(selectPricingPreview);", orderSummary, StringComparison.Ordinal);
        Assert.Contains("formatMoney(pricing.subtotal, currency)", orderSummary, StringComparison.Ordinal);
        Assert.Contains("formatMoney(pricing.couponDiscount, currency)", orderSummary, StringComparison.Ordinal);
        Assert.Contains("formatMoney(pricing.shipping, currency)", orderSummary, StringComparison.Ordinal);
        Assert.Contains("formatMoney(pricing.tax, currency)", orderSummary, StringComparison.Ordinal);
        Assert.Contains("formatMoney(pricing.paymentFee, currency)", orderSummary, StringComparison.Ordinal);
        Assert.Contains("formatMoney(pricing.total, currency)", orderSummary, StringComparison.Ordinal);
        Assert.Contains("t('checkout.summary.unavailable')", orderSummary, StringComparison.Ordinal);

        Assert.Contains("Checkout displays ONLY backend pricing quotes", reviewStep, StringComparison.Ordinal);
        Assert.Contains("const pricing = useCheckoutStore(selectPricingPreview);", reviewStep, StringComparison.Ordinal);
        Assert.Contains("disabled={isSubmitting || !isPhoneVerified || pricingLoading || !pricing || !isReviewValid}", reviewStep, StringComparison.Ordinal);
        Assert.Contains("formatMoney(pricing.total, currency)", reviewStep, StringComparison.Ordinal);
    }

    [Fact]
    public void StorefrontCheckoutPricingRecovery_MustNotPersistPricingAuthority()
    {
        var checkoutStore = ReadRepoFile("PointOfSale.Storefront/src/store/checkoutStore.ts");
        var recoverPage = ReadRepoFile("PointOfSale.Storefront/src/pages/RecoverCheckoutPage.tsx");
        var checkoutTypes = ReadRepoFile("PointOfSale.Storefront/src/types/checkout.ts");
        var checkoutSessionController = ReadRepoFile("PointOfSale.API/Controllers/CheckoutSessionController.cs");

        var partializeStart = checkoutStore.IndexOf("partialize: (state) => ({", StringComparison.Ordinal);
        Assert.True(partializeStart >= 0, "Checkout store must keep an explicit localStorage partialize boundary.");
        var partializeEnd = checkoutStore.IndexOf("}),", partializeStart, StringComparison.Ordinal);
        Assert.True(partializeEnd > partializeStart, "Checkout store partialize boundary must remain inspectable.");
        var localStoragePartialize = checkoutStore[partializeStart..partializeEnd];

        Assert.Contains("shippingMethodId: state.shippingMethodId", localStoragePartialize, StringComparison.Ordinal);
        Assert.Contains("paymentMethod: state.paymentMethod", localStoragePartialize, StringComparison.Ordinal);
        Assert.DoesNotContain("pricing", localStoragePartialize, StringComparison.Ordinal);
        Assert.DoesNotContain("subtotal", localStoragePartialize, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("discount", localStoragePartialize, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tax", localStoragePartialize, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("total", localStoragePartialize, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("paymentFee", localStoragePartialize, StringComparison.OrdinalIgnoreCase);

        var sensitiveSnapshotStart = checkoutStore.IndexOf("interface SensitiveCheckoutStateSnapshot", StringComparison.Ordinal);
        Assert.True(sensitiveSnapshotStart >= 0, "Checkout store must keep an explicit sessionStorage snapshot boundary.");
        var sensitiveSnapshotEnd = checkoutStore.IndexOf("function cloneShippingAddress", sensitiveSnapshotStart, StringComparison.Ordinal);
        Assert.True(sensitiveSnapshotEnd > sensitiveSnapshotStart, "Checkout store sessionStorage snapshot boundary must remain inspectable.");
        var sensitiveSnapshot = checkoutStore[sensitiveSnapshotStart..sensitiveSnapshotEnd];

        Assert.Contains("address: ShippingAddress;", sensitiveSnapshot, StringComparison.Ordinal);
        Assert.DoesNotContain("pricing", sensitiveSnapshot, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("subtotal", sensitiveSnapshot, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("discount", sensitiveSnapshot, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tax", sensitiveSnapshot, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("total", sensitiveSnapshot, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("paymentFee", sensitiveSnapshot, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("checkoutStore.reset();", recoverPage, StringComparison.Ordinal);
        Assert.Contains("cartWasRevalidated", checkoutTypes, StringComparison.Ordinal);
        Assert.Contains("cartChangedDuringRecovery", checkoutTypes, StringComparison.Ordinal);
        Assert.Contains("requiresReview", checkoutTypes, StringComparison.Ordinal);
        Assert.Contains("couponDiscount?: number | null;", checkoutTypes, StringComparison.Ordinal);

        Assert.Contains("var recoveredLiveCart = await TryRevalidateRecoveredCartAsync(", checkoutSessionController, StringComparison.Ordinal);
        Assert.Contains("RECOVERY_CART_REVALIDATION_FAILED", checkoutSessionController, StringComparison.Ordinal);
    }

    [Fact]
    public void BackendCheckoutPricingPreview_MustRecomputeEveryFinancialAmount()
    {
        var checkoutController = ReadRepoFile("PointOfSale.API/Controllers/CheckoutController.cs");
        var checkoutService = ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var pricingService = ReadRepoFile("PointOfSale.API/Services/Pricing/StorefrontPricingService.cs");
        var taxService = ReadRepoFile("PointOfSale.API/Services/Pricing/TaxCalculationService.cs");
        var checkoutDtos = ReadRepoFile("PointOfSale.API/DTOs/Checkout/CheckoutDtos.cs");

        Assert.Contains("[HttpPost(\"pricing\")]", checkoutController, StringComparison.Ordinal);
        Assert.Contains("_checkoutService.PreviewPricingAsync(", checkoutController, StringComparison.Ordinal);
        Assert.Contains("catch (CheckoutShippingMethodException ex)", checkoutController, StringComparison.Ordinal);
        Assert.Contains("catch (CheckoutPaymentMethodException ex)", checkoutController, StringComparison.Ordinal);
        Assert.Contains("catch (CheckoutPromotionCalculationException ex)", checkoutController, StringComparison.Ordinal);

        Assert.Contains("RecalculatePricesReadOnlyAsync(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ReconcileCheckoutDiscountAuthorityAsync(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ValidateShippingMethodAsync(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ValidatePaymentMethodAsync(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("_storefrontPricingService.CalculateCheckoutAsync", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ValidatePaymentMethodOrderAmountLimits(paymentMethod, pricing.FinalTotal", checkoutService, StringComparison.Ordinal);
        Assert.Contains("Subtotal = pricing.Subtotal", checkoutService, StringComparison.Ordinal);
        Assert.Contains("Shipping = pricing.Shipping", checkoutService, StringComparison.Ordinal);
        Assert.Contains("Tax = pricing.Tax", checkoutService, StringComparison.Ordinal);
        Assert.Contains("Discount = pricing.Discount", checkoutService, StringComparison.Ordinal);
        Assert.Contains("Total = pricing.Total", checkoutService, StringComparison.Ordinal);
        Assert.Contains("PaymentFee = pricing.PaymentFee", checkoutService, StringComparison.Ordinal);

        Assert.Contains("public class CheckoutPricingPreviewResponseDto : CheckoutExpectedPricingDto", checkoutDtos, StringComparison.Ordinal);
        Assert.Contains("public decimal TaxRate { get; set; }", checkoutDtos, StringComparison.Ordinal);
        Assert.Contains("public string TaxSource { get; set; }", checkoutDtos, StringComparison.Ordinal);
        Assert.Contains("public bool PricesRefreshed { get; set; }", checkoutDtos, StringComparison.Ordinal);

        Assert.Contains("new PricingRequestTotals(", pricingService, StringComparison.Ordinal);
        Assert.Contains("Subtotal: RoundCurrency(snapshot.Items.Sum(item => item.CurrentPrice * item.Quantity))", pricingService, StringComparison.Ordinal);
        Assert.Contains("requestTotals.TotalWeight", pricingService, StringComparison.Ordinal);
        Assert.Contains("_taxCalculationService.CalculateAsync(", pricingService, StringComparison.Ordinal);
        Assert.Contains("CalculatePaymentFee(paymentMethod, requestTotals.Subtotal)", pricingService, StringComparison.Ordinal);
        Assert.Contains("requestTotals.Subtotal - breakdown.TotalDiscount + shipping + taxComputation.TaxAmount + paymentFee", pricingService, StringComparison.Ordinal);

        Assert.Contains("setting.Key == \"Tax.Enable\" || setting.Key == \"Tax.Rate\"", taxService, StringComparison.Ordinal);
        Assert.Contains("ResolveTenantCountryAsync", taxService, StringComparison.Ordinal);
    }

    [Fact]
    public void BackendCheckoutFinalTotals_MustRejectMismatchesAndSnapshotBackendPricing()
    {
        var checkoutService = ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var checkoutDtos = ReadRepoFile("PointOfSale.API/DTOs/Checkout/CheckoutDtos.cs");

        Assert.Contains("public CheckoutExpectedPricingDto ExpectedPricing { get; set; }", checkoutDtos, StringComparison.Ordinal);
        Assert.Contains("var expectedPricingValidation = ExpectedPricing.Validate();", checkoutDtos, StringComparison.Ordinal);
        Assert.Contains("Expected pricing total is inconsistent.", checkoutDtos, StringComparison.Ordinal);

        Assert.Contains("RevalidateCheckoutAuthorityInsideTransactionAsync(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("lockAuthorityRow: true", checkoutService, StringComparison.Ordinal);
        Assert.Contains("RecalculatePricesInternalAsync(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("lockAuthorityRows: true", checkoutService, StringComparison.Ordinal);
        Assert.Contains("EnsureExpectedPricingMatches(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("CheckoutTotalMismatchException", checkoutService, StringComparison.Ordinal);
        Assert.Contains("AddExpectedPricingMismatchIfNeeded(mismatchedFields, \"subtotal\"", checkoutService, StringComparison.Ordinal);
        Assert.Contains("AddExpectedPricingMismatchIfNeeded(mismatchedFields, \"discount\"", checkoutService, StringComparison.Ordinal);
        Assert.Contains("AddExpectedPricingMismatchIfNeeded(mismatchedFields, \"coupon_discount\"", checkoutService, StringComparison.Ordinal);
        Assert.Contains("AddExpectedPricingMismatchIfNeeded(mismatchedFields, \"promotion_discount\"", checkoutService, StringComparison.Ordinal);
        Assert.Contains("AddExpectedPricingMismatchIfNeeded(mismatchedFields, \"cart_promotion_discount\"", checkoutService, StringComparison.Ordinal);
        Assert.Contains("AddExpectedPricingMismatchIfNeeded(mismatchedFields, \"shipping\"", checkoutService, StringComparison.Ordinal);
        Assert.Contains("AddExpectedPricingMismatchIfNeeded(mismatchedFields, \"tax\"", checkoutService, StringComparison.Ordinal);
        Assert.Contains("AddExpectedPricingMismatchIfNeeded(mismatchedFields, \"payment_fee\"", checkoutService, StringComparison.Ordinal);
        Assert.Contains("AddExpectedPricingMismatchIfNeeded(mismatchedFields, \"total\"", checkoutService, StringComparison.Ordinal);

        Assert.Contains("Order totals recalculated from authoritative pricing service", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ShippingCost = shippingCost", checkoutService, StringComparison.Ordinal);
        Assert.Contains("Subtotal = subtotal", checkoutService, StringComparison.Ordinal);
        Assert.Contains("PromotionDiscountAmount = promotionDiscountAmount", checkoutService, StringComparison.Ordinal);
        Assert.Contains("DiscountAmount = couponDiscountAmount", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ManualDiscountAmount = manualDiscountAmount", checkoutService, StringComparison.Ordinal);
        Assert.Contains("TotalDiscountAmount = totalDiscountAmount", checkoutService, StringComparison.Ordinal);
        Assert.Contains("TaxAmount = taxAmount", checkoutService, StringComparison.Ordinal);
        Assert.Contains("Total = total", checkoutService, StringComparison.Ordinal);
        Assert.Contains("PricingSnapshotJson = JsonSerializer.Serialize(new", checkoutService, StringComparison.Ordinal);
        Assert.Contains("paymentFee,", checkoutService, StringComparison.Ordinal);
        Assert.Contains("PaymentFee = paymentFee", checkoutService, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationArtifacts_MustRecordSfPa55Truth()
    {
        Assert.Contains(
            "PASS SF-PA5.5 Checkout Pricing & Totals Authority Certification",
            ReadRepoFile("docs/architecture/storefront-checkout-pricing-totals-authority-certification-findings.md"),
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
            if (current.GetFiles("*.sln").Any() || File.Exists(Path.Combine(current.FullName, "PointOfSale.API", "PointOfSale.API.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException($"Could not discover solution root from '{startPath}'.");
    }
}
