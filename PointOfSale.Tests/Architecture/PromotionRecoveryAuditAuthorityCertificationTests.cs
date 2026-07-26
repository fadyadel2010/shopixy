namespace PointOfSale.Tests.Architecture;

public sealed class PromotionRecoveryAuditAuthorityCertificationTests
{
    [Fact]
    public void StorefrontPromotionRecovery_MustRequestBackendRecoveryAndNeverRestoreStoredDiscountAuthority()
    {
        var recovery = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/StorefrontCartRecoveryController.cs");
        var promotionFreeze = CertificationSource.ReadRepoFile("PointOfSale.Tests/Architecture/StorefrontPromotionsAuthorityCertificationTests.cs");

        Assert.Contains("[Route(\"api/storefront/cart/recover\")]", recovery, StringComparison.Ordinal);
        Assert.Contains("_cartService.ValidateRecoveryToken(token)", recovery, StringComparison.Ordinal);
        Assert.Contains("_cartService.GetByCartIdAsync(tenantId, cartId, ct)", recovery, StringComparison.Ordinal);
        Assert.Contains("OwnershipMatches(cart, validation)", recovery, StringComparison.Ordinal);
        Assert.Contains("_liveCartService.GetCartAsync", recovery, StringComparison.Ordinal);
        Assert.Contains("stored snapshot is not cart authority", recovery, StringComparison.Ordinal);
        Assert.Contains("No backend-admitted live cart snapshot available", recovery, StringComparison.Ordinal);
        Assert.Contains("live_cart_unavailable", recovery, StringComparison.Ordinal);
        Assert.DoesNotContain("stored_snapshot_sanitized", recovery, StringComparison.Ordinal);
        Assert.DoesNotContain("StripStoredSnapshotDiscountAuthority", recovery, StringComparison.Ordinal);

        Assert.Contains("AbandonedCartRecovery_MustNotRestoreStoredDiscountAuthority", promotionFreeze, StringComparison.Ordinal);
    }

    [Fact]
    public void PromotionReservationAndUsage_MustRemainSqlOwnedTransactionalAndReplaySafe()
    {
        var checkoutService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var paymentLifecycle = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs");
        var dbContext = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");
        var usage = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/PromotionUsage.cs");

        Assert.Contains("ReserveAppliedOfferPromotionAsync", checkoutService, StringComparison.Ordinal);
        Assert.Contains("Offer promotion reservation must execute inside the checkout transaction.", checkoutService, StringComparison.Ordinal);
        Assert.Contains("IsolationLevel.Serializable", checkoutService, StringComparison.Ordinal);
        Assert.Contains("LockPromotionForCapacityMutationAsync", checkoutService, StringComparison.Ordinal);
        Assert.Contains("confirmedUsage + activeReservations", checkoutService, StringComparison.Ordinal);

        Assert.Contains("ConfirmOfferPromotionUsageAsync", paymentLifecycle, StringComparison.Ordinal);
        Assert.Contains("LockPromotionForUsageMutationAsync", paymentLifecycle, StringComparison.Ordinal);
        Assert.Contains("EnsurePromotionUsageAuthorityStillValidAsync", paymentLifecycle, StringComparison.Ordinal);
        Assert.Contains("CalculateCurrentPromotionDiscountForOrderAsync", paymentLifecycle, StringComparison.Ordinal);
        Assert.Contains("_db.PromotionUsages.Add(usage);", paymentLifecycle, StringComparison.Ordinal);
        Assert.Contains("IsDuplicatePromotionUsageOrderException", paymentLifecycle, StringComparison.Ordinal);
        Assert.Contains("PromotionAppliedEvent", paymentLifecycle, StringComparison.Ordinal);

        Assert.Contains("entity.HasIndex(pu => new { pu.TenantId, pu.PromotionId, pu.OrderId })", dbContext, StringComparison.Ordinal);
        Assert.Contains(".IsUnique()", dbContext, StringComparison.Ordinal);
        Assert.Contains("IX_PromotionUsages_TenantId_PromotionId_OrderId", paymentLifecycle, StringComparison.Ordinal);

        Assert.Contains("SOURCE OF TRUTH for promotion consumption", usage, StringComparison.Ordinal);
        Assert.Contains("Created ONLY after successful order placement", usage, StringComparison.Ordinal);
        Assert.Contains("PromotionUsage is IMMUTABLE after creation", usage, StringComparison.Ordinal);
    }

