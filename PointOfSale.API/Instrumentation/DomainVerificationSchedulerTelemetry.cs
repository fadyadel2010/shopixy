using System.Diagnostics.Metrics;

namespace PointOfSale.API.Instrumentation;

public sealed class DomainVerificationSchedulerTelemetry : IDisposable
{
    public const string MeterName = "PointOfSale.DomainVerification.Scheduler";

    private readonly Meter _meter;

    private readonly Counter<long> _runsTotal;
    private readonly Counter<long> _candidatesTotal;
    private readonly Counter<long> _processedTotal;
    private readonly Counter<long> _successTotal;
    private readonly Counter<long> _failureTotal;
    private readonly Counter<long> _skippedTotal;
    private readonly Histogram<double> _durationSeconds;
    private readonly UpDownCounter<long> _activeJobs;

    public DomainVerificationSchedulerTelemetry()
    {
        _meter = new Meter(MeterName, "1.0.0");

        _runsTotal = _meter.CreateCounter<long>(
            "domain_verification_scheduler_runs_total",
            unit: "count",
            description: "Domain verification scheduler executions.");
        _candidatesTotal = _meter.CreateCounter<long>(
            "domain_verification_scheduler_candidates_total",
            unit: "count",
            description: "Domain verification scheduler candidates discovered.");
        _processedTotal = _meter.CreateCounter<long>(
            "domain_verification_scheduler_processed_total",
            unit: "count",
            description: "Domain verification scheduler candidates processed.");
        _successTotal = _meter.CreateCounter<long>(
            "domain_verification_scheduler_success_total",
            unit: "count",
            description: "Domain verification scheduler successful verifications.");
        _failureTotal = _meter.CreateCounter<long>(
            "domain_verification_scheduler_failure_total",
            unit: "count",
            description: "Domain verification scheduler failed verification invocations.");
        _skippedTotal = _meter.CreateCounter<long>(
            "domain_verification_scheduler_skipped_total",
            unit: "count",
            description: "Domain verification scheduler skipped candidates.");
        _durationSeconds = _meter.CreateHistogram<double>(
            "domain_verification_scheduler_duration",
            unit: "s",
            description: "Domain verification scheduler execution duration.");
        _activeJobs = _meter.CreateUpDownCounter<long>(
            "domain_verification_scheduler_active_jobs",
            unit: "count",
            description: "Currently active domain verification scheduler executions.");
    }

    public void RecordRun(string outcome) =>
        _runsTotal.Add(1, new KeyValuePair<string, object?>("outcome", Normalize(outcome)));

    public void RecordCandidates(int count)
    {
        if (count > 0)
        {
            _candidatesTotal.Add(count);
        }
    }

    public void RecordProcessed(int count)
    {
        if (count > 0)
        {
            _processedTotal.Add(count);
        }
    }

    public void RecordSuccess(int count)
    {
        if (count > 0)
        {
            _successTotal.Add(count);
        }
    }

    public void RecordFailure(int count, string reason)
    {
        if (count > 0)
        {
            _failureTotal.Add(count, new KeyValuePair<string, object?>("reason", Normalize(reason)));
        }
    }

    public void RecordSkipped(int count, string reason)
    {
        if (count > 0)
        {
            _skippedTotal.Add(count, new KeyValuePair<string, object?>("reason", Normalize(reason)));
        }
    }

    public void RecordDuration(TimeSpan duration, string outcome) =>
        _durationSeconds.Record(
            Math.Max(0d, duration.TotalSeconds),
            new KeyValuePair<string, object?>("outcome", Normalize(outcome)));

    public void AddActiveJob() => _activeJobs.Add(1);
    public void RemoveActiveJob() => _activeJobs.Add(-1);

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var normalized = value.Trim().ToLowerInvariant().Replace(' ', '_');
        var filtered = new string(normalized
            .Where(ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-')
            .ToArray());

        if (string.IsNullOrWhiteSpace(filtered))
        {
            return "unknown";
        }

        return filtered.Length <= 64 ? filtered : filtered[..64];
    }

    public void Dispose() => _meter.Dispose();
}
