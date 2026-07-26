using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.Tests.Infrastructure;

public sealed class OperationalConvergenceTelemetryTests : IDisposable
{
    private readonly MeterListener _listener;
    private readonly List<(string MetricName, long Value, KeyValuePair<string, object?>[] Tags)> _measurements = [];

    public OperationalConvergenceTelemetryTests()
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
    public void RecordReplayDecision_UsesBoundedClassificationTagsAndTenantBuckets()
    {
        var tenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        OperationalConvergenceTelemetry.RecordReplayDecision(
            OperationalConvergencePhase.CacheHydration,
            "Storefront Homepage Cache Hydration With Dynamic Text",
            OperationalConvergenceAuthority.Cache,
            OperationalConvergenceDecision.Suppressed,
            OperationalConvergenceReason.CacheHydrationRejected,
            tenantId);

        var measurement = FindMeasurement(
            "operational_semantic_replay_decision_total",
            "surface",
            "storefront_homepage_cache_hydration_with_dynamic_text");

        Assert.Equal("cache_hydration", Tag(measurement, "phase"));
        Assert.Equal("storefront_homepage_cache_hydration_with_dynamic_text", Tag(measurement, "surface"));
        Assert.Equal("cache", Tag(measurement, "authority"));
        Assert.Equal("suppressed", Tag(measurement, "decision"));
        Assert.Equal("cache_hydration_rejected", Tag(measurement, "reason"));
        Assert.StartsWith("bucket_", Tag(measurement, "tenant_bucket"));
    }

    [Fact]
    public async Task ProjectionEventFence_DuplicateCompletedEvent_RecordsReplayCollapseTelemetry()
    {
        var tenantId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var key = new ProjectionEventFenceKey("OperationalConvergenceTestConsumer", "ReplayEvent", Guid.NewGuid());

        await using var dbContext = CreateDbContext(tenantId);
        dbContext.ProcessedEvents.Add(ProcessedEvent.Create(
            eventId,
            key.ConsumerName,
            key.EventType,
            key.EntityId,
            tenantId,
            "test-correlation"));
        await dbContext.SaveChangesAsync();

        var fence = await ProjectionEventFence.TryAcquireAsync(
            dbContext,
            eventId,
            key,
            NullLogger.Instance,
            CancellationToken.None);

        Assert.Null(fence);
        var measurement = FindMeasurement(
            "operational_semantic_replay_decision_total",
            "surface",
            "operational_convergence_test_consumer");

        Assert.Equal("replay", Tag(measurement, "phase"));
        Assert.Equal("projection_event_fence", Tag(measurement, "authority"));
        Assert.Equal("collapsed", Tag(measurement, "decision"));
        Assert.Equal("duplicate_completed_event", Tag(measurement, "reason"));
    }

    [Fact]
    public void DerivativeProjectionMutationGuard_RecordsAcceptedAndRejectedDangerousPrimitiveGovernance()
    {
        var accepted = ValidMutation("OperationalGuardAccepted");
        var rejected = accepted with { BoundaryName = "OperationalGuardRejected", HasSqlAuthority = false };

        _ = DerivativeProjectionMutationGuard.RequireReviewedMutation(accepted);
        Assert.Throws<InvalidOperationException>(() =>
            DerivativeProjectionMutationGuard.RequireReviewedMutation(rejected));

        var acceptedMeasurement = FindMeasurement(
            "operational_dangerous_primitive_governance_total",
            "surface",
            "operational_guard_accepted");
        var rejectedMeasurement = FindMeasurement(
            "operational_dangerous_primitive_governance_total",
            "surface",
            "operational_guard_rejected");

        Assert.Equal("accepted", Tag(acceptedMeasurement, "decision"));
        Assert.Equal("dangerous_primitive_accepted", Tag(acceptedMeasurement, "reason"));
        Assert.Equal("rejected", Tag(rejectedMeasurement, "decision"));
        Assert.Equal("missing_sql_authority", Tag(rejectedMeasurement, "reason"));
    }

