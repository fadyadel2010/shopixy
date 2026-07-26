using System.Threading.Channels;
using PointOfSale.API.DTOs;

namespace PointOfSale.API.Services.Audit;

public class AuditQueue : IAuditQueue
{
    private readonly Channel<AuditLogItem> _queue;
    private long _queueDepth;
    private long _retryCount;

    public long QueueDepth => Interlocked.Read(ref _queueDepth);
    public long RetryCount => Interlocked.Read(ref _retryCount);

    public AuditQueue()
    {
        // Unbounded channel to prevent blocking producers, or bounded to apply backpressure.
        // User requested "Queue size limit + fallback strategy (optional)".
        // For simplicity and performance, we'll start with Unbounded, but Bounded with DropOldest or Wait is safer for memory.
        // Given "Millions of rows" and "Never block API requests", Unbounded is risky if DB is down for long time.
        // But blocking API is worse. 
        // Let's use a large Bounded channel with DropWrite (logs are critical but app stability is more).
        // Actually, user said "Queue should retry". "queue size limit... optional". 
        // I'll stick to Unbounded for now as it's simplest and "Never block API" is key.
        
        _queue = Channel.CreateUnbounded<AuditLogItem>();
    }

    public void Enqueue(AuditLogItem item)
    {
        if (_queue.Writer.TryWrite(item))
        {
            Interlocked.Increment(ref _queueDepth);
        }
    }

    public async Task<AuditLogItem> DequeueAsync(CancellationToken cancellationToken)
    {
        var item = await _queue.Reader.ReadAsync(cancellationToken);
        Interlocked.Decrement(ref _queueDepth);
        return item;
    }

    public void Retry(AuditLogItem item)
    {
        Interlocked.Increment(ref _retryCount);
        Enqueue(item);
    }
}
