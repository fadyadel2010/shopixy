namespace PointOfSale.Tests.Architecture;

public sealed class SmartOfferEndToEndAuthorityCertificationTests
{
    [Fact]
    public void CompleteSmartOfferRuntime_MustRemainBackendOwnedFromAdminCandidateToPresentation()
    {
        var adminController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AdminSmartOffersController.cs");
        var storefrontController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/StorefrontSmartOffersController.cs");
        var tokenService = CertificationSource.ReadRepoFile("PointOfSale.API/Security/SmartOfferRedeemTokenService.cs");
        var cartController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/StorefrontCartController.cs");
        var checkoutService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var paymentLifecycle = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs");
        var webApi = CertificationSource.ReadRepoFile("PointOfSale.Web/src/services/adminSmartOffersApi.js");
        var webAdmission = CertificationSource.ReadRepoFile("PointOfSale.Web/src/auth/adminSmartOfferReadAuthority.js");
        var offerTypes = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/types/smartOffer.ts");
        var offerPopup = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/storefront/components/offers/OfferPopup.tsx");
        var couponInput = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/components/cart/CouponInput.tsx");
        var checkoutStore = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/store/checkoutStore.ts");

        Assert.Contains("[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]", adminController, StringComparison.Ordinal);
        Assert.Contains("Where(x => x.TenantId == tenantId)", adminController, StringComparison.Ordinal);
        Assert.Contains("ParseRequiredSmartOfferIfMatch", adminController, StringComparison.Ordinal);
        Assert.Contains("LoadSmartOfferForLifecycleMutationAsync", adminController, StringComparison.Ordinal);
        Assert.Contains("SyncGeneratedCouponAsync(entity, request", adminController, StringComparison.Ordinal);
        Assert.Contains("coupon.ApplicableProductIds = null;", adminController, StringComparison.Ordinal);
        Assert.Contains("MapToAdminDto(entity, currency, coupon)", adminController, StringComparison.Ordinal);

        Assert.Contains("ResolveAuthenticatedCustomerAsync()", storefrontController, StringComparison.Ordinal);
        Assert.Contains("ValidateAuthenticatedTenantClaim(", storefrontController, StringComparison.Ordinal);
        Assert.Contains("x.Status == SmartOfferStatus.Active", storefrontController, StringComparison.Ordinal);
        Assert.Contains("x.GeneratedCouponId.HasValue", storefrontController, StringComparison.Ordinal);
        Assert.Contains("x.IsActive", storefrontController, StringComparison.Ordinal);
        Assert.Contains("_smartOfferRedeemTokenService.CreateToken(", storefrontController, StringComparison.Ordinal);

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

        Assert.Contains("PreviewPricingAsync", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ReconcileCheckoutDiscountAuthorityAsync(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ValidateCouponByIdAsync(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("EnsureExpectedPricingMatches(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("RevalidateCheckoutAuthorityInsideTransactionAsync", checkoutService, StringComparison.Ordinal);
        Assert.Contains("await _couponValidationService.ReserveCouponAsync(", checkoutService, StringComparison.Ordinal);
        Assert.Contains("PricingSnapshotJson = JsonSerializer.Serialize(new", checkoutService, StringComparison.Ordinal);

        Assert.Contains("ConfirmCouponUsageAsync(", paymentLifecycle, StringComparison.Ordinal);
        Assert.Contains("ReleaseCouponReservationAsync(", paymentLifecycle, StringComparison.Ordinal);

        Assert.Contains("'Cache-Control': 'no-store'", webApi, StringComparison.Ordinal);
        Assert.Contains("'If-Match': `\"${rowVersion}\"`", webApi, StringComparison.Ordinal);
        Assert.Contains("requireAdminSmartOfferReadAuthority(response.data, currentUser", webApi, StringComparison.Ordinal);
        Assert.Contains("Smart Offer read authority rejected duplicate list evidence", webAdmission, StringComparison.Ordinal);

        Assert.Contains("export interface SmartOffer", offerTypes, StringComparison.Ordinal);
        Assert.DoesNotContain("discount", offerTypes, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("currency", offerTypes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("redeemSmartOffer(offer.id)", offerPopup, StringComparison.Ordinal);
        Assert.Contains("setPendingSmartOfferRedeemToken(redemption.redeemToken)", offerPopup, StringComparison.Ordinal);
        Assert.Contains("const success = await applyCoupon(null, normalizedRedeemToken);", couponInput, StringComparison.Ordinal);
        Assert.Contains("expectedPricing: buildExpectedPricing(latestState.pricing)", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("latestState.pricingLoading || !latestState.pricing || latestState.pricingError", checkoutStore, StringComparison.Ordinal);
    }

    [Fact]
    public void CompleteSmartOfferRecoveryAuditProjectionAndCaches_MustConvergeToSqlTruth()
    {
        var recoveryPage = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/pages/RecoverCheckoutPage.tsx");
        var checkoutSessionController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/CheckoutSessionController.cs");
        var checkoutSessionService = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/CheckoutSessionService.cs");
        var paymentRecovery = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutPaymentIntentRecoveryService.cs");
        var couponService = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Discounts/CouponValidationService.cs");
        var smartOfferProjection = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Consumers/SmartOfferProjectionConsumer.cs");
        var couponProjection = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Consumers/CouponProjectionConsumer.cs");
        var repair = CertificationSource.ReadRepoFile("PointOfSale.API/Services/ReadModels/DiscountReadModelRepairService.cs");
        var auditController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AuditLogsController.cs");
        var auditPage = CertificationSource.ReadRepoFile("PointOfSale.Web/src/pages/AuditLog.jsx");
        var dbContext = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");
        var financialFreeze = CertificationSource.ReadRepoFile("PointOfSale.Tests/Architecture/StorefrontSmartOfferFinancialAuthorityCertificationTests.cs");
        var recoveryAuditFreeze = CertificationSource.ReadRepoFile("PointOfSale.Tests/Architecture/StorefrontSmartOfferRecoveryAuditAuthorityCertificationTests.cs");

        Assert.Contains("const session = await recoverCheckout(token);", recoveryPage, StringComparison.Ordinal);
        Assert.Contains("useCartStore.getState().restoreRecoveredCart(recoveredCart);", recoveryPage, StringComparison.Ordinal);
        Assert.Contains("session.requiresReview || session.cartChangedDuringRecovery", recoveryPage, StringComparison.Ordinal);

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

        Assert.Contains("EnsureTransactionalMutation(\"Coupon reservation\")", couponService, StringComparison.Ordinal);
        Assert.Contains("EnsureTransactionalMutation(\"Coupon confirmation\")", couponService, StringComparison.Ordinal);
        Assert.Contains("EnsureTransactionalMutation(\"Coupon release\")", couponService, StringComparison.Ordinal);
        Assert.Contains("IsDuplicateUsageOrderException", couponService, StringComparison.Ordinal);

        Assert.Contains("_dbContext.SmartOffers", smartOfferProjection, StringComparison.Ordinal);
        Assert.Contains("MapToDocumentExpression", smartOfferProjection, StringComparison.Ordinal);
        Assert.Contains("GetCollection<StorefrontConfigDocument>(\"storefront_configs\")", smartOfferProjection, StringComparison.Ordinal);
        Assert.Contains("ProjectionEventFence.TryAcquireAsync", couponProjection, StringComparison.Ordinal);
        Assert.Contains("LoadSqlCouponAuthorityAsync", couponProjection, StringComparison.Ordinal);
        Assert.Contains("ProjectCouponUsageFromSqlAuthorityAsync", couponProjection, StringComparison.Ordinal);
        Assert.Contains("BuildFreshnessFilter(@event)", couponProjection, StringComparison.Ordinal);
        Assert.Contains("RebuildCouponsAsync", repair, StringComparison.Ordinal);
        Assert.Contains("SqlTruthRebuildGuard.RequireSqlAuthority", repair, StringComparison.Ordinal);
        Assert.Contains("DeleteManyAsync(staleFilter", repair, StringComparison.Ordinal);

        Assert.Contains("[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]", auditController, StringComparison.Ordinal);
        Assert.Contains("_context.AuditLogs", auditController, StringComparison.Ordinal);
        Assert.Contains(".Where(x => x.TenantId == tenantId)", auditController, StringComparison.Ordinal);
        Assert.Contains("'Cache-Control': 'no-store'", auditPage, StringComparison.Ordinal);
        Assert.Contains("GuardAppendOnlyEvidence<AuditLog>();", dbContext, StringComparison.Ordinal);

        Assert.Contains("StorefrontSmartOfferFrontend_MustPresentCandidatesAndRedeemTokenOnly", financialFreeze, StringComparison.Ordinal);
        Assert.Contains("BackendSmartOfferRecovery_MustReenterSqlCouponCheckoutPaymentAndSessionAuthority", recoveryAuditFreeze, StringComparison.Ordinal);
    }

    [Fact]
    public void CompleteSmartOfferAuthorityDocumentation_MustBeFrozen()
    {
        Assert.Contains(
            "PASS SF-SOF.FINAL Complete Smart Offer End-to-End Authority Certification",
            CertificationSource.ReadRepoFile("docs/architecture/complete-smart-offer-end-to-end-authority-certification-findings.md"),
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
            var source = CertificationSource.ReadRepoFile(path);
            Assert.Contains("PASS SF-SOF.FINAL", source, StringComparison.Ordinal);
            Assert.Contains("Complete Smart Offer", source, StringComparison.OrdinalIgnoreCase);
        }
    }
}
