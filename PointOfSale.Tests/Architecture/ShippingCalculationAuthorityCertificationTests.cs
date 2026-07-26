namespace PointOfSale.Tests.Architecture;

public sealed class ShippingCalculationAuthorityCertificationTests
{
    [Fact]
    public void BackendShippingCalculation_MustOwnFeeCurrencyRulesAndExpectedTotals()
    {
        var calculation = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Pricing/ShippingCalculationService.cs");
        var checkout = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var shippingMethods = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/ShippingMethodsController.cs");

        Assert.Contains("freeShippingThreshold.HasValue && subtotal >= freeShippingThreshold.Value", calculation, StringComparison.Ordinal);
        Assert.Contains("Math.Max(0m, baseRate + (perKgRate * Math.Max(0m, totalWeight)))", calculation, StringComparison.Ordinal);
        Assert.Contains("RoundCurrency(calculatedCost)", calculation, StringComparison.Ordinal);

        Assert.Contains("ValidateShippingMethodAsync(", checkout, StringComparison.Ordinal);
        Assert.Contains("LoadShippingMethodAuthorityAsync(", checkout, StringComparison.Ordinal);
        Assert.Contains("method.TenantId == tenantId", checkout, StringComparison.Ordinal);
        Assert.Contains("!method.IsDeleted", checkout, StringComparison.Ordinal);
        Assert.Contains("EnsureExpectedPricingMatches(", checkout, StringComparison.Ordinal);
        Assert.Contains("AddExpectedPricingMismatchIfNeeded(mismatchedFields, \"shipping\", expectedPricing.Shipping, actualPricing.Shipping)", checkout, StringComparison.Ordinal);
        Assert.Contains("EnsureCheckoutCurrencyAsync(", checkout, StringComparison.Ordinal);
        Assert.Contains("ShippingMethodId = shippingMethod.ShippingMethodId", checkout, StringComparison.Ordinal);
        Assert.Contains("ShippingCost = shippingCost", checkout, StringComparison.Ordinal);
        Assert.Contains("Currency = validatedOrderCurrency", checkout, StringComparison.Ordinal);

        Assert.Contains("var dtoValidationError = dto.Validate();", shippingMethods, StringComparison.Ordinal);
        Assert.Contains("BaseRate < 0m", shippingMethods, StringComparison.Ordinal);
        Assert.Contains("PerKgRate < 0m", shippingMethods, StringComparison.Ordinal);
        Assert.Contains("FreeShippingThreshold.HasValue && FreeShippingThreshold.Value < 0m", shippingMethods, StringComparison.Ordinal);
        Assert.Contains("EstimatedDeliveryDaysMax < EstimatedDeliveryDaysMin", shippingMethods, StringComparison.Ordinal);
        Assert.Contains("TotalWeight < 0m", shippingMethods, StringComparison.Ordinal);
        Assert.Contains("OrderTotal < 0m", shippingMethods, StringComparison.Ordinal);
    }

