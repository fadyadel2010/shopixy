using System.Diagnostics.Metrics;
using System.Collections.Concurrent;

namespace PointOfSale.API.Instrumentation;

public interface IManualPaymentOperationalTelemetryAuthority
{
    ManualPaymentTelemetryEmissionResult Emit(ManualPaymentOperationalTelemetryEvent telemetryEvent);
}

public enum ManualPaymentTelemetryEmissionStatus
{
    Emitted = 0,
    SuppressedDuplicate = 1
}

public sealed record ManualPaymentTelemetryEmissionResult(
    ManualPaymentTelemetryEmissionStatus Status,
    string EventName,
    string EmissionKey);

public sealed record ManualPaymentOperationalTelemetryEvent(
    string EventName,
    Guid TenantId,
    string Operation,
    string OperationId,
    string? Channel = null,
    string? Outcome = null,
    string? Capability = null,
    string? ReasonCode = null);

public sealed class ManualPaymentOperationalTelemetry : IManualPaymentOperationalTelemetryAuthority, IDisposable
{
    public const string MeterName = "PointOfSale.Payments.ManualPayment";
    public const string InitializedEventName = "ManualPayment.Initialized";
    public const string EvidenceSubmittedEventName = "ManualPayment.EvidenceSubmitted";
    public const string RuntimeRejectedEventName = "ManualPayment.RuntimeRejected";
    public const string ConfigurationRejectedEventName = "ManualPayment.ConfigurationRejected";
    public const string CapabilityRejectedEventName = "ManualPayment.CapabilityRejected";
    public const string ChannelRejectedEventName = "ManualPayment.ChannelRejected";
    public const string EnvironmentRejectedEventName = "ManualPayment.EnvironmentRejected";

    public static readonly IReadOnlyCollection<string> RequiredMetricNames = new[]
    {
        "manual_payment_initializations_total",
        "manual_payment_evidence_total",
        "manual_payment_runtime_rejections_total",
        "manual_payment_configuration_failures_total",
        "manual_payment_capability_failures_total",
        "manual_payment_environment_failures_total"
    };

    public static readonly IReadOnlyCollection<string> RequiredEventNames = new[]
    {
        InitializedEventName,
        EvidenceSubmittedEventName,
        RuntimeRejectedEventName,
        ConfigurationRejectedEventName,
        CapabilityRejectedEventName,
        ChannelRejectedEventName,
        EnvironmentRejectedEventName
    };

