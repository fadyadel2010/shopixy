namespace PointOfSale.Tests.Architecture;

public sealed class ReplayRebuildChaosCertificationTests
{
    [Fact]
    public void PaymentCancellationOutbox_MustCollapseActiveWorkerOverlapWithProcessingLease()
    {
        var entity = File.ReadAllText(CorePath("Entities", "PaymentCancellationOutboxMessage.cs"));
        var projectionEntity = File.ReadAllText(CorePath("Entities", "MarketplaceProjectionOutboxMessage.cs"));
        var processor = File.ReadAllText(InfrastructurePath("Payment", "PaymentCancellationOutboxProcessor.cs"));
        var options = File.ReadAllText(InfrastructurePath("Payment", "PaymentCancellationOutboxOptions.cs"));
        var configuration = File.ReadAllText(InfrastructurePath("Data", "Configurations", "PaymentCancellationOutboxMessageConfiguration.cs"));
        var projectionConfiguration = File.ReadAllText(InfrastructurePath("Data", "Configurations", "MarketplaceProjectionOutboxMessageConfiguration.cs"));
        var projectionProcessor = File.ReadAllText(InfrastructurePath("Eventing", "MarketplaceProjectionOutboxProcessor.cs"));
        var dbContext = File.ReadAllText(InfrastructurePath("Data", "ApplicationDbContext.cs"));

        Assert.Contains("ClaimedUntilUtc", entity, StringComparison.Ordinal);
        Assert.Contains("TryClaim", entity, StringComparison.Ordinal);
        Assert.Contains("MarkProcessed", entity, StringComparison.Ordinal);
        Assert.Contains("ClearClaim", entity, StringComparison.Ordinal);
        Assert.Contains("RowVersion", entity, StringComparison.Ordinal);

        Assert.Contains("ProcessingLeaseSeconds", options, StringComparison.Ordinal);
        Assert.Contains("m.ClaimedUntilUtc == null || m.ClaimedUntilUtc <= nowUtc", processor, StringComparison.Ordinal);
        Assert.Contains("message.TryClaim(_options.ProcessingLeaseSeconds)", processor, StringComparison.Ordinal);
        Assert.Contains("ComputeStableAdvisoryLockKey(message.TenantId, message.OrderId)", processor, StringComparison.Ordinal);
        Assert.Contains("PaymentCancellationClaimOutcome.ConcurrencyConflict", processor, StringComparison.Ordinal);
        Assert.Contains("OperationalConvergenceReason.ActiveDeliveryOverlap", processor, StringComparison.Ordinal);
        Assert.Contains("builder.Property(e => e.RowVersion)", configuration, StringComparison.Ordinal);
        Assert.Contains("x.ProcessedAtUtc, x.IsFailed, x.NextRetryAtUtc, x.ClaimedUntilUtc", configuration, StringComparison.Ordinal);

        Assert.Contains("RowVersion", projectionEntity, StringComparison.Ordinal);
        Assert.Contains("builder.Property(x => x.RowVersion)", projectionConfiguration, StringComparison.Ordinal);
        Assert.Contains("OperationalConvergenceReason.ActiveDeliveryOverlap", projectionProcessor, StringComparison.Ordinal);
        Assert.Contains("OperationalConvergenceReason.DuplicateCompletedRace", projectionProcessor, StringComparison.Ordinal);
        Assert.Contains("Entries<MarketplaceProjectionOutboxMessage>()", dbContext, StringComparison.Ordinal);
        Assert.Contains("Entries<PaymentCancellationOutboxMessage>()", dbContext, StringComparison.Ordinal);
    }

