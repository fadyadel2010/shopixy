using System.Diagnostics;
using System.Diagnostics.Tracing;

namespace PointOfSale.API.HealthChecks;

// The runtime emits these events on the request's execution context. Only timing,
// phase names and numeric connection identity are consumed; event payloads containing
// URLs, DNS names, certificates, headers or response bodies are never retained.
internal sealed class R2TransportTrace : IDisposable
{
    private static readonly AsyncLocal<R2TransportTrace?> Current = new();
    private static readonly Listener Events = new();
    private static readonly HashSet<long> Connections = [];
    private readonly R2TransportTrace? _previous;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Dictionary<string, double> _starts = [];
    private readonly Dictionary<string, double> _completed = [];
    private readonly object _gate = new();
    private double? _httpCompletedAt;
    private bool? _reused;

    public R2TransportTrace()
    {
        _ = Events;
        _previous = Current.Value;
        Current.Value = this;
    }

    internal void Observe(string source, string name, IReadOnlyList<object?>? payload)
    {
        var phase = source switch
        {
            "System.Net.NameResolution" => "dns",
            "System.Net.Sockets" => "tcp",
            "System.Net.Security" => "tls",
            _ => null
        };
        lock (_gate)
        {
            var now = _clock.Elapsed.TotalMilliseconds;
            if (phase != null)
            {
                if (name.EndsWith("Start", StringComparison.Ordinal)) _starts[phase] = now;
                if (name.EndsWith("Stop", StringComparison.Ordinal) && _starts.TryGetValue(phase, out var start))
                    _completed[phase] = now - start;
            }
            if (source != "System.Net.Http") return;
            if (name == "RequestStart") _starts["http"] = now;
            if (name == "RequestHeadersStart" && payload?.FirstOrDefault() is long connection)
            {
                _completed["connectionAcquisition"] = now - _starts.GetValueOrDefault("http");
                lock (Connections)
                {
                    _reused = Connections.Contains(connection) ? true : _completed.ContainsKey("tcp") ? false : null;
                    if (Connections.Count >= 256) Connections.Clear();
                    Connections.Add(connection);
                }
                _starts["responseWait"] = now;
            }
            if (name == "ResponseHeadersStop" && _starts.TryGetValue("responseWait", out var wait))
                _completed["responseWait"] = now - wait;
            if (name == "RequestStop") _httpCompletedAt = now;
        }
    }

    public void Complete(Dictionary<string, object> data)
    {
        lock (_gate)
        {
            data["transportTraceId"] = Activity.Current?.TraceId.ToString() ?? "unavailable";
            data["connectionReused"] = _reused.HasValue ? _reused.Value : "unobserved";
            data["transportPhases"] = new[] { "dns", "tcp", "tls", "connectionAcquisition", "responseWait" }
                .ToDictionary(phase => phase, phase => (object)(_completed.TryGetValue(phase, out var elapsed)
                    ? new { state = "completed", durationMs = (double?)elapsed }
                    : new { state = _starts.ContainsKey(phase) ? "started-not-completed" : "unobserved", durationMs = (double?)null }));
            // Includes response body consumption, SDK parsing and completion, not a
            // fictional parser-only measurement. Reused sockets need no new DNS/TLS.
            if (_httpCompletedAt.HasValue)
                data["bodyParsingAndCompletionMs"] = _clock.Elapsed.TotalMilliseconds - _httpCompletedAt.Value;
        }
    }

    public void Dispose() => Current.Value = _previous;

    private sealed class Listener : EventListener
    {
        protected override void OnEventSourceCreated(EventSource source)
        {
            if (source.Name is "System.Net.Http" or "System.Net.NameResolution" or "System.Net.Sockets" or "System.Net.Security")
                EnableEvents(source, EventLevel.Informational);
        }
        protected override void OnEventWritten(EventWrittenEventArgs e) =>
            Current.Value?.Observe(e.EventSource.Name, e.EventName ?? "", e.Payload);
    }
}
