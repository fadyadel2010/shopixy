namespace PointOfSale.Tests.Architecture;

public sealed class StorefrontPromotionFinancialAuthorityCertificationTests
{
    [Fact]
    public void StorefrontPromotionFinancialFrontend_MustSubmitCandidatesAndRenderBackendPricingOnly()
    {
        var checkoutStore = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/store/checkoutStore.ts");
        var checkoutTypes = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/types/checkout.ts");
        var orderSummary = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/components/checkout/OrderSummary.tsx");
        var cartSummary = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/components/cart/CartSummary.tsx");

        Assert.Contains("PRICING: '/api/storefront/checkout/pricing'", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("const response = await httpClient.post<CheckoutPricingBreakdown>", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("pricing: response.data", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("expectedPricing: buildExpectedPricing(latestState.pricing)", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("latestState.pricingLoading || !latestState.pricing || latestState.pricingError", checkoutStore, StringComparison.Ordinal);

        var partializeStart = checkoutStore.IndexOf("partialize: (state) => ({", StringComparison.Ordinal);
        Assert.True(partializeStart >= 0, "Checkout localStorage partialize boundary must remain explicit.");
        var partializeEnd = checkoutStore.IndexOf("}),", partializeStart, StringComparison.Ordinal);
        Assert.True(partializeEnd > partializeStart, "Checkout localStorage partialize boundary must remain inspectable.");
        var localStoragePartialize = checkoutStore[partializeStart..partializeEnd];

        Assert.Contains("shippingMethodId: state.shippingMethodId", localStoragePartialize, StringComparison.Ordinal);
        Assert.Contains("paymentMethod: state.paymentMethod", localStoragePartialize, StringComparison.Ordinal);
        Assert.DoesNotContain("pricing", localStoragePartialize, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("discount", localStoragePartialize, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tax", localStoragePartialize, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("total", localStoragePartialize, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("promotionDiscount: number;", checkoutTypes, StringComparison.Ordinal);
        Assert.Contains("cartPromotionDiscount: number;", checkoutTypes, StringComparison.Ordinal);
        Assert.Contains("promotionId?: string | null;", checkoutTypes, StringComparison.Ordinal);
        Assert.Contains("cartPromotionDiscount?: number | null;", checkoutTypes, StringComparison.Ordinal);

        Assert.Contains("Checkout displays ONLY server-quoted pricing", orderSummary, StringComparison.Ordinal);
        Assert.Contains("const pricing = useCheckoutStore(selectPricingPreview);", orderSummary, StringComparison.Ordinal);
        Assert.Contains("formatMoney(pricing.promotionDiscount, currency)", orderSummary, StringComparison.Ordinal);
        Assert.Contains("formatMoney(pricing.cartPromotionDiscount, currency)", orderSummary, StringComparison.Ordinal);
        Assert.Contains("formatMoney(pricing.shipping, currency)", orderSummary, StringComparison.Ordinal);
        Assert.Contains("formatMoney(pricing.tax, currency)", orderSummary, StringComparison.Ordinal);
        Assert.Contains("formatMoney(pricing.total, currency)", orderSummary, StringComparison.Ordinal);

        Assert.Contains("promotionDiscountTotal", cartSummary, StringComparison.Ordinal);
        Assert.Contains("cartPromotionDiscountTotal", cartSummary, StringComparison.Ordinal);
        Assert.Contains("formatMoney(grandTotal, currency)", cartSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void BackendPromotionFinancialPricing_MustRecomputeWinnerDiscountAndTotals()
    {
        var checkoutService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var pricingService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Pricing/StorefrontPricingService.cs");
        var promotionEngine = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Promotions/PromotionEngine.cs");
        var checkoutDtos = CertificationSource.ReadRepoFile("PointOfSale.API/DTOs/Checkout/CheckoutDtos.cs");

        Assert.Contains("RecalculatePricesReadOnlyAsync(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ReconcileCheckoutDiscountAuthorityAsync(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("_promotionEngine.CalculateAsync", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ApplyPromotionResultOrThrow", checkoutService, StringComparison.Ordinal);
        Assert.Contains("RecordPromotionFailClosed", checkoutService, StringComparison.Ordinal);
        Assert.Contains("throw CreatePromotionCalculationFailureException(mode, correlationId, exception);", checkoutService, StringComparison.Ordinal);
        Assert.Contains("recalculatedSnapshot.PromotionDiscountTotal = promotionResult.DiscountAmount", checkoutService, StringComparison.Ordinal);
        Assert.Contains("recalculatedSnapshot.AppliedPromotionId = promotionResult.PromotionId", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ClearAllPromotionFields(recalculatedSnapshot)", checkoutService, StringComparison.Ordinal);

        Assert.Contains("DetermineWinner(bestOffer, couponResult)", promotionEngine, StringComparison.Ordinal);
        Assert.Contains("return Task.FromResult(false)", promotionEngine, StringComparison.Ordinal);
        Assert.Contains("LoadActiveOffersAsync", promotionEngine, StringComparison.Ordinal);
        Assert.Contains("PrecomputeOfferUsageCountsAsync", promotionEngine, StringComparison.Ordinal);
        Assert.Contains("HasOfferCapacity", promotionEngine, StringComparison.Ordinal);
        Assert.Contains("offer.MinOrderAmount.HasValue && cart.Subtotal < offer.MinOrderAmount.Value", promotionEngine, StringComparison.Ordinal);
        Assert.Contains("CalculateDiscountAmount(offer", promotionEngine, StringComparison.Ordinal);
        Assert.Contains("coupon.DiscountAmount >= offer.DiscountAmount", promotionEngine, StringComparison.Ordinal);

        Assert.Contains("PromotionDiscount: aggregatePromotionDiscount", pricingService, StringComparison.Ordinal);
        Assert.Contains("CartPromotionDiscount = requestTotals.CartPromotionDiscount", pricingService, StringComparison.Ordinal);
        Assert.Contains("_shippingCalculationService.Calculate(shippingMethod, requestTotals.Subtotal)", pricingService, StringComparison.Ordinal);
        Assert.Contains("_taxCalculationService.CalculateAsync(", pricingService, StringComparison.Ordinal);
        Assert.Contains("CalculatePaymentFee(paymentMethod, requestTotals.Subtotal)", pricingService, StringComparison.Ordinal);
        Assert.Contains("requestTotals.Subtotal - breakdown.TotalDiscount + shipping + taxComputation.TaxAmount + paymentFee", pricingService, StringComparison.Ordinal);

        Assert.Contains("Customer-visible checkout pricing must fail closed", checkoutDtos, StringComparison.Ordinal);
        Assert.DoesNotContain("fail-open", checkoutDtos, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BackendPromotionFinancialCheckout_MustRejectReplayAndPersistSqlSnapshotOnlyAfterAuthority()
    {
        var checkoutService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var paymentLifecycle = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs");

        Assert.Contains("RevalidateCheckoutAuthorityInsideTransactionAsync(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("lockAuthorityRow: true", checkoutService, StringComparison.Ordinal);
        Assert.Contains("lockAuthorityRows: true", checkoutService, StringComparison.Ordinal);
        Assert.Contains("EnsureExpectedPricingMatches(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("AddExpectedPricingMismatchIfNeeded(mismatchedFields, \"promotion_discount\"", checkoutService, StringComparison.Ordinal);
        Assert.Contains("AddExpectedPricingMismatchIfNeeded(mismatchedFields, \"cart_promotion_discount\"", checkoutService, StringComparison.Ordinal);
        Assert.Contains("AddExpectedPricingMismatchIfNeeded(mismatchedFields, \"shipping\"", checkoutService, StringComparison.Ordinal);
        Assert.Contains("AddExpectedPricingMismatchIfNeeded(mismatchedFields, \"tax\"", checkoutService, StringComparison.Ordinal);
        Assert.Contains("AddExpectedPricingMismatchIfNeeded(mismatchedFields, \"payment_fee\"", checkoutService, StringComparison.Ordinal);
        Assert.Contains("AddExpectedPricingMismatchIfNeeded(mismatchedFields, \"total\"", checkoutService, StringComparison.Ordinal);

        Assert.Contains("RefreshOrderAuthoritySnapshot(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("order.PromotionDiscountAmount = promotionDiscountAmount", checkoutService, StringComparison.Ordinal);
        Assert.Contains("order.TotalDiscountAmount = totalDiscountAmount", checkoutService, StringComparison.Ordinal);
        Assert.Contains("order.ShippingCost = shippingCost", checkoutService, StringComparison.Ordinal);
        Assert.Contains("order.TaxAmount = taxAmount", checkoutService, StringComparison.Ordinal);
        Assert.Contains("order.Total = total", checkoutService, StringComparison.Ordinal);
        Assert.Contains("order.Currency = validatedOrderCurrency", checkoutService, StringComparison.Ordinal);
        Assert.Contains("order.PricingSnapshotJson = JsonSerializer.Serialize(new", checkoutService, StringComparison.Ordinal);
        Assert.Contains("_dbContext.Orders.Add(order);", checkoutService, StringComparison.Ordinal);
        Assert.Contains("await _dbContext.SaveChangesAsync(cancellationToken);", checkoutService, StringComparison.Ordinal);

        Assert.Contains("ConfirmOfferPromotionUsageAsync", paymentLifecycle, StringComparison.Ordinal);
        Assert.Contains("EnsurePromotionUsageAuthorityStillValidAsync", paymentLifecycle, StringComparison.Ordinal);
        Assert.Contains("CalculateCurrentPromotionDiscountForOrderAsync", paymentLifecycle, StringComparison.Ordinal);
        Assert.Contains("_db.PromotionUsages.Add(usage);", paymentLifecycle, StringComparison.Ordinal);
        Assert.Contains("promotion.CurrentUsage = confirmedUsageCount;", paymentLifecycle, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationArtifacts_MustRecordSfPrmFinancialTruth()
    {
        Assert.Contains(
            "PASS SF-PRM.FIN Promotion Financial Authority Certification",
            CertificationSource.ReadRepoFile("docs/architecture/storefront-promotion-financial-authority-certification-findings.md"),
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
            Assert.Contains("PASS SF-PRM.FIN", CertificationSource.ReadRepoFile(path), StringComparison.Ordinal);
        }
    }
}
