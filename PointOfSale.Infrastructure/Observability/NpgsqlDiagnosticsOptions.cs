namespace PointOfSale.Infrastructure.Observability;

/// <summary>
/// Controls Npgsql command and connection instrumentation. Metrics and activities are
/// enabled by default; potentially noisy command logging is opt-in.
/// </summary>
public sealed class NpgsqlDiagnosticsOptions
{
    public const string SectionName = "NpgsqlDiagnostics";

    public bool Enabled { get; set; } = true;

    public bool SlowCommandLoggingEnabled { get; set; }

    public bool CaptureCommandTextInActivities { get; set; }

    public bool CaptureConnectionIdentityInActivities { get; set; }

    public double SlowCommandThresholdMs { get; set; } = 2000;

    public int MaxStatementLength { get; set; } = 500;

    public int MaxSlowCommandLogsPerSecond { get; set; } = 10;
}

/// <summary>
/// Controls bounded per-request diagnostic logging. Request metrics and context
/// correlation remain available when detailed logging is disabled.
/// </summary>
public sealed class RequestPhaseDiagnosticsOptions
{
    public const string SectionName = "RequestPhaseDiagnostics";

    public bool Enabled { get; set; }

    public double SlowRequestThresholdMs { get; set; } = 2000;

    public int MaxRecordsPerSecond { get; set; } = 10;
}
