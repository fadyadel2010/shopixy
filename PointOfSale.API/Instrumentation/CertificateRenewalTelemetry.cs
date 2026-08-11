using System.Diagnostics.Metrics;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Enums;

namespace PointOfSale.API.Instrumentation;

public sealed class CertificateRenewalTelemetry : IDisposable
{
    public const string MeterName = "PointOfSale.CertificateRenewal";

    private readonly Meter _meter;
    private readonly Counter<long> _scanTotal;
    private readonly Counter<long> _dueTotal;
    private readonly Counter<long> _startedTotal;
    private readonly Counter<long> _successTotal;
    private readonly Counter<long> _failureTotal;
    private readonly Counter<long> _rateLimitedTotal;
    private readonly Counter<long> _sniFailureTotal;
    private readonly Counter<long> _revocationStartedTotal;
    private readonly Counter<long> _revocationSuccessTotal;
    private readonly Counter<long> _revocationFailureTotal;
    private readonly Counter<long> _recoveryStartedTotal;
    private readonly Counter<long> _recoverySuccessTotal;
    private readonly Counter<long> _recoveryFailureTotal;
    private readonly Counter<long> _expiredTotal;
    private readonly Counter<long> _compromisedTotal;
    private readonly Counter<long> _edgeMismatchTotal;
    private readonly Counter<long> _sslDegradedTotal;
    private readonly Counter<long> _sslRestoredTotal;
    private readonly Histogram<double> _durationSeconds;

    public CertificateRenewalTelemetry()
    {
        _meter = new Meter(MeterName, "1.0.0");
        _scanTotal = _meter.CreateCounter<long>("certificate_renewal_scan_total");
        _dueTotal = _meter.CreateCounter<long>("certificate_renewal_due_total");
        _startedTotal = _meter.CreateCounter<long>("certificate_renewal_started_total");
        _successTotal = _meter.CreateCounter<long>("certificate_renewal_success_total");
        _failureTotal = _meter.CreateCounter<long>("certificate_renewal_failure_total");
        _rateLimitedTotal = _meter.CreateCounter<long>("certificate_renewal_rate_limited_total");
        _sniFailureTotal = _meter.CreateCounter<long>("certificate_renewal_sni_failure_total");
        _revocationStartedTotal = _meter.CreateCounter<long>("certificate_revocation_started_total");
        _revocationSuccessTotal = _meter.CreateCounter<long>("certificate_revocation_success_total");
        _revocationFailureTotal = _meter.CreateCounter<long>("certificate_revocation_failure_total");
        _recoveryStartedTotal = _meter.CreateCounter<long>("certificate_recovery_started_total");
        _recoverySuccessTotal = _meter.CreateCounter<long>("certificate_recovery_success_total");
        _recoveryFailureTotal = _meter.CreateCounter<long>("certificate_recovery_failure_total");
        _expiredTotal = _meter.CreateCounter<long>("certificate_expired_total");
        _compromisedTotal = _meter.CreateCounter<long>("certificate_compromised_total");
        _edgeMismatchTotal = _meter.CreateCounter<long>("certificate_edge_mismatch_total");
        _sslDegradedTotal = _meter.CreateCounter<long>("certificate_ssl_degraded_total");
        _sslRestoredTotal = _meter.CreateCounter<long>("certificate_ssl_restored_total");
        _durationSeconds = _meter.CreateHistogram<double>("certificate_renewal_duration_seconds", unit: "s");
    }

    public void RecordScan(int candidates)
    {
        _scanTotal.Add(1);
        if (candidates > 0)
        {
            _dueTotal.Add(candidates);
        }
    }

    public void RecordStarted(CertificateRenewalServiceResult result) =>
        _startedTotal.Add(1, Tags(result));

    public void RecordSuccess(CertificateRenewalServiceResult result) =>
        _successTotal.Add(1, Tags(result));

    public void RecordFailure(CertificateRenewalServiceResult result)
    {
        _failureTotal.Add(1, Tags(result));
        if (string.Equals(result.FailureCode, "RATE_LIMITED", StringComparison.OrdinalIgnoreCase))
        {
            _rateLimitedTotal.Add(1, Tags(result));
        }

        if (string.Equals(result.FailureCode, "SNI_FAILURE", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(result.FailureCode, "SNI_PROBE_FAILED", StringComparison.OrdinalIgnoreCase))
        {
            _sniFailureTotal.Add(1, Tags(result));
        }

        if (string.Equals(result.FailureCode, "EXPIRED", StringComparison.OrdinalIgnoreCase))
        {
            _expiredTotal.Add(1, Tags(result));
            _sslDegradedTotal.Add(1, Tags(result));
        }

        if (string.Equals(result.FailureCode, "EDGE_CERTIFICATE_MISMATCH", StringComparison.OrdinalIgnoreCase))
        {
            _edgeMismatchTotal.Add(1, Tags(result));
        }
    }

    public void RecordRecoveryStarted(CertificateRenewalServiceResult result) =>
        _recoveryStartedTotal.Add(1, Tags(result));

    public void RecordRecoverySuccess(CertificateRenewalServiceResult result)
    {
        _recoverySuccessTotal.Add(1, Tags(result));
        _sslRestoredTotal.Add(1, Tags(result));
    }

    public void RecordRecoveryFailure(CertificateRenewalServiceResult result) =>
        _recoveryFailureTotal.Add(1, Tags(result));

    public void RecordRevocationStarted(CertificateRevocationServiceResult result)
    {
        _revocationStartedTotal.Add(1, Tags(result));
        if (string.Equals(result.ReasonCode, "SECURITY_COMPROMISE", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(result.ReasonCode, "CERTIFICATE_COMPROMISED", StringComparison.OrdinalIgnoreCase))
        {
            _compromisedTotal.Add(1, Tags(result));
        }
    }

    public void RecordRevocationSuccess(CertificateRevocationServiceResult result) =>
        _revocationSuccessTotal.Add(1, Tags(result));

    public void RecordRevocationFailure(CertificateRevocationServiceResult result) =>
        _revocationFailureTotal.Add(1, Tags(result));

    public void RecordDuration(TimeSpan duration, string outcome) =>
        _durationSeconds.Record(Math.Max(0d, duration.TotalSeconds), new KeyValuePair<string, object?>("outcome", Normalize(outcome)));

    private static KeyValuePair<string, object?>[] Tags(CertificateRenewalServiceResult result) =>
    [
        new("provider_type", result.ProviderType.ToString()),
        new("phase", result.Phase.ToString()),
        new("outcome", Normalize(result.Outcome)),
        new("failure_code", Normalize(result.FailureCode))
    ];

    private static KeyValuePair<string, object?>[] Tags(CertificateRevocationServiceResult result) =>
    [
        new("provider_type", result.ProviderType.ToString()),
        new("phase", result.Phase.ToString()),
        new("outcome", Normalize(result.Outcome)),
        new("failure_code", Normalize(result.FailureCode)),
        new("reason", Normalize(result.ReasonCode))
    ];

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "none";
        }

        var filtered = new string(value.Trim().ToLowerInvariant().Replace(' ', '_')
            .Where(ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-')
            .ToArray());
        return string.IsNullOrWhiteSpace(filtered) ? "none" : filtered[..Math.Min(64, filtered.Length)];
    }

    public void Dispose() => _meter.Dispose();
}
