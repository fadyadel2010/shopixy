using System.Diagnostics.Metrics;

namespace PointOfSale.Infrastructure.Instrumentation;

/// <summary>
/// AF-2.5: Observability instrumentation for the Affiliate Commission Pipeline.
///
/// Uses System.Diagnostics.Metrics (the .NET standard for OpenTelemetry-compatible
/// metrics). This aligns with the MarketplaceTelemetry pattern already in the codebase.
///
/// DESIGN CHOICE: System.Diagnostics.Metrics over Prometheus-net or AppInsights because:
/// - Zero external dependency (built into .NET 8+)
/// - Natively supported by OpenTelemetry, Prometheus exporters, and Azure Monitor
/// - Consistent with existing MarketplaceTelemetry
/// - Testable via MeterListener
///
/// ALL METRICS are tenant-tagged via KeyValuePair tags on record calls.
/// </summary>
public class AffiliateTelemetry
{
    public const string MeterName = "PointOfSale.Affiliate";
    private readonly Meter _meter;

    // ═══════════════════════════════════════════════════════════════════
    // COMMISSION PIPELINE COUNTERS
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Incremented every time a payment-capture consumer invokes RecordCommissionForOrderAsync.
    /// Tags: tenant_id, event_type (OnlinePayment, Cod, Wallet)
    /// </summary>
    public Counter<long> CommissionAttemptTotal { get; }

    /// <summary>
    /// Incremented for each OrderItem that gets a NEW ledger entry.
    /// Tags: tenant_id
    /// </summary>
    public Counter<long> CommissionSuccessTotal { get; }

    /// <summary>
    /// Incremented when commission is skipped (no attribution, inactive affiliate,
    /// unpaid item, no product policy).
    /// Tags: tenant_id, reason (no_attribution, inactive_affiliate, unpaid_item, no_policy, order_not_found)
    /// </summary>
    public Counter<long> CommissionSkippedTotal { get; }

    /// <summary>
    /// Incremented when recording fails with non-idempotent exception.
    /// Tags: tenant_id, event_type
    /// </summary>
    public Counter<long> CommissionFailedTotal { get; }

    /// <summary>
    /// Incremented when a reversal ledger entry is successfully created.
    /// Tags: tenant_id
    /// </summary>
    public Counter<long> RefundSuccessTotal { get; }

    /// <summary>
    /// Incremented when a duplicate ledger write is caught (23505 unique violation).
    /// Tags: tenant_id, entry_type (earned, reversed)
    /// </summary>
    public Counter<long> IdempotentSkipTotal { get; }

    // ═══════════════════════════════════════════════════════════════════
    // LAG DETECTION
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Histogram of event-to-consumer lag in seconds.
    /// lag = UtcNow - event.OccurredAtUtc
    /// Tags: tenant_id, event_type
    /// </summary>
    public Histogram<double> EventLagSeconds { get; }

    // ═══════════════════════════════════════════════════════════════════
    // DRIFT DETECTION (design-only gauge for future use)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Tracks detected inconsistencies between expected and actual commission state.
    /// For future drift-detection jobs (e.g., comparing Order.PaidAmount against ledger sums).
    /// Tags: tenant_id
    /// </summary>
    public Counter<long> CommissionDriftCount { get; }

    // ═══════════════════════════════════════════════════════════════════
    // AF-14C: ACCOUNTING HARDENING COUNTERS
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Incremented when a Paid event arrives before the Earned journal exists.
    /// Tags: tenant_id
    /// </summary>
    public Counter<long> OutOfOrderBlockedTotal { get; }

    /// <summary>
    /// Incremented when a partial payout attempt is rejected.
    /// Tags: tenant_id
    /// </summary>
    public Counter<long> PartialPayoutBlockedTotal { get; }

    /// <summary>
    /// Incremented when a posting is prevented due to negative payable guard.
    /// Tags: tenant_id
    /// </summary>
    public Counter<long> NegativePreventedTotal { get; }

    // ═══════════════════════════════════════════════════════════════════
    // PROCESSING DURATION
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Histogram of end-to-end consumer processing time in milliseconds.
    /// Tags: tenant_id, event_type
    /// </summary>
    public Histogram<double> ProcessingDurationMs { get; }

    /// <summary>
    /// Event lag threshold in seconds. Above this, a warning is logged.
    /// </summary>
    public const double LagWarningThresholdSeconds = 30.0;

