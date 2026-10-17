using System.Diagnostics;
using System.Text.Json;
using PointOfSale.API.HealthChecks;

namespace PointOfSale.Tests.Infrastructure;

public sealed class R2TransportTraceTests
{
    [Fact]
    public void Trace_distinguishes_unobserved_incomplete_and_completed_phases_without_payload_disclosure()
    {
        using var activity = new Activity("synthetic-r2-trace").Start();
        using var trace = new R2TransportTrace();
        trace.Observe("System.Net.NameResolution", "ResolutionStart", ["SYNTHETIC-SECRET-HOST"]);
        trace.Observe("System.Net.NameResolution", "ResolutionStop", []);
        trace.Observe("System.Net.Sockets", "ConnectStart", ["SYNTHETIC-SECRET-ADDRESS"]);
        var data = new Dictionary<string, object>(); trace.Complete(data);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(data));
        var phases = json.RootElement.GetProperty("transportPhases");
        Assert.Equal("completed", phases.GetProperty("dns").GetProperty("state").GetString());
        Assert.True(phases.GetProperty("dns").GetProperty("durationMs").GetDouble() >= 0);
        Assert.Equal("started-not-completed", phases.GetProperty("tcp").GetProperty("state").GetString());
        Assert.Equal("unobserved", phases.GetProperty("tls").GetProperty("state").GetString());
        Assert.Equal("unobserved", data["connectionReused"]);
        Assert.Equal(activity.TraceId.ToString(), data["transportTraceId"]);
        Assert.DoesNotContain("SYNTHETIC-SECRET", JsonSerializer.Serialize(data));
        Assert.False(data.ContainsKey("bodyParsingAndCompletionMs"));
    }

    [Fact]
    public void Previously_seen_connection_is_reported_reused_but_unseen_warm_connection_is_not_called_cold()
    {
        var connection = Random.Shared.NextInt64(long.MaxValue / 2, long.MaxValue);
        using (var trace = new R2TransportTrace())
        {
            trace.Observe("System.Net.Http", "RequestStart", []);
            trace.Observe("System.Net.Http", "RequestHeadersStart", [connection]);
            var data = new Dictionary<string, object>(); trace.Complete(data);
            Assert.Equal("unobserved", data["connectionReused"]);
        }
        using (var trace = new R2TransportTrace())
        {
            trace.Observe("System.Net.Http", "RequestStart", []);
            trace.Observe("System.Net.Http", "RequestHeadersStart", [connection]);
            var data = new Dictionary<string, object>(); trace.Complete(data);
            Assert.Equal(true, data["connectionReused"]);
        }
    }
}
