using System.Diagnostics.Metrics;

namespace PointOfSale.Infrastructure.Instrumentation;

public sealed class AbandonedCartRecoveryTelemetry
{
    public const string MeterName = "PointOfSale.AbandonedCartRecovery";
    private readonly Meter _meter;

    public Counter<long> CallbackSuccessTotal { get; }
    public Counter<long> CallbackDuplicateTotal { get; }
    public Counter<long> CallbackValidationFailureTotal { get; }
    public Counter<long> CallbackAuthFailureTotal { get; }
    public Counter<long> CallbackPersistenceFailureTotal { get; }
    public Counter<long> StatsFailureTotal { get; }
    public Counter<long> RecoveryTokenInvalidTotal { get; }
    public Counter<long> RecoveryTokenSignatureFailedTotal { get; }

    public AbandonedCartRecoveryTelemetry()
    {
        _meter = new Meter(MeterName, "1.0.0");
        CallbackSuccessTotal = _meter.CreateCounter<long>(
            "abandoned_cart.callback.success_total",
            "count",
            "Recovery callback requests successfully persisted.");
        CallbackDuplicateTotal = _meter.CreateCounter<long>(
            "abandoned_cart.callback.duplicate_total",
            "count",
            "Recovery callback requests identified as idempotent duplicates.");
        CallbackValidationFailureTotal = _meter.CreateCounter<long>(
            "abandoned_cart.callback.validation_failure_total",
            "count",
            "Recovery callback requests rejected due to invalid payloads.");
        CallbackAuthFailureTotal = _meter.CreateCounter<long>(
            "abandoned_cart.callback.auth_failure_total",
            "count",
            "Recovery callback requests rejected for missing or invalid authentication.");
        CallbackPersistenceFailureTotal = _meter.CreateCounter<long>(
            "abandoned_cart.callback.persistence_failure_total",
            "count",
            "Recovery callback requests that failed during persistence.");
        StatsFailureTotal = _meter.CreateCounter<long>(
            "abandoned_cart.stats.failure_total",
            "count",
            "Abandoned-cart stats requests that failed.");
        RecoveryTokenInvalidTotal = _meter.CreateCounter<long>(
            "recovery_token_invalid_total",
            "count",
            "Abandoned-cart recovery tokens rejected because they were invalid, expired, or no longer matched cart ownership.");
        RecoveryTokenSignatureFailedTotal = _meter.CreateCounter<long>(
            "recovery_token_signature_failed_total",
            "count",
            "Abandoned-cart recovery tokens rejected because signature or protector validation failed.");
    }

    public void RecordCallbackSuccess(Guid tenantId) =>
        CallbackSuccessTotal.Add(1, OperationalTelemetryDimensions.CreateTenantBucketTag(tenantId));

    public void RecordCallbackDuplicate(Guid tenantId) =>
        CallbackDuplicateTotal.Add(1, OperationalTelemetryDimensions.CreateTenantBucketTag(tenantId));

    public void RecordCallbackValidationFailure(Guid tenantId, string reason) =>
        CallbackValidationFailureTotal.Add(
            1,
            OperationalTelemetryDimensions.CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("reason", NormalizeReason(reason)));

    public void RecordCallbackAuthFailure(Guid tenantId, string reason) =>
        CallbackAuthFailureTotal.Add(
            1,
            OperationalTelemetryDimensions.CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("reason", NormalizeReason(reason)));

    public void RecordCallbackPersistenceFailure(Guid tenantId, string reason) =>
        CallbackPersistenceFailureTotal.Add(
            1,
            OperationalTelemetryDimensions.CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("reason", NormalizeReason(reason)));

    public void RecordStatsFailure(Guid tenantId) =>
        StatsFailureTotal.Add(1, OperationalTelemetryDimensions.CreateTenantBucketTag(tenantId));

    public void RecordRecoveryTokenInvalid(Guid? tenantId, string reason) =>
        RecoveryTokenInvalidTotal.Add(
            1,
            CreateTenantTag(tenantId),
            new KeyValuePair<string, object?>("reason", NormalizeReason(reason)));

    public void RecordRecoveryTokenSignatureFailed(Guid? tenantId, string reason) =>
        RecoveryTokenSignatureFailedTotal.Add(
            1,
            CreateTenantTag(tenantId),
            new KeyValuePair<string, object?>("reason", NormalizeReason(reason)));

    private static KeyValuePair<string, object?> CreateTenantTag(Guid? tenantId) =>
        OperationalTelemetryDimensions.CreateOptionalTenantBucketTag(tenantId);

    private static string NormalizeReason(string? reason)
        => OperationalTelemetryDimensions.NormalizeTagValue(reason);
}
