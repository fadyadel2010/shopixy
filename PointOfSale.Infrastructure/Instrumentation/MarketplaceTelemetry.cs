using System.Diagnostics.Metrics;
using PointOfSale.Core.Abstractions;

namespace PointOfSale.Infrastructure.Instrumentation;

/// <summary>
/// Operational Telemetry for Marketplace.
/// Defines all metrics (Meters/Counters/Histograms) for runtime monitoring.
/// </summary>
public class MarketplaceTelemetry : IMarketplaceTelemetry
{
    public const string MeterName = "PointOfSale.Marketplace";
    private readonly Meter _meter;

    /// <summary>
    /// Normalizes a reason string to prevent high-cardinality metric dimensions.
    /// Ensures the reason is lowercase, alphanumeric with underscores/hyphens only,
    /// and truncated to a safe length. This prevents exception messages, stack traces,
    /// or dynamic values from creating unbounded time series.
    /// </summary>
    /// <param name="value">The raw string from caller</param>
    /// <returns>A normalized, bounded string safe for metric dimensions</returns>
    private static string NormalizeTag(string? value) =>
        OperationalTelemetryDimensions.NormalizeTagValue(value);

    // Chat Metrics
    public Counter<long> ChatMessagesSent { get; }
    public Counter<long> ChatRateLimitHits { get; }
    public Counter<long> ChatAuthFailures { get; }
    public Counter<long> ChatAttachmentUrlRejectedTotal { get; }
    public Counter<long> ChatAttachmentInvalidInputTotal { get; }
    public UpDownCounter<long> ChatSignalRConnections { get; }
    public Histogram<double> ChatSendLatency { get; }

    // Job Metrics (Drift Detection)
    public Counter<long> TrustDriftDetected { get; }
    public Counter<long> ElasticsearchDriftDetected { get; }

    // File Storage Metrics
    public UpDownCounter<long> UploadDiskUsageBytes { get; }

    // Event Bus Metrics
    public Counter<long> EventsPublished { get; }
    public Counter<long> EventsFailed { get; }

    // Phase MR-1: Marketing Review Metrics
    public Counter<long> MarketingReviewsCreated { get; }
    public Counter<long> MarketingReviewsPosVerified { get; }
    public Counter<long> MarketingReviewsSelfReported { get; }
    public Histogram<double> MarketingWeightedScoreAvg { get; }

    // Lock Contention
    public Histogram<double> ChatLockWaitDuration { get; }
    public Counter<long> AttachmentUnauthorizedAccessTotal { get; }
    public Counter<long> AttachmentAccessDeniedTotal { get; }
    public Counter<long> AttachmentServedTotal { get; }

