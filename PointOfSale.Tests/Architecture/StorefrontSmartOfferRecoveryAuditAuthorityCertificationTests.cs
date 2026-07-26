namespace PointOfSale.Tests.Architecture;

public sealed class StorefrontSmartOfferRecoveryAuditAuthorityCertificationTests
{
    [Fact]
    public void StorefrontSmartOfferRecoveryPresentation_MustRemainBackendDtoAndTokenIntentOnly()
    {
        var recoverCheckoutPage = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/pages/RecoverCheckoutPage.tsx");
        var offerProvider = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/storefront/providers/OfferProvider.tsx");
        var offerPopup = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/storefront/components/offers/OfferPopup.tsx");
        var bridge = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/storefront/services/smartOfferCouponBridge.ts");
        var couponInput = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/components/cart/CouponInput.tsx");
        var cartStore = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/store/cartStore.ts");
        var checkoutStore = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/store/checkoutStore.ts");
        var checkoutPendingPage = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/pages/CheckoutPendingPage.tsx");
        var orderConfirmationPage = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/pages/OrderConfirmationPage.tsx");

        Assert.Contains("const session = await recoverCheckout(token);", recoverCheckoutPage, StringComparison.Ordinal);
        Assert.Contains("useCartStore.getState().restoreRecoveredCart(recoveredCart);", recoverCheckoutPage, StringComparison.Ordinal);
        Assert.Contains("session.requiresReview || session.cartChangedDuringRecovery", recoverCheckoutPage, StringComparison.Ordinal);
        Assert.Contains("checkoutStore.setError(", recoverCheckoutPage, StringComparison.Ordinal);
        Assert.DoesNotContain("CouponUsage", recoverCheckoutPage, StringComparison.Ordinal);
        Assert.DoesNotContain("CouponReservation", recoverCheckoutPage, StringComparison.Ordinal);

        Assert.Contains("redeemSmartOffer(offer.id)", offerPopup, StringComparison.Ordinal);
        Assert.Contains("setPendingSmartOfferRedeemToken(redemption.redeemToken)", offerPopup, StringComparison.Ordinal);
        Assert.Contains("const STORAGE_KEY = 'smartOffer:pendingRedeemToken';", bridge, StringComparison.Ordinal);
        Assert.Contains("window.dispatchEvent(new CustomEvent(SMART_OFFER_REDEEM_EVENT));", bridge, StringComparison.Ordinal);
        Assert.Contains("const success = await applyCoupon(null, normalizedRedeemToken);", couponInput, StringComparison.Ordinal);
        Assert.Contains("clearPendingSmartOfferRedeemToken();", couponInput, StringComparison.Ordinal);

        Assert.Contains("loadSmartOffers()", offerProvider, StringComparison.Ordinal);
        Assert.Contains("markOfferDisplayed(offer)", offerProvider, StringComparison.Ordinal);
        Assert.Contains("hydrationSource: 'recovery'", cartStore, StringComparison.Ordinal);
        Assert.Contains("const cart = await applyCouponApi(code, smartOfferRedeemToken);", cartStore, StringComparison.Ordinal);
        Assert.Contains("detectCartStateMismatch(cart)", cartStore, StringComparison.Ordinal);
        Assert.Contains("await useCartStore.getState().fetchCart();", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("await get().refreshPricingPreview();", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("expectedPricing: buildExpectedPricing(latestState.pricing)", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("paymentStatusApi.getCanonicalPaymentStatus", checkoutPendingPage, StringComparison.Ordinal);
        Assert.Contains("resolvePendingCheckoutAction", checkoutPendingPage, StringComparison.Ordinal);
        Assert.Contains("paymentStatus?.status !== 'PAID'", orderConfirmationPage, StringComparison.Ordinal);
        Assert.Contains("resolveBackendPaymentScreen", orderConfirmationPage, StringComparison.Ordinal);
    }

    [Fact]
    public void BackendSmartOfferRecovery_MustReenterSqlCouponCheckoutPaymentAndSessionAuthority()
    {
        var smartOffersController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/StorefrontSmartOffersController.cs");
        var tokenService = CertificationSource.ReadRepoFile("PointOfSale.API/Security/SmartOfferRedeemTokenService.cs");
        var cartController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/StorefrontCartController.cs");
        var couponService = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Discounts/CouponValidationService.cs");
        var checkoutService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var checkoutSessionController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/CheckoutSessionController.cs");
        var checkoutSessionService = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/CheckoutSessionService.cs");
        var paymentRecovery = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutPaymentIntentRecoveryService.cs");
        var paymentLifecycle = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs");

        Assert.Contains("RedeemSmartOffer", smartOffersController, StringComparison.Ordinal);
        Assert.Contains("ResolveAuthenticatedCustomerAsync()", smartOffersController, StringComparison.Ordinal);
        Assert.Contains("ValidateAuthenticatedTenantClaim(", smartOffersController, StringComparison.Ordinal);
        Assert.Contains("x.Status == SmartOfferStatus.Active", smartOffersController, StringComparison.Ordinal);
        Assert.Contains("x.GeneratedCouponId.HasValue", smartOffersController, StringComparison.Ordinal);
        Assert.Contains("x.IsActive", smartOffersController, StringComparison.Ordinal);
        Assert.Contains("CreateToken(", smartOffersController, StringComparison.Ordinal);

        Assert.Contains("ValidateToken(string token, Guid expectedTenantId, Guid expectedCustomerId)", tokenService, StringComparison.Ordinal);
        Assert.Contains("payload.TenantId != expectedTenantId", tokenService, StringComparison.Ordinal);
        Assert.Contains("payload.CustomerId != expectedCustomerId", tokenService, StringComparison.Ordinal);
        Assert.Contains("payload.ExpiresAtUtc <= DateTimeOffset.UtcNow", tokenService, StringComparison.Ordinal);

        Assert.Contains("ResolveSmartOfferRedemptionAsync(", cartController, StringComparison.Ordinal);
        Assert.Contains("ValidateToken(redeemToken, tenantId, customerId)", cartController, StringComparison.Ordinal);
        Assert.Contains("x.GeneratedCouponId == validation.CouponId", cartController, StringComparison.Ordinal);
        Assert.Contains("_couponService.ValidateCouponAsync(", cartController, StringComparison.Ordinal);
        Assert.Contains("await PersistCouponApplicationAsync(", cartController, StringComparison.Ordinal);
        Assert.Contains("StorefrontCartCouponStates", cartController, StringComparison.Ordinal);
        Assert.Contains("TrySaveCartCacheAsync", cartController, StringComparison.Ordinal);

        Assert.Contains("EnsureTransactionalMutation(\"Coupon reservation\")", couponService, StringComparison.Ordinal);
        Assert.Contains("EnsureTransactionalMutation(\"Coupon confirmation\")", couponService, StringComparison.Ordinal);
        Assert.Contains("EnsureTransactionalMutation(\"Coupon release\")", couponService, StringComparison.Ordinal);
        Assert.Contains("IsDuplicateUsageOrderException", couponService, StringComparison.Ordinal);
        Assert.Contains("CouponConsumedEvent", couponService, StringComparison.Ordinal);

        Assert.Contains("StorefrontCartCouponStates", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ApplyPersistedCouponState", checkoutService, StringComparison.Ordinal);
        Assert.Contains("await _couponValidationService.ReserveCouponAsync(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("IsolationLevel.Serializable", checkoutService, StringComparison.Ordinal);
        Assert.Contains("EnsureExpectedPricingMatches(", checkoutService, StringComparison.Ordinal);

        Assert.Contains("RecoverByToken", checkoutSessionController, StringComparison.Ordinal);
        Assert.Contains("ValidateRecoveryToken(token, tenantId)", checkoutSessionController, StringComparison.Ordinal);
        Assert.Contains("ResolveRecoverySessionAsync(sessionId, tenantId", checkoutSessionController, StringComparison.Ordinal);
        Assert.Contains("TryRevalidateRecoveredCartAsync", checkoutSessionController, StringComparison.Ordinal);
        Assert.Contains("_cartService.GetCartAsync", checkoutSessionController, StringComparison.Ordinal);
        Assert.Contains("RECOVERY_CART_REVALIDATION_FAILED", checkoutSessionController, StringComparison.Ordinal);
        Assert.Contains("CheckoutSessions", checkoutSessionService, StringComparison.Ordinal);
        Assert.Contains("MarkAsConvertedAsync", checkoutSessionService, StringComparison.Ordinal);

        Assert.Contains("RepairPendingPaymentIntentOrdersAsync", paymentRecovery, StringComparison.Ordinal);
        Assert.Contains("ReleaseCouponReservationAsync(", paymentRecovery, StringComparison.Ordinal);
        Assert.Contains("ConfirmCouponUsageAsync(", paymentLifecycle, StringComparison.Ordinal);
        Assert.Contains("ReleaseCouponReservationAsync(", paymentLifecycle, StringComparison.Ordinal);
    }

    [Fact]
    public void SmartOfferProjectionAuditAndReplay_MustRemainDerivativeSqlOwnedAndNoStore()
    {
        var smartOfferProjection = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Consumers/SmartOfferProjectionConsumer.cs");
        var couponProjection = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Consumers/CouponProjectionConsumer.cs");
        var repair = CertificationSource.ReadRepoFile("PointOfSale.API/Services/ReadModels/DiscountReadModelRepairService.cs");
        var auditController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AuditLogsController.cs");
        var auditPage = CertificationSource.ReadRepoFile("PointOfSale.Web/src/pages/AuditLog.jsx");
        var dbContext = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        Assert.Contains("_dbContext.SmartOffers", smartOfferProjection, StringComparison.Ordinal);
        Assert.Contains("MapToDocumentExpression", smartOfferProjection, StringComparison.Ordinal);
        Assert.Contains("GetCollection<StorefrontConfigDocument>(\"storefront_configs\")", smartOfferProjection, StringComparison.Ordinal);

        Assert.Contains("ProjectionEventFence.TryAcquireAsync", couponProjection, StringComparison.Ordinal);
        Assert.Contains("Coupon projection duplicate or active replay collapsed before MongoDB writes", couponProjection, StringComparison.Ordinal);
        Assert.Contains("LoadSqlCouponAuthorityAsync", couponProjection, StringComparison.Ordinal);
        Assert.Contains("ProjectCouponUsageFromSqlAuthorityAsync", couponProjection, StringComparison.Ordinal);
        Assert.Contains("_dbContext.CouponUsages", couponProjection, StringComparison.Ordinal);
        Assert.Contains("BuildFreshnessFilter(@event)", couponProjection, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentUsage = @event.CurrentUsage", couponProjection, StringComparison.Ordinal);

        Assert.Contains("RebuildCouponsAsync", repair, StringComparison.Ordinal);
        Assert.Contains("RebuildPromotionsAsync", repair, StringComparison.Ordinal);
        Assert.Contains("SqlTruthRebuildGuard.RequireSqlAuthority", repair, StringComparison.Ordinal);
        Assert.Contains("_dbContext.Coupons", repair, StringComparison.Ordinal);
        Assert.Contains("_dbContext.Promotions", repair, StringComparison.Ordinal);
        Assert.Contains("DeleteManyAsync(staleFilter", repair, StringComparison.Ordinal);

        Assert.Contains("[Route(\"api/audit-logs\")]", auditController, StringComparison.Ordinal);
        Assert.Contains("[Authorize(Roles = \"Owner,Admin\")]", auditController, StringComparison.Ordinal);
        Assert.Contains("[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]", auditController, StringComparison.Ordinal);
        Assert.Contains("_context.AuditLogs", auditController, StringComparison.Ordinal);
        Assert.Contains(".Where(x => x.TenantId == tenantId)", auditController, StringComparison.Ordinal);
        Assert.Contains("api.get('/audit-logs'", auditPage, StringComparison.Ordinal);
        Assert.Contains("api.get('/audit-logs/export'", auditPage, StringComparison.Ordinal);
        Assert.Contains("'Cache-Control': 'no-store'", auditPage, StringComparison.Ordinal);
        Assert.Contains("Pragma: 'no-cache'", auditPage, StringComparison.Ordinal);
        Assert.DoesNotContain("api.post('/audit-logs", auditPage, StringComparison.Ordinal);

        Assert.Contains("GuardAppendOnlyEvidence<AuditLog>();", dbContext, StringComparison.Ordinal);
        Assert.Contains("forensic evidence records are append-only", dbContext, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationArtifacts_MustRecordSmartOfferRecoveryAuditAuthorityTruth()
    {
        Assert.Contains(
            "PASS SF-SOF.RECOVERY-AUDIT Smart Offer Recovery and Audit Authority Certification",
            CertificationSource.ReadRepoFile("docs/architecture/storefront-smart-offer-recovery-audit-authority-certification-findings.md"),
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
            Assert.Contains("PASS SF-SOF.RECOVERY-AUDIT", CertificationSource.ReadRepoFile(path), StringComparison.Ordinal);
        }
    }
}
