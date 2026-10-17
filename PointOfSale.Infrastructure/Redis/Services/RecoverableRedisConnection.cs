using StackExchange.Redis;

namespace PointOfSale.Infrastructure.Redis.Services;

// Keeps a single in-flight connection even if the initial caller's bounded wait
// expires. Late success is retained (or disposed on shutdown); an actual failure
// can be retried after a cooldown rather than becoming a process-lifetime null.
internal sealed class RecoverableRedisConnection(Func<IConnectionMultiplexer?> connect, TimeSpan budget, TimeSpan cooldown) : IDisposable
{
    private readonly object _gate = new();
    private Task<IConnectionMultiplexer?>? _pending;
    private IConnectionMultiplexer? _value;
    private DateTime _retryAfter;
    private bool _disposed;

    private Task<IConnectionMultiplexer?> GetTask()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_value != null) return Task.FromResult<IConnectionMultiplexer?>(_value);
            if (_pending != null) return _pending;
            if (DateTime.UtcNow < _retryAfter) return Task.FromResult<IConnectionMultiplexer?>(null);
            var completion = new TaskCompletionSource<IConnectionMultiplexer?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending = completion.Task;
            new Thread(() => {
                IConnectionMultiplexer? value = null;
                try { value = connect(); }
                catch { /* Factory logs safe connection diagnostics; callers see unavailable. */ }
                lock (_gate)
                {
                    if (_disposed) value?.Dispose();
                    else _value = value;
                    _retryAfter = DateTime.UtcNow + cooldown;
                    _pending = null;
                    completion.SetResult(_disposed ? null : value);
                }
            }) { IsBackground = true, Name = "RedisConnection" }.Start();
            return completion.Task;
        }
    }

    public IConnectionMultiplexer? Value
    {
        get
        {
            var task = GetTask();
            // Task.Wait uses a bounded kernel wait, not a thread-pool timer callback.
            return task.Wait(budget) ? task.GetAwaiter().GetResult() : null;
        }
    }

    public async Task<IConnectionMultiplexer?> GetAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return await GetTask().WaitAsync(budget, ct).ConfigureAwait(false);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _value?.Dispose();
            _value = null;
        }
    }
}
