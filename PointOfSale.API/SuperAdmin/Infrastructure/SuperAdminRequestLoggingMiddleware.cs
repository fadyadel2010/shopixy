using System.Diagnostics;
using System.Security.Claims;

namespace PointOfSale.API.SuperAdmin.Infrastructure;

public sealed class SuperAdminRequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SuperAdminRequestLoggingMiddleware> _logger;

    public SuperAdminRequestLoggingMiddleware(
        RequestDelegate next,
        ILogger<SuperAdminRequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsSuperAdminRequest(context))
        {
            await _next(context);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await _next(context);
        }
        finally
        {
            stopwatch.Stop();

            var requestId = SuperAdminResponseFactory.GetRequestId(context);
            var endpoint = context.GetEndpoint()?.DisplayName ?? context.Request.Path.Value ?? "unknown";
            var superAdminId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                               ?? context.User.FindFirstValue("sub");
            var statusCode = context.Response.StatusCode;
            var logLevel = statusCode >= 500
                ? LogLevel.Error
                : statusCode >= 400
                    ? LogLevel.Warning
                    : LogLevel.Information;

            _logger.Log(
                logLevel,
                "SuperAdmin request completed | RequestId: {RequestId} | SuperAdminId: {SuperAdminId} | Endpoint: {Endpoint} | Method: {Method} | StatusCode: {StatusCode} | ExecutionTimeMs: {ExecutionTimeMs} | TimestampUtc: {TimestampUtc}",
                requestId,
                superAdminId ?? "anonymous",
                endpoint,
                context.Request.Method,
                statusCode,
                stopwatch.Elapsed.TotalMilliseconds,
                DateTime.UtcNow);
        }
    }

    private static bool IsSuperAdminRequest(HttpContext context)
    {
        return context.Request.Path.StartsWithSegments("/api/super-admin", StringComparison.OrdinalIgnoreCase);
    }
}
