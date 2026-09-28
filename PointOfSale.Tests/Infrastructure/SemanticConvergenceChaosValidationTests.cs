using System.Diagnostics.Metrics;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Security;
using PointOfSale.API.SuperAdmin.Infrastructure;
using PointOfSale.API.SuperAdmin.Models;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Helpers;
using PointOfSale.Core.SuperAdmin.Models;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.Tests.Infrastructure;

public sealed class SemanticConvergenceChaosValidationTests : IDisposable
{
    private readonly MeterListener _listener;
    private readonly List<(string MetricName, long Value, KeyValuePair<string, object?>[] Tags)> _measurements = [];

    public SemanticConvergenceChaosValidationTests()
    {
        _listener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == OperationalConvergenceTelemetry.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            }
        };

        _listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            _measurements.Add((instrument.Name, measurement, tags.ToArray()));
        });
        _listener.Start();
    }

    [Fact]
    public async Task ReplayStormAndDuplicateRebuildChaos_CollapsesBeforeDerivativeSideEffects()
    {
        var tenantId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var fenceKey = new ProjectionEventFenceKey(
            "ChaosReplayStormConsumer",
            "DuplicateOutboxDelivery",
            entityId);

        await using var dbContext = CreateDbContext(tenantId);
        dbContext.ProcessedEvents.Add(ProcessedEvent.Create(
            eventId,
            fenceKey.ConsumerName,
            fenceKey.EventType,
            entityId,
            tenantId,
            "chaos-replay-storm"));
        await dbContext.SaveChangesAsync();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var duplicateFence = await ProjectionEventFence.TryAcquireAsync(
                dbContext,
                eventId,
                fenceKey,
                logger: null,
                CancellationToken.None);

            Assert.Null(duplicateFence);
        }

        var rebuildA = SqlTruthRebuildGuard.RequireSqlAuthority(
            SqlTruthRebuildTarget.ProjectionRepair,
            tenantId,
            entityId,
            SqlAuthorityOverlayKind.Order | SqlAuthorityOverlayKind.Shipping);
        var rebuildB = SqlTruthRebuildGuard.RequireSqlAuthority(
            SqlTruthRebuildTarget.ProjectionRepair,
            tenantId,
            entityId,
            SqlAuthorityOverlayKind.Order | SqlAuthorityOverlayKind.Shipping);

        Assert.Equal(rebuildA, rebuildB);

        var staleFullReplacement = ValidMutation("ChaosReplayStormProductProjection") with
        {
            HasSqlAuthority = false
        };

        Assert.Throws<InvalidOperationException>(() =>
            DerivativeProjectionMutationGuard.RequireReviewedMutation(staleFullReplacement));

        var duplicateReplayMeasurements = Measurements(
            "operational_semantic_replay_decision_total",
            "surface",
            "chaos_replay_storm_consumer");
        Assert.True(
            duplicateReplayMeasurements.Count >= 5,
            "Replay storm attempts should be classified, not silently ignored.");
        Assert.All(duplicateReplayMeasurements, measurement =>
        {
            Assert.Equal("collapsed", Tag(measurement, "decision"));
            Assert.Equal("duplicate_completed_event", Tag(measurement, "reason"));
        });

        var rebuildMeasurements = Measurements(
            "operational_rebuild_diagnostic_total",
            "surface",
            "projection_repair");
        Assert.True(rebuildMeasurements.Count >= 2);
        Assert.All(rebuildMeasurements, measurement =>
        {
            Assert.Equal("authority_required", Tag(measurement, "decision"));
            Assert.Equal("order+shipping", Tag(measurement, "authority_kinds"));
        });

        var rejectedPrimitive = LastMeasurement(
            "operational_dangerous_primitive_governance_total",
            "surface",
            "chaos_replay_storm_product_projection");
        Assert.Equal("rejected", Tag(rejectedPrimitive, "decision"));
        Assert.Equal("missing_sql_authority", Tag(rejectedPrimitive, "reason"));
    }

    [Fact]
    public void RebuildInterruptionProjectionAndCacheChaos_FailsClosedAndKeepsCacheMutationReviewed()
    {
        var tenantId = Guid.NewGuid();

        Assert.Throws<InvalidOperationException>(() =>
            SqlTruthRebuildGuard.RequireSqlAuthority(
                SqlTruthRebuildTarget.Search,
                tenantId,
                Guid.NewGuid(),
                SqlAuthorityOverlayKind.None));

        var acceptedCacheInvalidation = ValidMutation("ChaosNavigationCacheInvalidation") with
        {
            Primitive = DangerousProjectionPrimitive.RedisCacheMutation,
            EntityName = "StorefrontNavigationCache",
            AuthorityKinds = SqlAuthorityOverlayKind.StorefrontNavigation | SqlAuthorityOverlayKind.StorefrontConfig,
            HasSqlAuthority = false
        };
        var missingFreshnessCacheInvalidation = acceptedCacheInvalidation with
        {
            BoundaryName = "ChaosStaleCacheHydration",
            HasFreshnessGuard = false
        };

        var accepted = DerivativeProjectionMutationGuard.RequireReviewedMutation(acceptedCacheInvalidation);
        Assert.Equal(DangerousProjectionPrimitive.RedisCacheMutation, accepted.Primitive);

        Assert.Throws<InvalidOperationException>(() =>
            DerivativeProjectionMutationGuard.RequireReviewedMutation(missingFreshnessCacheInvalidation));

        var rebuildRejected = LastMeasurement(
            "operational_rebuild_diagnostic_total",
            "surface",
            "search");
        Assert.Equal("rejected", Tag(rebuildRejected, "decision"));
        Assert.Equal("missing_sql_authority", Tag(rebuildRejected, "reason"));

        var cacheAccepted = LastMeasurement(
            "operational_dangerous_primitive_governance_total",
            "surface",
            "chaos_navigation_cache_invalidation");
        var cacheRejected = LastMeasurement(
            "operational_dangerous_primitive_governance_total",
            "surface",
            "chaos_stale_cache_hydration");

        Assert.Equal("accepted", Tag(cacheAccepted, "decision"));
        Assert.Equal("storefront_config+storefront_navigation", Tag(cacheAccepted, "authority_kinds"));
        Assert.Equal("rejected", Tag(cacheRejected, "decision"));
        Assert.Equal("stale_replay", Tag(cacheRejected, "reason"));
    }

    [Fact]
    public async Task TenantIsolationChaos_RejectsStaleTenantReplayAndCrossTenantDerivativeKeys()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var inactiveTenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var inactiveTenantProductId = Guid.NewGuid();
        var storeConfigId = Guid.NewGuid();

        await using var dbContext = CreateDbContext(tenantId);
        dbContext.Tenants.AddRange(
            Tenant(tenantId),
            Tenant(otherTenantId),
            Tenant(inactiveTenantId, isActive: false));
        dbContext.Products.AddRange(
            Product(otherTenantId, productId),
            Product(inactiveTenantId, inactiveTenantProductId));
        dbContext.StoreConfigs.Add(new StoreConfig
        {
            Id = storeConfigId,
            TenantId = otherTenantId,
            StoreName = "Cross-tenant stale config"
        });
        await dbContext.SaveChangesAsync();

        var crossTenantReplay = await TenantLineageAuthority.ValidateCurrentTenantOwnerAsync<Product>(
            dbContext,
            tenantId,
            productId,
            CancellationToken.None);
        var inactiveTenantReplay = await TenantLineageAuthority.ValidateCurrentTenantOwnerAsync<Product>(
            dbContext,
            inactiveTenantId,
            inactiveTenantProductId,
            CancellationToken.None);
        var crossTenantStoreConfig = await TenantLineageAuthority.LoadCurrentStoreConfigProjectionAuthorityAsync(
            dbContext,
            tenantId,
            storeConfigId,
            CancellationToken.None);

        Assert.False(crossTenantReplay.IsCurrent);
        Assert.Equal("tenant_mismatch", crossTenantReplay.Reason);
        Assert.False(inactiveTenantReplay.IsCurrent);
        Assert.Equal("tenant_inactive_or_deleted", inactiveTenantReplay.Reason);
        Assert.Null(crossTenantStoreConfig);
        Assert.Equal(
            $"{tenantId:N}_{productId:N}",
            TenantScopedDerivativeKey.ForTenantEntity(tenantId, productId));
        Assert.Throws<ArgumentException>(() =>
            TenantScopedDerivativeKey.ForTenantEntity(Guid.Empty, productId));

        Assert.Contains(_measurements, measurement =>
            measurement.MetricName == "operational_authority_failure_total" &&
            Tag(measurement, "authority") == "tenant_lineage" &&
            Tag(measurement, "decision") == "failed_closed");
    }

    [Fact]
    public async Task ActorAuthorityChaos_RejectsStaleJwtAndDelayedAdminRepairReplay()
    {
        var tenantId = Guid.NewGuid();
        var inactiveTenantId = Guid.NewGuid();
        var downgradedUserId = Guid.NewGuid();
        var disabledUserId = Guid.NewGuid();
        var inactiveTenantUserId = Guid.NewGuid();

        await using var dbContext = CreateDbContext(tenantId);
        dbContext.Tenants.AddRange(
            Tenant(tenantId),
            Tenant(inactiveTenantId, isActive: false));
        dbContext.Users.AddRange(
            User(downgradedUserId, tenantId, UserRole.Manager),
            User(disabledUserId, tenantId, UserRole.Admin, isActive: false),
            User(inactiveTenantUserId, inactiveTenantId, UserRole.Admin));
        await dbContext.SaveChangesAsync();

        var authority = new AdminTenantAuthority(dbContext);
        var requiredAdmin = RoleSets("Owner,Admin");

        var staleRole = await authority.ValidateTenantActorAsync(
            Principal(downgradedUserId, tenantId, "Admin"),
            tenantId,
            requiredAdmin,
            CancellationToken.None);
        var disabledActor = await authority.ValidateTenantActorAsync(
            Principal(disabledUserId, tenantId, "Admin"),
            tenantId,
            requiredAdmin,
            CancellationToken.None);
        var inactiveTenant = await authority.ValidateTenantActorAsync(
            Principal(inactiveTenantUserId, inactiveTenantId, "Admin"),
            inactiveTenantId,
            requiredAdmin,
            CancellationToken.None);

        Assert.False(staleRole.Authorized);
        Assert.Equal("current_actor_role_not_authorized", staleRole.Reason);
        Assert.False(disabledActor.Authorized);
        Assert.Equal("inactive_or_deleted_actor", disabledActor.Reason);
        Assert.False(inactiveTenant.Authorized);
        Assert.Equal("tenant_inactive_or_deleted", inactiveTenant.Reason);

        var actorFailures = Measurements(
            "operational_authority_failure_total",
            "authority",
            "admin_tenant");
        Assert.True(actorFailures.Count >= 3);
        Assert.All(actorFailures, measurement =>
            Assert.Equal("failed_closed", Tag(measurement, "decision")));
    }

    [Fact]
    public async Task PaymentAccountingAndFulfillmentChaos_RecomposesCurrentSqlTruth()
    {
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();

        await using var dbContext = CreateDbContext(tenantId);
        dbContext.Orders.Add(Order(tenantId, orderId, PaymentStatus.Refunded, FulfillmentStatus.Delivered));
        dbContext.Shipments.Add(Shipment(tenantId, orderId, shipmentId, ShipmentStatus.Delivered));
        await dbContext.SaveChangesAsync();

        var paymentVisibility = await OrderPaymentLineageAuthority.LoadCurrentVisibilityAsync(
            dbContext,
            tenantId,
            orderId);
        var shipmentVisibility = await OrderFulfillmentLineageAuthority.LoadCurrentShipmentAsync(
            dbContext,
            tenantId,
            orderId,
            shipmentId);
        var detachedShipmentReplay = await OrderFulfillmentLineageAuthority.LoadCurrentShipmentAsync(
            dbContext,
            tenantId,
            Guid.NewGuid(),
            shipmentId);

        var staleCollection = AccountingLedgerAuthority.EvaluateOrderCollectionPosting(
            Order(tenantId, Guid.NewGuid(), PaymentStatus.Failed, FulfillmentStatus.Created),
            "OnlinePayment");
        var invalidRefundReplay = AccountingLedgerAuthority.EvaluateRefundPosting(
            new RefundAccountingAuthoritySnapshot(
                tenantId,
                orderId,
                "refund-chaos",
                Amount: 0m,
                Currency: "EGP",
                IsFullRefund: false,
                PaymentMethod: "card",
                IsPostSettlementRefund: false,
                SettlementBatchId: null,
                SettlementType: null,
                SettlementId: null,
                PayoutBatchId: null,
                SettledAtUtc: null,
                FinancialPostingStatus: FinancialPostingStatus.Pending,
                FinancialPostingJournalId: null,
                CurrentOrderPaymentStatus: PaymentStatus.Refunded,
                SettlementAuthorityMissing: false,
                RecordedSettlementOrderCount: null,
                CurrentSettlementOrderCount: null,
                SettlementIncludesOrder: null));
        var invalidSettlementReplay = AccountingLedgerAuthority.EvaluateSettlementPosting(
            new SettlementAccountingAuthoritySnapshot(
                tenantId,
                Guid.NewGuid(),
                "gateway",
                "settlement-chaos",
                "stripe",
                GrossAmount: 100m,
                Fees: 150m,
                Currency: "EGP",
                SettledAtUtc: DateTime.UtcNow,
                RecordedOrderCount: 1,
                CurrentOrderCount: 1));

        Assert.NotNull(paymentVisibility);
        Assert.Equal(PaymentStatus.Refunded, paymentVisibility!.PaymentStatus);
        Assert.NotNull(shipmentVisibility);
        Assert.Equal(ShipmentStatus.Delivered, shipmentVisibility!.ShipmentStatus);
        Assert.Null(detachedShipmentReplay);
        Assert.False(staleCollection.ShouldPost);
        Assert.False(staleCollection.ShouldRetry);
        Assert.False(invalidRefundReplay.ShouldPost);
        Assert.True(invalidRefundReplay.ShouldRetry);
        Assert.False(invalidSettlementReplay.ShouldPost);
        Assert.True(invalidSettlementReplay.ShouldRetry);

        Assert.Contains(_measurements, measurement =>
            measurement.MetricName == "operational_semantic_replay_decision_total" &&
            Tag(measurement, "authority") == "accounting_ledger" &&
            Tag(measurement, "reason") == "stale_collection_payment_truth");
        Assert.Contains(_measurements, measurement =>
            measurement.MetricName == "operational_authority_failure_total" &&
            Tag(measurement, "authority") == "order_fulfillment" &&
            Tag(measurement, "reason") == "missing_sql_authority");
    }

    [Fact]
    public void OperationalToolingChaos_MustRemainTenantScopedAuditableAndSqlFirst()
    {
        var searchReindex = ReadApi("Controllers", "SearchReindexController.cs");
        var affiliateReindexController = ReadApi("Controllers", "Admin", "AF16E_ReindexController.cs");
        var affiliateReindexScript = ReadInfrastructure("Scripts", "AF16E_ReindexAffiliateProductsWithStorefrontUrl.cs");
        var discountRepair = ReadApi("Services", "ReadModels", "DiscountReadModelRepairService.cs");
        var marketplaceRebuild = ReadInfrastructure("ReadModels", "Marketplace", "MarketplaceReadModelProjectionServices.cs");
        var navigationProjection = ReadInfrastructure("Consumers", "NavigationProjectionConsumer.cs");
        var productCache = ReadInfrastructure("Consumers", "ProductCacheInvalidationConsumer.cs");

        Assert.Contains("GLOBAL_SEARCH_CONTROL_FORBIDDEN", searchReindex, StringComparison.Ordinal);
        Assert.Contains("_tenantContext.RequiredTenantId", searchReindex, StringComparison.Ordinal);
        Assert.Contains("ReindexTenantAsync", affiliateReindexController, StringComparison.Ordinal);
        Assert.Contains("ReindexTenantAsync(Guid tenantId", affiliateReindexScript, StringComparison.Ordinal);
        Assert.Contains("tenantScopeId", affiliateReindexScript, StringComparison.Ordinal);
        Assert.Contains("TenantScopedDerivativeKey.ForTenantEntity", affiliateReindexScript, StringComparison.Ordinal);
        Assert.Contains("t.IsActive", affiliateReindexScript, StringComparison.Ordinal);
        Assert.Contains("SqlTruthRebuildGuard.RequireSqlAuthority", affiliateReindexScript, StringComparison.Ordinal);
        Assert.Contains("CurrencyNormalizationHelper.NormalizeCurrencyOrThrow", affiliateReindexScript, StringComparison.Ordinal);
        Assert.DoesNotContain("? \"USD\"", affiliateReindexScript, StringComparison.Ordinal);

        Assert.Contains("SqlTruthRebuildGuard.RequireSqlAuthority", discountRepair, StringComparison.Ordinal);
        Assert.Contains("SqlTruthRebuildGuard.RequireSqlAuthority", marketplaceRebuild, StringComparison.Ordinal);
        Assert.Contains("ProjectionEventFence.TryAcquireAsync", navigationProjection, StringComparison.Ordinal);
        Assert.Contains("DerivativeProjectionMutationGuard.RequireReviewedMutation", navigationProjection, StringComparison.Ordinal);
        Assert.Contains("ProjectionConsumerEventFence", productCache, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmergencyAuthorityChaos_RejectsStaleRevokedAndGovernanceRestrictedSuperAdminSessions()
    {
        var tenantId = Guid.NewGuid();

        await using var dbContext = CreateDbContext(tenantId);
        var staleSessionAdmin = CreateSuperAdmin("stale-root@commercehub.test");
        staleSessionAdmin.SetRefreshToken("refresh-hash", DateTime.UtcNow.AddDays(1));
        var staleTokenVersion = staleSessionAdmin.TokenVersion;
        staleSessionAdmin.ClearRefreshToken();

        var governanceRejectedAdmin = CreateSuperAdmin("restricted-root@commercehub.test");
        governanceRejectedAdmin.SetRefreshToken("refresh-hash", DateTime.UtcNow.AddDays(1));

        dbContext.SuperAdmins.AddRange(staleSessionAdmin, governanceRejectedAdmin);
        dbContext.UserGovernanceProfiles.Add(new UserGovernanceProfile
        {
            Id = Guid.NewGuid(),
            SubjectId = governanceRejectedAdmin.Id,
            Role = UserGovernanceRole.Admin,
            Status = UserGovernanceStatus.Suspended,
            CreatedAtUtc = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var authority = new SuperAdminPlatformAuthority(dbContext);

        var staleSession = await authority.ValidatePlatformActorAsync(
            SuperAdminPrincipal(staleSessionAdmin.Id, staleTokenVersion));
        var revokedAuthority = await authority.ValidatePlatformActorAsync(
            SuperAdminPrincipal(Guid.NewGuid(), 1));
        var governanceRejected = await authority.ValidatePlatformActorAsync(
            SuperAdminPrincipal(governanceRejectedAdmin.Id, governanceRejectedAdmin.TokenVersion));

        Assert.False(staleSession.Authorized);
        Assert.Equal("STALE_SUPER_ADMIN_TOKEN", staleSession.Reason);
        Assert.False(revokedAuthority.Authorized);
        Assert.Equal("SUPER_ADMIN_NOT_FOUND", revokedAuthority.Reason);
        Assert.False(governanceRejected.Authorized);
        Assert.Equal("SUPER_ADMIN_GOVERNANCE_REJECTED", governanceRejected.Reason);

        var platformFailures = Measurements(
            "operational_authority_failure_total",
            "authority",
            "platform_admin");
        Assert.True(platformFailures.Count >= 3);
        Assert.Contains(platformFailures, measurement => Tag(measurement, "reason") == "actor_scope_rejected");
        Assert.Contains(platformFailures, measurement => Tag(measurement, "reason") == "missing_sql_authority");
    }

    [Fact]
    public async Task FinalOperationalChaosFreeze_CollapsesReplayOverlapAndRejectsDangerousMisuse()
    {
        var tenantId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var listingId = Guid.NewGuid();
        var fenceKey = new ProjectionEventFenceKey(
            "FinalSuperAdminChaosCertification",
            "DeadLetterRetryOverlap",
            listingId);

        await using var dbContext = CreateDbContext(tenantId);
        await using (var firstFence = await ProjectionEventFence.TryAcquireAsync(
                         dbContext,
                         eventId,
                         fenceKey,
                         logger: null,
                         CancellationToken.None))
        {
            Assert.NotNull(firstFence);
            await firstFence!.CompleteAsync(
                eventId,
                tenantId,
                "final-chaos-certification",
                fenceKey,
                CancellationToken.None);
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var duplicateFence = await ProjectionEventFence.TryAcquireAsync(
                dbContext,
                eventId,
                fenceKey,
                logger: null,
                CancellationToken.None);

            Assert.Null(duplicateFence);
        }

        var rebuildOverlapA = SqlTruthRebuildGuard.RequireSqlAuthority(
            SqlTruthRebuildTarget.CommerceHub,
            tenantId,
            listingId,
            SqlAuthorityOverlayKind.CommerceHubListing |
            SqlAuthorityOverlayKind.Variant |
            SqlAuthorityOverlayKind.Category |
            SqlAuthorityOverlayKind.Stock |
            SqlAuthorityOverlayKind.Pricing |
            SqlAuthorityOverlayKind.Tombstone);
        var rebuildOverlapB = SqlTruthRebuildGuard.RequireSqlAuthority(
            SqlTruthRebuildTarget.CommerceHub,
            tenantId,
            listingId,
            SqlAuthorityOverlayKind.CommerceHubListing |
            SqlAuthorityOverlayKind.Variant |
            SqlAuthorityOverlayKind.Category |
            SqlAuthorityOverlayKind.Stock |
            SqlAuthorityOverlayKind.Pricing |
            SqlAuthorityOverlayKind.Tombstone);

        Assert.Equal(rebuildOverlapA, rebuildOverlapB);

        var unsafeSearchWrite = ValidMutation("FinalChaosRawSearchBypass") with
        {
            Primitive = DangerousProjectionPrimitive.ElasticsearchVisibilityWrite,
            EntityName = "CommerceHubProductSearchDocument",
            AuthorityKinds = SqlAuthorityOverlayKind.CommerceHubListing,
            HasSqlAuthority = false
        };
        var unsupportedRollback = ValidMutation("FinalChaosDerivativeRollback") with
        {
            FailsClosedWhenAuthorityMissing = false
        };

        Assert.Throws<InvalidOperationException>(() =>
            DerivativeProjectionMutationGuard.RequireReviewedMutation(unsafeSearchWrite));
        Assert.Throws<InvalidOperationException>(() =>
            DerivativeProjectionMutationGuard.RequireReviewedMutation(unsupportedRollback));

        var replayMeasurements = Measurements(
            "operational_semantic_replay_decision_total",
            "surface",
            "final_super_admin_chaos_certification");
        Assert.Contains(replayMeasurements, measurement =>
            Tag(measurement, "decision") == "applied" &&
            Tag(measurement, "reason") == "fence_completed");
        Assert.True(replayMeasurements.Count(measurement =>
            Tag(measurement, "decision") == "collapsed" &&
            Tag(measurement, "reason") == "duplicate_completed_event") >= 3);

        var searchBypassRejection = LastMeasurement(
            "operational_dangerous_primitive_governance_total",
            "surface",
            "final_chaos_raw_search_bypass");
        var rollbackRejection = LastMeasurement(
            "operational_dangerous_primitive_governance_total",
            "surface",
            "final_chaos_derivative_rollback");

        Assert.Equal("rejected", Tag(searchBypassRejection, "decision"));
        Assert.Equal("missing_sql_authority", Tag(searchBypassRejection, "reason"));
        Assert.Equal("rejected", Tag(rollbackRejection, "decision"));
        Assert.Equal("dangerous_primitive_rejected", Tag(rollbackRejection, "reason"));
    }

    public void Dispose()
    {
        _listener.Dispose();
    }

    private static ApplicationDbContext CreateDbContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"SemanticConvergenceChaos_{Guid.NewGuid():N}")
            .Options;

        return new ApplicationDbContext(options, new TestTenantContext(tenantId));
    }

    private static ReviewedDerivativeProjectionMutation ValidMutation(string boundaryName) =>
        new(
            DangerousProjectionPrimitive.MongoFullDocumentReplace,
            boundaryName,
            "ProductDocument",
            Guid.NewGuid().ToString("N"),
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTime.UtcNow,
            7,
            SqlAuthorityOverlayKind.Pricing | SqlAuthorityOverlayKind.Stock | SqlAuthorityOverlayKind.Tombstone,
            HasSqlAuthority: true,
            HasFreshnessGuard: true,
            FailsClosedWhenAuthorityMissing: true,
            "Reviewed SQL recomposition before semantic convergence chaos validation.");

    private static Tenant Tenant(Guid tenantId, bool isActive = true) =>
        new()
        {
            Id = tenantId,
            Name = $"tenant-{tenantId:N}",
            Slug = $"tenant-{tenantId:N}",
            IsActive = isActive,
            Status = isActive ? TenantStatus.Active : TenantStatus.Suspended
        };

    private static Product Product(Guid tenantId, Guid productId) =>
        new()
        {
            Id = productId,
            TenantId = tenantId,
            Name = $"Product {productId:N}",
            CategoryId = Guid.NewGuid(),
            SalePrice = 10m,
            PurchasePrice = 5m,
            CreatedAt = DateTime.UtcNow
        };

    private static User User(Guid userId, Guid tenantId, UserRole role, bool isActive = true) =>
        new()
        {
            Id = userId,
            TenantId = tenantId,
            Username = $"user-{userId:N}",
            Email = $"user-{userId:N}@example.test",
            PasswordHash = "hash",
            FirstName = "Chaos",
            LastName = "Actor",
            Role = role,
            IsActive = isActive,
            CreatedAt = DateTime.UtcNow
        };

    private static SuperAdminUser CreateSuperAdmin(string emailAddress)
    {
        var passwordHasher = new PasswordHasher<SuperAdminUser>();
        var email = EmailNormalizer.Normalize(emailAddress)!;
        var superAdmin = SuperAdminUser.Create(email, "TEMP_HASH", true, DateTime.UtcNow);
        superAdmin.UpdatePasswordHash(passwordHasher.HashPassword(superAdmin, "P@ssword123!"));
        return superAdmin;
    }

    private static ClaimsPrincipal Principal(Guid userId, Guid tenantId, string tokenRole) =>
        new(new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString("D")),
                new Claim(ClaimTypes.Role, tokenRole),
                new Claim("tenant_id", tenantId.ToString("D"))
            },
            "ChaosAuth"));

    private static ClaimsPrincipal SuperAdminPrincipal(Guid superAdminId, int tokenVersion) =>
        new(new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, superAdminId.ToString("D")),
                new Claim(ClaimTypes.Role, SuperAdminSecurityConstants.Role),
                new Claim(SuperAdminSecurityConstants.AuthTypeClaim, SuperAdminSecurityConstants.AuthTypeValue),
                new Claim(SuperAdminSecurityConstants.TokenVersionClaim, tokenVersion.ToString())
            },
            SuperAdminSecurityConstants.AuthenticationScheme));

    private static IReadOnlyCollection<IReadOnlySet<string>> RoleSets(params string[] roleSets) =>
        roleSets
            .Select(roles => roles
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase))
            .Cast<IReadOnlySet<string>>()
            .ToArray();

    private static Order Order(
        Guid tenantId,
        Guid orderId,
        PaymentStatus paymentStatus,
        FulfillmentStatus fulfillmentStatus) =>
        new()
        {
            Id = orderId,
            TenantId = tenantId,
            OrderNumber = $"ORD-{orderId:N}",
            Status = fulfillmentStatus is FulfillmentStatus.Delivered or FulfillmentStatus.Completed
                ? OrderStatus.Delivered
                : OrderStatus.Confirmed,
            PaymentStatus = paymentStatus,
            FulfillmentStatus = fulfillmentStatus,
            Currency = "EGP",
            ShippingAddress = "Chaos SQL authority street",
            CreatedAt = DateTime.UtcNow
        };

    private static Shipment Shipment(Guid tenantId, Guid orderId, Guid shipmentId, ShipmentStatus status) =>
        new()
        {
            Id = shipmentId,
            TenantId = tenantId,
            OrderId = orderId,
            ShipmentNumber = $"SHP-{shipmentId:N}",
            Status = status,
            TrackingNumber = "TRACK-CURRENT-SQL",
            CarrierCode = "sql-carrier",
            Currency = "EGP",
            RecipientName = "Chaos Recipient",
            ShippingAddress = "Chaos SQL authority street",
            CreatedAt = DateTime.UtcNow
        };

    private (string MetricName, long Value, KeyValuePair<string, object?>[] Tags) LastMeasurement(
        string metricName,
        string tagName,
        string tagValue)
    {
        return Measurements(metricName, tagName, tagValue).Last();
    }

    private List<(string MetricName, long Value, KeyValuePair<string, object?>[] Tags)> Measurements(
        string metricName,
        string tagName,
        string tagValue)
    {
        return _measurements
            .Where(measurement =>
                measurement.MetricName == metricName &&
                string.Equals(Tag(measurement, tagName), tagValue, StringComparison.Ordinal))
            .ToList();
    }

    private static string Tag(
        (string MetricName, long Value, KeyValuePair<string, object?>[] Tags) measurement,
        string key)
    {
        return measurement.Tags.Single(tag => tag.Key == key).Value?.ToString() ?? string.Empty;
    }

    private static string ReadApi(params string[] parts) =>
        File.ReadAllText(ProjectPath("PointOfSale.API", parts));

    private static string ReadInfrastructure(params string[] parts) =>
        File.ReadAllText(ProjectPath("PointOfSale.Infrastructure", parts));

    private static string ProjectPath(string projectName, params string[] parts) =>
        Path.GetFullPath(Path.Combine(
            new[] { AppContext.BaseDirectory, "..", "..", "..", "..", projectName }
                .Concat(parts)
                .ToArray()));
}