    [Fact]
    public void PromotionProjectionRecovery_MustRebuildOnlyFromSqlTruthAndCollapseReplay()
    {
        var projection = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Consumers/PromotionProjectionConsumer.cs");
        var repair = CertificationSource.ReadRepoFile("PointOfSale.API/Services/ReadModels/DiscountReadModelRepairService.cs");
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/PromotionsController.cs");

        Assert.Contains("ProjectionConsumerEventFence.TryAcquireAsync", projection, StringComparison.Ordinal);
        Assert.Contains("Duplicate or active promotion projection replay collapsed before side effects", projection, StringComparison.Ordinal);
        Assert.Contains("LoadLiveSqlPromotionAuthorityAsync", projection, StringComparison.Ordinal);
        Assert.Contains("LoadDeletedSqlPromotionAuthorityAsync", projection, StringComparison.Ordinal);
        Assert.Contains("LoadExpiredSqlPromotionAuthorityAsync", projection, StringComparison.Ordinal);
        Assert.Contains("BuildFreshnessFilter(@event)", projection, StringComparison.Ordinal);
        var usageChangedHandler = ExtractSourceBetween(
            projection,
            "public async Task Consume(ConsumeContext<PromotionUsageChangedEvent> context)",
            "public async Task Consume(ConsumeContext<PromotionAppliedEvent> context)");
        var appliedHandler = ExtractSourceBetween(
            projection,
            "public async Task Consume(ConsumeContext<PromotionAppliedEvent> context)",
            "public async Task Consume(ConsumeContext<PromotionExpiredEvent> context)");

        Assert.Contains("CurrentUsage, sqlPromotion.CurrentUsage", usageChangedHandler, StringComparison.Ordinal);
        Assert.Contains("CurrentUsage, sqlPromotion.CurrentUsage", appliedHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentUsage, @event.CurrentUsage", usageChangedHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentUsage = @event.CurrentUsage", usageChangedHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentUsage, @event.NewTotalUsageCount", appliedHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentUsage = @event.NewTotalUsageCount", appliedHandler, StringComparison.Ordinal);

        Assert.Contains("RebuildPromotionsAsync", repair, StringComparison.Ordinal);
        Assert.Contains("SqlTruthRebuildGuard.RequireSqlAuthority", repair, StringComparison.Ordinal);
        Assert.Contains("_dbContext.Promotions", repair, StringComparison.Ordinal);
        Assert.Contains("BuildPromotionDocument(promotion)", repair, StringComparison.Ordinal);
        Assert.Contains("DeleteManyAsync(staleFilter", repair, StringComparison.Ordinal);
        Assert.Contains("RebuildPromotionDecorationsAsync", repair, StringComparison.Ordinal);
        Assert.Contains("InvalidatePromotionCacheAsync", repair, StringComparison.Ordinal);

        Assert.Contains("RebuildPromotionsAsync", controller, StringComparison.Ordinal);
        Assert.Contains("Promotions rebuilt from SQL authority into read models", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void PromotionAuditReads_MustRemainBackendOwnedTenantScopedNoStoreAndAppendOnly()
    {
        var auditController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AuditLogsController.cs");
        var auditPage = CertificationSource.ReadRepoFile("PointOfSale.Web/src/pages/AuditLog.jsx");
        var auditDto = CertificationSource.ReadRepoFile("PointOfSale.API/DTOs/AuditLogItem.cs");
        var dbContext = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        Assert.Contains("[Route(\"api/audit-logs\")]", auditController, StringComparison.Ordinal);
        Assert.Contains("[Authorize(Roles = \"Owner,Admin\")]", auditController, StringComparison.Ordinal);
        Assert.Contains("[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]", auditController, StringComparison.Ordinal);
        Assert.Contains("_context.AuditLogs", auditController, StringComparison.Ordinal);
        Assert.Contains(".AsNoTracking()", auditController, StringComparison.Ordinal);
        Assert.Contains(".Where(x => x.TenantId == tenantId)", auditController, StringComparison.Ordinal);
        Assert.Contains("OrderByDescending(x => x.TimestampUtc)", auditController, StringComparison.Ordinal);
        Assert.Contains("BeforeDataJson", auditController, StringComparison.Ordinal);
        Assert.Contains("AfterDataJson", auditController, StringComparison.Ordinal);

        Assert.Contains("api.get('/audit-logs'", auditPage, StringComparison.Ordinal);
        Assert.Contains("api.get('/audit-logs/export'", auditPage, StringComparison.Ordinal);
        Assert.Contains("'Cache-Control': 'no-store'", auditPage, StringComparison.Ordinal);
        Assert.Contains("Pragma: 'no-cache'", auditPage, StringComparison.Ordinal);
        Assert.DoesNotContain("api.post('/audit-logs", auditPage, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage", auditPage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sessionStorage", auditPage, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("public Guid? TenantId { get; set; }", auditDto, StringComparison.Ordinal);
        Assert.Contains("GuardAppendOnlyEvidence<AuditLog>();", dbContext, StringComparison.Ordinal);
        Assert.Contains("forensic evidence records are append-only", dbContext, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationArtifacts_MustRecordPromotionRecoveryAuditAuthorityTruth()
    {
        Assert.Contains(
            "PASS SF-PRM.RECOVERY-AUDIT Promotion Recovery and Audit Authority Certification",
            CertificationSource.ReadRepoFile("docs/architecture/promotion-recovery-audit-authority-certification-findings.md"),
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
            Assert.Contains("PASS SF-PRM.RECOVERY-AUDIT", CertificationSource.ReadRepoFile(path), StringComparison.Ordinal);
        }
    }

    private static string ExtractSourceBetween(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Expected source to contain marker {startMarker}.");

        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"Expected source to contain marker {endMarker} after {startMarker}.");

        return source[start..end];
    }
}
