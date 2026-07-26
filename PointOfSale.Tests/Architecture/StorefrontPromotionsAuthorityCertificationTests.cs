namespace PointOfSale.Tests.Architecture;

public sealed class StorefrontPromotionsAuthorityCertificationTests
{
    [Fact]
    public void Coupons_MustValidateReserveAndConfirmThroughSqlAuthority()
    {
        var validation = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Discounts/CouponValidationService.cs");
        var adapter = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Coupons/CouponUsageService.cs");

        Assert.Contains("ValidateCouponByCodeAsync", validation, StringComparison.Ordinal);
        Assert.Contains("entity.TenantId == tenantId", validation, StringComparison.Ordinal);
        Assert.Contains("RequireAuthenticatedCustomer", validation, StringComparison.Ordinal);
        Assert.Contains("LockCouponForCapacityMutationAsync", validation, StringComparison.Ordinal);
        Assert.Contains("EnsureTransactionalMutation(\"Coupon reservation\")", validation, StringComparison.Ordinal);
        Assert.Contains("EnsureTransactionalMutation(\"Coupon confirmation\")", validation, StringComparison.Ordinal);
        Assert.Contains("GetActiveReservationCountAsync", validation, StringComparison.Ordinal);
        Assert.Contains("GetCustomerConfirmedUsageCountAsync", validation, StringComparison.Ordinal);
        Assert.Contains("CustomerHasCompletedOrdersAsync", validation, StringComparison.Ordinal);
        Assert.Contains("CouponConsumedEvent", validation, StringComparison.Ordinal);
        Assert.Contains("COUPON_EARLY_CONSUMPTION_REMOVED", adapter, StringComparison.Ordinal);
    }

    [Fact]
    public void PromotionEngineAndCheckout_MustRecomputeOneWinningDiscount()
    {
        var engine = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Promotions/PromotionEngine.cs");
        var checkout = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");

        Assert.Contains("DetermineWinner(bestOffer, couponResult)", engine, StringComparison.Ordinal);
        Assert.Contains("return Task.FromResult(false)", engine, StringComparison.Ordinal);
        Assert.Contains("PrecomputeOfferUsageCountsAsync", engine, StringComparison.Ordinal);
        Assert.Contains("HasOfferCapacity", engine, StringComparison.Ordinal);
        Assert.Contains("CalculatePrevalidatedCouponDiscount", engine, StringComparison.Ordinal);
        Assert.Contains("ValidateCouponByIdAsync", checkout, StringComparison.Ordinal);
        Assert.Contains("BuildPromotionAuthoritySnapshot", checkout, StringComparison.Ordinal);
        Assert.Contains("_promotionEngine.CalculateAsync", checkout, StringComparison.Ordinal);
        Assert.Contains("ClearAllPromotionFields", checkout, StringComparison.Ordinal);
        Assert.Contains("EnsureExpectedPricingMatches", checkout, StringComparison.Ordinal);
    }

    [Fact]
    public void PaidPromotionUsage_MustRevalidateCurrentPromotionBeforeUsageMutation()
    {
        var paymentLifecycle = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs");

        Assert.Contains("ConfirmOfferPromotionUsageAsync", paymentLifecycle, StringComparison.Ordinal);
        Assert.Contains("LockPromotionForUsageMutationAsync", paymentLifecycle, StringComparison.Ordinal);
        Assert.Contains("EnsurePromotionUsageAuthorityStillValidAsync", paymentLifecycle, StringComparison.Ordinal);
        Assert.Contains("promotion.Validate()", paymentLifecycle, StringComparison.Ordinal);
        Assert.Contains("promotion.StartDate > nowUtc || promotion.EndDate < nowUtc", paymentLifecycle, StringComparison.Ordinal);
        Assert.Contains("promotion.MaxUsage.HasValue && confirmedUsageCount >= promotion.MaxUsage.Value", paymentLifecycle, StringComparison.Ordinal);
        Assert.Contains("CalculateCurrentPromotionDiscountForOrderAsync", paymentLifecycle, StringComparison.Ordinal);
        Assert.Contains("PromotionAppliedEvent", paymentLifecycle, StringComparison.Ordinal);
        Assert.Contains("IX_PromotionUsages_TenantId_PromotionId_OrderId", paymentLifecycle, StringComparison.Ordinal);
    }

