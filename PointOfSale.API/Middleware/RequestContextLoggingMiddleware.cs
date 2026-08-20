using System.Diagnostics;

namespace PointOfSale.API.Middleware;

public sealed class RequestContextLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestContextLoggingMiddleware> _logger;

    public RequestContextLoggingMiddleware(
        RequestDelegate next,
        ILogger<RequestContextLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = RequestContextMetadata.GetCorrelationId(context);
        var tenantId = RequestContextMetadata.GetTenantId(context) ?? "unresolved";
        var userId = RequestContextMetadata.GetUserId(context) ?? "anonymous";
        // FEED-1B security fix: public feed URLs embed the raw token as the
        // final path segment; it must never reach logs, scopes, or activity
        // tags. All other paths are returned unchanged by the redactor.
        var requestPath = PublicFeedPathRedactor.Redact(context.Request.Path.Value ?? "/");
        var operation = $"{context.Request.Method} {requestPath}";
        var startedAt = Stopwatch.GetTimestamp();

        Activity.Current?.SetTag("correlation.id", correlationId);
        Activity.Current?.SetTag("tenant.id", tenantId);
        Activity.Current?.SetTag("user.id", userId);
        Activity.Current?.SetTag("operation", operation);

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = correlationId,
            ["TenantId"] = tenantId,
            ["UserId"] = userId,
            ["RequestPath"] = requestPath,
            ["RequestMethod"] = context.Request.Method,
        });

        var result = "success";
        var statusCode = StatusCodes.Status200OK;
        Exception? caughtException = null;

        try
        {
            await _next(context);
            statusCode = context.Response.StatusCode;
            result = statusCode >= StatusCodes.Status500InternalServerError
                ? "error"
                : statusCode >= StatusCodes.Status400BadRequest
                    ? "failed"
                    : "success";
        }
        catch (Exception ex)
        {
            caughtException = ex;
            result = "error";
            statusCode = context.Response.HasStarted 
                ? context.Response.StatusCode 
                : StatusCodes.Status500InternalServerError;

            Activity.Current?.SetTag("exception.type", ex.GetType().FullName);
            Activity.Current?.SetTag("exception.message", ex.Message);

            throw;
        }
        finally
        {
            var latencyMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;

            Activity.Current?.SetTag("operation.result", result);
            Activity.Current?.SetTag("operation.latency_ms", latencyMs);

            _logger.LogInformation(
                "HTTP request completed | CorrelationId: {CorrelationId} | TenantId: {TenantId} | UserId: {UserId} | Operation: {Operation} | Result: {Result} | StatusCode: {StatusCode} | LatencyMs: {LatencyMs}",
                correlationId,
                tenantId,
                userId,
                operation,
                result,
                statusCode,
                latencyMs);
        }
    }
}
