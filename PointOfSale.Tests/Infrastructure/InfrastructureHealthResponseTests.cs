using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PointOfSale.API.HealthChecks;

namespace PointOfSale.Tests.Infrastructure;

public sealed class InfrastructureHealthResponseTests
{
    [Fact]
    public async Task FailureCategory_IsSafe_AndIndependentChecksKeepTheirOwnStatus()
    {
        var entries = new Dictionary<string, HealthReportEntry>
        {
            ["redis"] = new(HealthStatus.Unhealthy, "Redis unavailable", TimeSpan.FromMilliseconds(1000),
                new TimeoutException("synthetic-secret-never-publish"), new Dictionary<string, object> { ["FailureCategory"] = "timeout" }),
            ["postgresql"] = new(HealthStatus.Healthy, "PostgreSQL reachable", TimeSpan.FromMilliseconds(2), null, null),
            ["projection_drift"] = new(HealthStatus.Degraded, "Foreign projections preserved", TimeSpan.FromMilliseconds(3), null, null)
        };
        var report = new HealthReport(entries, TimeSpan.FromSeconds(1));
        Assert.Equal(HealthStatus.Unhealthy, report.Status);
        var http = new DefaultHttpContext(); http.Response.Body = new MemoryStream();
        await HealthCheckJsonResponseWriter.WriteAsync(http, report);
        http.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(http.Response.Body);
        var result = json.RootElement.GetProperty("entries");
        Assert.Equal("TimeoutException", result.GetProperty("redis").GetProperty("exceptionCategory").GetString());
        Assert.Equal("Healthy", result.GetProperty("postgresql").GetProperty("status").GetString());
        Assert.Equal("Degraded", result.GetProperty("projection_drift").GetProperty("status").GetString());
        Assert.DoesNotContain("synthetic-secret", json.RootElement.GetRawText());
        Assert.False(result.GetProperty("redis").TryGetProperty("exception", out _));
        Assert.Equal(JsonValueKind.Null, result.GetProperty("postgresql").GetProperty("exceptionCategory").ValueKind);
    }
}
