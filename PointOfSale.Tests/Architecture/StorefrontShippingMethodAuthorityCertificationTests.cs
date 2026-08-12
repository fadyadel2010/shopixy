namespace PointOfSale.Tests.Architecture;

public sealed class StorefrontShippingMethodAuthorityCertificationTests
{
    [Fact]
    public void StorefrontShippingMethodFrontend_MustSubmitCandidatesOnly()
    {
        var checkoutTypes = ReadRepoFile("PointOfSale.Storefront/src/types/checkout.ts");
        var checkoutStore = ReadRepoFile("PointOfSale.Storefront/src/store/checkoutStore.ts");
        var shippingStep = ReadRepoFile("PointOfSale.Storefront/src/components/checkout/ShippingStep.tsx");
        var reviewStep = ReadRepoFile("PointOfSale.Storefront/src/components/checkout/ReviewStep.tsx");

        Assert.Contains("export interface ShippingMethod", checkoutTypes, StringComparison.Ordinal);
        Assert.Contains("id: string", checkoutTypes, StringComparison.Ordinal);
        Assert.Contains("price: number", checkoutTypes, StringComparison.Ordinal);
        Assert.Contains("estimatedDays?: number", checkoutTypes, StringComparison.Ordinal);
        Assert.Contains("shippingMethodId: string;", checkoutTypes, StringComparison.Ordinal);
        Assert.Contains("shippingMethodId?: string;", checkoutTypes, StringComparison.Ordinal);
        Assert.Contains("expectedPricing: CheckoutExpectedPricing", checkoutTypes, StringComparison.Ordinal);

        Assert.Contains("SHIPPING_METHODS: '/api/storefront/shipping-methods'", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("PRICING: '/api/storefront/checkout/pricing'", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("setShippingMethod: (id) =>", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("shippingMethodId: id", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("shippingSelectionStatus: 'valid'", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("const response = await httpClient.get<unknown>", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("const expectedTenantId = requireStorefrontShippingTenantScope(candidate.tenantId);", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("tenantId.toLowerCase() !== expectedTenantId.toLowerCase()", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("const methodIds = methods.map(m => m.id)", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("isCurrentSelectionValid", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("shippingSelectionStatus !== 'stale'", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("shippingMethodId: state.shippingMethodId", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("return state.pricing?.shipping ?? 0", checkoutStore, StringComparison.Ordinal);

        Assert.Contains("onSelect={() => setShippingMethod(method.id)}", shippingStep, StringComparison.Ordinal);
        Assert.Contains("price={method.price}", shippingStep, StringComparison.Ordinal);
        Assert.Contains("formatPrice={formatPrice}", shippingStep, StringComparison.Ordinal);
        Assert.Contains("price > 0 ? formatPrice(price) : 'Free'", shippingStep, StringComparison.Ordinal);
        Assert.Contains("Retry shipping options", shippingStep, StringComparison.Ordinal);
        Assert.Contains("ARCHITECTURAL RULE: Checkout displays ONLY backend pricing quotes.", reviewStep, StringComparison.Ordinal);
        Assert.Contains("Shipping, tax, discounts, and total come from the pricing preview endpoint", reviewStep, StringComparison.Ordinal);
        Assert.Contains("shippingMethod.name", reviewStep, StringComparison.Ordinal);
        Assert.Contains("shippingMethod.estimatedDays", reviewStep, StringComparison.Ordinal);
        Assert.Contains("pricing.shipping > 0 ? formatMoney(pricing.shipping, currency) : 'Free'", reviewStep, StringComparison.Ordinal);

        Assert.DoesNotContain("new ShippingMethod", checkoutStore, StringComparison.Ordinal);
        Assert.DoesNotContain("crypto.randomUUID()", shippingStep, StringComparison.Ordinal);
    }

    [Fact]
    public void StorefrontShippingMethodRecovery_MustRemainCandidateUntilBackendReadAndAdmission()
    {
        var checkoutStore = ReadRepoFile("PointOfSale.Storefront/src/store/checkoutStore.ts");
        var recoverPage = ReadRepoFile("PointOfSale.Storefront/src/pages/RecoverCheckoutPage.tsx");
        var checkoutSessionController = ReadRepoFile("PointOfSale.API/Controllers/CheckoutSessionController.cs");

        var partializeStart = checkoutStore.IndexOf("partialize: (state) => ({", StringComparison.Ordinal);
        Assert.True(partializeStart >= 0, "Checkout store must keep an explicit localStorage partialize boundary.");
        var partializeEnd = checkoutStore.IndexOf("}),", partializeStart, StringComparison.Ordinal);
        Assert.True(partializeEnd > partializeStart, "Checkout store partialize boundary must remain inspectable.");
        var localStoragePartialize = checkoutStore[partializeStart..partializeEnd];

        Assert.Contains("shippingMethodId: state.shippingMethodId", localStoragePartialize, StringComparison.Ordinal);
        Assert.DoesNotContain("shippingMethods", localStoragePartialize, StringComparison.Ordinal);
        Assert.DoesNotContain("pricing", localStoragePartialize, StringComparison.Ordinal);
        Assert.DoesNotContain("shippingCost", localStoragePartialize, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("checkoutStore.reset();", recoverPage, StringComparison.Ordinal);
        Assert.Contains("shippingMethodId: session.shippingMethod?.id ?? session.shippingMethodId ?? null", recoverPage, StringComparison.Ordinal);
        Assert.Contains("session.requiresReview || session.cartChangedDuringRecovery", recoverPage, StringComparison.Ordinal);

        Assert.Contains("var recoveredLiveCart = await TryRevalidateRecoveredCartAsync(", checkoutSessionController, StringComparison.Ordinal);
        Assert.Contains("if (recoveredLiveCart.Cart == null)", checkoutSessionController, StringComparison.Ordinal);
        Assert.Contains("RECOVERY_CART_REVALIDATION_FAILED", checkoutSessionController, StringComparison.Ordinal);
        Assert.Contains("ShippingMethod = session.ShippingMethodId.HasValue", checkoutSessionController, StringComparison.Ordinal);
        Assert.Contains("ShippingMethodId = session.ShippingMethodId", checkoutSessionController, StringComparison.Ordinal);
    }

    [Fact]
    public void BackendStorefrontShippingList_MustCalculateMethodsFromBackendCartAndTenantScope()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/StorefrontShippingMethodsController.cs");
        var service = ReadRepoFile("PointOfSale.API/Services/Storefront/ShippingMethodService.cs");
        var dto = ReadRepoFile("PointOfSale.API/DTOs/Storefront/ShippingMethodDto.cs");
        var shippingCalculation = ReadRepoFile("PointOfSale.API/Services/Pricing/ShippingCalculationService.cs");

        Assert.Contains("[Route(\"api/storefront/shipping-methods\")]", controller, StringComparison.Ordinal);
        Assert.Contains("_cartService.GetCartAsync(", controller, StringComparison.Ordinal);
        Assert.Contains("cart.Subtotal", controller, StringComparison.Ordinal);
        Assert.Contains("cart.TotalWeight", controller, StringComparison.Ordinal);
        Assert.Contains("ValidateTenantHeader(traceId)", controller, StringComparison.Ordinal);
        Assert.Contains("ValidateAuthenticatedTenantClaim", controller, StringComparison.Ordinal);

        Assert.Contains("GetShippingMethodsAsync(", service, StringComparison.Ordinal);
        Assert.Contains("TryValidateCachedTenantScope(tenantId, documents", service, StringComparison.Ordinal);
        Assert.Contains("GetActiveShippingMethodsAsync(tenantId", service, StringComparison.Ordinal);
        Assert.Contains("ApplyCalculatedPrices(shippingMethods, cartSubtotal, totalWeight)", service, StringComparison.Ordinal);
        Assert.Contains("method.TenantId == tenantId", service, StringComparison.Ordinal);
        Assert.Contains("shippingMethod.Price = _shippingCalculationService.Calculate(", service, StringComparison.Ordinal);

        Assert.Contains("public Guid TenantId { get; set; }", dto, StringComparison.Ordinal);
        Assert.Contains("public Guid Id { get; set; }", dto, StringComparison.Ordinal);
        Assert.Contains("public decimal Price { get; set; }", dto, StringComparison.Ordinal);
        Assert.Contains("public int? EstimatedDays { get; set; }", dto, StringComparison.Ordinal);
        Assert.Contains("public class ShippingMethodsListResponseDto", dto, StringComparison.Ordinal);

        Assert.Contains("baseRate + (perKgRate * Math.Max(0m, totalWeight))", shippingCalculation, StringComparison.Ordinal);
        Assert.Contains("freeShippingThreshold.HasValue && subtotal >= freeShippingThreshold.Value", shippingCalculation, StringComparison.Ordinal);
    }

    [Fact]
    public void BackendCheckoutShippingAdmission_MustRevalidateSqlCarrierCostAndSnapshot()
    {
        var checkoutController = ReadRepoFile("PointOfSale.API/Controllers/CheckoutController.cs");
        var checkoutDtos = ReadRepoFile("PointOfSale.API/DTOs/Checkout/CheckoutDtos.cs");
        var checkoutService = ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var pricingService = ReadRepoFile("PointOfSale.API/Services/Pricing/StorefrontPricingService.cs");

        Assert.Contains("[HttpPost(\"pricing\")]", checkoutController, StringComparison.Ordinal);
        Assert.Contains("_checkoutService.PreviewPricingAsync(", checkoutController, StringComparison.Ordinal);
        Assert.Contains("catch (CheckoutShippingMethodException ex)", checkoutController, StringComparison.Ordinal);
        Assert.Contains("catch (CheckoutTotalMismatchException ex)", checkoutController, StringComparison.Ordinal);
        Assert.Contains("return Conflict(new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId)", checkoutController, StringComparison.Ordinal);

        Assert.Contains("public Guid ShippingMethodId { get; set; }", checkoutDtos, StringComparison.Ordinal);
        Assert.Contains("if (ShippingMethodId == Guid.Empty)", checkoutDtos, StringComparison.Ordinal);
        Assert.Contains("public Guid? ShippingMethodId { get; set; }", checkoutDtos, StringComparison.Ordinal);
        Assert.Contains("public class CheckoutPricingPreviewResponseDto : CheckoutExpectedPricingDto", checkoutDtos, StringComparison.Ordinal);

        Assert.Contains("ValidateShippingMethodAsync(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("LoadShippingMethodAuthorityAsync(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("method.Id == shippingMethodId", checkoutService, StringComparison.Ordinal);
        Assert.Contains("method.TenantId == tenantId", checkoutService, StringComparison.Ordinal);
        Assert.Contains("!method.IsDeleted", checkoutService, StringComparison.Ordinal);
        Assert.Contains("!shippingMethod.IsActive", checkoutService, StringComparison.Ordinal);
        Assert.Contains("Carrier is not currently available", checkoutService, StringComparison.Ordinal);
        Assert.Contains("lockAuthorityRow: true", checkoutService, StringComparison.Ordinal);
        Assert.Contains("CalculateCheckoutAsync(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("EnsureExpectedPricingMatches(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("Checkout authority revalidated inside final order transaction", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ShippingMethodId = shippingMethod.ShippingMethodId", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ShippingMethodName = shippingMethod.Name", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ShippingCost = shippingCost", checkoutService, StringComparison.Ordinal);
        Assert.Contains("EstimatedDeliveryDays = FormatEstimatedDelivery(shippingMethod)", checkoutService, StringComparison.Ordinal);

        Assert.Contains("var shipping = shippingMethod == null", pricingService, StringComparison.Ordinal);
        Assert.Contains("_shippingCalculationService.Calculate(shippingMethod, requestTotals.Subtotal)", pricingService, StringComparison.Ordinal);
        Assert.Contains("Shipping = shipping", pricingService, StringComparison.Ordinal);
        Assert.Contains("FinalTotal = total", pricingService, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationArtifacts_MustRecordSfPa53Truth()
    {
        Assert.Contains(
            "PASS SF-PA5.3 Shipping Method Authority Certification",
            ReadRepoFile("docs/architecture/storefront-shipping-method-authority-certification-findings.md"),
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
            Assert.Contains("PASS SF-PA5.3", ReadRepoFile(path), StringComparison.Ordinal);
        }
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
