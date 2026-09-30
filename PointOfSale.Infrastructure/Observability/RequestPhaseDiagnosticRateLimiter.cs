namespace PointOfSale.Infrastructure.Observability;

public sealed class RequestPhaseDiagnosticRateLimiter
{
    private readonly object _gate = new();
    private long _windowSecond;
    private int _recordsThisWindow;

    public bool TryAcquire(int maxRecordsPerSecond)
    {
        if (maxRecordsPerSecond <= 0)
            return false;

        var currentSecond = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        lock (_gate)
        {
            if (_windowSecond != currentSecond)
            {
                _windowSecond = currentSecond;
                _recordsThisWindow = 0;
            }

            if (_recordsThisWindow >= maxRecordsPerSecond)
                return false;

            _recordsThisWindow++;
            return true;
        }
    }
}

public sealed class NpgsqlDiagnosticLogRateLimiter
{
    private readonly RequestPhaseDiagnosticRateLimiter _limiter = new();

    public bool TryAcquire(int maxRecordsPerSecond) => _limiter.TryAcquire(maxRecordsPerSecond);
}
