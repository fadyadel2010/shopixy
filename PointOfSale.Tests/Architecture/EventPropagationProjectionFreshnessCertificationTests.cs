namespace PointOfSale.Tests.Architecture;

public sealed class EventPropagationProjectionFreshnessCertificationTests
{
    [Fact]
    public void Pass101ReadinessChecks_MustExposeProjectionLagThroughHealthAndConvergenceTelemetry()
    {
        var healthChecks = File.ReadAllText(InfrastructurePath("HealthChecks", "ProductionHealthChecks.cs"));
        var observabilityRegistration = File.ReadAllText(InfrastructurePath("Extensions", "ObservabilityServiceExtensions.cs"));
        var telemetry = File.ReadAllText(InfrastructurePath("Instrumentation", "OperationalConvergenceTelemetry.cs"));

        Assert.Contains("ProjectionOutboxHealthCheck", observabilityRegistration, StringComparison.Ordinal);
        Assert.Contains("MassTransitBusOutboxReadinessHealthCheck", observabilityRegistration, StringComparison.Ordinal);
        Assert.Contains("AccountingOutboxReadinessHealthCheck", observabilityRegistration, StringComparison.Ordinal);
        Assert.Contains("PaymentCancellationOutboxReadinessHealthCheck", observabilityRegistration, StringComparison.Ordinal);

        Assert.Contains("OperationalConvergenceTelemetry.RecordDriftDiagnostic", healthChecks, StringComparison.Ordinal);
        Assert.Contains("\"projection_outbox\"", healthChecks, StringComparison.Ordinal);
        Assert.Contains("\"mass_transit_bus_outbox\"", healthChecks, StringComparison.Ordinal);
        Assert.Contains("OperationalConvergenceReason.ProjectionLagDetected", healthChecks, StringComparison.Ordinal);
        Assert.Contains("OperationalConvergenceReason.ProjectionLagWithinTolerance", healthChecks, StringComparison.Ordinal);
        Assert.Contains("OperationalConvergenceReason.OutboxReadinessCheckFailed", healthChecks, StringComparison.Ordinal);

        Assert.Contains("ProjectionLagDetected", telemetry, StringComparison.Ordinal);
        Assert.Contains("ProjectionLagWithinTolerance", telemetry, StringComparison.Ordinal);
        Assert.Contains("OutboxReadinessCheckFailed", telemetry, StringComparison.Ordinal);
    }

