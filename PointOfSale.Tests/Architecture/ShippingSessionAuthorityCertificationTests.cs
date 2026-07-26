namespace PointOfSale.Tests.Architecture;

public sealed class ShippingSessionAuthorityCertificationTests
{
    [Fact]
    public void StorefrontShippingSessionFrontend_MustPersistCandidatesOnlyAndAdmitBackendDtos()
    {
        var checkoutTypes = ReadRepoFile("PointOfSale.Storefront/src/types/checkout.ts");
        var checkoutStore = ReadRepoFile("PointOfSale.Storefront/src/store/checkoutStore.ts");
        var shippingStep = ReadRepoFile("PointOfSale.Storefront/src/components/checkout/ShippingStep.tsx");
        var reviewStep = ReadRepoFile("PointOfSale.Storefront/src/components/checkout/ReviewStep.tsx");
        var shippingQuotesHook = ReadRepoFile("PointOfSale.Web/src/modules/storefront/shipping/hooks/useShippingApi.js");

        Assert.Contains("export interface ShippingAddress", checkoutTypes, StringComparison.Ordinal);
        Assert.Contains("export interface ShippingMethod", checkoutTypes, StringComparison.Ordinal);
        Assert.Contains("shippingMethodId: string;", checkoutTypes, StringComparison.Ordinal);
        Assert.Contains("expectedPricing: CheckoutExpectedPricing", checkoutTypes, StringComparison.Ordinal);

        Assert.Contains("setShippingMethod: (id) =>", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("shippingMethodId: id", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("shippingSelectionStatus: 'valid'", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("shippingSelectionStatus !== 'stale'", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("shippingMethodId: state.shippingMethodId", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("return state.pricing?.shipping ?? 0", checkoutStore, StringComparison.Ordinal);

        var partializeStart = checkoutStore.IndexOf("partialize: (state) => ({", StringComparison.Ordinal);
        Assert.True(partializeStart >= 0, "Checkout store must keep an explicit localStorage partialize boundary.");
        var partializeEnd = checkoutStore.IndexOf("}),", partializeStart, StringComparison.Ordinal);
        Assert.True(partializeEnd > partializeStart, "Checkout store partialize boundary must remain inspectable.");
        var persistedState = checkoutStore[partializeStart..partializeEnd];
        Assert.Contains("shippingMethodId: state.shippingMethodId", persistedState, StringComparison.Ordinal);
        Assert.DoesNotContain("shippingMethods", persistedState, StringComparison.Ordinal);
        Assert.DoesNotContain("pricing", persistedState, StringComparison.Ordinal);
        Assert.DoesNotContain("checkoutToken", persistedState, StringComparison.Ordinal);
        Assert.DoesNotContain("orderResult", persistedState, StringComparison.Ordinal);

        Assert.Contains("onSelect={() => setShippingMethod(method.id)}", shippingStep, StringComparison.Ordinal);
        Assert.Contains("price={method.price}", shippingStep, StringComparison.Ordinal);
        Assert.Contains("formatPrice={formatPrice}", shippingStep, StringComparison.Ordinal);
        Assert.Contains("ARCHITECTURAL RULE: Checkout displays ONLY backend pricing quotes.", reviewStep, StringComparison.Ordinal);
        Assert.Contains("pricing.shipping > 0 ? formatMoney(pricing.shipping, currency) : 'Free'", reviewStep, StringComparison.Ordinal);

        Assert.Contains("requireBackendShippingQuoteResponse", shippingQuotesHook, StringComparison.Ordinal);
        Assert.Contains("'Cache-Control': 'no-store'", shippingQuotesHook, StringComparison.Ordinal);
        Assert.Contains("Shipping quote response contained duplicate quote evidence", shippingQuotesHook, StringComparison.Ordinal);
        Assert.Contains("Shipping quote response contained invalid currency evidence", shippingQuotesHook, StringComparison.Ordinal);
        Assert.Contains("Shipping quote response contained contradictory delivery window evidence", shippingQuotesHook, StringComparison.Ordinal);
    }

    [Fact]
    public void BackendShippingSessionAndQuoteApis_MustBeTenantCartCustomerAndNoStoreBound()
    {
        var checkoutController = ReadRepoFile("PointOfSale.API/Controllers/CheckoutController.cs");
        var checkoutSessionController = ReadRepoFile("PointOfSale.API/Controllers/CheckoutSessionController.cs");
        var shippingQuotesController = ReadRepoFile("PointOfSale.API/Controllers/StorefrontShippingQuotesController.cs");
        var checkoutDtos = ReadRepoFile("PointOfSale.API/DTOs/Checkout/CheckoutDtos.cs");
        var checkoutSessionService = ReadRepoFile("PointOfSale.Infrastructure/Services/CheckoutSessionService.cs");
        var checkoutService = ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");

        Assert.Contains("[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]", checkoutController, StringComparison.Ordinal);
        Assert.Contains("[HttpPost(\"pricing\")]", checkoutController, StringComparison.Ordinal);
        Assert.Contains("SetNoStoreHeaders();", checkoutController, StringComparison.Ordinal);

        Assert.Contains("[HttpPost(\"quotes\")]", shippingQuotesController, StringComparison.Ordinal);
        Assert.Contains("[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]", shippingQuotesController, StringComparison.Ordinal);
        Assert.Contains("SetNoStoreHeaders();", shippingQuotesController, StringComparison.Ordinal);
        Assert.Contains("Response.Headers.CacheControl = \"no-store, no-cache, max-age=0\"", shippingQuotesController, StringComparison.Ordinal);

        Assert.Contains("CreateOrResume", checkoutSessionController, StringComparison.Ordinal);
        Assert.Contains("Update(", checkoutSessionController, StringComparison.Ordinal);
        Assert.Contains("LoadAuthoritativeCartAsync(", checkoutSessionController, StringComparison.Ordinal);
        Assert.Contains("authoritativeCart.TenantId != tenantId", checkoutSessionController, StringComparison.Ordinal);
        Assert.Contains("authoritativeCart.CartId != request.CartId", checkoutSessionController, StringComparison.Ordinal);
        Assert.Contains("isOwnerBySessionId", checkoutSessionController, StringComparison.Ordinal);
        Assert.Contains("isOwnerByCustomerId", checkoutSessionController, StringComparison.Ordinal);
        Assert.Contains("RecoverByToken", checkoutSessionController, StringComparison.Ordinal);
        Assert.Contains("ValidateRecoveryToken(token, tenantId)", checkoutSessionController, StringComparison.Ordinal);
        Assert.Contains("RECOVERY_CART_REVALIDATION_FAILED", checkoutSessionController, StringComparison.Ordinal);
        Assert.Contains("CheckoutToken = null", checkoutSessionController, StringComparison.Ordinal);

        Assert.Contains("IX_CheckoutSessions_TenantId_CartId_Active_Unique", checkoutSessionService, StringComparison.Ordinal);
        Assert.Contains("AcquireCartScopedLockAsync", checkoutSessionService, StringComparison.Ordinal);
        Assert.Contains("ResolveSessionAccess(", checkoutSessionService, StringComparison.Ordinal);
        Assert.Contains("TrackDuplicateSessions", checkoutSessionService, StringComparison.Ordinal);
        Assert.Contains("InvalidateSession(", checkoutSessionService, StringComparison.Ordinal);
        Assert.Contains("Status = CheckoutSessionStatus.ConvertedToOrder", checkoutSessionService, StringComparison.Ordinal);

        Assert.Contains("public Guid ShippingMethodId { get; set; }", checkoutDtos, StringComparison.Ordinal);
        Assert.Contains("if (ShippingMethodId == Guid.Empty)", checkoutDtos, StringComparison.Ordinal);
        Assert.Contains("public string? CheckoutToken { get; set; }", checkoutDtos, StringComparison.Ordinal);

        Assert.Contains("ValidateShippingMethodAsync(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("LoadShippingMethodAuthorityAsync(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("method.TenantId == tenantId", checkoutService, StringComparison.Ordinal);
        Assert.Contains("!method.IsDeleted", checkoutService, StringComparison.Ordinal);
        Assert.Contains("lockAuthorityRow: true", checkoutService, StringComparison.Ordinal);
        Assert.Contains("EnsureExpectedPricingMatches(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ShippingMethodId = shippingMethod.ShippingMethodId", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ShippingCost = shippingCost", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ShouldIssueCheckoutToken(Order order)", checkoutService, StringComparison.Ordinal);
        Assert.Contains("CreatePaymentToken(tenantId, orderId)", checkoutService, StringComparison.Ordinal);
    }

    [Fact]
    public void RecoveryReplayCacheAndAdjacentDomains_MustNotBecomeShippingSessionAuthority()
    {
        var checkoutStore = ReadRepoFile("PointOfSale.Storefront/src/store/checkoutStore.ts");
        var recoverPage = ReadRepoFile("PointOfSale.Storefront/src/pages/RecoverCheckoutPage.tsx");
        var checkoutSessionController = ReadRepoFile("PointOfSale.API/Controllers/CheckoutSessionController.cs");
        var checkoutSessionService = ReadRepoFile("PointOfSale.Infrastructure/Services/CheckoutSessionService.cs");
        var checkoutService = ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var shippingMethodService = ReadRepoFile("PointOfSale.API/Services/Storefront/ShippingMethodService.cs");
        var shippingReadRepository = ReadRepoFile("PointOfSale.Infrastructure/MongoDB/Services/ShippingMethodReadRepository.cs");

        Assert.Contains("checkoutStore.reset();", recoverPage, StringComparison.Ordinal);
        Assert.Contains("shippingMethodId: session.shippingMethod?.id ?? session.shippingMethodId ?? null", recoverPage, StringComparison.Ordinal);
        Assert.Contains("session.requiresReview || session.cartChangedDuringRecovery", recoverPage, StringComparison.Ordinal);

        Assert.Contains("cancelCheckoutAutosaveRequest();", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("checkoutAutosaveRequestSequence", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("pricingPreviewRequestSequence", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("clearSensitiveCheckoutSessionStorage();", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("address: INITIAL_ADDRESS", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("checkoutSessionId: null", checkoutStore, StringComparison.Ordinal);

        Assert.Contains("TryRevalidateRecoveredCartAsync(", checkoutSessionController, StringComparison.Ordinal);
        Assert.Contains("CartChangedDuringRecovery = recoveredLiveCart.CartChanged", checkoutSessionController, StringComparison.Ordinal);
        Assert.Contains("RequiresReview = requiresReview", checkoutSessionController, StringComparison.Ordinal);

        Assert.Contains("MarkAsConvertedAsync(", checkoutSessionService, StringComparison.Ordinal);
        Assert.Contains("InvalidatedAtUtc = nowUtc", checkoutSessionService, StringComparison.Ordinal);
        Assert.Contains("Recovery token/session mismatch", checkoutSessionController, StringComparison.Ordinal);

        Assert.Contains("Checkout authority revalidated inside final order transaction", checkoutService, StringComparison.Ordinal);
        Assert.Contains("PaymentIntent", checkoutService, StringComparison.Ordinal);
        Assert.Contains("PromotionCalculationFailed", checkoutService, StringComparison.Ordinal);
        Assert.Contains("AffiliateAttributionAuthority", checkoutService, StringComparison.Ordinal);

        Assert.Contains("TryValidateCachedTenantScope", shippingMethodService, StringComparison.Ordinal);
        Assert.Contains("GetActiveShippingMethodsAsync", shippingReadRepository, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationArtifacts_MustRecordSfShp2Truth()
    {
        Assert.Contains(
            "PASS SF-SHP2 Shipping Session Authority Certification",
            ReadRepoFile("docs/architecture/shipping-session-authority-certification-findings.md"),
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
            Assert.Contains("PASS SF-SHP2", ReadRepoFile(path), StringComparison.Ordinal);
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
