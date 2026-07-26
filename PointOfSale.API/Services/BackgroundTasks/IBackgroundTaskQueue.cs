namespace PointOfSale.API.Services.BackgroundTasks;

/// <summary>
/// Interface for a background task queue that processes work items asynchronously.
/// </summary>
public interface IBackgroundTaskQueue<T> where T : class
{
    /// <summary>
    /// Enqueues a work item for background processing.
    /// </summary>
    /// <param name="workItem">The work item to enqueue.</param>
    /// <param name="cancellationToken">Optional cancellation token for the enqueue operation.</param>
    /// <returns>
    /// True if the item was enqueued successfully, false if the queue is full (for non-blocking queues) 
    /// or operation was cancelled.
    /// 
    /// IMPORTANT: For critical work items (e.g., RedirectUrlRecoveryWorkItem), this method may BLOCK 
    /// until space becomes available in the queue. Callers should be aware that the behavior depends 
    /// on the work item type and queue configuration.
    /// </returns>
    ValueTask<bool> QueueAsync(T workItem, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dequeues a work item for processing.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for graceful shutdown.</param>
    /// <returns>The next work item to process.</returns>
    ValueTask<T> DequeueAsync(CancellationToken cancellationToken);
}