    [Fact]
    public void Pass101OutboxAndOrderingControls_MustRemainVisibleOnReviewedBackbones()
    {
        var eventPublisher = File.ReadAllText(InfrastructurePath("EventBus", "MassTransitEventPublisher.cs"));
        var marketplaceOutbox = File.ReadAllText(InfrastructurePath("Eventing", "MarketplaceEventOutboxProcessor.cs"));
        var marketplaceProjectionOutbox = File.ReadAllText(InfrastructurePath("Eventing", "MarketplaceProjectionOutboxProcessor.cs"));
        var stockSyncOutbox = File.ReadAllText(InfrastructurePath("Eventing", "StockSyncOutboxProcessor.cs"));
        var accountingOutbox = File.ReadAllText(InfrastructurePath("Accounting", "AccountingOutboxProcessor.cs"));
        var paymentCancellationOutbox = File.ReadAllText(InfrastructurePath("Payment", "PaymentCancellationOutboxProcessor.cs"));
        var massTransitHealth = File.ReadAllText(InfrastructurePath("HealthChecks", "ProductionHealthChecks.cs"));

        Assert.Contains("EnsureTransactionalPublishBoundary", eventPublisher, StringComparison.Ordinal);
        Assert.Contains("EnsureAuthoritativeTenantScope", eventPublisher, StringComparison.Ordinal);
        Assert.Contains("ValidateAuthoritativeEventContract", eventPublisher, StringComparison.Ordinal);
        Assert.Contains("EnrichAuthoritativeOrderingMetadataAsync", eventPublisher, StringComparison.Ordinal);

        Assert.Contains("HasUnpublishedEarlierEntityEventAsync", marketplaceOutbox, StringComparison.Ordinal);
        Assert.Contains("TryClaim(claimDurationSeconds: _options.ClaimLeaseSeconds)", marketplaceOutbox, StringComparison.Ordinal);
        Assert.Contains("RecordNonTerminalProjectionFailure", marketplaceOutbox, StringComparison.Ordinal);

        Assert.Contains("CompletionAlreadyExistsAsync", marketplaceProjectionOutbox, StringComparison.Ordinal);
        Assert.Contains("DuplicateCompletedRace", marketplaceProjectionOutbox, StringComparison.Ordinal);
        Assert.Contains("ProcessedEvent.Create", marketplaceProjectionOutbox, StringComparison.Ordinal);

        Assert.Contains("HasUnpublishedEarlierVariantEventAsync", stockSyncOutbox, StringComparison.Ordinal);
        Assert.Contains("ResolveStockSyncSuppressionReasonAsync", stockSyncOutbox, StringComparison.Ordinal);
        Assert.Contains("RecordStockSyncDeliveryLag", stockSyncOutbox, StringComparison.Ordinal);

        Assert.Contains("HasUnprocessedEarlierAggregateVersionAsync", accountingOutbox, StringComparison.Ordinal);
        Assert.Contains("CriticalFinancialTruthEventTypes", accountingOutbox, StringComparison.Ordinal);
        Assert.Contains("TryClaim(_options.ProcessingLeaseSeconds)", accountingOutbox, StringComparison.Ordinal);

        Assert.Contains("PaymentCancellationClaimOutcome.ConcurrencyConflict", paymentCancellationOutbox, StringComparison.Ordinal);
        Assert.Contains("ComputeStableAdvisoryLockKey(message.TenantId, message.OrderId)", paymentCancellationOutbox, StringComparison.Ordinal);
        Assert.Contains("ClearClaim", paymentCancellationOutbox, StringComparison.Ordinal);

        Assert.Contains("message.SequenceNumber > state.LastSequenceNumber", massTransitHealth, StringComparison.Ordinal);
        Assert.Contains("BuildCriticalMassTransitOutboxMessageTypeFilter", massTransitHealth, StringComparison.Ordinal);
    }

    [Fact]
    public void Pass101Documentation_MustRecordFindingsGuaranteesHardeningAndDebt()
    {
        var findings = File.ReadAllText(DocsPath("architecture", "event-propagation-projection-freshness-certification-findings.md"));

        foreach (var marker in new[]
        {
            "POS-ADMIN-CERTIFICATION-01 PASS 10.1",
            "Event Flow Findings",
            "Outbox Findings",
            "Consumer Reliability Findings",
            "Idempotency Findings",
            "Ordering Findings",
            "Projection Freshness Findings",
            "Lag Detection Findings",
            "Replay & Catch-Up Findings",
            "Cross-System Propagation Findings",
            "Startup Recovery Findings",
            "Chaos Certification Findings",
            "Runtime Hardening Performed",
            "Monitoring Improvements",
            "Governance Updates",
            "Honest Remaining Debt"
        })
        {
            Assert.Contains(marker, findings, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string InfrastructurePath(params string[] parts) =>
        Path.Combine([DiscoverSolutionRoot(AppContext.BaseDirectory), "PointOfSale.Infrastructure", .. parts]);

    private static string DocsPath(params string[] parts) =>
        Path.Combine([DiscoverSolutionRoot(AppContext.BaseDirectory), "docs", .. parts]);

    private static string DiscoverSolutionRoot(string startPath)
    {
        var currentDir = new DirectoryInfo(startPath);

        while (currentDir != null)
        {
            if (currentDir.GetFiles("*.sln").Any() ||
                currentDir.GetDirectories("PointOfSale.API").Any())
            {
                return currentDir.FullName;
            }

            currentDir = currentDir.Parent;
        }

        throw new InvalidOperationException($"Could not discover solution root from '{startPath}'.");
    }
}