    public MarketplaceTelemetry()
    {
        _meter = new Meter(MeterName, "1.0.0");

        // Chat
        ChatMessagesSent = _meter.CreateCounter<long>("marketplace.chat.messages_sent", "count", "Number of messages successfully sent");
        ChatRateLimitHits = _meter.CreateCounter<long>("marketplace.chat.rate_limit_hits", "count", "Number of rate limit exceeded events");
        ChatAuthFailures = _meter.CreateCounter<long>("marketplace.chat.auth_failures", "count", "Number of unauthorized chat attempts");
        ChatAttachmentUrlRejectedTotal = _meter.CreateCounter<long>("marketplace.chat.attachment_url_rejected_total", "count", "Rejected marketplace chat attachment references that contained an external or absolute URL.");
        ChatAttachmentInvalidInputTotal = _meter.CreateCounter<long>("marketplace.chat.attachment_invalid_input_total", "count", "Rejected marketplace chat attachment references that were not valid server-issued object keys.");
        ChatSignalRConnections = _meter.CreateUpDownCounter<long>("marketplace.chat.signalr_connections", "count", "Active SignalR connections");
        ChatSendLatency = _meter.CreateHistogram<double>("marketplace.chat.send_latency", "ms", "End-to-end latency of SendMessageAsync");
        ChatLockWaitDuration = _meter.CreateHistogram<double>("marketplace.chat.lock_wait_ms", "ms", "Database lock acquisition wait time (simulated or measured)");
        AttachmentUnauthorizedAccessTotal = _meter.CreateCounter<long>("attachment_unauthorized_access_total", "count", "Anonymous attachment access attempts rejected before marketplace authorization.");
        AttachmentAccessDeniedTotal = _meter.CreateCounter<long>("attachment_access_denied_total", "count", "Authenticated marketplace attachment access attempts denied because the caller was not in scope.");
        AttachmentServedTotal = _meter.CreateCounter<long>("attachment_served_total", "count", "Authorized marketplace attachments served through the secure access path.");

        // Drift
        TrustDriftDetected = _meter.CreateCounter<long>("marketplace.trust.drift_count", "count", "Trust aggregate inconsistencies detected");
        ElasticsearchDriftDetected = _meter.CreateCounter<long>("marketplace.elasticsearch.drift_count", "count", "Search index vs DB inconsistencies detected");

        // Storage (ObservableGauge for current state)
        _meter.CreateObservableGauge<long>("marketplace.upload.disk_usage_bytes", () => CurrentUploadDiskUsage, "bytes", "Current size of upload directory");

        // Events
        EventsPublished = _meter.CreateCounter<long>("marketplace.events.published", "count", "Marketplace events published");
        EventsFailed = _meter.CreateCounter<long>("marketplace.events.failed", "count", "Marketplace events failed to publish");

        // Phase MR-1: Marketing Reviews
        MarketingReviewsCreated = _meter.CreateCounter<long>("marketplace.marketing_reviews.created", "count", "Marketing reviews created");
        MarketingReviewsPosVerified = _meter.CreateCounter<long>("marketplace.marketing_reviews.pos_verified", "count", "POS-verified marketing reviews");
        MarketingReviewsSelfReported = _meter.CreateCounter<long>("marketplace.marketing_reviews.self_reported", "count", "Self-reported marketing reviews");
        MarketingWeightedScoreAvg = _meter.CreateHistogram<double>("marketplace.marketing_weighted_score.avg", "score", "Marketing weighted score distribution");
    }

    // State Tracking for Health Checks
    public long TrustDriftCount { get; private set; }
    public long ElasticsearchDriftCount { get; private set; }
    public long CurrentUploadDiskUsage { get; private set; }

    public void RecordTrustDrift(long count)
    {
        TrustDriftDetected.Add(count);
        TrustDriftCount += count;
    }

    public void RecordElasticsearchDrift(long count)
    {
        ElasticsearchDriftDetected.Add(count);
        ElasticsearchDriftCount += count;
    }

    public void UpdateDiskUsage(long bytes)
    {
        CurrentUploadDiskUsage = bytes;
    }

    public void RecordChatAttachmentUrlRejected(Guid tenantId)
    {
        ChatAttachmentUrlRejectedTotal.Add(
            1,
            OperationalTelemetryDimensions.CreateTenantBucketTag(tenantId));
    }

    public void RecordChatAttachmentInvalidInput(Guid tenantId, string reason)
    {
        ChatAttachmentInvalidInputTotal.Add(
            1,
            OperationalTelemetryDimensions.CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("reason", NormalizeTag(reason)));
    }

    public void RecordAttachmentUnauthorizedAccess()
    {
        AttachmentUnauthorizedAccessTotal.Add(1);
    }

    public void RecordAttachmentAccessDenied(Guid tenantId, string reason)
    {
        AttachmentAccessDeniedTotal.Add(
            1,
            OperationalTelemetryDimensions.CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("reason", NormalizeTag(reason)));
    }

    public void RecordAttachmentServed(Guid tenantId, string deliveryMode)
    {
        AttachmentServedTotal.Add(
            1,
            OperationalTelemetryDimensions.CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("delivery_mode", NormalizeTag(deliveryMode)));
    }
}
