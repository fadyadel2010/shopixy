using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PointOfSale.API.HealthChecks;

namespace PointOfSale.Tests.Infrastructure;

public class HealthCheckJsonResponseWriterTests
{
    [Fact]
    public async Task WriteAsync_EmitsCorrelationIdAndDependencyEntries()
    {
        var httpContext = new DefaultHttpContext
        {
            TraceIdentifier = "corr-health-1"
        };
        httpContext.Response.Body = new MemoryStream();

        var report = new HealthReport(
            new Dictionary<string, HealthReportEntry>
            {
                ["postgresql"] = new(
                    HealthStatus.Healthy,
                    "Database reachable.",
                    TimeSpan.FromMilliseconds(12),
                    exception: null,
                    data: new Dictionary<string, object>
                    {
                        ["latencyMs"] = 12d
                    })
            },
            TimeSpan.FromMilliseconds(12));

        await HealthCheckJsonResponseWriter.WriteAsync(httpContext, report);

        Assert.Equal("application/json", httpContext.Response.ContentType);

        httpContext.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(httpContext.Response.Body);

        Assert.Equal("Healthy", document.RootElement.GetProperty("status").GetString());
        Assert.Equal("corr-health-1", document.RootElement.GetProperty("correlationId").GetString());
        Assert.True(document.RootElement.GetProperty("totalDuration").GetDouble() >= 0d);

        var entry = document.RootElement.GetProperty("entries").GetProperty("postgresql");
        Assert.Equal("Healthy", entry.GetProperty("status").GetString());
        Assert.Equal("Database reachable.", entry.GetProperty("description").GetString());
        Assert.True(entry.GetProperty("duration").GetDouble() >= 0d);
        Assert.Equal(12d, entry.GetProperty("data").GetProperty("latencyMs").GetDouble());
    }
}