    [Fact]
    public void StorefrontFooterReplay_MustFailClosedWhenTenantLineageIsLost()
    {
        var consumer = File.ReadAllText(InfrastructurePath("Consumers", "StorefrontFooterProjectionConsumer.cs"));

        Assert.Contains("ProjectionEventFence.TryAcquireAsync", consumer, StringComparison.Ordinal);
        Assert.Contains("TenantLineageAuthority.IsActiveTenantAsync", consumer, StringComparison.Ordinal);
        Assert.Contains("FailClosedFooterProjectionAsync", consumer, StringComparison.Ordinal);
        Assert.Contains("DeleteOneAsync", consumer, StringComparison.Ordinal);
        Assert.Contains("OperationalConvergenceTelemetry.RecordAuthorityFailure", consumer, StringComparison.Ordinal);
        Assert.Contains("OperationalConvergenceDecision.Deleted", consumer, StringComparison.Ordinal);
        Assert.Contains("OperationalConvergenceReason.TenantInactiveOrDeleted", consumer, StringComparison.Ordinal);
        Assert.Contains("await fence.CompleteAsync", consumer, StringComparison.Ordinal);

        var tenantLineageIndex = consumer.IndexOf("TenantLineageAuthority.IsActiveTenantAsync", StringComparison.Ordinal);
        var staleReplayIndex = consumer.IndexOf("ProjectionFreshnessHelper.IsExistingEventNewerOrEqual", StringComparison.Ordinal);
        Assert.True(
            tenantLineageIndex >= 0 && staleReplayIndex > tenantLineageIndex,
            "Tenant-lineage loss must fail closed before stale replay suppression can preserve an existing footer document.");
    }

    [Fact]
    public void RedisInvalidationReplay_MustCollapseDuplicateCacheSideEffects()
    {
        var consumer = File.ReadAllText(ApiPath("Consumers", "RedisInvalidationConsumer.cs"));

        Assert.Contains("ApplicationDbContext", consumer, StringComparison.Ordinal);
        Assert.Contains("ProjectionEventFence.TryAcquireAsync", consumer, StringComparison.Ordinal);
        Assert.Contains("RunIntegrationInvalidationWithFenceAsync", consumer, StringComparison.Ordinal);
        Assert.Contains("RunMarketplaceInvalidationWithFenceAsync", consumer, StringComparison.Ordinal);
        Assert.Contains("Duplicate or active Redis invalidation collapsed", consumer, StringComparison.Ordinal);
        Assert.Contains("Duplicate or active marketplace Redis invalidation collapsed", consumer, StringComparison.Ordinal);
        Assert.Contains("fence.CompleteAsync", consumer, StringComparison.Ordinal);
        Assert.Contains("MarketplaceReviewApprovedConsumerName", consumer, StringComparison.Ordinal);
        Assert.Contains("StorefrontFooterConsumerName", consumer, StringComparison.Ordinal);
    }

    [Fact]
    public void Pass7Documentation_MustRecordConvergenceGuaranteesDebtAndFreezeInvariants()
    {
        var findings = File.ReadAllText(DocsPath("architecture", "replay-rebuild-chaos-certification-findings.md"));
        var runbook = File.ReadAllText(DocsPath("operations", "operational-recovery-runbooks.md"));
        var primitives = File.ReadAllText(DocsPath("architecture", "dangerous-operational-primitives.md"));
        var architectureTruth = File.ReadAllText(ContextPath("ARCHITECTURE_TRUTH.md"));
        var codexRules = File.ReadAllText(ContextPath("CODEX_RULES.md"));

        foreach (var marker in new[]
        {
            "POS-ADMIN-CERTIFICATION-01 PASS 7",
            "Replay Storm Certification",
            "Rebuild Overlap Certification",
            "Worker Restart / Crash Recovery Certification",
            "Replay Amplification Protections",
            "Convergence Guarantees",
            "Remaining Chaos / Recovery Debt",
            "Regression Freeze Invariants",
            "Payment cancellation outbox"
        })
        {
            Assert.Contains(marker, findings, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("Payment-cancellation outbox replay must retain row leases", runbook, StringComparison.Ordinal);
        Assert.Contains("POS-ADMIN-CERTIFICATION-01 PASS 7 Replay/Rebuild/Chaos Freeze", primitives, StringComparison.Ordinal);
        Assert.Contains("PASS 7 Replay / Rebuild / Chaos Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS 7 Replay / Rebuild / Chaos Freeze", codexRules, StringComparison.Ordinal);
    }

    private static string ContextPath(string fileName) =>
        Path.Combine(DiscoverSolutionRoot(AppContext.BaseDirectory), ".ai-context", fileName);

    private static string CorePath(params string[] parts) =>
        Path.Combine([DiscoverSolutionRoot(AppContext.BaseDirectory), "PointOfSale.Core", .. parts]);

    private static string InfrastructurePath(params string[] parts) =>
        Path.Combine([DiscoverSolutionRoot(AppContext.BaseDirectory), "PointOfSale.Infrastructure", .. parts]);

    private static string ApiPath(params string[] parts) =>
        Path.Combine([DiscoverSolutionRoot(AppContext.BaseDirectory), "PointOfSale.API", .. parts]);

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