    [Fact]
    public void SqlTruthRebuildGuard_RecordsRebuildAuditabilityAndInvalidAuthorityRejection()
    {
        var tenantId = Guid.NewGuid();

        _ = SqlTruthRebuildGuard.RequireSqlAuthority(
            SqlTruthRebuildTarget.ProjectionRepair,
            tenantId,
            Guid.NewGuid(),
            SqlAuthorityOverlayKind.Order | SqlAuthorityOverlayKind.Shipping);

        Assert.Throws<InvalidOperationException>(() =>
            SqlTruthRebuildGuard.RequireSqlAuthority(
                SqlTruthRebuildTarget.Search,
                tenantId,
                Guid.NewGuid(),
                SqlAuthorityOverlayKind.None));

        var accepted = FindMeasurement(
            "operational_rebuild_diagnostic_total",
            "surface",
            "projection_repair");
        var rejected = FindMeasurement(
            "operational_rebuild_diagnostic_total",
            "surface",
            "search");

        Assert.Equal("authority_required", Tag(accepted, "decision"));
        Assert.Equal("order+shipping", Tag(accepted, "authority_kinds"));
        Assert.Equal("rejected", Tag(rejected, "decision"));
        Assert.Equal("missing_sql_authority", Tag(rejected, "reason"));
    }

    [Fact]
    public void AccountingLedgerAuthority_CollapsedCollectionReplay_RecordsReplayAndAuthorityDiagnostics()
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            OrderNumber = "OBS-REPLAY",
            Status = PointOfSale.Core.Enums.OrderStatus.Confirmed,
            PaymentStatus = PointOfSale.Core.Enums.PaymentStatus.Failed,
            Currency = "EGP",
            ShippingAddress = "Replay diagnostics",
            CreatedAt = DateTime.UtcNow
        };

        var decision = AccountingLedgerAuthority.EvaluateOrderCollectionPosting(order, "OnlinePayment");

        Assert.False(decision.ShouldPost);
        Assert.False(decision.ShouldRetry);

        var replay = FindMeasurement(
            "operational_semantic_replay_decision_total",
            "surface",
            "accounting_outbox");
        var authority = FindMeasurement(
            "operational_authority_failure_total",
            "surface",
            "accounting_outbox");

        Assert.Equal("collapsed", Tag(replay, "decision"));
        Assert.Equal("stale_collection_payment_truth", Tag(replay, "reason"));
        Assert.Equal("accounting_ledger", Tag(authority, "authority"));
        Assert.Equal("failed_closed", Tag(authority, "decision"));
    }

    public void Dispose()
    {
        _listener.Dispose();
    }

    private static ApplicationDbContext CreateDbContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"OperationalConvergenceTelemetry_{Guid.NewGuid():N}")
            .Options;

        return new ApplicationDbContext(options, new TestTenantContext(tenantId));
    }

    private static ReviewedDerivativeProjectionMutation ValidMutation(string boundaryName) =>
        new(
            DangerousProjectionPrimitive.MongoFullDocumentReplace,
            boundaryName,
            "StorefrontFooterDocument",
            Guid.NewGuid().ToString("N"),
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTime.UtcNow,
            3,
            SqlAuthorityOverlayKind.StorefrontFooter,
            HasSqlAuthority: true,
            HasFreshnessGuard: true,
            FailsClosedWhenAuthorityMissing: true,
            "Reviewed SQL recomposition before operational convergence test primitive.");

    private (string MetricName, long Value, KeyValuePair<string, object?>[] Tags) FindMeasurement(
        string metricName,
        string tagName,
        string tagValue)
    {
        return _measurements.Last(measurement =>
            measurement.MetricName == metricName &&
            string.Equals(Tag(measurement, tagName), tagValue, StringComparison.Ordinal));
    }

    private static string Tag(
        (string MetricName, long Value, KeyValuePair<string, object?>[] Tags) measurement,
        string key)
    {
        return measurement.Tags.Single(tag => tag.Key == key).Value?.ToString() ?? string.Empty;
    }
}
