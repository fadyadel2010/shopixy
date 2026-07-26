namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubRecoveryOperationalResilienceArchitectureCertificationTests
{
    [Fact]
    public void RuntimeRecoveryPaths_MustReenterSqlAuthorityAndRemainDerivative()
    {
        var program = ReadRepoFile("PointOfSale.API/Program.cs");
        var eventOutbox = ReadRepoFile("PointOfSale.Infrastructure/Eventing/MarketplaceEventOutboxProcessor.cs");
        var projectionOutbox = ReadRepoFile("PointOfSale.Infrastructure/Eventing/MarketplaceProjectionOutboxProcessor.cs");
        var commerceHubProjectionRepair = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubOrderProjectionRepairService.cs");
        var commerceHubProjectionReconciliation = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubProjectionReconciliationService.cs");
        var storefrontProjectionRepair = ReadRepoFile("PointOfSale.API/Services/Storefront/StorefrontOrderProjectionRepairService.cs");
        var storefrontProjectionReconciliation = ReadRepoFile("PointOfSale.API/Services/Storefront/StorefrontOrderProjectionReconciliationService.cs");
        var searchOverlay = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchSqlAuthorityOverlay.cs");
        var reindex = ReadRepoFile("PointOfSale.Infrastructure/Jobs/CommerceHubReindexService.cs");
        var redisCache = ReadRepoFile("PointOfSale.Infrastructure/Redis/Services/RedisJsonCacheService.cs");
        var paymentIntentRecovery = ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutPaymentIntentRecoveryService.cs");
        var paymentReconciliation = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubFinanceReconciliationJob.cs");
        var payoutReconciliation = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPayoutReconciliationHostedService.cs");
        var refundHostedRecovery = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubRefundRecoveryHostedService.cs");
        var refundRecovery = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubRefundService.cs");
        var refundExecutionRecovery = ReadRepoFile("PointOfSale.API/Services/CommerceHub/RefundExecutionService.cs");
        var shipmentRecovery = ReadRepoFile("PointOfSale.API/Services/Shipping/ShipmentAuthorityRecoveryHostedService.cs");
        var hangfireInitialization = ReadRepoFile("PointOfSale.API/Jobs/HangfireInitializationHostedService.cs");
        var abandonedCartRecovery = ReadRepoFile("PointOfSale.Infrastructure/Consumers/AbandonedCart/RecoveryOrchestrationConsumer.cs");

        foreach (var marker in new[]
        {
            "CommerceHubUnresolvedPaymentReconciliationHostedService",
            "CommerceHubPayoutReconciliationHostedService",
            "CommerceHubRefundRecoveryHostedService",
            "CommerceHubProjectionReconciliationService",
            "StorefrontOrderProjectionReconciliationService",
            "CheckoutPaymentIntentRecoveryHostedService",
            "ShipmentAuthorityRecoveryHostedService",
            "MongoDbIndexInitializer",
            "CommerceHubMongoDbIndexInitializer",
            "CacheWarmupService",
            "UseMessageRetry"
        })
        {
            Assert.Contains(marker, program, StringComparison.Ordinal);
        }

        Assert.Contains("RetryFailedMessageAsync", eventOutbox, StringComparison.Ordinal);
        Assert.Contains("ResetForRetry", eventOutbox, StringComparison.Ordinal);
        Assert.Contains("HasUnpublishedEarlierEntityEventAsync", eventOutbox, StringComparison.Ordinal);
        Assert.Contains("pg_advisory_xact_lock", eventOutbox, StringComparison.Ordinal);

        Assert.Contains("ProcessedEvent", projectionOutbox, StringComparison.Ordinal);
        Assert.Contains("CompletionAlreadyExistsAsync", projectionOutbox, StringComparison.Ordinal);
        Assert.Contains("DeleteOutboxResidueAfterDuplicateCompletionAsync", projectionOutbox, StringComparison.Ordinal);

        foreach (var projectionRepair in new[] { commerceHubProjectionRepair, commerceHubProjectionReconciliation })
        {
            Assert.Contains("BuildProjectionRecoveryFilter", projectionRepair, StringComparison.Ordinal);
            Assert.Contains("ProjectionProvesSqlAuthority", projectionRepair, StringComparison.Ordinal);
            Assert.Contains("CommerceHubBoundaryMutationScope.BeginProjection()", projectionRepair, StringComparison.Ordinal);
        }

        Assert.Contains("CommerceHubProjectionRepairException", commerceHubProjectionRepair, StringComparison.Ordinal);

        foreach (var storefrontProjection in new[] { storefrontProjectionRepair, storefrontProjectionReconciliation })
        {
            Assert.Contains("IgnoreQueryFilters", storefrontProjection, StringComparison.Ordinal);
            Assert.Contains("OrderSource.Ecommerce", storefrontProjection, StringComparison.Ordinal);
            Assert.Contains("BuildFreshnessGuard", storefrontProjection, StringComparison.Ordinal);
            Assert.Contains("ReplaceOneAsync", storefrontProjection, StringComparison.Ordinal);
        }

        Assert.Contains("BuildAuthorityQuery", storefrontProjectionRepair, StringComparison.Ordinal);

        Assert.Contains("SqlTruthRebuildGuard.RequireSqlAuthority", searchOverlay, StringComparison.Ordinal);
        Assert.Contains("SqlAuthorityOverlayKind.CommerceHubListing", searchOverlay, StringComparison.Ordinal);
        Assert.Contains("SqlAuthorityOverlayKind.Tombstone", searchOverlay, StringComparison.Ordinal);
        Assert.Contains("FinancialCurrencyGuard.PlatformBaseCurrencyCode", searchOverlay, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", reindex, StringComparison.Ordinal);
        Assert.Contains("LoadSqlVisibleListingIdentitiesAsync", reindex, StringComparison.Ordinal);
        Assert.Contains("DeleteStaleSearchDocumentsAsync", reindex, StringComparison.Ordinal);
        Assert.Contains("ReconcileMissingSqlListingsAsync", reindex, StringComparison.Ordinal);

        Assert.Contains("CacheEnvelope", redisCache, StringComparison.Ordinal);
        Assert.Contains("ShouldBypassForProjectionLag", redisCache, StringComparison.Ordinal);
        Assert.Contains("KeyDeleteAsync", redisCache, StringComparison.Ordinal);

        Assert.Contains("EnsureStorefrontOrderReservationsActiveAsync", paymentIntentRecovery, StringComparison.Ordinal);
        Assert.Contains("CreateProviderAccuratePaymentIntentAsync", paymentIntentRecovery, StringComparison.Ordinal);
        Assert.Contains("BuildRecoveryIdempotencyKey", paymentIntentRecovery, StringComparison.Ordinal);
        Assert.Contains("TransitionCheckoutState(CheckoutState.Expired)", paymentIntentRecovery, StringComparison.Ordinal);
        Assert.Contains("OrderStatusSaveChangesInterceptor.BeginAuthorizedContext", paymentIntentRecovery, StringComparison.Ordinal);

        Assert.Contains("ResolvePaidPaymentForWebhookAsync", paymentReconciliation, StringComparison.Ordinal);
        Assert.Contains("PaymentAmountMatches", paymentReconciliation, StringComparison.Ordinal);
        Assert.Contains("FinancialCurrencyGuard.RequireEgpCurrency", paymentReconciliation, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginOrderLifecycle()", paymentReconciliation, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", paymentReconciliation, StringComparison.Ordinal);

        Assert.Contains("CommerceHubBoundaryMutationScope.BeginPayout()", payoutReconciliation, StringComparison.Ordinal);
        Assert.Contains("FinancialCurrencyGuard.RequireEgpCurrency", payoutReconciliation, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPayoutStatus.ReconciliationRequired", payoutReconciliation, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", payoutReconciliation, StringComparison.Ordinal);

        Assert.Contains("ReconcileProcessingRefundsAsync", refundHostedRecovery, StringComparison.Ordinal);
        Assert.Contains("RecoverReconciliationRequiredRefundsAsync", refundHostedRecovery, StringComparison.Ordinal);
        Assert.Contains("BuildRefundRecoveryLockKey", refundRecovery, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginRefund()", refundRecovery, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginFinancialWorkflow()", refundRecovery, StringComparison.Ordinal);
        Assert.Contains("RecoverReconciliationRequiredRefundsAsync", refundExecutionRecovery, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginRefund()", refundExecutionRecovery, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginFinancialWorkflow()", refundExecutionRecovery, StringComparison.Ordinal);
        Assert.Contains("RequireConsistentExecutionCurrency", refundExecutionRecovery, StringComparison.Ordinal);

        Assert.Contains("tenantContext.SetTenantId", shipmentRecovery, StringComparison.Ordinal);
        Assert.Contains("CreateShipmentAsync", shipmentRecovery, StringComparison.Ordinal);
        Assert.Contains("ExecuteCarrierForExistingShipmentAsync", shipmentRecovery, StringComparison.Ordinal);
        Assert.Contains("ReconcileShipmentAuthorityAsync", shipmentRecovery, StringComparison.Ordinal);

        Assert.Contains("RecurringJob.AddOrUpdate", hangfireInitialization, StringComparison.Ordinal);
        Assert.Contains("TrackingSyncJob", hangfireInitialization, StringComparison.Ordinal);
        Assert.Contains("CommerceHubReindexService", hangfireInitialization, StringComparison.Ordinal);

        Assert.Contains("TenantLineageAuthority.IsActiveTenantAsync", abandonedCartRecovery, StringComparison.Ordinal);
        Assert.Contains("TryMarkAsPendingAsync", abandonedCartRecovery, StringComparison.Ordinal);
        Assert.Contains("ConfirmTriggerSuccessAsync", abandonedCartRecovery, StringComparison.Ordinal);
        Assert.Contains("RecordTriggerFailureAsync", abandonedCartRecovery, StringComparison.Ordinal);
    }

    [Fact]
    public void DocumentationFreeze_MustRecordArchFiveRecoveryOperationalCertification()
    {
        var findings = ReadRepoFile("docs/architecture/commercehub-recovery-operational-resilience-review-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        foreach (var marker in new[]
        {
            "ARCH-5 CommerceHub Recovery, Replay & Operational Resilience Review",
            "Recovery Findings",
            "Replay Findings",
            "Retry Findings",
            "Repair Findings",
            "Rebuild Findings",
            "Projection Recovery Findings",
            "Mongo Recovery Findings",
            "Redis Recovery Findings",
            "Elasticsearch Recovery Findings",
            "Outbox Recovery Findings",
            "RabbitMQ Recovery Findings",
            "Hosted Service Findings",
            "Background Worker Findings",
            "Failure Recovery Findings",
            "Lifecycle Findings",
            "Concurrency Findings",
            "Tenant Findings",
            "Operational Resilience Findings",
            "Architectural Risks",
            "Runtime Hardening Performed",
            "Exact Runtime Files Modified",
            "Regression Freezes Added",
            "Architecture Updates"
        })
        {
            Assert.Contains(marker, findings, StringComparison.Ordinal);
        }

        Assert.Contains("PASS ARCH-5 CommerceHub Recovery, Replay & Operational Resilience Review", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS ARCH-5 CommerceHub Recovery Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS ARCH-5 recovery runtime certifiable", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS ARCH-5 CommerceHub Recovery, Replay & Operational Resilience Review", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS ARCH-5 CommerceHub Recovery Operational Resilience Architecture Freeze", codexRules, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
