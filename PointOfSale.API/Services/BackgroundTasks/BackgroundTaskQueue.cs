using System.Threading.Channels;

namespace PointOfSale.API.Services.BackgroundTasks;

/// <summary>
/// Channel-based background task queue for processing work items asynchronously.
/// 
/// BEHAVIOR NOTES:
/// - For RedirectUrlRecoveryWorkItem: Uses BoundedChannelFullMode.Wait (BLOCKING behavior when queue is full)
/// - For other work items: Uses BoundedChannelFullMode.DropWrite (returns false immediately when queue is full)
/// 
/// This type-based behavior ensures critical payment recovery tasks are never dropped, but may block 
/// request threads if the queue is full. Callers should be aware of this blocking behavior for payment recovery.
/// </summary>
public sealed class BackgroundTaskQueue<T> : IBackgroundTaskQueue<T> where T : class
{
    private readonly Channel<T> _queue;
    private readonly ILogger<BackgroundTaskQueue<T>> _logger;
    private readonly bool _isPaymentRecovery;

    public BackgroundTaskQueue(int capacity, ILogger<BackgroundTaskQueue<T>> logger)
    {
        _logger = logger;
        _isPaymentRecovery = typeof(T) == typeof(RedirectUrlRecoveryWorkItem);
        
        // For payment recovery work items, use Wait strategy to avoid losing critical recovery actions
        // For other work items, use DropWrite to prevent memory issues
        var options = new BoundedChannelOptions(capacity)
        {
            FullMode = _isPaymentRecovery 
                ? BoundedChannelFullMode.Wait 
                : BoundedChannelFullMode.DropWrite
        };
        
        _queue = Channel.CreateBounded<T>(options);
    }

    public async ValueTask<bool> QueueAsync(T workItem, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);

        if (_isPaymentRecovery)
        {
            // BLOCKING BEHAVIOR: For payment recovery, wait for space to become available
            // This may block the calling thread if the queue is full. Returns false only on cancellation.
            // This ensures critical payment recovery tasks are never dropped.
            try
            {
                await _queue.Writer.WriteAsync(workItem, cancellationToken);
                return true;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning(
                    "Failed to enqueue payment recovery work item - operation cancelled | WorkItemType: {WorkItemType}",
                    typeof(T).Name);
                return false;
            }
        }
        else
        {
            // NON-BLOCKING BEHAVIOR: For other work items, use TryWrite which returns false immediately if queue is full
            var result = _queue.Writer.TryWrite(workItem);
            
            if (!result)
            {
                _logger.LogWarning(
                    "Failed to enqueue work item - queue is full | WorkItemType: {WorkItemType}",
                    typeof(T).Name);
            }

            return result;
        }
    }

    public async ValueTask<T> DequeueAsync(CancellationToken cancellationToken)
    {
        return await _queue.Reader.ReadAsync(cancellationToken);
    }
}
