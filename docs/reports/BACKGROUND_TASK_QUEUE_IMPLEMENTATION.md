# Background Task Queue Implementation for RedirectUrl Recovery

## Summary

Replaced the fire-and-forget `Task.Run` pattern in `StripePaymentService` with a proper managed background queue system using `IHostedService` and `Channel<T>`. This ensures graceful shutdown, proper lifecycle management, and observable telemetry for background RedirectUrl recovery tasks.

## Changes Made

### 1. New Infrastructure Components

#### `RedirectUrlRecoveryWorkItem.cs`
- Work item model containing TenantId, OrderId, CorrelationId, and EnqueuedAt timestamp
- Used to pass recovery tasks through the queue

#### `IBackgroundTaskQueue<T>.cs`
- Generic interface for background task queues
- Provides `QueueAsync` and `DequeueAsync` methods
- Reusable for other background task patterns

#### `BackgroundTaskQueue<T>.cs`
- Channel-based implementation using `System.Threading.Channels`
- Bounded channel with capacity of 1000 items
- `DropWrite` behavior to prevent memory issues under load
- Logs warnings when queue is full

#### `RedirectUrlRecoveryHostedService.cs`
- `BackgroundService` implementation that processes the queue
- Respects `IHostApplicationLifetime` for graceful shutdown
- Uses proper cancellation tokens (not `CancellationToken.None`)
- Comprehensive telemetry:
  - Queue time tracking
  - Processing duration
  - Success/failure logging
  - Stripe API error handling
- Creates scoped services for each work item
- Handles Stripe configuration retrieval and API calls

### 2. Updated `StripePaymentService.cs`

#### Constructor Changes
- Added `IBackgroundTaskQueue<RedirectUrlRecoveryWorkItem>` dependency
- Injected via DI container

#### Webhook Processing Changes
- Replaced `Task.Run` fire-and-forget with `_recoveryQueue.QueueAsync()`
- Added telemetry for enqueue success/failure
- Logs when queue is full (non-critical warning)

#### Removed Dead Code
- Removed `RecoverRedirectUrlAsync` (never called)
- Removed `RecoverRedirectUrlInScopeAsync` (replaced by hosted service)

### 3. DI Registration (`Program.cs`)

```csharp
// Background Task Queue (Singleton)
builder.Services.AddSingleton<IBackgroundTaskQueue<RedirectUrlRecoveryWorkItem>>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<BackgroundTaskQueue<RedirectUrlRecoveryWorkItem>>>();
    return new BackgroundTaskQueue<RedirectUrlRecoveryWorkItem>(1000, logger);
});

// Hosted Service
builder.Services.AddHostedService<RedirectUrlRecoveryHostedService>();
```

## Benefits

### 1. Graceful Shutdown
- Hosted service respects application lifetime
- In-flight work items complete before shutdown
- No orphaned background tasks

### 2. Observable Telemetry
- Queue time tracking (time from enqueue to processing start)
- Processing duration tracking
- Success/failure metrics
- Stripe API error visibility
- All failures are logged (not silently swallowed)

### 3. Backpressure Handling
- Bounded queue prevents memory exhaustion
- DropWrite behavior under extreme load
- Warnings logged when queue is full

### 4. Proper Cancellation
- Uses `IHostApplicationLifetime` cancellation token
- Respects shutdown signals
- No more `CancellationToken.None`

### 5. Testability
- Queue interface can be mocked
- Hosted service can be tested independently
- Work items are simple POCOs

## Migration Notes

### Before
```csharp
var recoveryTask = Task.Run(async () =>
{
    try
    {
        using var scope = _serviceScopeFactory.CreateScope();
        // ... recovery logic
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Recovery failed");
    }
});
// Fire and forget - no tracking
```

### After
```csharp
var workItem = new RedirectUrlRecoveryWorkItem
{
    TenantId = tenantId,
    OrderId = orderId,
    CorrelationId = correlationId,
    EnqueuedAt = DateTime.UtcNow
};

var enqueued = await _recoveryQueue.QueueAsync(workItem);

if (enqueued)
{
    _logger.LogInformation("Enqueued RedirectUrl recovery work item | OrderId: {OrderId}", orderId);
}
else
{
    _logger.LogWarning("Failed to enqueue - queue full | OrderId: {OrderId}", orderId);
}
```

## Production Considerations

### Queue Capacity
- Default: 1000 items
- Adjust based on webhook volume and Stripe API latency
- Monitor queue full warnings in production logs

### Hosted Service Lifecycle
- Starts automatically with application
- Stops gracefully on shutdown
- Processes items sequentially (one at a time)
- Consider multiple workers if throughput is insufficient

### Monitoring
- Track queue time metrics
- Alert on queue full warnings
- Monitor processing duration
- Track Stripe API error rates

## Future Enhancements

1. **Multiple Workers**: Process items in parallel for higher throughput
2. **Retry Logic**: Add exponential backoff for transient Stripe API failures
3. **Dead Letter Queue**: Move failed items to a separate queue for manual review
4. **Metrics Export**: Export queue metrics to Prometheus/Application Insights
5. **Priority Queue**: Prioritize certain tenants or high-value orders

## Testing

### Unit Tests Needed
- `BackgroundTaskQueue<T>` enqueue/dequeue behavior
- Queue full scenarios
- Hosted service graceful shutdown
- Work item processing success/failure paths

### Integration Tests Needed
- End-to-end webhook → queue → recovery flow
- Graceful shutdown with in-flight work items
- Queue capacity limits under load

## Files Modified

1. `PointOfSale.API/Services/BackgroundTasks/RedirectUrlRecoveryWorkItem.cs` (new)
2. `PointOfSale.API/Services/BackgroundTasks/IBackgroundTaskQueue.cs` (new)
3. `PointOfSale.API/Services/BackgroundTasks/BackgroundTaskQueue.cs` (new)
4. `PointOfSale.API/Services/BackgroundTasks/RedirectUrlRecoveryHostedService.cs` (new)
5. `PointOfSale.API/Services/Payments/Stripe/StripePaymentService.cs` (modified)
6. `PointOfSale.API/Program.cs` (modified)

## Deployment Notes

- No database migrations required
- No configuration changes required
- Backward compatible (no breaking changes to public APIs)
- Hosted service starts automatically on deployment
- Existing in-flight webhooks will use the new queue system immediately
