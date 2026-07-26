using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Infrastructure.Instrumentation;

namespace PointOfSale.Infrastructure.Accounting;

/// <summary>
/// Bounded OpenTelemetry-compatible metrics for accounting operations.
///
/// This keeps long-lived operational telemetry exportable while avoiding
/// unbounded tenant/gateway/error cardinality in singleton in-memory state.
/// </summary>
public sealed class AccountingMetrics : IAccountingMetrics, IDisposable
{
    public const string MeterName = "PointOfSale.Accounting";

    private readonly ILogger<AccountingMetrics> _logger;
    private readonly Meter _meter;
    private readonly Counter<long> _postedTotal;
    private readonly Counter<long> _failureTotal;
    private readonly Counter<long> _idempotentTotal;
    private readonly Counter<long> _outboxDeadLetteredTotal;
    private readonly Counter<long> _outboxCleanupTotal;
    private readonly Histogram<double> _postingLatencyMilliseconds;
    private readonly ConcurrentDictionary<string, long> _counters = new();
    private int _outboxPendingCount;
    private int _outboxFailedCount;

    public AccountingMetrics(ILogger<AccountingMetrics> logger)
    {
        _logger = logger;
        _meter = new Meter(MeterName, "1.0.0");
        _postedTotal = _meter.CreateCounter<long>(
            "accounting.posted_total",
            unit: "count",
            description: "Accounting postings completed on the reviewed outbox boundary.");
        _failureTotal = _meter.CreateCounter<long>(
            "accounting.failure_total",
            unit: "count",
            description: "Accounting outbox posting failures grouped by bounded operation and error type.");
        _idempotentTotal = _meter.CreateCounter<long>(
            "accounting.idempotent_total",
            unit: "count",
            description: "Duplicate/idempotent accounting outbox deliveries collapsed safely.");
        _outboxDeadLetteredTotal = _meter.CreateCounter<long>(
            "accounting.outbox.dead_lettered_total",
            unit: "count",
            description: "Accounting outbox rows that exhausted retries and require operator recovery.");
        _outboxCleanupTotal = _meter.CreateCounter<long>(
            "accounting.outbox.cleanup_total",
            unit: "count",
            description: "Accounting outbox cleanup deletions grouped by bounded row class.");
        _postingLatencyMilliseconds = _meter.CreateHistogram<double>(
            "accounting.posting_latency_ms",
            unit: "ms",
            description: "Accounting posting latency grouped by bounded operation type.");

        _meter.CreateObservableGauge(
            "accounting.outbox.pending",
            () => _outboxPendingCount,
            unit: "count",
            description: "Current pending accounting outbox rows.");
        _meter.CreateObservableGauge(
            "accounting.outbox.failed",
            () => _outboxFailedCount,
            unit: "count",
            description: "Current failed accounting outbox rows awaiting operator recovery.");
    }

    public void IncrementPosSalesPosted(Guid tenantId)
    {
        RecordPosted(tenantId, "pos_sale");
        _logger.LogDebug("Metric: pos_sales_posted_total for tenant {TenantId}", tenantId);
    }

    public void IncrementPosPurchasesPosted(Guid tenantId)
    {
        RecordPosted(tenantId, "pos_purchase");
        _logger.LogDebug("Metric: pos_purchases_posted_total for tenant {TenantId}", tenantId);
    }

    public void IncrementEcommercePaymentsPosted(Guid tenantId, string gateway)
    {
        var normalizedGateway = Normalize(gateway);
        RecordPosted(
            tenantId,
            "online_payment",
            new KeyValuePair<string, object?>("gateway", normalizedGateway),
            normalizedGateway);
        _logger.LogDebug(
            "Metric: ecommerce_payments_posted_total for tenant {TenantId}, gateway {Gateway}",
            tenantId,
            gateway);
    }

    public void IncrementRefundsPosted(Guid tenantId)
    {
        RecordPosted(tenantId, "refund");
        _logger.LogDebug("Metric: refunds_posted_total for tenant {TenantId}", tenantId);
    }

    public void IncrementSettlementsPosted(Guid tenantId, string type)
    {
        var normalizedType = Normalize(type);
        RecordPosted(
            tenantId,
            "settlement",
            new KeyValuePair<string, object?>("settlement_type", normalizedType),
            normalizedType);
        _logger.LogDebug(
            "Metric: settlements_posted_total for tenant {TenantId}, type {Type}",
            tenantId,
            type);
    }

    public void IncrementAccountingFailures(Guid tenantId, string operationType, string? errorType = null)
    {
        var tenantBucket = TenantBucket(tenantId);
        var normalizedOperationType = Normalize(operationType);
        var normalizedErrorType = Normalize(errorType);
        IncrementCounter(BuildCounterKey("accounting.failure_total", tenantBucket, normalizedOperationType, normalizedErrorType));
        _failureTotal.Add(
            1,
            CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("operation_type", normalizedOperationType),
            new KeyValuePair<string, object?>("error_type", normalizedErrorType));
        _logger.LogWarning(
            "Metric: accounting_failures_total for tenant {TenantId}, operation {OperationType}, error {ErrorType}",
            tenantId,
            operationType,
            errorType ?? "unknown");
    }

