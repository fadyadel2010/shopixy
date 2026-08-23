using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PointOfSale.API.Services.ProductAuthoring.Bulk;

/// <summary>
/// Active periodic renewal of an owned bulk idempotency claim lease.
///
/// The outer claim is fenced by a bounded lease; a single slow item authoring
/// call can otherwise outlive the lease and let another execution take over a
/// live worker. The heartbeat renews the lease on a fixed interval that is
/// safely below the lease duration (one third by default) for the entire owned
/// execution, including while an item authoring call is awaiting. Renewal
/// remains the existing atomic compare-and-swap over the durable lease token,
/// so a stale worker after takeover can never renew: its next attempt reports
/// lost ownership and stops the loop permanently.
/// </summary>
internal sealed class BulkClaimLeaseHeartbeat : IAsyncDisposable
{
    /// <summary>
    /// Default renewal interval: one third of the claim lease duration, keeping
    /// at least two successful renewal opportunities inside every lease window.
    /// </summary>
    public static readonly TimeSpan DefaultInterval =
        TimeSpan.FromTicks(BulkIdempotencyService.DefaultClaimLeaseDuration.Ticks / 3);

    private readonly Func<CancellationToken, Task<bool>> _renewOnceAsync;
    private readonly Action _ownershipLostCallback;
    private readonly int _maxConsecutiveFailures;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _stopSource = new();
    private readonly Task _loopTask;

    private BulkClaimLeaseHeartbeat(
        Func<CancellationToken, Task<bool>> renewOnceAsync,
        TimeSpan interval,
        Action ownershipLostCallback,
        int maxConsecutiveFailures,
        ILogger logger)
    {
        _renewOnceAsync = renewOnceAsync;
        Interval = interval == Timeout.InfiniteTimeSpan || interval > TimeSpan.Zero
            ? interval
            : throw new ArgumentOutOfRangeException(nameof(interval), interval, "Heartbeat interval must be positive.");
        _ownershipLostCallback = ownershipLostCallback;
        _maxConsecutiveFailures = Math.Max(1, maxConsecutiveFailures);
        _logger = logger;
        _loopTask = RunAsync();
    }

    public TimeSpan Interval { get; }

    /// <summary>
    /// Starts the periodic renewal loop. <paramref name="renewOnceAsync"/> must
    /// perform one atomic lease compare-and-swap and return false exactly when
    /// ownership was lost; thrown exceptions count as unproven ownership and are
    /// tolerated only while they cannot span the lease window.
    /// </summary>
    public static BulkClaimLeaseHeartbeat Start(
        Func<CancellationToken, Task<bool>> renewOnceAsync,
        TimeSpan interval,
        Action ownershipLostCallback,
        ILogger? logger = null,
        int maxConsecutiveFailures = 3) =>
        new(renewOnceAsync, interval, ownershipLostCallback, maxConsecutiveFailures, logger ?? NullLogger.Instance);

    private async Task RunAsync()
    {
        var consecutiveFailures = 0;
        while (!_stopSource.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Interval, _stopSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_stopSource.IsCancellationRequested)
            {
                break;
            }

            try
            {
                var renewed = await _renewOnceAsync(_stopSource.Token).ConfigureAwait(false);
                if (!renewed)
                {
                    // The atomic renewal CAS failed: another execution owns the
                    // claim. Never touch it again and interrupt the item work.
                    NotifyOwnershipLost();
                    break;
                }

                consecutiveFailures = 0;
            }
            catch (OperationCanceledException) when (_stopSource.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                consecutiveFailures++;
                _logger.LogWarning(
                    exception,
                    "Bulk lease heartbeat renewal failed | ConsecutiveFailures: {ConsecutiveFailures}",
                    consecutiveFailures);

                // Ownership can no longer be guaranteed once failures span
                // roughly one lease window without a successful renewal.
                if (consecutiveFailures >= _maxConsecutiveFailures)
                {
                    NotifyOwnershipLost();
                    break;
                }
            }
        }
    }

    private void NotifyOwnershipLost()
    {
        try
        {
            _ownershipLostCallback();
        }
        catch (Exception callbackException)
        {
            _logger.LogError(callbackException, "Bulk lease ownership-lost callback threw unexpectedly.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stopSource.CancelAsync().ConfigureAwait(false);
        try
        {
            await _loopTask.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The loop contains every renewal failure internally.
        }
        finally
        {
            _stopSource.Dispose();
        }
    }
}