    public AffiliateTelemetry()
    {
        _meter = new Meter(MeterName, "1.0.0");

        // Commission pipeline
        CommissionAttemptTotal = _meter.CreateCounter<long>(
            "affiliate.commission.attempt_total", "count",
            "Commission recording attempts from payment-capture events");

        CommissionSuccessTotal = _meter.CreateCounter<long>(
            "affiliate.commission.success_total", "count",
            "Successful commission ledger entries created");

        CommissionSkippedTotal = _meter.CreateCounter<long>(
            "affiliate.commission.skipped_total", "count",
            "Commission recordings skipped (no attribution, unpaid, etc.)");

        CommissionFailedTotal = _meter.CreateCounter<long>(
            "affiliate.commission.failed_total", "count",
            "Commission recording failures (non-idempotent)");

        RefundSuccessTotal = _meter.CreateCounter<long>(
            "affiliate.refund.success_total", "count",
            "Successful commission reversal entries created");

        IdempotentSkipTotal = _meter.CreateCounter<long>(
            "affiliate.idempotent.skip_total", "count",
            "Duplicate ledger writes caught by unique constraint");

        // Lag
        EventLagSeconds = _meter.CreateHistogram<double>(
            "affiliate.event.lag_seconds", "s",
            "Lag between event OccurredAtUtc and consumer processing time");

        // Drift
        CommissionDriftCount = _meter.CreateCounter<long>(
            "affiliate.commission.drift_count", "count",
            "Detected inconsistencies between expected and actual commission state");

        // AF-14C: Hardening counters
        OutOfOrderBlockedTotal = _meter.CreateCounter<long>(
            "affiliate.accounting.out_of_order_blocked", "count",
            "Paid events blocked because earned journal does not yet exist");

        PartialPayoutBlockedTotal = _meter.CreateCounter<long>(
            "affiliate.accounting.partial_payout_blocked", "count",
            "Partial payout attempts rejected by design enforcement");

        NegativePreventedTotal = _meter.CreateCounter<long>(
            "affiliate.accounting.negative_prevented", "count",
            "Postings prevented due to negative payable guard");

        // Processing
        ProcessingDurationMs = _meter.CreateHistogram<double>(
            "affiliate.processing.duration_ms", "ms",
            "End-to-end consumer processing duration");

        // AF-15: Attribution flow version tracking
        V2UsedTotal = _meter.CreateCounter<long>(
            "affiliate.v2.used", "count",
            "Attributions using the v2 PromotionLink flow");

        V1FallbackTotal = _meter.CreateCounter<long>(
            "affiliate.v1.fallback", "count",
            "Attributions falling back to v1 AffiliateAccountId flow");
    }

    // ═══════════════════════════════════════════════════════════════════
    // CONVENIENCE METHODS (for consistent tagging)
    // ═══════════════════════════════════════════════════════════════════

    public void RecordAttempt(Guid tenantId, string eventType)
        => CommissionAttemptTotal.Add(1,
            CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("event_type", NormalizeTag(eventType)));

    public void RecordSuccess(Guid tenantId, int itemCount)
        => CommissionSuccessTotal.Add(itemCount,
            CreateTenantBucketTag(tenantId));

    public void RecordSkipped(Guid tenantId, string reason)
        => CommissionSkippedTotal.Add(1,
            CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("reason", NormalizeTag(reason)));

    public void RecordFailed(Guid tenantId, string eventType)
        => CommissionFailedTotal.Add(1,
            CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("event_type", NormalizeTag(eventType)));

    public void RecordRefundSuccess(Guid tenantId)
        => RefundSuccessTotal.Add(1,
            CreateTenantBucketTag(tenantId));

    public void RecordIdempotentSkip(Guid tenantId, string entryType)
        => IdempotentSkipTotal.Add(1,
            CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("entry_type", NormalizeTag(entryType)));

    public void RecordLag(Guid tenantId, string eventType, double lagSeconds)
        => EventLagSeconds.Record(lagSeconds,
            CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("event_type", NormalizeTag(eventType)));

    public void RecordProcessingDuration(Guid tenantId, string eventType, double durationMs)
        => ProcessingDurationMs.Record(durationMs,
            CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("event_type", NormalizeTag(eventType)));

    public void RecordDrift(Guid tenantId)
        => CommissionDriftCount.Add(1,
            CreateTenantBucketTag(tenantId));

    // AF-14C: Hardening convenience methods
    public void RecordOutOfOrderBlocked(Guid tenantId)
        => OutOfOrderBlockedTotal.Add(1,
            CreateTenantBucketTag(tenantId));

    public void RecordPartialPayoutBlocked(Guid tenantId)
        => PartialPayoutBlockedTotal.Add(1,
            CreateTenantBucketTag(tenantId));

    public void RecordNegativePrevented(Guid tenantId)
        => NegativePreventedTotal.Add(1,
            CreateTenantBucketTag(tenantId));

    // ═══════════════════════════════════════════════════════════════════
    // AF-15: ATTRIBUTION FLOW VERSION TRACKING
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// AF-15: Incremented when a v2 PromotionLink flow is used for attribution.
    /// Tags: tenant_id
    /// </summary>
    public Counter<long> V2UsedTotal { get; }

    /// <summary>
    /// AF-15: Incremented when v1 AffiliateAccountId fallback is used.
    /// Tags: tenant_id
    /// </summary>
    public Counter<long> V1FallbackTotal { get; }

    public void RecordV2Used(Guid tenantId)
        => V2UsedTotal.Add(1,
            CreateTenantBucketTag(tenantId));

    public void RecordV1Fallback(Guid tenantId)
        => V1FallbackTotal.Add(1,
            CreateTenantBucketTag(tenantId));

    private static KeyValuePair<string, object?> CreateTenantBucketTag(Guid tenantId) =>
        OperationalTelemetryDimensions.CreateTenantBucketTag(tenantId);

    private static string NormalizeTag(string? value) =>
        OperationalTelemetryDimensions.NormalizeTagValue(value);
}