    private static readonly ISet<string> CanonicalEventNames =
        new HashSet<string>(RequiredEventNames, StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, byte> _emissionFence = new(StringComparer.Ordinal);
    private readonly Meter _meter;
    private readonly Counter<long> _initializations;
    private readonly Counter<long> _evidence;
    private readonly Counter<long> _runtimeRejections;
    private readonly Counter<long> _configurationFailures;
    private readonly Counter<long> _capabilityFailures;
    private readonly Counter<long> _environmentFailures;

    public ManualPaymentOperationalTelemetry()
    {
        _meter = new Meter(MeterName, "1.0.0");
        _initializations = _meter.CreateCounter<long>("manual_payment_initializations_total");
        _evidence = _meter.CreateCounter<long>("manual_payment_evidence_total");
        _runtimeRejections = _meter.CreateCounter<long>("manual_payment_runtime_rejections_total");
        _configurationFailures = _meter.CreateCounter<long>("manual_payment_configuration_failures_total");
        _capabilityFailures = _meter.CreateCounter<long>("manual_payment_capability_failures_total");
        _environmentFailures = _meter.CreateCounter<long>("manual_payment_environment_failures_total");
    }

    public ManualPaymentTelemetryEmissionResult Emit(ManualPaymentOperationalTelemetryEvent telemetryEvent)
    {
        ArgumentNullException.ThrowIfNull(telemetryEvent);
        ValidateSchema(telemetryEvent);

        var emissionKey = BuildEmissionKey(telemetryEvent);
        if (!_emissionFence.TryAdd(emissionKey, 0))
        {
            return new ManualPaymentTelemetryEmissionResult(
                ManualPaymentTelemetryEmissionStatus.SuppressedDuplicate,
                telemetryEvent.EventName,
                emissionKey);
        }

        switch (telemetryEvent.EventName)
        {
            case InitializedEventName:
                RecordInitialization(telemetryEvent.TenantId, telemetryEvent.Channel!);
                break;
            case EvidenceSubmittedEventName:
                RecordEvidence(telemetryEvent.TenantId, telemetryEvent.Outcome!);
                break;
            case RuntimeRejectedEventName:
                RecordRuntimeRejected(telemetryEvent.TenantId, telemetryEvent.Operation, telemetryEvent.ReasonCode!);
                break;
            case ConfigurationRejectedEventName:
                RecordConfigurationFailure(telemetryEvent.TenantId, telemetryEvent.ReasonCode!);
                break;
            case CapabilityRejectedEventName:
            case ChannelRejectedEventName:
                RecordCapabilityFailure(telemetryEvent.TenantId, telemetryEvent.Capability!);
                break;
            case EnvironmentRejectedEventName:
                RecordEnvironmentFailure(telemetryEvent.TenantId, telemetryEvent.ReasonCode!);
                break;
        }

        return new ManualPaymentTelemetryEmissionResult(
            ManualPaymentTelemetryEmissionStatus.Emitted,
            telemetryEvent.EventName,
            emissionKey);
    }

    private void RecordInitialization(Guid tenantId, string channel) =>
        _initializations.Add(1, Tags(tenantId, ("channel", channel)));

    private void RecordEvidence(Guid tenantId, string outcome) =>
        _evidence.Add(1, Tags(tenantId, ("outcome", outcome)));

    private void RecordRuntimeRejected(Guid tenantId, string operation, string reasonCode) =>
        _runtimeRejections.Add(1, Tags(tenantId, ("operation", operation), ("reason_code", reasonCode)));

    private void RecordConfigurationFailure(Guid tenantId, string reasonCode) =>
        _configurationFailures.Add(1, Tags(tenantId, ("reason_code", reasonCode)));

    private void RecordCapabilityFailure(Guid tenantId, string capability) =>
        _capabilityFailures.Add(1, Tags(tenantId, ("capability", capability)));

    private void RecordEnvironmentFailure(Guid tenantId, string reasonCode) =>
        _environmentFailures.Add(1, Tags(tenantId, ("reason_code", reasonCode)));

    public void Dispose() => _meter.Dispose();

    private static void ValidateSchema(ManualPaymentOperationalTelemetryEvent telemetryEvent)
    {
        if (!CanonicalEventNames.Contains(telemetryEvent.EventName))
        {
            throw new InvalidOperationException("Manual Payment operational telemetry event is unknown.");
        }

        if (telemetryEvent.TenantId == Guid.Empty ||
            string.IsNullOrWhiteSpace(telemetryEvent.Operation) ||
            string.IsNullOrWhiteSpace(telemetryEvent.OperationId))
        {
            throw new InvalidOperationException("Manual Payment operational telemetry schema is invalid.");
        }

        switch (telemetryEvent.EventName)
        {
            case InitializedEventName when string.IsNullOrWhiteSpace(telemetryEvent.Channel):
            case EvidenceSubmittedEventName when string.IsNullOrWhiteSpace(telemetryEvent.Outcome):
            case RuntimeRejectedEventName when string.IsNullOrWhiteSpace(telemetryEvent.ReasonCode):
            case ConfigurationRejectedEventName when string.IsNullOrWhiteSpace(telemetryEvent.ReasonCode):
            case EnvironmentRejectedEventName when string.IsNullOrWhiteSpace(telemetryEvent.ReasonCode):
            case CapabilityRejectedEventName when string.IsNullOrWhiteSpace(telemetryEvent.Capability):
            case ChannelRejectedEventName when string.IsNullOrWhiteSpace(telemetryEvent.Capability):
                throw new InvalidOperationException("Manual Payment operational telemetry schema is invalid.");
        }
    }

    private static string BuildEmissionKey(ManualPaymentOperationalTelemetryEvent telemetryEvent) =>
        string.Join(
            '|',
            telemetryEvent.EventName,
            telemetryEvent.TenantId.ToString("N"),
            telemetryEvent.Operation,
            telemetryEvent.OperationId,
            telemetryEvent.Channel ?? string.Empty,
            telemetryEvent.Outcome ?? string.Empty,
            telemetryEvent.Capability ?? string.Empty,
            telemetryEvent.ReasonCode ?? string.Empty);

    private static KeyValuePair<string, object?>[] Tags(
        Guid tenantId,
        params (string Key, string Value)[] values)
    {
        var tags = new List<KeyValuePair<string, object?>>
        {
            new("tenant_bucket", BucketTenant(tenantId))
        };

        tags.AddRange(values.Select(value => new KeyValuePair<string, object?>(value.Key, value.Value)));
        return tags.ToArray();
    }

    private static string BucketTenant(Guid tenantId) =>
        tenantId == Guid.Empty ? "unknown" : tenantId.ToString("N")[..8];
}