    [Fact]
    public void StorefrontCheckout_MustSubmitOnlyCandidatesAndTreatPricingAsStaleDetection()
    {
        var checkoutTypes = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/types/checkout.ts");
        var checkoutStore = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/store/checkoutStore.ts");
        var shippingStep = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/components/checkout/ShippingStep.tsx");
        var reviewStep = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/components/checkout/ReviewStep.tsx");

        Assert.Contains("shippingMethodId: string;", checkoutTypes, StringComparison.Ordinal);
        Assert.Contains("expectedPricing: CheckoutExpectedPricing;", checkoutTypes, StringComparison.Ordinal);
        Assert.DoesNotContain("shippingFee:", checkoutTypes, StringComparison.Ordinal);
        Assert.DoesNotContain("shippingCurrency:", checkoutTypes, StringComparison.Ordinal);
        Assert.DoesNotContain("freeShipping:", checkoutTypes, StringComparison.Ordinal);

        Assert.Contains("setShippingMethod: (id) =>", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("shippingMethodId: id", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("refreshPricingPreview: async () =>", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("expectedPricing: buildExpectedPricing(latestState.pricing)", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("'Cache-Control': 'no-store'", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("return state.pricing?.shipping ?? 0", checkoutStore, StringComparison.Ordinal);

        var partializeStart = checkoutStore.IndexOf("partialize: (state) => ({", StringComparison.Ordinal);
        Assert.True(partializeStart >= 0, "Checkout store must keep an explicit persistence boundary.");
        var partializeEnd = checkoutStore.IndexOf("}),", partializeStart, StringComparison.Ordinal);
        Assert.True(partializeEnd > partializeStart, "Checkout store persistence boundary must remain inspectable.");
        var persistedState = checkoutStore[partializeStart..partializeEnd];
        Assert.Contains("shippingMethodId: state.shippingMethodId", persistedState, StringComparison.Ordinal);
        Assert.DoesNotContain("pricing", persistedState, StringComparison.Ordinal);
        Assert.DoesNotContain("shippingMethods", persistedState, StringComparison.Ordinal);
        Assert.DoesNotContain("orderResult", persistedState, StringComparison.Ordinal);

        Assert.Contains("onSelect={() => setShippingMethod(method.id)}", shippingStep, StringComparison.Ordinal);
        Assert.Contains("price={method.price}", shippingStep, StringComparison.Ordinal);
        Assert.Contains("ARCHITECTURAL RULE: Checkout displays ONLY backend pricing quotes.", reviewStep, StringComparison.Ordinal);
        Assert.Contains("pricing.shipping > 0 ? formatMoney(pricing.shipping, currency) : 'Free'", reviewStep, StringComparison.Ordinal);
    }

    [Fact]
    public void QuoteAndReadBoundaries_MustBeNoStoreAdmittedPresentationOnly()
    {
        var quotesController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/StorefrontShippingQuotesController.cs");
        var quoteHook = CertificationSource.ReadRepoFile("PointOfSale.Web/src/modules/storefront/shipping/hooks/useShippingApi.js");
        var shippingRead = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Storefront/ShippingMethodService.cs");

        Assert.Contains("[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]", quotesController, StringComparison.Ordinal);
        Assert.Contains("SetNoStoreHeaders();", quotesController, StringComparison.Ordinal);
        Assert.Contains("request == null", quotesController, StringComparison.Ordinal);
        Assert.Contains("WeightKg.HasValue && WeightKg.Value < 0m", quotesController, StringComparison.Ordinal);
        Assert.Contains("PackageCount.HasValue && PackageCount.Value <= 0", quotesController, StringComparison.Ordinal);
        Assert.Contains("CodAmount.HasValue && CodAmount.Value < 0m", quotesController, StringComparison.Ordinal);
        Assert.Contains("OrderSubtotal.HasValue && OrderSubtotal.Value < 0m", quotesController, StringComparison.Ordinal);

        Assert.Contains("requireBackendShippingQuoteResponse", quoteHook, StringComparison.Ordinal);
        Assert.Contains("'Cache-Control': 'no-store'", quoteHook, StringComparison.Ordinal);
        Assert.Contains("Shipping quote response contained invalid currency evidence", quoteHook, StringComparison.Ordinal);
        Assert.Contains("requireShippingQuoteAmount(quote.cost, 'cost')", quoteHook, StringComparison.Ordinal);
        Assert.Contains("requireShippingQuoteAmount(quote.originalCost, 'originalCost')", quoteHook, StringComparison.Ordinal);
        Assert.Contains("Shipping quote response contained duplicate quote evidence", quoteHook, StringComparison.Ordinal);
        Assert.Contains("Shipping quote response contained contradictory delivery window evidence", quoteHook, StringComparison.Ordinal);

        Assert.Contains("TryValidateCachedTenantScope", shippingRead, StringComparison.Ordinal);
        Assert.Contains("ApplyCalculatedPrices", shippingRead, StringComparison.Ordinal);
        Assert.Contains("TryResolveProjectedCurrency", shippingRead, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationArtifacts_MustRecordSfShp3Truth()
    {
        Assert.Contains(
            "PASS SF-SHP3 Shipping Calculation Authority Certification",
            CertificationSource.ReadRepoFile("docs/architecture/shipping-calculation-authority-certification-findings.md"),
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
            Assert.Contains("PASS SF-SHP3", CertificationSource.ReadRepoFile(path), StringComparison.Ordinal);
        }
    }
}
