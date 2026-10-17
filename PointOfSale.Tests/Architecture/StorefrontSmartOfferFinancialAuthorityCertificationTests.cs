namespace PointOfSale.Tests.Architecture;

public sealed class StorefrontSmartOfferFinancialAuthorityCertificationTests
{
    [Fact]
    public void StorefrontSmartOfferFrontend_MustPresentCandidatesAndRedeemTokenOnly()
    {
        var offerTypes = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/types/smartOffer.ts");
        var offerEngine = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/storefront/services/smartOfferEngine.ts");
        var offerProvider = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/storefront/providers/OfferProvider.tsx");
        var offerPopup = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/storefront/components/offers/OfferPopup.tsx");
        var bridge = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/storefront/services/smartOfferCouponBridge.ts");
        var couponInput = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/components/cart/CouponInput.tsx");
        var storefrontApi = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/services/api/storefrontApi.ts");

        Assert.Contains("export interface SmartOffer", offerTypes, StringComparison.Ordinal);
        Assert.Contains("priority: number;", offerTypes, StringComparison.Ordinal);
        Assert.Contains("popupTitle: string;", offerTypes, StringComparison.Ordinal);
        Assert.DoesNotContain("discount", offerTypes, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("coupon", offerTypes, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("currency", offerTypes, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("total", offerTypes, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("fetchSmartOffers()", offerEngine, StringComparison.Ordinal);
        // Display candidates now have scoped, expiring, generation-fenced memory
        // instead of an indefinitely reusable session-storage snapshot. Actual
        // race behavior is exercised by smartOfferEngine.phase3.test.ts.
        Assert.Contains("loadedScope = scope;", offerEngine, StringComparison.Ordinal);
        Assert.Contains("request !== generation || currentScope() !== scope", offerEngine, StringComparison.Ordinal);
        Assert.Contains("Date.now() - loadedAt < MAX_AGE_MS", offerEngine, StringComparison.Ordinal);
        Assert.Contains("getEligibleOffers", offerProvider, StringComparison.Ordinal);
        Assert.Contains("markOfferDisplayed(offer)", offerProvider, StringComparison.Ordinal);
        Assert.Contains("redeemSmartOffer(offer.id)", offerPopup, StringComparison.Ordinal);
        Assert.Contains("setPendingSmartOfferRedeemToken(redemption.redeemToken)", offerPopup, StringComparison.Ordinal);

        Assert.Contains("const STORAGE_KEY = 'smartOffer:pendingRedeemToken';", bridge, StringComparison.Ordinal);
        Assert.Contains("window.dispatchEvent(new CustomEvent(SMART_OFFER_REDEEM_EVENT));", bridge, StringComparison.Ordinal);
        Assert.Contains("const success = await applyCoupon(null, normalizedRedeemToken);", couponInput, StringComparison.Ordinal);
        Assert.Contains("clearPendingSmartOfferRedeemToken();", couponInput, StringComparison.Ordinal);
        Assert.Contains("smartOfferRedeemToken: smartOfferRedeemToken?.trim() || null", storefrontApi, StringComparison.Ordinal);
    }

    [Fact]
    public void BackendSmartOfferRedeem_MustResolveGeneratedCouponThroughCurrentSqlAuthority()
    {
        var smartOffersController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/StorefrontSmartOffersController.cs");
        var cartController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/StorefrontCartController.cs");
        var tokenService = CertificationSource.ReadRepoFile("PointOfSale.API/Security/SmartOfferRedeemTokenService.cs");
        var couponService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Storefront/StorefrontCouponService.cs");
        var requestDto = CertificationSource.ReadRepoFile("PointOfSale.API/DTOs/Storefront/CouponDtos.cs");

        Assert.Contains("ResolveAuthenticatedCustomerAsync()", smartOffersController, StringComparison.Ordinal);
        Assert.Contains("ValidateAuthenticatedTenantClaim(", smartOffersController, StringComparison.Ordinal);
        Assert.Contains("x.Status == SmartOfferStatus.Active", smartOffersController, StringComparison.Ordinal);
        Assert.Contains("x.GeneratedCouponId.HasValue", smartOffersController, StringComparison.Ordinal);
        Assert.Contains("x.IsActive", smartOffersController, StringComparison.Ordinal);
        Assert.Contains("_smartOfferRedeemTokenService.CreateToken(", smartOffersController, StringComparison.Ordinal);

        Assert.Contains("ValidateToken(string token, Guid expectedTenantId, Guid expectedCustomerId)", tokenService, StringComparison.Ordinal);
        Assert.Contains("payload.TenantId != expectedTenantId", tokenService, StringComparison.Ordinal);
        Assert.Contains("payload.CustomerId != expectedCustomerId", tokenService, StringComparison.Ordinal);
        Assert.Contains("payload.ExpiresAtUtc <= DateTimeOffset.UtcNow", tokenService, StringComparison.Ordinal);

        Assert.Contains("ResolveSmartOfferRedemptionAsync(", cartController, StringComparison.Ordinal);
        Assert.Contains("ValidateToken(redeemToken, tenantId, customerId)", cartController, StringComparison.Ordinal);
        Assert.Contains("x.Status == SmartOfferStatus.Active", cartController, StringComparison.Ordinal);
        Assert.Contains("x.GeneratedCouponId == validation.CouponId", cartController, StringComparison.Ordinal);
        Assert.Contains("coupon.Code", cartController, StringComparison.Ordinal);
        Assert.Contains("_couponService.ValidateCouponAsync(", cartController, StringComparison.Ordinal);
        Assert.Contains("cart.ApplyCouponDiscount(couponInfo);", cartController, StringComparison.Ordinal);
        Assert.Contains("await PersistCouponApplicationAsync(", cartController, StringComparison.Ordinal);
        Assert.Contains("SmartOfferRedeemToken", requestDto, StringComparison.Ordinal);
        Assert.Contains("ValidateCouponByCodeAsync", couponService, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckoutSmartOfferFinancialImpact_MustBeRevalidatedBeforeAcceptedPricingAndSqlSnapshot()
    {
        var checkoutStore = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/store/checkoutStore.ts");
        var orderSummary = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/components/checkout/OrderSummary.tsx");
        var reviewStep = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/components/checkout/ReviewStep.tsx");
        var checkoutService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var pricingService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Pricing/StorefrontPricingService.cs");

        Assert.Contains("PRICING: '/api/storefront/checkout/pricing'", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("pricing: admittedPricing", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("expectedPricing: buildExpectedPricing(latestState.pricing)", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("latestState.pricingLoading || !latestState.pricing || latestState.pricingError", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("Checkout displays ONLY server-quoted pricing", orderSummary, StringComparison.Ordinal);
        Assert.Contains("formatMoney(pricing.couponDiscount, currency)", orderSummary, StringComparison.Ordinal);
        Assert.Contains("Checkout displays ONLY backend pricing quotes", reviewStep, StringComparison.Ordinal);
        Assert.Contains("formatMoney(pricing.total, currency)", reviewStep, StringComparison.Ordinal);

        var partializeStart = checkoutStore.IndexOf("partialize: (state) => ({", StringComparison.Ordinal);
        Assert.True(partializeStart >= 0, "Checkout localStorage partialize boundary must remain explicit.");
        var partializeEnd = checkoutStore.IndexOf("}),", partializeStart, StringComparison.Ordinal);
        Assert.True(partializeEnd > partializeStart, "Checkout localStorage partialize boundary must remain inspectable.");
        var localStoragePartialize = checkoutStore[partializeStart..partializeEnd];

        Assert.DoesNotContain("pricing", localStoragePartialize, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("discount", localStoragePartialize, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tax", localStoragePartialize, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("total", localStoragePartialize, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("paymentFee", localStoragePartialize, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("ReconcileCheckoutDiscountAuthorityAsync(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ValidateCouponByIdAsync(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("snapshot.DiscountTotal = couponValidation.CalculatedDiscountAmount ?? 0m;", checkoutService, StringComparison.Ordinal);
        Assert.Contains("snapshot.CouponDiscountTotal = snapshot.DiscountTotal;", checkoutService, StringComparison.Ordinal);
        Assert.Contains("_storefrontPricingService.CalculateCheckoutAsync", checkoutService, StringComparison.Ordinal);
        Assert.Contains("EnsureExpectedPricingMatches(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("AddExpectedPricingMismatchIfNeeded(mismatchedFields, \"coupon_discount\"", checkoutService, StringComparison.Ordinal);
        Assert.Contains("order.DiscountAmount = couponDiscountAmount;", checkoutService, StringComparison.Ordinal);
        Assert.Contains("order.TotalDiscountAmount = totalDiscountAmount;", checkoutService, StringComparison.Ordinal);
        Assert.Contains("order.Total = total;", checkoutService, StringComparison.Ordinal);
        Assert.Contains("order.Currency = validatedOrderCurrency;", checkoutService, StringComparison.Ordinal);
        Assert.Contains("order.PricingSnapshotJson = JsonSerializer.Serialize(new", checkoutService, StringComparison.Ordinal);
        Assert.Contains("CouponDiscount: RoundCurrency(snapshot.CouponDiscountTotal > 0m ? snapshot.CouponDiscountTotal : snapshot.DiscountTotal)", pricingService, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationArtifacts_MustRecordSfSofFinancialTruth()
    {
        Assert.Contains(
            "PASS SF-SOF.FIN Smart Offer Financial Authority Certification",
            CertificationSource.ReadRepoFile("docs/architecture/storefront-smart-offer-financial-authority-certification-findings.md"),
            StringComparison.Ordinal);

    }
}
