using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace PointOfSale.API.Middleware;

public class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;
    public const string RequestIdHeaderName = "X-Request-Id";
    public const string CorrelationIdHeaderName = "X-Correlation-Id";

    public CorrelationIdMiddleware(
        RequestDelegate next,
        ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task Invoke(HttpContext context)
    {
        var correlationId = GetCorrelationId(context);
        
        // Store in HttpContext.TraceIdentifier which is standard in ASP.NET Core
        context.TraceIdentifier = correlationId;

        // Set the headers immediately so tests and early pipeline readers can observe them
        // even if the response never reaches the normal OnStarting callback path.
        context.Response.Headers[RequestIdHeaderName] = new StringValues(correlationId);
        context.Response.Headers[CorrelationIdHeaderName] = new StringValues(correlationId);
        Activity.Current?.SetTag("correlation.id", correlationId);
        Activity.Current?.SetTag("request.id", correlationId);

        // Also add to response headers so client knows the ID
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[RequestIdHeaderName] = new StringValues(correlationId);
            context.Response.Headers[CorrelationIdHeaderName] = new StringValues(correlationId);
            return Task.CompletedTask;
        });

        // Set Activity.Current.Id to ensure it propagates if using System.Diagnostics
        // Note: Activity might already be started by ASP.NET Core
        // For simplicity, we rely on TraceIdentifier for our custom info

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = correlationId,
            ["TraceId"] = correlationId,
            ["RequestPath"] = context.Request.Path.Value ?? string.Empty,
        });

        await _next(context);
    }

    private static string GetCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(RequestIdHeaderName, out var requestId) &&
            !StringValues.IsNullOrEmpty(requestId) &&
            !string.IsNullOrWhiteSpace(requestId.ToString()))
        {
            return requestId.ToString().Trim();
        }

        if (context.Request.Headers.TryGetValue(CorrelationIdHeaderName, out var correlationId) &&
            !StringValues.IsNullOrEmpty(correlationId) &&
            !string.IsNullOrWhiteSpace(correlationId.ToString()))
        {
            return correlationId.ToString().Trim();
        }

        return Guid.NewGuid().ToString();
    }
}
