using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PointOfSale.API.Middleware;

namespace PointOfSale.API.HealthChecks;

public static class HealthCheckJsonResponseWriter
{
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var payload = new
        {
            status = report.Status.ToString(),
            correlationId = RequestContextMetadata.GetCorrelationId(context),
            timestampUtc = DateTime.UtcNow,
            totalDuration = report.TotalDuration.TotalMilliseconds,
            entries = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => new
                {
                    status = entry.Value.Status.ToString(),
                    description = entry.Value.Description,
                    duration = entry.Value.Duration.TotalMilliseconds,
                    data = entry.Value.Data.ToDictionary(item => item.Key, item => item.Value)
                })
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        }));
    }
}