    public void SetOutboxPendingCount(int count)
    {
        _outboxPendingCount = count;

        if (count > 100)
        {
            _logger.LogWarning(
                "ALERT: outbox_pending_count={Count} exceeds threshold of 100",
                count);
        }
        else
        {
            _logger.LogDebug("Metric: outbox_pending_count={Count}", count);
        }
    }

    public void SetOutboxFailedCount(int count)
    {
        _outboxFailedCount = count;

        if (count > 0)
        {
            _logger.LogWarning(
                "ALERT: outbox_failed_count={Count} - manual intervention may be required",
                count);
        }
    }

    public void RecordPostingLatency(double milliseconds, string operationType)
    {
        var normalizedOperationType = Normalize(operationType);
        _postingLatencyMilliseconds.Record(
            milliseconds,
            new KeyValuePair<string, object?>("operation_type", normalizedOperationType));
        _logger.LogDebug(
            "Metric: posting_latency_ms={Latency} for operation {OperationType}",
            milliseconds,
            operationType);

        if (milliseconds > 1000)
        {
            _logger.LogWarning(
                "SLOW: Posting operation {OperationType} took {Latency}ms",
                operationType,
                milliseconds);
        }
    }

    public void IncrementIdempotentCalls(Guid tenantId, string operationType)
    {
        var tenantBucket = TenantBucket(tenantId);
        var normalizedOperationType = Normalize(operationType);
        IncrementCounter(BuildCounterKey("accounting.idempotent_total", tenantBucket, normalizedOperationType));
        _idempotentTotal.Add(
            1,
            CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("operation_type", normalizedOperationType));
        _logger.LogDebug(
            "Metric: idempotent_calls_total for tenant {TenantId}, operation {OperationType}",
            tenantId,
            operationType);
    }

    public void IncrementOutboxDeadLettered(Guid tenantId, string operationType)
    {
        var tenantBucket = TenantBucket(tenantId);
        var normalizedOperationType = Normalize(operationType);
        IncrementCounter(BuildCounterKey("accounting.outbox.dead_lettered_total", tenantBucket, normalizedOperationType));
        _outboxDeadLetteredTotal.Add(
            1,
            CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("operation_type", normalizedOperationType));
        _logger.LogError(
            "ALERT: outbox_dead_lettered_total incremented for tenant {TenantId}, operation {OperationType}",
            tenantId,
            operationType);
    }

    public void RecordOutboxCleanup(int processedDeleted, int failedDeleted)
    {
        if (processedDeleted > 0)
        {
            IncrementCounter("accounting.outbox.cleanup_total:processed", processedDeleted);
            _outboxCleanupTotal.Add(
                processedDeleted,
                new KeyValuePair<string, object?>("row_class", "processed"));
        }

        if (failedDeleted > 0)
        {
            IncrementCounter("accounting.outbox.cleanup_total:failed", failedDeleted);
            _outboxCleanupTotal.Add(
                failedDeleted,
                new KeyValuePair<string, object?>("row_class", "failed"));
        }

        if (processedDeleted > 0 || failedDeleted > 0)
        {
            _logger.LogInformation(
                "Metric: outbox_cleanup processed_deleted={ProcessedDeleted} failed_deleted={FailedDeleted}",
                processedDeleted,
                failedDeleted);
        }
    }

    public int GetOutboxPendingCount() => _outboxPendingCount;

    public int GetOutboxFailedCount() => _outboxFailedCount;

    public long GetCounter(string key)
    {
        return _counters.TryGetValue(key, out var value) ? value : 0;
    }

    public IReadOnlyDictionary<string, long> GetAllCounters() => _counters;

    public void Dispose()
    {
        _meter.Dispose();
    }

    private void RecordPosted(
        Guid tenantId,
        string operationType,
        KeyValuePair<string, object?>? extraTag = null,
        string? extraKeyComponent = null)
    {
        var tenantBucket = TenantBucket(tenantId);
        IncrementCounter(BuildCounterKey("accounting.posted_total", tenantBucket, operationType, extraKeyComponent));

        if (extraTag.HasValue)
        {
            _postedTotal.Add(
                1,
                CreateTenantBucketTag(tenantId),
                new KeyValuePair<string, object?>("operation_type", operationType),
                extraTag.Value);
            return;
        }

        _postedTotal.Add(
            1,
            CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("operation_type", operationType));
    }

    private void IncrementCounter(string key, long amount = 1)
    {
        _counters.AddOrUpdate(key, amount, (_, existing) => existing + amount);
    }

    private static string BuildCounterKey(params string?[] components) =>
        string.Join(':', components.Where(static component => !string.IsNullOrWhiteSpace(component)));

    private static KeyValuePair<string, object?> CreateTenantBucketTag(Guid tenantId) =>
        OperationalTelemetryDimensions.CreateTenantBucketTag(tenantId);

    private static string TenantBucket(Guid tenantId) =>
        CreateTenantBucketTag(tenantId).Value?.ToString() ?? "none";

    private static string Normalize(string? value) =>
        OperationalTelemetryDimensions.NormalizeTagValue(value);
}
