using System.Text.RegularExpressions;

namespace PointOfSale.Tests.Architecture;

public sealed class DataAuthorityViolationHardeningTests
{
    [Fact]
    public void CommerceHubListingCommands_MustUseCentralMerchantOverrideAuthority()
    {
        var source = File.ReadAllText(ApiPath("CommerceHub", "Handlers", "CommerceHubCommandHandlers.cs"));
        var controller = File.ReadAllText(ApiPath("Controllers", "AdminCommerceHubController.cs"));
        var entity = File.ReadAllText(CorePath("Entities", "CommerceHubListing.cs"));
        var publishMethod = ExtractMethodBody(source, "HandleAsync");
        var resolveMethod = ExtractMethodBody(source, "ResolveEffectivePrice");
        Assert.NotNull(publishMethod);
        Assert.NotNull(resolveMethod);

        Assert.Contains("CommerceHubPriceOverrideRuntimePolicy.IsEnabledForTenant", source, StringComparison.Ordinal);
        Assert.Contains("ValidateCommerceHubPriceOverride", entity, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPrice = commerceHubPrice", entity, StringComparison.Ordinal);
        Assert.Contains("new CommerceHubPriceAuthority()", resolveMethod, StringComparison.Ordinal);
        Assert.Contains(".ResolveAsync(variant, overridePrice)", resolveMethod, StringComparison.Ordinal);
        Assert.Contains("command.CommerceHubPrice", publishMethod, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPriceOverride = listing.CommerceHubPrice", source, StringComparison.Ordinal);
        Assert.Contains("Price = v.EffectiveOnlinePrice", controller, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPrice = l.CommerceHubPrice", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("Price = v.OnlinePrice ?? v.Price", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubProjection_MustRecomposeMerchantOverridesAndAcceptPosPriceEvents()
    {
        var source = File.ReadAllText(InfrastructurePath("Consumers", "CommerceHubProjectionConsumer.cs"));

        Assert.Contains("CommerceHubPriceOverrideRuntimePolicy.IsEnabledForTenant", source, StringComparison.Ordinal);
        Assert.Contains(".Set(x => x.CommerceHubPriceOverride, priceResolution.OverridePrice)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("listing.CommerceHubPriceOverride.HasValue", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplicationDbContext_MustValidateCommerceHubPriceOverridesBeforePersistingListings()
    {
        var source = File.ReadAllText(InfrastructurePath("Data", "ApplicationDbContext.cs"));

        // Verify the guard exists and rejects invalid or unattributed override persistence.
        Assert.Contains("GuardCommerceHubPriceAuthority", source, StringComparison.Ordinal);
        Assert.Contains("CommerceHub price overrides must be positive when present.", source, StringComparison.Ordinal);
        Assert.Contains("CommerceHub price overrides require merchant actor and timestamp audit metadata.", source, StringComparison.Ordinal);

        // Verify SaveChanges invokes the guard before persistence
        Assert.Matches(
            @"override\s+int\s+SaveChanges\s*\(\s*\)[\s\S]+ApplyAuditAndCommerceHubImmutabilityGuards\s*\(\s*\)[\s\S]+base\.SaveChanges\s*\(\s*\)",
            source);
        Assert.Matches(
            @"private\s+void\s+ApplyAuditAndCommerceHubImmutabilityGuards\s*\(\s*\)[\s\S]+GuardCommerceHubPriceAuthority\s*\(\s*\)",
            source);

        // Verify SaveChangesAsync invokes the guard before persistence
        Assert.Matches(
            @"override\s+async\s+Task<int>\s+SaveChangesAsync\s*\([^)]*\)[\s\S]+ApplyAuditAndCommerceHubImmutabilityGuardsAsync\s*\([^)]*\)[\s\S]+base\.SaveChangesAsync\s*\(",
            source);
    }

    [Fact]
    public void CanonicalIdentityLinks_MustBeTenantScopedAndBackfilled()
    {
        var config = File.ReadAllText(InfrastructurePath("Data", "Configurations", "CanonicalIdentityLinkConfiguration.cs"));
        var s31 = File.ReadAllText(InfrastructurePath("Migrations", "20260424163000_S31_CanonicalCrossSystemIdentityLinks.sql"));
        var s34 = File.ReadAllText(InfrastructurePath("Migrations", "20260424180000_S34_DataAuthorityBoundaryHardening.sql"));

        Assert.Contains("UX_CanonicalIdentityLinks_Tenant_Subject_Active", config, StringComparison.Ordinal);
        Assert.Contains("UX_CanonicalIdentityLinks_Global_Subject_Active", config, StringComparison.Ordinal);
        Assert.Contains("Filtered unique indexes are defined in migrations", config, StringComparison.Ordinal);

        foreach (var migration in new[] { s31, s34 })
        {
            Assert.Contains("UX_CanonicalIdentityLinks_Tenant_Subject_Active", migration, StringComparison.Ordinal);
            Assert.Contains("UX_CanonicalIdentityLinks_Global_Subject_Active", migration, StringComparison.Ordinal);
            Assert.Contains("\"TenantId\" IS NOT NULL", migration, StringComparison.Ordinal);
            Assert.Contains("\"TenantId\" IS NULL", migration, StringComparison.Ordinal);
            Assert.Contains("FROM \"Users\"", migration, StringComparison.Ordinal);
            Assert.Contains("FROM \"Customers\"", migration, StringComparison.Ordinal);
            Assert.Contains("FROM \"OnlineCustomers\"", migration, StringComparison.Ordinal);
            Assert.Contains("FROM \"CommerceHubCustomers\"", migration, StringComparison.Ordinal);
            Assert.Contains("FROM \"AffiliateAccounts\"", migration, StringComparison.Ordinal);
            Assert.Contains("FROM \"AffiliateUsers\"", migration, StringComparison.Ordinal);
            Assert.Contains("FROM \"Tenants\"", migration, StringComparison.Ordinal);
        }

        Assert.Contains("DROP INDEX IF EXISTS \"UX_CanonicalIdentityLinks_Subject_Active\"", s34, StringComparison.Ordinal);
    }

    [Fact]
    public void CanonicalIdentityAuthority_MustContinuouslyRepairEfBypassWritesAndGateReadiness()
    {
        var reconciler = File.ReadAllText(InfrastructurePath("Identity", "CanonicalIdentityReconciliationService.cs"));
        var healthChecks = File.ReadAllText(InfrastructurePath("HealthChecks", "ProductionHealthChecks.cs"));
        var observability = File.ReadAllText(InfrastructurePath("Extensions", "ObservabilityServiceExtensions.cs"));
        var program = File.ReadAllText(ApiPath("Program.cs"));

        Assert.Contains("CanonicalIdentityReconciliationHostedService", reconciler, StringComparison.Ordinal);
        Assert.Contains("ReconcileUsersAsync", reconciler, StringComparison.Ordinal);
        Assert.Contains("ReconcileCustomersAsync", reconciler, StringComparison.Ordinal);
        Assert.Contains("ReconcileOnlineCustomersAsync", reconciler, StringComparison.Ordinal);
        Assert.Contains("ReconcileCommerceHubCustomersAsync", reconciler, StringComparison.Ordinal);
        Assert.Contains("ReconcileAffiliatesAsync", reconciler, StringComparison.Ordinal);
        Assert.Contains("ReconcileCommerceHubBuyerLinksAsync", reconciler, StringComparison.Ordinal);
        Assert.Contains("Canonical identity conflict", reconciler, StringComparison.Ordinal);

        Assert.Contains("CanonicalIdentityAuthorityHealthCheck", healthChecks, StringComparison.Ordinal);
        Assert.Contains("MissingCanonicalIdentityLinks", healthChecks, StringComparison.Ordinal);
        Assert.Contains("CanonicalIdentityConflicts", healthChecks, StringComparison.Ordinal);
        Assert.Contains("canonical_identity_authority", observability, StringComparison.Ordinal);
        Assert.Contains("AddHostedService<PointOfSale.Infrastructure.Identity.CanonicalIdentityReconciliationHostedService>", program, StringComparison.Ordinal);

        // Verify startup delay to avoid cold start database hits
        Assert.Matches(@"\bStartupDelay\b", reconciler);
        Assert.Matches(@"\bTask\.Delay\s*\(\s*\w*StartupDelay\w*\s*,\s*stoppingToken\s*\)", reconciler);
        Assert.Contains("delaying startup", reconciler, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubMarketplaceStatusEvents_MustDeclareAllAffectedStoreAuthorities()
    {
        var events = File.ReadAllText(CorePath("Events", "CommerceHubEvents.cs"));
        var marketplaceOrder = File.ReadAllText(CorePath("Entities", "CommerceHubMarketplaceOrder.cs"));
        var paymentService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPaymentService.cs"));
        var checkoutService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubCheckoutService.cs"));
        var refundService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubRefundService.cs"));
        var refundExecutionService = File.ReadAllText(ApiPath("Services", "CommerceHub", "RefundExecutionService.cs"));
        var orderManagementService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubOrderManagementService.cs"));

        Assert.Matches(
            @"class\s+CommerceHubMarketplaceOrderStatusUpdatedEvent[\s\S]+IAffectedTenantScopedIntegrationEvent[\s\S]+List<Guid>\s+AffectedStoreIds",
            events);
        Assert.Contains("GetAffectedTenantIds() => AffectedStoreIds", events, StringComparison.Ordinal);
        Assert.Contains("GetAffectedStoreIds", marketplaceOrder, StringComparison.Ordinal);
        Assert.Contains(".Select(subOrder => subOrder.StoreId)", marketplaceOrder, StringComparison.Ordinal);
        Assert.Contains(".Distinct()", marketplaceOrder, StringComparison.Ordinal);
        Assert.Contains(".OrderBy(storeId => storeId)", marketplaceOrder, StringComparison.Ordinal);

        foreach (var publisher in new[] { paymentService, checkoutService, refundService, refundExecutionService, orderManagementService })
        {
            Assert.Contains("affectedStoreIds = marketplaceOrder.GetAffectedStoreIds()", publisher, StringComparison.Ordinal);
            Assert.Contains("TenantId = affectedStoreIds.First", publisher, StringComparison.Ordinal);
            Assert.Contains("AffectedStoreIds = affectedStoreIds", publisher, StringComparison.Ordinal);
            Assert.Contains("affectedStoreIds.Count == 0", publisher, StringComparison.Ordinal);
            Assert.DoesNotContain("TenantId = affectedStoreIds.FirstOrDefault()", publisher, StringComparison.Ordinal);
        }

        Assert.Contains("subOrder.StoreId == Guid.Empty", paymentService, StringComparison.Ordinal);
        Assert.Contains("subOrder.StoreId == Guid.Empty", refundService, StringComparison.Ordinal);
        Assert.Contains("subOrder.StoreId == Guid.Empty", refundExecutionService, StringComparison.Ordinal);
        Assert.Contains("subOrder.StoreId == Guid.Empty", orderManagementService, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubCustomerAuthority_MustNotAcceptStorefrontIdentityAliases()
    {
        var orderController = File.ReadAllText(ApiPath("Controllers", "CommerceHubOrdersController.cs"));
        var checkout = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubCheckoutService.cs"));
        var marketplaceOrderConfig = File.ReadAllText(InfrastructurePath("Data", "Configurations", "CommerceHubMarketplaceOrderConfiguration.cs"));
        var checkoutIdempotencyConfig = File.ReadAllText(InfrastructurePath("Data", "Configurations", "CommerceHubCheckoutIdempotencyConfiguration.cs"));
        var cartController = File.ReadAllText(ApiPath("Controllers", "CommerceHubCartController.cs"));
        var cartService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubCartService.cs"));
        var dbContext = File.ReadAllText(InfrastructurePath("Data", "ApplicationDbContext.cs"));
        var migration = File.ReadAllText(InfrastructurePath("Migrations", "20260430110000_S61_DataAuthorityAndEventScopeClosure.sql"));
        var closureMigration = File.ReadAllText(InfrastructurePath("Migrations", "20260501160000_S63_DataAuthorityAndEventOrderingClosure.sql"));

        Assert.Contains("CustomerSecurityConstants.AuthenticationScheme", orderController, StringComparison.Ordinal);
        Assert.DoesNotContain("StorefrontJwt", orderController, StringComparison.Ordinal);
        Assert.Contains("authenticatedCustomerId.Value == authority.CustomerId", orderController, StringComparison.Ordinal);
        Assert.DoesNotContain("document.OnlineCustomerId", orderController, StringComparison.Ordinal);
        Assert.Contains("OnlineCustomerId = null", checkout, StringComparison.Ordinal);
        Assert.DoesNotContain("OnlineCustomerId = customer.Id", checkout, StringComparison.Ordinal);
        Assert.Contains("OnlineCustomerId = customerId", cartController, StringComparison.Ordinal);
        Assert.Contains("Guid? OnlineCustomerId", cartService, StringComparison.Ordinal);

        Assert.Contains("CK_CommerceHubMarketplaceOrders_CustomerAuthorityBoundary", marketplaceOrderConfig, StringComparison.Ordinal);
        Assert.Contains("\\\"CustomerId\\\" <> '00000000-0000-0000-0000-000000000000'", marketplaceOrderConfig, StringComparison.Ordinal);
        Assert.Contains("\\\"OnlineCustomerId\\\" IS NULL", marketplaceOrderConfig, StringComparison.Ordinal);
        Assert.Contains("CK_CommerceHubCheckoutIdempotency_CustomerAuthorityBoundary", checkoutIdempotencyConfig, StringComparison.Ordinal);
        Assert.Contains("\\\"CommerceHubCustomerId\\\" IS NOT NULL", checkoutIdempotencyConfig, StringComparison.Ordinal);
        Assert.Contains("\\\"OnlineCustomerId\\\" IS NULL", checkoutIdempotencyConfig, StringComparison.Ordinal);
        Assert.DoesNotContain("new { e.TenantId, e.OnlineCustomerId }", checkoutIdempotencyConfig, StringComparison.Ordinal);
        Assert.Contains("CK_CommerceHubCarts_CustomerAuthorityBoundary", dbContext, StringComparison.Ordinal);
        Assert.Contains("\\\"CommerceHubCustomerId\\\" IS NOT NULL", dbContext, StringComparison.Ordinal);
        Assert.Contains("\\\"OnlineCustomerId\\\" = \\\"CommerceHubCustomerId\\\"", dbContext, StringComparison.Ordinal);
        Assert.DoesNotContain("cart => new { cart.TenantId, cart.OnlineCustomerId }", dbContext, StringComparison.Ordinal);
        Assert.DoesNotContain("record => new { record.TenantId, record.OnlineCustomerId }", dbContext, StringComparison.Ordinal);

        Assert.Contains("SET \"OnlineCustomerId\" = NULL", migration, StringComparison.Ordinal);
        Assert.Contains("SET \"OnlineCustomerId\" = \"CommerceHubCustomerId\"", migration, StringComparison.Ordinal);
        Assert.Contains("VALIDATE CONSTRAINT \"CK_CommerceHubMarketplaceOrders_CustomerAuthorityBoundary\"", migration, StringComparison.Ordinal);
        Assert.Contains("VALIDATE CONSTRAINT \"CK_CommerceHubCarts_CustomerAuthorityBoundary\"", migration, StringComparison.Ordinal);
        Assert.Contains("DELETE FROM \"CommerceHubCheckoutIdempotency\"", closureMigration, StringComparison.Ordinal);
        Assert.Contains("DROP INDEX IF EXISTS \"IX_CommerceHubCheckoutIdempotency_TenantId_OnlineCustomerId\"", closureMigration, StringComparison.Ordinal);
        Assert.Contains("DROP INDEX IF EXISTS \"IX_CommerceHubCarts_TenantId_OnlineCustomerId\"", closureMigration, StringComparison.Ordinal);
        Assert.Contains("VALIDATE CONSTRAINT \"CK_CommerceHubCheckoutIdempotency_CustomerAuthorityBoundary\"", closureMigration, StringComparison.Ordinal);
    }

    [Fact]
    public void PaymentOutcomeEvents_MustBeOwnedOnlyByCentralPaymentLifecycleAuthority()
    {
        var productionSources = string.Join(
            Environment.NewLine,
            Directory
                .GetFiles(ApiPath("."), "*.cs", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(InfrastructurePath("."), "*.cs", SearchOption.AllDirectories))
                .Concat(Directory.GetFiles(CorePath("."), "*.cs", SearchOption.AllDirectories))
                .Select(File.ReadAllText));

        var lifecycle = File.ReadAllText(InfrastructurePath("Payments", "PaymentLifecycleService.cs"));
        var program = File.ReadAllText(ApiPath("Program.cs"));

        Assert.DoesNotContain("IPaymentOutcomeEventPublisher", productionSources, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentOutcomeEventPublisher", productionSources, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentOutcomeNotification", productionSources, StringComparison.Ordinal);
        Assert.DoesNotContain("_paymentOutcomeEventPublisher", productionSources, StringComparison.Ordinal);
        Assert.DoesNotContain("AddScoped<PointOfSale.API.Services.Payments.IPaymentOutcomeEventPublisher", program, StringComparison.Ordinal);

        Assert.Contains("PublishCanonicalAuthorizedOutcomeEventAsync", lifecycle, StringComparison.Ordinal);
        Assert.Contains("PublishCanonicalSuccessOutcomeEventsAsync", lifecycle, StringComparison.Ordinal);
        Assert.Contains("PublishCanonicalFailureOutcomeEventsAsync", lifecycle, StringComparison.Ordinal);
        Assert.Contains("PaymentAuthorizedEvent", lifecycle, StringComparison.Ordinal);
        Assert.Contains("PaymentCapturedEvent", lifecycle, StringComparison.Ordinal);
        Assert.Contains("OrderPaidEvent", lifecycle, StringComparison.Ordinal);
        Assert.Contains("InventoryConfirmedEvent", lifecycle, StringComparison.Ordinal);
        Assert.Contains("PaymentFailedEvent", lifecycle, StringComparison.Ordinal);
        Assert.Contains("InventoryReleasedEvent", lifecycle, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubOrderFinancialTruth_MustBeOwnedByExplicitMutationBoundaries()
    {
        var dbContext = File.ReadAllText(InfrastructurePath("Data", "ApplicationDbContext.cs"));
        var scope = File.ReadAllText(InfrastructurePath("Data", "CommerceHubBoundaryMutationScope.cs"));
        var paymentService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPaymentService.cs"));
        var refundExecution = File.ReadAllText(ApiPath("Services", "CommerceHub", "RefundExecutionService.cs"));
        var financialWorkflowService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubFinancialWorkflowService.cs"));
        var orderManagementService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubOrderManagementService.cs"));

        Assert.Contains("IsOrderLifecycleAuthorized", scope, StringComparison.Ordinal);
        Assert.Contains("IsFinancialWorkflowAuthorized", scope, StringComparison.Ordinal);

        Assert.Contains("GuardBoundaryOwnedEntityCreation<CommerceHubMarketplaceOrder>", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardBoundaryOwnedEntityCreation<CommerceHubSubOrder>", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardBoundaryOwnedEntityCreation<CommerceHubSubOrderItem>", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardBoundaryOwnedEntityCreation<CommerceHubPayment>", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardBoundaryOwnedEntityCreation<CommerceHubPaymentItem>", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardBoundaryOwnedEntityCreation<CommerceHubPaymentAttempt>", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardBoundaryOwnedEntityCreation<CommerceHubPaymentLog>", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardBoundaryOwnedEntityCreation<CommerceHubRefund>", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardBoundaryOwnedEntityCreation<CommerceHubPayout>", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardBoundaryOwnedEntityCreation<CommerceHubPayoutItem>", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardBoundaryOwnedEntityCreation<CommerceHubVendorEarning>", dbContext, StringComparison.Ordinal);
        Assert.Contains("ChangeTracker.Entries<TEntity>().Any(entry => entry.State == EntityState.Added)", dbContext, StringComparison.Ordinal);
        Assert.Contains("CommerceHub marketplace order lifecycle fields can only be changed", dbContext, StringComparison.Ordinal);
        Assert.Contains("CommerceHub marketplace order payment fields can only be changed", dbContext, StringComparison.Ordinal);
        Assert.Contains("CommerceHub sub-order financial snapshot fields can only be changed", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardBoundaryOwnedProperties<CommerceHubSubOrderItem>", dbContext, StringComparison.Ordinal);

        Assert.Contains("nameof(CommerceHubSubOrder.CommissionSnapshotJson)", dbContext, StringComparison.Ordinal);
        Assert.Contains("nameof(CommerceHubSubOrder.VendorEarningAmount)", dbContext, StringComparison.Ordinal);
        Assert.Contains("nameof(CommerceHubSubOrder.IsEarningCreated)", dbContext, StringComparison.Ordinal);

        Assert.Contains("CommerceHubBoundaryMutationScope.BeginPayment()", paymentService, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginRefund()", refundExecution, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginFinancialWorkflow()", financialWorkflowService, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginOrderLifecycle()", orderManagementService, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubPaymentCompensationWorker_MustMutatePaymentStateInsideAuthorityBoundaries()
    {
        var worker = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPaymentCompensationOutboxWorker.cs"));

        Assert.Contains("CommerceHubBoundaryMutationScope.BeginPayment()", worker, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginRefund()", worker, StringComparison.Ordinal);
        Assert.Contains("payment.Status = CommerceHubPaymentRecordStatus.Paid", worker, StringComparison.Ordinal);
        Assert.Contains("payment.RefundedAmount", worker, StringComparison.Ordinal);
        Assert.Contains("ApplyRefundStatuses", worker, StringComparison.Ordinal);
        Assert.Contains("CommerceHubRefunds", worker, StringComparison.Ordinal);

        Assert.Matches(
            @"BeginPayment\(\)[\s\S]+BeginRefund\(\)[\s\S]+SaveChangesAsync\(cancellationToken\)",
            worker);
    }

    [Fact]
    public void CommerceHubPaymentCompensation_MustDeclareAllAffectedStoreAuthorities()
    {
        var paymentService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPaymentService.cs"));
        var entity = File.ReadAllText(CorePath("Entities", "CommerceHubPaymentCompensationOutboxMessage.cs"));
        var configuration = File.ReadAllText(InfrastructurePath("Data", "Configurations", "CommerceHubPaymentCompensationOutboxMessageConfiguration.cs"));
        var migration = File.ReadAllText(InfrastructurePath("Migrations", "20260428143000_S46_CommerceHubPaymentCompensationOutbox.sql"));
        var snapshotMigration = File.ReadAllText(InfrastructurePath("Migrations", "20260506110000_S72_CrossSystemCriticalRiskCompensationSnapshots.sql"));
        var refundSnapshotMigration = File.ReadAllText(InfrastructurePath("Migrations", "20260508143000_S77_CommerceHubProviderRefundAuthorityConvergence.sql"));
        var worker = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPaymentCompensationOutboxWorker.cs"));

        Assert.Contains("AffectedStoreIdsJson", entity, StringComparison.Ordinal);
        Assert.Contains("StoreAllocationSnapshotJson", entity, StringComparison.Ordinal);
        Assert.Contains("RefundAllocationSnapshotJson", entity, StringComparison.Ordinal);
        Assert.Contains("AffectedStoreIdsJson", configuration, StringComparison.Ordinal);
        Assert.Contains("StoreAllocationSnapshotJson", configuration, StringComparison.Ordinal);
        Assert.Contains("RefundAllocationSnapshotJson", configuration, StringComparison.Ordinal);
        Assert.Contains("\"AffectedStoreIdsJson\"", migration, StringComparison.Ordinal);
        Assert.Contains("\"StoreAllocationSnapshotJson\"", snapshotMigration, StringComparison.Ordinal);
        Assert.Contains("\"RefundAllocationSnapshotJson\"", refundSnapshotMigration, StringComparison.Ordinal);
        Assert.Contains("affectedStoreIds = payment.Order.SubOrders", paymentService, StringComparison.Ordinal);
        Assert.Contains("TenantId = affectedStoreIds.Count == 1 ? affectedStoreIds[0] : null", paymentService, StringComparison.Ordinal);
        Assert.Contains("AffectedStoreIdsJson = JsonSerializer.Serialize(affectedStoreIds)", paymentService, StringComparison.Ordinal);
        Assert.Contains("StoreAllocationSnapshotJson = SerializeStoreAllocationSnapshot", paymentService, StringComparison.Ordinal);
        Assert.Contains("RefundAllocationSnapshotJson = SerializeRefundAllocationSnapshot", paymentService, StringComparison.Ordinal);
        Assert.Contains("ResolveStoreAllocations", worker, StringComparison.Ordinal);
        Assert.Contains("ParseStoreAllocationSnapshot", worker, StringComparison.Ordinal);
        Assert.Contains("ParseRefundAllocationSnapshot", worker, StringComparison.Ordinal);
        Assert.DoesNotContain("payment.Order?.SubOrders?", worker, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubProjectionRecovery_MustPreserveSqlCustomerAuthorityAndOrderingWatermarks()
    {
        var recovery = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubProjectionReconciliationService.cs"));
        var support = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubOrderProjectionRepairSupport.cs"));
        var markProjected = ExtractMethodBody(recovery, "MarkProjectedAsync");

        Assert.NotNull(markProjected);

        Assert.Contains("CommerceHubBoundaryMutationScope.BeginProjection()", markProjected!, StringComparison.Ordinal);
        Assert.Contains("BuildProjectionRecoveryFilter", support, StringComparison.Ordinal);
        Assert.Contains("ProjectionProvesSqlAuthority", support, StringComparison.Ordinal);
        Assert.Contains("ResolveSourceMutationVersion(order)", support, StringComparison.Ordinal);
        Assert.Contains("ProductEventVersionHelper.ToProductEventVersion(sourceMutationVersion)", support, StringComparison.Ordinal);
        Assert.Contains("CustomerId = order.CustomerId", support, StringComparison.Ordinal);
        Assert.Contains("LastEventOccurredAtUtc = projectionTimestampUtc", support, StringComparison.Ordinal);
        Assert.Contains("LastEventVersion =", support, StringComparison.Ordinal);
        Assert.Contains("LastSourceMutationTicks = sourceMutationVersion", support, StringComparison.Ordinal);
        Assert.Contains("existing.CustomerId == customerId", support, StringComparison.Ordinal);
        Assert.DoesNotContain(".AnyAsync(cancellationToken)", recovery, StringComparison.Ordinal);
        Assert.DoesNotContain("CustomerId = Guid.Empty", support, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductSearchIndexConsumers_MustUseMongoProjectionAsEsAuthority()
    {
        var created = File.ReadAllText(InfrastructurePath("Consumers", "ProductSearchIndexConsumer.cs"));
        var updated = File.ReadAllText(InfrastructurePath("Consumers", "ProductUpdatedSearchIndexConsumer.cs"));
        var stockPrice = File.ReadAllText(InfrastructurePath("Consumers", "ProductStockPriceSearchIndexConsumer.cs"));
        var promotions = File.ReadAllText(InfrastructurePath("Consumers", "ProductPromotionSearchIndexConsumer.cs"));

        foreach (var consumer in new[] { created, updated, stockPrice, promotions })
        {
            Assert.Contains("IMongoCollection<ProductDocument>", consumer, StringComparison.Ordinal);
            Assert.Contains("ProjectionNotReadyException", consumer, StringComparison.Ordinal);
            Assert.Contains("SearchIndexSyncException", consumer, StringComparison.Ordinal);
            Assert.Contains("ProductSearchDocumentMapper.MapFromProductDocument", consumer, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("using event data only", updated, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fallbackDocument", updated, StringComparison.Ordinal);
        Assert.Contains("_indexService.DeleteProductAsync", created, StringComparison.Ordinal);
        Assert.Contains("_indexService.DeleteProductAsync", stockPrice, StringComparison.Ordinal);
    }

    [Fact]
    public void PayoutExternalTruth_MustRequireEvidenceAndReadinessReconciliation()
    {
        var payoutService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPayoutService.cs"));
        var hangfire = File.ReadAllText(ApiPath("Jobs", "HangfireInitializationHostedService.cs"));
        var healthChecks = File.ReadAllText(InfrastructurePath("HealthChecks", "ProductionHealthChecks.cs"));
        var observability = File.ReadAllText(InfrastructurePath("Extensions", "ObservabilityServiceExtensions.cs"));

        Assert.Contains("EnsureTerminalPayoutEvidence", payoutService, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_EXTERNAL_CONFIRMATION_REQUIRED", payoutService, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_EXTERNAL_EVIDENCE_ALREADY_USED", payoutService, StringComparison.Ordinal);
        Assert.Contains("commercehub-finance-reconciliation", hangfire, StringComparison.Ordinal);

        Assert.Contains("PayoutExternalTruthReadinessHealthCheck", healthChecks, StringComparison.Ordinal);
        Assert.Contains("CompletedPayoutsWithoutExternalEvidence", healthChecks, StringComparison.Ordinal);
        Assert.Contains("CommerceHubFinancialReconciliationReport", healthChecks, StringComparison.Ordinal);
        Assert.Contains("payout_external_truth_readiness", observability, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubExternalOutcomeRecovery_MustBeObservableAsReadinessState()
    {
        var healthChecks = File.ReadAllText(InfrastructurePath("HealthChecks", "ProductionHealthChecks.cs"));
        var observability = File.ReadAllText(InfrastructurePath("Extensions", "ObservabilityServiceExtensions.cs"));

        Assert.Contains("CommerceHubExternalOutcomeRecoveryHealthCheck", healthChecks, StringComparison.Ordinal);
        Assert.Contains("UnresolvedRefundCount", healthChecks, StringComparison.Ordinal);
        Assert.Contains("UnresolvedCapturedWebhookCount", healthChecks, StringComparison.Ordinal);
        Assert.Contains("ReconciliationRequiredShipmentCount", healthChecks, StringComparison.Ordinal);
        Assert.Contains("commercehub_external_outcome_recovery", observability, StringComparison.Ordinal);
    }

    [Fact]
    public void VendorRepaymentApproval_MustRequireExternalTransferAuthority()
    {
        var service = File.ReadAllText(ApiPath("Services", "CommerceHub", "Finance", "CommerceHubVendorPaymentsService.cs"));
        var configuration = File.ReadAllText(InfrastructurePath("Data", "Configurations", "CommerceHubVendorPaymentRequestConfiguration.cs"));
        var migration = File.ReadAllText(InfrastructurePath("Migrations", "20260501170000_S64_CrossSystemCriticalRiskProductionClosure.sql"));

        Assert.Contains("EnsureApprovedRepaymentHasExternalTransferReference(paymentRequest)", service, StringComparison.Ordinal);
        Assert.Contains("PAYMENT_TRANSFER_REFERENCE_REQUIRED", service, StringComparison.Ordinal);
        Assert.Contains("CK_CommerceHubVendorPaymentRequests_Approved_TransferReference", configuration, StringComparison.Ordinal);
        Assert.Contains("\\\"Status\\\" <> 1 OR NULLIF(btrim(\\\"TransferReference\\\"), '') IS NOT NULL", configuration, StringComparison.Ordinal);
        Assert.Contains("approved vendor repayments without external transfer references exist", migration, StringComparison.Ordinal);
        Assert.Contains("ADD CONSTRAINT \"CK_CommerceHubVendorPaymentRequests_Approved_TransferReference\"", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void AffiliatePayoutDecisions_MustUseSqlLedgersNotMongoMoneyProjections()
    {
        var payoutService = File.ReadAllText(InfrastructurePath("Services", "Affiliate", "AffiliatePayoutService.cs"));
        var orchestrator = File.ReadAllText(InfrastructurePath("Services", "Affiliate", "AffiliateSettlementOrchestrator.cs"));
        var adminController = File.ReadAllText(ApiPath("Controllers", "AdminAffiliateController.cs"));
        var portalController = File.ReadAllText(ApiPath("Controllers", "AffiliatePortalController.cs"));

        Assert.DoesNotContain("MongoDbContext", payoutService, StringComparison.Ordinal);
        Assert.DoesNotContain("AffiliateDashboardDocument", payoutService, StringComparison.Ordinal);
        Assert.DoesNotContain("AffiliateEarningsSummaryDocument", payoutService, StringComparison.Ordinal);

        Assert.DoesNotContain("MongoDbContext", orchestrator, StringComparison.Ordinal);
        Assert.DoesNotContain("AffiliateDashboardDocument", orchestrator, StringComparison.Ordinal);
        Assert.Contains("AffiliateCommissionLedger", payoutService, StringComparison.Ordinal);
        Assert.Contains("AffiliateBalanceLedger", payoutService, StringComparison.Ordinal);
        Assert.Contains("BalanceLedger.Withdrawal", orchestrator, StringComparison.Ordinal);

        var requestPayoutMethod = ExtractMethodBody(portalController, "RequestPayout");
        Assert.NotNull(requestPayoutMethod);
        Assert.Contains("body.StoreTenantId != Guid.Empty", requestPayoutMethod!, StringComparison.Ordinal);
        Assert.Contains("_payoutService.GetAvailableBalanceAsync", requestPayoutMethod!, StringComparison.Ordinal);
        Assert.Contains("balance.RawAvailable", requestPayoutMethod!, StringComparison.Ordinal);
        Assert.Matches(
            @"GetAvailableBalanceAsync[\s\S]+RequestPayoutAsync",
            requestPayoutMethod!);
        Assert.Matches(
            @"ExecuteAffiliatePayoutAsync[\s\S]+AffiliateCommissionPaidEvent",
            orchestrator);
        var markPaidMethod = ExtractMethodBody(adminController, "MarkPayoutPaid");
        Assert.NotNull(markPaidMethod);
        Assert.DoesNotContain("new AffiliateCommissionPaidEvent", markPaidMethod!, StringComparison.Ordinal);
    }

    [Fact]
    public void AdminAffiliateVisibility_MustUseCommissionLedgerNotMongoDashboardTotals()
    {
        var source = File.ReadAllText(ApiPath("Controllers", "AdminAffiliateController.cs"));
        var method = ExtractMethodBody(source, "ListAffiliates");

        Assert.NotNull(method);
        Assert.Contains("var activeAffiliateIdsSubquery = _dbContext.AffiliateCommissionLedger", method!, StringComparison.Ordinal);
        Assert.Contains("entry.StoreTenantId == tenantId", method!, StringComparison.Ordinal);
        Assert.Contains("entry.EntryType == CommissionEntryType.Earned", method!, StringComparison.Ordinal);
        Assert.Contains(".Select(entry => entry.AffiliateAccountId)", method!, StringComparison.Ordinal);
        Assert.Contains(".Distinct()", method!, StringComparison.Ordinal);
        Assert.Contains("activeAffiliateIdsSubquery.Contains(a.Id)", method!, StringComparison.Ordinal);

        var ledgerIndex = method!.IndexOf("_dbContext.AffiliateCommissionLedger", StringComparison.Ordinal);
        var affiliateQueryIndex = method.IndexOf("var query = _dbContext.AffiliateAccounts", StringComparison.Ordinal);

        Assert.True(ledgerIndex >= 0, "Affiliate visibility must start from the SQL commission ledger.");
        Assert.True(affiliateQueryIndex > ledgerIndex, "The affiliate SQL query must be constrained by SQL ledger visibility.");
        Assert.Contains("BuildAffiliateLedgerTotalsByAffiliateAsync", method, StringComparison.Ordinal);
        Assert.DoesNotContain("_mongoDbContext.AffiliateDashboards.Find", method, StringComparison.Ordinal);

        var visibilitySelection = method.Substring(ledgerIndex, affiliateQueryIndex - ledgerIndex);
        Assert.DoesNotContain("_mongoDbContext", visibilitySelection, StringComparison.Ordinal);
        Assert.DoesNotContain("AffiliateDashboards", visibilitySelection, StringComparison.Ordinal);
        Assert.DoesNotContain("TotalOrders", visibilitySelection, StringComparison.Ordinal);
    }

    [Fact]
    public void AffiliateFinancialReadApis_MustUseSqlCommissionLedgerNotMongoFinancialProjections()
    {
        var adminController = File.ReadAllText(ApiPath("Controllers", "AdminAffiliateController.cs"));
        var portalController = File.ReadAllText(ApiPath("Controllers", "AffiliatePortalController.cs"));

        Assert.Contains("_dbContext.AffiliateCommissionLedger", adminController, StringComparison.Ordinal);
        Assert.Contains("_dbContext.AffiliateCommissionLedger", portalController, StringComparison.Ordinal);

        var portalDashboard = ExtractMethodBody(portalController, "GetDashboard");
        var portalEarnings = ExtractMethodBody(portalController, "GetEarningsSummary");
        var adminList = ExtractMethodBody(adminController, "ListAffiliates");
        var adminDetail = ExtractMethodBody(adminController, "GetAffiliateDetail");
        var adminEarnings = ExtractMethodBody(adminController, "GetAffiliateEarnings");

        Assert.NotNull(portalDashboard);
        Assert.NotNull(portalEarnings);
        Assert.NotNull(adminList);
        Assert.NotNull(adminDetail);
        Assert.NotNull(adminEarnings);

        Assert.Contains("BuildAffiliateLedgerTotalsAsync", portalDashboard!, StringComparison.Ordinal);
        Assert.Contains("BuildAffiliateLedgerEarningsSummaryAsync", portalEarnings!, StringComparison.Ordinal);
        Assert.Contains("BuildAffiliateLedgerTotalsByAffiliateAsync", adminList!, StringComparison.Ordinal);
        Assert.Contains("BuildAffiliateLedgerTotalsAsync", adminDetail!, StringComparison.Ordinal);
        Assert.Contains("BuildAffiliateLedgerEarningsSummaryAsync", adminEarnings!, StringComparison.Ordinal);

        foreach (var method in new[] { portalDashboard!, portalEarnings!, adminList!, adminDetail!, adminEarnings! })
        {
            // Ban direct MongoDB collection access
            Assert.DoesNotContain("_mongoDbContext.AffiliateDashboards", method, StringComparison.Ordinal);
            Assert.DoesNotContain("_mongoDbContext.AffiliateEarningsSummaries", method, StringComparison.Ordinal);
            
            // Ban document types
            Assert.DoesNotContain("AffiliateDashboardDocument", method, StringComparison.Ordinal);
            Assert.DoesNotContain("AffiliateEarningsSummaryDocument", method, StringComparison.Ordinal);
            
            // Ban common MongoDB query methods
            Assert.DoesNotContain(".Find(", method, StringComparison.Ordinal);
            Assert.DoesNotContain(".FindAsync(", method, StringComparison.Ordinal);
            Assert.DoesNotContain(".FindOne", method, StringComparison.Ordinal);
            Assert.DoesNotContain(".Aggregate(", method, StringComparison.Ordinal);
            Assert.DoesNotContain(".AsQueryable(", method, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AffiliateOrderLists_MustUseMongoOnlyForDisplayMetadataAndSqlLedgerForMoney()
    {
        var adminController = File.ReadAllText(ApiPath("Controllers", "AdminAffiliateController.cs"));
        var portalController = File.ReadAllText(ApiPath("Controllers", "AffiliatePortalController.cs"));

        var adminOrders = ExtractMethodBody(adminController, "GetAffiliateOrders");
        var portalOrders = ExtractMethodBody(portalController, "GetOrders");

        Assert.NotNull(adminOrders);
        Assert.NotNull(portalOrders);

        foreach (var method in new[] { adminOrders!, portalOrders! })
        {
            Assert.Contains("BuildAffiliateOrderLedgerSnapshotsAsync", method, StringComparison.Ordinal);
            Assert.Contains("BuildAffiliateOrderIdsForLedgerStatusAsync", method, StringComparison.Ordinal);
            Assert.DoesNotContain("Builders<AffiliateOrderDocument>.Filter.Eq(d => d.Status, status)", method, StringComparison.Ordinal);
            Assert.DoesNotContain("TotalCommission = d.TotalCommission", method, StringComparison.Ordinal);
            Assert.DoesNotContain("TotalApproved = d.TotalApproved", method, StringComparison.Ordinal);
            Assert.DoesNotContain("TotalPaid = d.TotalPaid", method, StringComparison.Ordinal);
            Assert.DoesNotContain("TotalReversed = d.TotalReversed", method, StringComparison.Ordinal);
            Assert.DoesNotContain("PaidAmount = d.PaidAmount", method, StringComparison.Ordinal);
            Assert.DoesNotContain("PaidAmount = i.PaidAmount", method, StringComparison.Ordinal);
            Assert.DoesNotContain("CommissionAmount = i.CommissionAmount", method, StringComparison.Ordinal);
            Assert.Contains("ledger?.TotalCommission", method, StringComparison.Ordinal);
            Assert.Contains("itemLedger?.CommissionAmount", method, StringComparison.Ordinal);
        }

        foreach (var source in new[] { adminController, portalController })
        {
            Assert.Contains("_dbContext.AffiliateCommissionLedger", source, StringComparison.Ordinal);
            Assert.Contains("BuildAffiliateOrderLedgerSnapshotsAsync", source, StringComparison.Ordinal);
            Assert.Contains("BuildAffiliateOrderItemLedgerSnapshot", source, StringComparison.Ordinal);
            Assert.Contains("DetermineLedgerOrderStatus", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DataAuthorityMigration_MustClearHistoricalCommerceHubPriceOverrides()
    {
        var migration = File.ReadAllText(InfrastructurePath("Migrations", "20260424180000_S34_DataAuthorityBoundaryHardening.sql"));
        var s50 = File.ReadAllText(InfrastructurePath("Migrations", "20260429110000_S50_DataAuthorityViolationClosure.sql"));
        var config = File.ReadAllText(InfrastructurePath("Data", "Configurations", "CommerceHubListingConfiguration.cs"));

        Assert.Contains("UPDATE \"CommerceHubListings\"", migration, StringComparison.Ordinal);
        Assert.Contains("SET \"CommerceHubPrice\" = NULL", migration, StringComparison.Ordinal);
        Assert.Contains("CK_CommerceHubListings_CommerceHubPrice_Positive", config, StringComparison.Ordinal);
        Assert.Contains("\\\"CommerceHubPrice\\\" IS NULL OR \\\"CommerceHubPrice\\\" > 0", config, StringComparison.Ordinal);
        Assert.Contains("UPDATE \"CommerceHubListings\"", s50, StringComparison.Ordinal);
        Assert.Contains("SET \"CommerceHubPrice\" = NULL", s50, StringComparison.Ordinal);
        Assert.Contains("CK_CommerceHubListings_CommerceHubPrice_PosAuthorityNull", s50, StringComparison.Ordinal);
        Assert.Contains("VALIDATE CONSTRAINT \"CK_CommerceHubListings_CommerceHubPrice_PosAuthorityNull\"", s50, StringComparison.Ordinal);
    }

    [Fact]
    public void InventoryAvailabilityAuthority_MustNotSubtractDamagedStockTwice()
    {
        var stockEntity = File.ReadAllText(CorePath("Entities", "ProductVariantStock.cs"));
        var stockRepository = File.ReadAllText(InfrastructurePath("Data", "Repositories", "ProductVariantStockRepository.cs"));

        // Validate behavior through executable code, not comments
        Assert.Contains("AvailableQuantity => Quantity - ReservedQuantity", stockEntity, StringComparison.Ordinal);
        Assert.Contains("stock.Quantity - stock.ReservedQuantity", stockRepository, StringComparison.Ordinal);
        Assert.DoesNotContain("stock.Quantity - stock.ReservedQuantity - stock.DamagedQuantity", stockRepository, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckoutAffiliateAttribution_MustResolveThroughAuthorityBoundaries()
    {
        var checkout = File.ReadAllText(ApiPath("Services", "Checkout", "CheckoutService.cs"));
        var migration = File.ReadAllText(InfrastructurePath("Migrations", "Sql", "20260424_CrossSystemCriticalRiskHardening.sql"));

        Assert.Contains("ResolveAffiliateAttributionAuthorityAsync", checkout, StringComparison.Ordinal);
        Assert.Contains("ResolveMiddlewareAffiliateAuthorityAsync", checkout, StringComparison.Ordinal);
        Assert.Contains("promotionLink.TenantId != tenantId", checkout, StringComparison.Ordinal);
        Assert.DoesNotContain("!checkoutProductIds.Contains(promotionLink.ProductId)", checkout, StringComparison.Ordinal);
        Assert.Contains("ThrowNonUniqueAffiliateReferralCode", checkout, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_REFERRAL_CODE_NOT_UNIQUE", checkout, StringComparison.Ordinal);
        Assert.Contains("account.Status == AffiliateStatus.Active", checkout, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_PROMOTION_LINK_MISMATCH", checkout, StringComparison.Ordinal);

        Assert.Contains("UX_AffiliateAccounts_ReferralCode_Global_CI_Active", migration, StringComparison.Ordinal);
        Assert.Contains("UX_AffiliatePromotionLinks_ReferralCode_Global_CI_Active", migration, StringComparison.Ordinal);
        Assert.Contains("UPPER(\"ReferralCode\")", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void StorefrontCheckoutShippingAndPayment_MustValidateAgainstSqlAuthority()
    {
        var source = File.ReadAllText(ApiPath("Services", "Checkout", "CheckoutService.cs"));
        var shippingValidation = ExtractMethodBody(source, "ValidateShippingMethodAsync");
        var paymentValidation = ExtractMethodBody(source, "ValidatePaymentMethodAsync");
        var shippingAuthorityLoad = ExtractMethodBody(source, "LoadShippingMethodAuthorityAsync");
        var paymentAuthorityLoad = ExtractMethodBody(source, "LoadPaymentMethodAuthorityAsync");
        var promotionFailurePolicy = ExtractMethodBody(source, "ApplyPromotionResultOrThrow");

        Assert.NotNull(shippingValidation);
        Assert.NotNull(paymentValidation);
        Assert.NotNull(shippingAuthorityLoad);
        Assert.NotNull(paymentAuthorityLoad);
        Assert.NotNull(promotionFailurePolicy);

        Assert.Contains("LoadShippingMethodAuthorityAsync", shippingValidation!, StringComparison.Ordinal);
        Assert.Contains("_dbContext.ShippingMethods", shippingAuthorityLoad!, StringComparison.Ordinal);
        Assert.Contains("MapAuthoritativeShippingMethodDocument", shippingValidation!, StringComparison.Ordinal);
        Assert.DoesNotContain("_shippingRead.GetByIdAsync", shippingValidation!, StringComparison.Ordinal);
        Assert.DoesNotContain("Shipping method validated from MongoDB", shippingValidation!, StringComparison.Ordinal);

        Assert.Contains("LoadPaymentMethodAuthorityAsync", paymentValidation!, StringComparison.Ordinal);
        Assert.Contains("_dbContext.TenantPaymentMethods", paymentAuthorityLoad!, StringComparison.Ordinal);
        Assert.Contains("EvaluatePaymentMethodConfiguration", paymentValidation!, StringComparison.Ordinal);
        Assert.Contains("MapAuthoritativePaymentMethodDocument", paymentValidation!, StringComparison.Ordinal);
        Assert.DoesNotContain("_paymentRead.GetByCodeAsync", paymentValidation!, StringComparison.Ordinal);
        Assert.DoesNotContain("Payment method validated from MongoDB", paymentValidation!, StringComparison.Ordinal);

        Assert.Contains("RecordPromotionFailClosed", promotionFailurePolicy!, StringComparison.Ordinal);
        Assert.Contains("throw CreatePromotionCalculationFailureException", promotionFailurePolicy!, StringComparison.Ordinal);
        Assert.DoesNotContain("_storefrontConfigCollection", promotionFailurePolicy!, StringComparison.Ordinal);
        Assert.DoesNotContain(".Find(c => c.TenantId == tenantId)", promotionFailurePolicy!, StringComparison.Ordinal);
    }

    [Fact]
    public void AffiliateReadinessGate_MustUseSqlStorefrontPaymentAndShippingAuthority()
    {
        var source = File.ReadAllText(InfrastructurePath("Services", "Affiliate", "AffiliateReadinessService.cs"));

        Assert.Contains("_dbContext.StoreConfigs", source, StringComparison.Ordinal);
        Assert.Contains("_dbContext.TenantPaymentMethods", source, StringComparison.Ordinal);
        Assert.Contains("_dbContext.ShippingMethods", source, StringComparison.Ordinal);
        Assert.Contains("PaymentMethodConfigurationValidator.Validate", source, StringComparison.Ordinal);
        Assert.Contains("PaymentMethodSecretsProtectorPurpose", source, StringComparison.Ordinal);
        Assert.Contains("method.IsEnabled", source, StringComparison.Ordinal);
        Assert.Contains("sm.IsActive", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MongoDbContext", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StorefrontConfigDocument", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_mongoDbContext.StorefrontConfigs", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_dbContext.PaymentMethods", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubAdminFinanceOverview_MustUseLedgerAsFinancialAuthority()
    {
        var source = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubFinanceReadService.cs"));
        var overview = ExtractMethodBody(source, "GetOverviewAsync");
        var ledgerSnapshot = ExtractMethodBody(source, "BuildLedgerAuthoritySnapshotAsync");
        var workflowSnapshot = ExtractMethodBody(source, "BuildOperationalWorkflowSnapshotAsync");

        Assert.NotNull(overview);
        Assert.NotNull(ledgerSnapshot);
        Assert.NotNull(workflowSnapshot);

        Assert.Contains("BuildLedgerAuthoritySnapshotAsync", overview!, StringComparison.Ordinal);
        Assert.DoesNotContain("CommerceHubPayments", overview!, StringComparison.Ordinal);
        Assert.DoesNotContain("CommerceHubSubOrders", overview!, StringComparison.Ordinal);
        Assert.DoesNotContain("CommerceHubRefunds", overview!, StringComparison.Ordinal);
        Assert.DoesNotContain("CommerceHubVendorEarnings", overview!, StringComparison.Ordinal);

        Assert.Contains("CommerceHubFinancialTransactions", ledgerSnapshot!, StringComparison.Ordinal);
        Assert.Contains("CommerceHubFinancialEntityType.Platform", ledgerSnapshot!, StringComparison.Ordinal);
        Assert.Contains("CommerceHubFinancialEntityType.Vendor", ledgerSnapshot!, StringComparison.Ordinal);
        Assert.Contains("CommerceHubFinancialBucket.Payable", ledgerSnapshot!, StringComparison.Ordinal);
        Assert.DoesNotContain("CommerceHubPayments", ledgerSnapshot!, StringComparison.Ordinal);
        Assert.DoesNotContain("CommerceHubSubOrders", ledgerSnapshot!, StringComparison.Ordinal);
        Assert.DoesNotContain("CommerceHubRefunds", ledgerSnapshot!, StringComparison.Ordinal);
        Assert.DoesNotContain("CommerceHubVendorEarnings", ledgerSnapshot!, StringComparison.Ordinal);

        Assert.Contains("Math.Min", workflowSnapshot!, StringComparison.Ordinal);
        Assert.Contains("ledgerVendorPayableBalance", workflowSnapshot!, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubVendorPayoutQueue_MustBlockCurrencyAuthorityConflicts()
    {
        var readService = File.ReadAllText(ApiPath("Services", "CommerceHub", "Finance", "CommerceHubVendorPayoutReadService.cs"));
        var superAdmin = File.ReadAllText(ApiPath("SuperAdmin", "Services", "SuperAdminPayoutsService.cs"));
        var constants = File.ReadAllText(CorePath("Constants", "PayoutRiskFlags.cs"));

        // Verify the constants class exists and contains all required risk flags
        Assert.Contains("PayoutRiskFlags", constants, StringComparison.Ordinal);
        Assert.Contains("OperationalCurrencyMismatch", constants, StringComparison.Ordinal);
        Assert.Contains("PayoutCurrencyMismatch", constants, StringComparison.Ordinal);
        Assert.Contains("RefundCurrencyMismatch", constants, StringComparison.Ordinal);
        Assert.Contains("PayoutBlockedByAuthorityConflict", constants, StringComparison.Ordinal);
        Assert.Contains("LedgerCurrencySplit", constants, StringComparison.Ordinal);
        Assert.Contains("IsAuthorityBlockingRisk", constants, StringComparison.Ordinal);

        // Verify the read service uses the constants
        Assert.Contains("LedgerPayableSummary", readService, StringComparison.Ordinal);
        Assert.Contains("HasCurrencySplit", readService, StringComparison.Ordinal);
        Assert.Contains("NormalizeCurrency", readService, StringComparison.Ordinal);
        Assert.Contains("CurrencyEquals", readService, StringComparison.Ordinal);
        Assert.Contains("PayoutRiskFlags.", readService, StringComparison.Ordinal);
        Assert.Contains("eligibleAmount = 0m", readService, StringComparison.Ordinal);

        // Verify the super admin service uses the constants
        Assert.Contains("IsPayoutAuthorityBlockingRisk", superAdmin, StringComparison.Ordinal);
        Assert.Contains("PayoutRiskFlags.", superAdmin, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_AUTHORITY_CONFLICT", superAdmin, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginPayout()", superAdmin, StringComparison.Ordinal);
    }

    private static string ApiPath(params string[] parts) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "PointOfSale.API", Path.Combine(parts)));

    private static string CorePath(params string[] parts) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "PointOfSale.Core", Path.Combine(parts)));

    private static string InfrastructurePath(params string[] parts) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "PointOfSale.Infrastructure", Path.Combine(parts)));

    private static string? ExtractMethodBody(string source, string methodName)
    {
        var methodPattern = methodName.Contains(' ', StringComparison.Ordinal)
            ? Regex.Escape(methodName) + @"\s*\("
            : $@"\b{Regex.Escape(methodName)}\s*\(";
        foreach (Match match in Regex.Matches(source, methodPattern))
        {
            var openParenIndex = source.IndexOf('(', match.Index);
            if (openParenIndex == -1)
            {
                continue;
            }

            var closeParenIndex = FindMatching(source, openParenIndex, '(', ')');
            if (closeParenIndex == -1)
            {
                continue;
            }

            var openBraceIndex = FindMethodOpeningBrace(source, closeParenIndex + 1);
            if (openBraceIndex == -1)
            {
                continue;
            }

            var closeBraceIndex = FindMatching(source, openBraceIndex, '{', '}');
            if (closeBraceIndex != -1)
            {
                return source.Substring(match.Index, closeBraceIndex - match.Index + 1);
            }
        }

        return null;
    }

    private static int FindMatching(string source, int openIndex, char open, char close)
    {
        var depth = 1;
        for (var i = openIndex + 1; i < source.Length; i++)
        {
            if (source[i] == open)
            {
                depth++;
            }
            else if (source[i] == close)
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private static int FindMethodOpeningBrace(string source, int startIndex)
    {
        for (var i = startIndex; i < source.Length; i++)
        {
            if (char.IsWhiteSpace(source[i]))
            {
                continue;
            }

            return source[i] == '{' ? i : -1;
        }

        return -1;
    }
}
