namespace PointOfSale.Tests.Architecture;

public sealed class PromotionEndToEndAuthorityCertificationTests
{
    [Fact]
    public void CompletePromotionRuntime_MustRemainBackendOwnedFromAdminCandidateToPresentation()
    {
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/PromotionsController.cs");
        var promotionEngine = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Promotions/PromotionEngine.cs");
        var checkoutService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var paymentLifecycle = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs");
        var projection = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Consumers/PromotionProjectionConsumer.cs");
        var repair = CertificationSource.ReadRepoFile("PointOfSale.API/Services/ReadModels/DiscountReadModelRepairService.cs");
        var webApi = CertificationSource.ReadRepoFile("PointOfSale.Web/src/services/adminPromotionsApi.js");
        var webAdmission = CertificationSource.ReadRepoFile("PointOfSale.Web/src/auth/adminPromotionReadAuthority.js");
        var checkoutStore = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/store/checkoutStore.ts");

        Assert.Contains("Where(p => p.TenantId == tenantId && !p.IsDeleted)", controller, StringComparison.Ordinal);
        Assert.Contains("ValidatePromotionRuleAdmissionAsync(tenantId, dto", controller, StringComparison.Ordinal);
        Assert.Contains("ParseRequiredPromotionIfMatch", controller, StringComparison.Ordinal);
        Assert.Contains("Property(p => p.RowVersion).OriginalValue = expectedRowVersion.RowVersion.Value", controller, StringComparison.Ordinal);
        Assert.Contains("MapToDto(promotion, currency)", controller, StringComparison.Ordinal);

        Assert.Contains("LoadActiveOffersAsync", promotionEngine, StringComparison.Ordinal);
        Assert.Contains("PrecomputeOfferUsageCountsAsync", promotionEngine, StringComparison.Ordinal);
        Assert.Contains("HasOfferCapacity", promotionEngine, StringComparison.Ordinal);
        Assert.Contains("DetermineWinner(bestOffer, couponResult)", promotionEngine, StringComparison.Ordinal);

        Assert.Contains("PreviewPricingAsync", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ApplyPromotionResultOrThrow", checkoutService, StringComparison.Ordinal);
        Assert.Contains("EnsureExpectedPricingMatches", checkoutService, StringComparison.Ordinal);
        Assert.Contains("RevalidateCheckoutAuthorityInsideTransactionAsync", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ReserveAppliedOfferPromotionAsync", checkoutService, StringComparison.Ordinal);
        Assert.Contains("IsolationLevel.Serializable", checkoutService, StringComparison.Ordinal);

        Assert.Contains("ConfirmOfferPromotionUsageAsync", paymentLifecycle, StringComparison.Ordinal);
        Assert.Contains("EnsurePromotionUsageAuthorityStillValidAsync", paymentLifecycle, StringComparison.Ordinal);
        Assert.Contains("_db.PromotionUsages.Add(usage);", paymentLifecycle, StringComparison.Ordinal);
        Assert.Contains("IsDuplicatePromotionUsageOrderException", paymentLifecycle, StringComparison.Ordinal);

        Assert.Contains("ProjectionConsumerEventFence.TryAcquireAsync", projection, StringComparison.Ordinal);
        Assert.Contains("LoadLiveSqlPromotionAuthorityAsync", projection, StringComparison.Ordinal);
        Assert.Contains("BuildFreshnessFilter(@event)", projection, StringComparison.Ordinal);
        Assert.Contains("RebuildPromotionsAsync", repair, StringComparison.Ordinal);
        Assert.Contains("SqlTruthRebuildGuard.RequireSqlAuthority", repair, StringComparison.Ordinal);

        Assert.Contains("'Cache-Control': 'no-store'", webApi, StringComparison.Ordinal);
        Assert.Contains("'If-Match': `\"${rowVersion}\"`", webApi, StringComparison.Ordinal);
        Assert.Contains("requireAdminPromotionReadAuthority(response.data, currentUser", webApi, StringComparison.Ordinal);
        Assert.Contains("Promotion read authority rejected duplicate promotion list evidence", webAdmission, StringComparison.Ordinal);

        Assert.Contains("expectedPricing: buildExpectedPricing(latestState.pricing)", checkoutStore, StringComparison.Ordinal);
        Assert.Contains("latestState.pricingLoading || !latestState.pricing || latestState.pricingError", checkoutStore, StringComparison.Ordinal);
    }

    [Fact]
    public void CompletePromotionCachesReplayRecoveryAndProjection_MustNeverBecomeAuthority()
    {
        var recovery = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/StorefrontCartRecoveryController.cs");
        var checkoutRecovery = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/CheckoutSessionController.cs");
        var projection = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Consumers/PromotionProjectionConsumer.cs");
        var repair = CertificationSource.ReadRepoFile("PointOfSale.API/Services/ReadModels/DiscountReadModelRepairService.cs");
        var dbContext = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");
        var usage = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/PromotionUsage.cs");
        var financialFreeze = CertificationSource.ReadRepoFile("PointOfSale.Tests/Architecture/StorefrontPromotionFinancialAuthorityCertificationTests.cs");
        var recoveryAuditFreeze = CertificationSource.ReadRepoFile("PointOfSale.Tests/Architecture/PromotionRecoveryAuditAuthorityCertificationTests.cs");

        Assert.Contains("_liveCartService.GetCartAsync", recovery, StringComparison.Ordinal);
        Assert.Contains("stored snapshot is not cart authority", recovery, StringComparison.Ordinal);
        Assert.Contains("live_cart_unavailable", recovery, StringComparison.Ordinal);
        Assert.Contains("Unable to safely recover checkout because the live cart could not be revalidated", checkoutRecovery, StringComparison.Ordinal);

        Assert.Contains("ProjectionConsumerEventFence.TryAcquireAsync", projection, StringComparison.Ordinal);
        Assert.Contains("CurrentUsage, sqlPromotion.CurrentUsage", projection, StringComparison.Ordinal);
        Assert.Contains("BuildFreshnessFilter(@event)", projection, StringComparison.Ordinal);
        Assert.Contains("DeleteManyAsync(staleFilter", repair, StringComparison.Ordinal);
        Assert.Contains("InvalidatePromotionCacheAsync", repair, StringComparison.Ordinal);

        Assert.Contains("entity.HasIndex(pu => new { pu.TenantId, pu.PromotionId, pu.OrderId })", dbContext, StringComparison.Ordinal);
        Assert.Contains(".IsUnique()", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardAppendOnlyEvidence<AuditLog>();", dbContext, StringComparison.Ordinal);
        Assert.Contains("PromotionUsage is IMMUTABLE after creation", usage, StringComparison.Ordinal);

        Assert.Contains("StorefrontPromotionFinancialFrontend_MustSubmitCandidatesAndRenderBackendPricingOnly", financialFreeze, StringComparison.Ordinal);
        Assert.Contains("PromotionProjectionRecovery_MustRebuildOnlyFromSqlTruthAndCollapseReplay", recoveryAuditFreeze, StringComparison.Ordinal);
    }

    [Fact]
    public void CompletePromotionAuthorityDocumentation_MustBeFrozen()
    {
        Assert.Contains(
            "PASS SF-PRM.FINAL Complete Promotion End-to-End Authority Certification",
            CertificationSource.ReadRepoFile("docs/architecture/complete-promotion-end-to-end-authority-certification-findings.md"),
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
            Assert.Contains("PASS SF-PRM.FINAL", source, StringComparison.Ordinal);
            Assert.Contains("Complete Promotion", source, StringComparison.OrdinalIgnoreCase);
        }
    }
}
