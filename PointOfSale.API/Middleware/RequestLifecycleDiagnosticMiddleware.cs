using System.Diagnostics;
using Microsoft.Extensions.Options;
using PointOfSale.Infrastructure.Observability;

namespace PointOfSale.API.Middleware;

/// <summary>
/// Establishes request-local Npgsql phase correlation and emits opt-in, rate-limited
/// slow-request records. It never records tenant, user, token, query-string, or body data.
/// </summary>
public sealed class RequestLifecycleDiagnosticMiddleware
{
    internal const string ActivitySourceName = "PointOfSale.Http.Diagnostics";
    internal const string ActivitySourceVersion = "1.0.0";
    internal static readonly ActivitySource SharedActivitySource =
        new(ActivitySourceName, ActivitySourceVersion);

    private const double VerySlowThresholdMs = 5000;
    private const double CriticalThresholdMs = 30000;

    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLifecycleDiagnosticMiddleware> _logger;
    private readonly NpgsqlDiagnosticsTelemetry _telemetry;
    private readonly RequestPhaseDiagnosticRateLimiter _rateLimiter;
    private readonly IOptionsMonitor<RequestPhaseDiagnosticsOptions> _options;

    public RequestLifecycleDiagnosticMiddleware(
        RequestDelegate next,
        ILogger<RequestLifecycleDiagnosticMiddleware> logger,
        NpgsqlDiagnosticsTelemetry telemetry,
        RequestPhaseDiagnosticRateLimiter rateLimiter,
        IOptionsMonitor<RequestPhaseDiagnosticsOptions> options)
    {
        _next = next;
        _logger = logger;
        _telemetry = telemetry;
        _rateLimiter = rateLimiter;
        _options = options;
    }

    public async Task Invoke(HttpContext context)
    {
        var options = _options.CurrentValue;
        var startTimestamp = Stopwatch.GetTimestamp();
        using var fallbackActivity = Activity.Current is null
            ? SharedActivitySource.StartActivity("HTTP.Request", ActivityKind.Server)
            : null;
        var activity = Activity.Current ?? fallbackActivity;

        using var phaseScope = RequestPhaseDiagnosticsContext.Begin();
        var phaseContext = RequestPhaseDiagnosticsContext.Current!;
        phaseContext.RequestId = activity?.TraceId.ToString() ?? context.TraceIdentifier;
        phaseContext.TraceId = activity?.TraceId.ToString() ?? string.Empty;
        phaseContext.SpanId = activity?.SpanId.ToString() ?? string.Empty;
        phaseContext.Method = context.Request.Method;
        phaseContext.ThresholdMs = options.SlowRequestThresholdMs;
        phaseContext.MarkEndpointStart();

        try
        {
            await _next(context);
        }
        catch (Exception exception)
        {
            phaseContext.HasException = true;
            phaseContext.Cancelled = exception is OperationCanceledException;
            fallbackActivity?.SetStatus(ActivityStatusCode.Error);
            throw;
        }
        finally
        {
            phaseContext.MarkEndpointEnd();
            phaseContext.StatusCode = context.Response.StatusCode;
            phaseContext.TotalMs = ElapsedMilliseconds(startTimestamp);

            var endpoint = context.GetEndpoint();
            phaseContext.Endpoint = endpoint?.DisplayName ?? "unknown";
            phaseContext.Route = GetRouteTemplate(context, endpoint);

            var isSlow = phaseContext.TotalMs >= options.SlowRequestThresholdMs;
            var isVerySlow = phaseContext.TotalMs >= VerySlowThresholdMs;
            var isCritical = phaseContext.TotalMs >= CriticalThresholdMs;
            var isTimeout = context.Response.StatusCode == StatusCodes.Status408RequestTimeout
                || phaseContext.Cancelled;

            _telemetry.RecordRequest(
                phaseContext.TotalMs,
                isSlow,
                isVerySlow,
                isCritical,
                isTimeout,
                phaseContext.Cancelled);

            if (isSlow && options.Enabled && _rateLimiter.TryAcquire(options.MaxRecordsPerSecond))
                LogSlowRequest(phaseContext.Snapshot(_telemetry.GetSnapshot()));

            activity?.SetTag("http.request.method", phaseContext.Method);
            activity?.SetTag("http.route", phaseContext.Route);
            activity?.SetTag("http.response.status_code", phaseContext.StatusCode);
            activity?.SetTag("http.request.duration_ms", phaseContext.TotalMs);
            activity?.SetTag("http.request.slow", isSlow);

            if (fallbackActivity is not null && !phaseContext.HasException)
                fallbackActivity.SetStatus(ActivityStatusCode.Ok);
        }
    }

    private void LogSlowRequest(RequestPhaseDiagnosticRecord record)
    {
        var logLevel = record.TotalMs >= CriticalThresholdMs
            ? LogLevel.Critical
            : record.TotalMs >= VerySlowThresholdMs
                ? LogLevel.Error
                : LogLevel.Warning;

        if (!_logger.IsEnabled(logLevel))
            return;

        _logger.Log(
            logLevel,
            "SLOW NPGSQL REQUEST | RequestId: {RequestId} | TraceId: {TraceId} | Method: {Method} | Route: {Route} | Endpoint: {Endpoint} | StatusCode: {StatusCode} | TotalMs: {TotalMs} | ThresholdMs: {ThresholdMs} | NpgsqlConnectionAcquireCount: {NpgsqlConnectionAcquireCount} | NpgsqlConnectionAcquireTotalMs: {NpgsqlConnectionAcquireTotalMs} | NpgsqlCommandCount: {NpgsqlCommandCount} | NpgsqlCommandTotalMs: {NpgsqlCommandTotalMs} | NpgsqlResultConsumptionTotalMs: {NpgsqlResultConsumptionTotalMs} | NpgsqlMaterializationTotalMs: {NpgsqlMaterializationTotalMs} | NpgsqlCleanupTotalMs: {NpgsqlCleanupTotalMs} | ActiveConnections: {ActiveConnections} | ExecutingCommands: {ExecutingCommands} | HasException: {HasException} | Cancelled: {Cancelled}",
            record.RequestId,
            record.TraceId,
            record.Method,
            record.Route,
            record.Endpoint,
            record.StatusCode,
            record.TotalMs,
            record.ThresholdMs,
            record.Npgsql.ConnectionAcquireCount,
            record.Npgsql.ConnectionAcquireTotalMs,
            record.Npgsql.CommandCount,
            record.Npgsql.CommandTotalMs,
            record.Npgsql.ResultConsumptionTotalMs,
            record.Npgsql.MaterializationTotalMs,
            record.Npgsql.CleanupTotalMs,
            record.Concurrency?.ActiveConnections,
            record.Concurrency?.ExecutingCommands,
            record.HasException,
            record.Cancelled);
    }

    private static string GetRouteTemplate(HttpContext context, Endpoint? endpoint)
    {
        var routePattern = (endpoint as RouteEndpoint)?.RoutePattern.RawText;
        return !string.IsNullOrWhiteSpace(routePattern)
            ? routePattern
            : context.Request.Path.HasValue
                ? "unmatched"
                : "unknown";
    }

    private static double ElapsedMilliseconds(long startTimestamp) =>
        (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;
}