    [Fact]
    public void SmartOffers_MustRemainTokenGatedCouponIntentOnly()
    {
        var storefrontOffers = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/StorefrontSmartOffersController.cs");
        var storefrontCart = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/StorefrontCartController.cs");
        var tokenService = CertificationSource.ReadRepoFile("PointOfSale.API/Security/SmartOfferRedeemTokenService.cs");

        Assert.Contains("You must sign in to redeem smart offers.", storefrontOffers, StringComparison.Ordinal);
        Assert.Contains("ValidateAuthenticatedTenantClaim", storefrontOffers, StringComparison.Ordinal);
        Assert.Contains("x.GeneratedCouponId.HasValue", storefrontOffers, StringComparison.Ordinal);
        Assert.Contains("CreateToken(", storefrontOffers, StringComparison.Ordinal);
        Assert.Contains("SmartOfferRedeemTokens.v1", tokenService, StringComparison.Ordinal);
        Assert.Contains("payload.TenantId != expectedTenantId", tokenService, StringComparison.Ordinal);
        Assert.Contains("payload.CustomerId != expectedCustomerId", tokenService, StringComparison.Ordinal);
        Assert.Contains("SmartOfferTokenRequired", storefrontCart, StringComparison.Ordinal);
        Assert.Contains("ValidateToken(redeemToken, tenantId, customerId)", storefrontCart, StringComparison.Ordinal);
        Assert.Contains("_couponService.ValidateCouponAsync", storefrontCart, StringComparison.Ordinal);
    }

    [Fact]
    public void AbandonedCartRecovery_MustNotRestoreStoredDiscountAuthority()
    {
        var recovery = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/StorefrontCartRecoveryController.cs");

        Assert.Contains("_liveCartService.GetCartAsync", recovery, StringComparison.Ordinal);
        Assert.Contains("stored snapshot is not cart authority", recovery, StringComparison.Ordinal);
        Assert.Contains("live_cart_unavailable", recovery, StringComparison.Ordinal);
        Assert.DoesNotContain("stored_snapshot_sanitized", recovery, StringComparison.Ordinal);
        Assert.DoesNotContain("StripStoredSnapshotDiscountAuthority", recovery, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationArtifacts_MustRecordP2CPromotionsAuthority()
    {
        var certification = CertificationSource.ReadRepoFile("docs/architecture/storefront-promotions-authority-certification-findings.md");

        foreach (var marker in new[]
        {
            "PASS P2-C",
            "## Coupon Findings",
            "## Coupon Usage Findings",
            "## Promotion Findings",
            "## Conflict Resolution Findings",
            "## Smart Offer Findings",
            "## Customer Eligibility Findings",
            "## Scheduling Findings",
            "## Pricing Findings",
            "## Checkout Integration Findings",
            "## Order Integration Findings",
            "## Recovery Findings",
            "## Tenant Isolation Findings",
            "## Concurrency Findings",
            "## Historical Consistency Findings",
            "## Adversarial Findings",
            "## Runtime Hardening Performed",
            "## Required Answers"
        })
        {
            Assert.Contains(marker, certification, StringComparison.Ordinal);
        }

        foreach (var path in new[]
        {
            ".ai-context/PROJECT_CONTEXT.md",
            ".ai-context/ARCHITECTURE_TRUTH.md",
            ".ai-context/KNOWN_RISKS.md",
            ".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md",
            ".ai-context/CODEX_RULES.md"
        })
        {
            Assert.Contains("PASS P2-C", CertificationSource.ReadRepoFile(path), StringComparison.Ordinal);
        }
    }
}
