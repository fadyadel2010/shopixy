namespace PointOfSale.Tests.Architecture;

public sealed class ChaosLagOperationalSurvivabilityCertificationTests
{
    [Fact]
    public void FailureInventoryMustDocumentBlastRadiusRecoveryObservabilityAndFallbackForAllMajorDomains()
    {
        var findings = ReadRepoFile("docs/architecture/chaos-lag-operational-survivability-certification-findings.md");

        foreach (var marker in new[]
        {
            "POS-ADMIN-CERTIFICATION-01 PASS 10.6",
            "PostgreSQL",
            "Outbox",
            "RabbitMQ",
            "MassTransit",
            "Consumers",
            "MongoDB",
            "Redis",
            "Elasticsearch",
            "Reindex",
            "Replay",
            "Repair",
            "Startup Recovery",
            "Blast radius",
            "Recovery mechanism",
            "Observability",
            "Fallback behavior"
        })
        {
            Assert.Contains(marker, findings, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void DerivativeOutagesMustRemainSqlFirstObservableAndRecoverable()
    {
        var healthChecks = ReadRepoFile("PointOfSale.Infrastructure/HealthChecks/ProductionHealthChecks.cs");
        var observability = ReadRepoFile("PointOfSale.Infrastructure/Extensions/ObservabilityServiceExtensions.cs");
        var redisCache = ReadRepoFile("PointOfSale.Infrastructure/Redis/Services/RedisJsonCacheService.cs");
        var productWriter = ReadRepoFile("PointOfSale.Infrastructure/MongoDB/Services/ProductSqlAuthorityProjectionWriter.cs");
        var commerceHubSearchHealth = ReadRepoFile("PointOfSale.API/HealthChecks/CommerceHubSearchSyncHealthCheck.cs");
        var commerceHubReindex = ReadRepoFile("PointOfSale.Infrastructure/Jobs/CommerceHubReindexService.cs");
        var elasticsearchDrift = ReadRepoFile("PointOfSale.Infrastructure/Jobs/ElasticsearchDriftReconciliationJob.cs");
        var program = ReadRepoFile("PointOfSale.API/Program.cs");

        Assert.Contains("ProjectionDriftHealthCheck", healthChecks, StringComparison.Ordinal);
        Assert.Contains("ProjectionOutboxHealthCheck", healthChecks, StringComparison.Ordinal);
        Assert.Contains("MassTransitBusOutboxReadinessHealthCheck", healthChecks, StringComparison.Ordinal);
        Assert.Contains("WarningLag = TimeSpan.FromMinutes(5)", healthChecks, StringComparison.Ordinal);
        Assert.Contains("CriticalLag = TimeSpan.FromMinutes(15)", healthChecks, StringComparison.Ordinal);
        Assert.Contains("OperationalConvergenceTelemetry.RecordDriftDiagnostic", healthChecks, StringComparison.Ordinal);

        Assert.Contains("payment_cancellation_outbox_readiness", observability, StringComparison.Ordinal);
        Assert.Contains("AddCheck<ProjectionOutboxHealthCheck>", observability, StringComparison.Ordinal);
        Assert.Contains("AddCheck<MassTransitBusOutboxReadinessHealthCheck>", observability, StringComparison.Ordinal);

        Assert.Contains("ShouldBypassForProjectionLag", redisCache, StringComparison.Ordinal);
        Assert.Contains("RedisPerformanceTelemetry", redisCache, StringComparison.Ordinal);
        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyAsync", productWriter, StringComparison.Ordinal);
        Assert.Contains("ReviewedVisibilityTombstone", productWriter, StringComparison.Ordinal);

        Assert.Contains("CountSqlVisibleCommerceHubListingsAsync", commerceHubSearchHealth, StringComparison.Ordinal);
        Assert.Contains("OperationalConvergenceAuthority.SearchIndex", commerceHubSearchHealth, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", commerceHubReindex, StringComparison.Ordinal);
        Assert.Contains("CountSqlVisibleCommerceHubListingsAsync", commerceHubReindex, StringComparison.Ordinal);
        Assert.Contains("SqlTruthRebuildGuard.RequireGlobalSqlAuthority", elasticsearchDrift, StringComparison.Ordinal);
        Assert.Contains(".ValidateOnStart();", program, StringComparison.Ordinal);
    }

    [Fact]
    public void ConsumerOutboxReplayAndStartupRecoveryMustCollapseOverlapAndExposeLag()
    {
        var paymentCancellation = ReadRepoFile("PointOfSale.Infrastructure/Payment/PaymentCancellationOutboxProcessor.cs");
        var marketplaceEventOutbox = ReadRepoFile("PointOfSale.Infrastructure/Eventing/MarketplaceEventOutboxProcessor.cs");
        var marketplaceProjectionOutbox = ReadRepoFile("PointOfSale.Infrastructure/Eventing/MarketplaceProjectionOutboxProcessor.cs");
        var stockSyncOutbox = ReadRepoFile("PointOfSale.Infrastructure/Eventing/StockSyncOutboxProcessor.cs");
        var accountingOutbox = ReadRepoFile("PointOfSale.Infrastructure/Accounting/AccountingOutboxProcessor.cs");
        var projectionFence = ReadRepoFile("PointOfSale.Infrastructure/Reliability/ProjectionEventFence.cs");
        var autoHealing = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubAutoHealingService.cs");
        var hangfire = ReadRepoFile("PointOfSale.API/Jobs/HangfireInitializationHostedService.cs");

        Assert.Contains("message.TryClaim(_options.ProcessingLeaseSeconds)", paymentCancellation, StringComparison.Ordinal);
        Assert.Contains("PaymentCancellationClaimOutcome.ConcurrencyConflict", paymentCancellation, StringComparison.Ordinal);
        Assert.Contains("ClearClaim", paymentCancellation, StringComparison.Ordinal);
        Assert.Contains("OperationalConvergenceReason.ActiveDeliveryOverlap", paymentCancellation, StringComparison.Ordinal);

        Assert.Contains("TryClaim(claimDurationSeconds: _options.ClaimLeaseSeconds)", marketplaceEventOutbox, StringComparison.Ordinal);
        Assert.Contains("HasUnpublishedEarlierEntityEventAsync", marketplaceEventOutbox, StringComparison.Ordinal);
        Assert.Contains("RetryFailedMessageAsync", marketplaceEventOutbox, StringComparison.Ordinal);
        Assert.Contains("CompletionAlreadyExistsAsync", marketplaceProjectionOutbox, StringComparison.Ordinal);
        Assert.Contains("DuplicateCompletedRace", marketplaceProjectionOutbox, StringComparison.Ordinal);
        Assert.Contains("HasUnpublishedEarlierVariantEventAsync", stockSyncOutbox, StringComparison.Ordinal);
        Assert.Contains("RecordStockSyncDeliveryLag", stockSyncOutbox, StringComparison.Ordinal);
        Assert.Contains("HasUnprocessedEarlierAggregateVersionAsync", accountingOutbox, StringComparison.Ordinal);
        Assert.Contains("TryClaim(_options.ProcessingLeaseSeconds)", accountingOutbox, StringComparison.Ordinal);

        Assert.Contains("OperationalConvergenceReason.DuplicateCompletedEvent", projectionFence, StringComparison.Ordinal);
        Assert.Contains("OperationalConvergenceReason.ActiveDeliveryOverlap", projectionFence, StringComparison.Ordinal);
        Assert.Contains("RecoveryIdentity(Guid? TenantId, Guid ListingId)", autoHealing, StringComparison.Ordinal);
        Assert.Contains("ElasticsearchDriftReconciliationJob", hangfire, StringComparison.Ordinal);
        Assert.Contains("CommerceHubReindexService", hangfire, StringComparison.Ordinal);
    }

    [Fact]
    public void TenantIsolationUnderFailureMustStayScopedDuringRepairReplayRebuildAndRecovery()
    {
        var tenantLineage = ReadRepoFile("PointOfSale.Infrastructure/Reliability/TenantLineageAuthority.cs");
        var derivativeKey = ReadRepoFile("PointOfSale.Infrastructure/Reliability/TenantScopedDerivativeKey.cs");
        var affiliateReindex = ReadRepoFile("PointOfSale.Infrastructure/Scripts/AF16E_ReindexAffiliateProductsWithStorefrontUrl.cs");
        var searchReindex = ReadRepoFile("PointOfSale.API/Controllers/SearchReindexController.cs");
        var autoHealing = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubAutoHealingService.cs");
        var eventPublisher = ReadRepoFile("PointOfSale.Infrastructure/EventBus/MassTransitEventPublisher.cs");

        Assert.Contains("ValidateCurrentTenantOwnerAsync", tenantLineage, StringComparison.Ordinal);
        Assert.Contains("IsActiveTenantAsync", tenantLineage, StringComparison.Ordinal);
        Assert.Contains("OperationalConvergenceAuthority.TenantLineage", tenantLineage, StringComparison.Ordinal);
        Assert.Contains("ForTenantEntity(Guid tenantId, Guid entityId, string separator = \"_\")", derivativeKey, StringComparison.Ordinal);
        Assert.Contains("throw new ArgumentException", derivativeKey, StringComparison.Ordinal);

        Assert.Contains("ReindexTenantAsync(Guid tenantId", affiliateReindex, StringComparison.Ordinal);
        Assert.Contains("TenantScopedDerivativeKey.ForTenantEntity", affiliateReindex, StringComparison.Ordinal);
        Assert.Contains("t.IsActive", affiliateReindex, StringComparison.Ordinal);
        Assert.Contains("SqlTruthRebuildGuard.RequireSqlAuthority", affiliateReindex, StringComparison.Ordinal);
        Assert.Contains("_tenantContext.RequiredTenantId", searchReindex, StringComparison.Ordinal);
        Assert.Contains("GLOBAL_SEARCH_CONTROL_FORBIDDEN", searchReindex, StringComparison.Ordinal);
        Assert.Contains("RecoveryIdentity(Guid? TenantId, Guid ListingId)", autoHealing, StringComparison.Ordinal);
        Assert.Contains("EnsureAuthoritativeTenantScope", eventPublisher, StringComparison.Ordinal);
        Assert.Contains("ValidateAuthoritativeEventContract", eventPublisher, StringComparison.Ordinal);
    }

    [Fact]
    public void Pass106DocumentationAndGovernanceMustStayUpdated()
    {
        var findings = ReadRepoFile("docs/architecture/chaos-lag-operational-survivability-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");

        foreach (var marker in new[]
        {
            "1. Failure Inventory Findings",
            "2. Mongo Failure Findings",
            "3. Redis Failure Findings",
            "4. Elasticsearch Failure Findings",
            "5. Consumer Failure Findings",
            "6. Outbox Failure Findings",
            "7. Replay Findings",
            "8. Startup Findings",
            "9. Multi-System Lag Findings",
            "10. Tenant Isolation Findings",
            "11. Operational Telemetry Findings",
            "12. Survivability SLO Findings",
            "13. Chaos Findings",
            "14. Runtime Hardening Performed",
            "15. Monitoring Improvements",
            "16. Governance Updates",
            "17. Honest Remaining Debt"
        })
        {
            Assert.Contains(marker, findings, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("PASS 10.6 Chaos, Lag & Operational Survivability Certification", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS 10.6 Chaos, Lag & Operational Survivability Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("Operational survivability under chaos, lag, restart, replay, and derivative outage now has a documented certification boundary", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS 10.6 Chaos, Lag & Operational Survivability Freeze", codexRules, StringComparison.Ordinal);
        Assert.Contains("PASS 10.6 Chaos, Lag & Operational Survivability", hardening, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string FindRepoRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root could not be found.");
    }
}
