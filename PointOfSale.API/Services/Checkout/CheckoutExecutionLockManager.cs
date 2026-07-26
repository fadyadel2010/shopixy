using System.Diagnostics;
using Microsoft.Extensions.Options;
using PointOfSale.Infrastructure.Redis.Services;

namespace PointOfSale.API.Services.Checkout;

public sealed class CheckoutExecutionLockOptions
{
    public TimeSpan LockTtl { get; set; } = TimeSpan.FromMinutes(2);
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan MaxExecutionTime { get; set; } = TimeSpan.FromMinutes(5);
    public double HighContentionWarningThreshold { get; set; } = 0.25d;
    public int HighContentionMinimumAttempts { get; set; } = 10;
}

public interface ICheckoutExecutionLockLease : IAsyncDisposable
{
    Guid CartId { get; }
    string CheckoutId { get; }
    bool OwnershipLost { get; }
    void ThrowIfOwnershipLost();
}

public interface ICheckoutExecutionLockManager
{
    Task<ICheckoutExecutionLockLease?> TryAcquireAsync(
        Guid tenantId,
        Guid cartId,
        string checkoutId,
        string correlationId,
        CancellationToken cancellationToken = default);
}

public sealed class CheckoutExecutionLockManager : ICheckoutExecutionLockManager
{
    private readonly IDistributedLockService _distributedLockService;
    private readonly CheckoutExecutionLockOptions _options;
    private readonly CheckoutLockTelemetry _telemetry;
    private readonly ILogger<CheckoutExecutionLockManager> _logger;

    public CheckoutExecutionLockManager(
        IDistributedLockService distributedLockService,
        IOptions<CheckoutExecutionLockOptions> options,
        CheckoutLockTelemetry telemetry,
        ILogger<CheckoutExecutionLockManager> logger)
    {
        _distributedLockService = distributedLockService;
        _options = options.Value;
        _telemetry = telemetry;
        _logger = logger;
    }

    public async Task<ICheckoutExecutionLockLease?> TryAcquireAsync(
        Guid tenantId,
        Guid cartId,
        string checkoutId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var acquisitionStopwatch = Stopwatch.StartNew();
        var ttl = NormalizeLockTtl(_options.LockTtl);
        var heartbeatInterval = NormalizeHeartbeatInterval(ttl, _options.HeartbeatInterval);
        var maxExecutionTime = NormalizeMaxExecutionTime(_options.MaxExecutionTime);
        var lockToken = Guid.NewGuid().ToString("N");
        var ownedLocks = new[]
        {
            BuildCheckoutLockKey(tenantId, checkoutId),
            BuildCartLockKey(tenantId, cartId),
        };

        var checkoutLockResult = DistributedLockResult.InfrastructureFailure();
        try
        {
            checkoutLockResult = await _distributedLockService.TryAcquireLockAsync(
                ownedLocks[0],
                lockToken,
                ttl);
        }
        catch (Exception ex)
        {
            acquisitionStopwatch.Stop();
            _telemetry.RecordInfrastructureFailure();
            _logger.LogError(
                ex,
                "Checkout lock acquisition threw exception | CheckoutId: {CheckoutId} | CorrelationId: {CorrelationId}",
                checkoutId,
                correlationId);
            throw new CheckoutLockInfrastructureException(cartId);
        }

        if (checkoutLockResult.IsInfrastructureFailure)
        {
            acquisitionStopwatch.Stop();
            _telemetry.RecordInfrastructureFailure();
            _logger.LogError(
                "Checkout lock infrastructure failure | CheckoutId: {CheckoutId} | CorrelationId: {CorrelationId}",
                checkoutId,
                correlationId);
            throw new CheckoutLockInfrastructureException(cartId);
        }

        if (!checkoutLockResult.IsAcquired)
        {
            acquisitionStopwatch.Stop();
            _telemetry.RecordAcquisitionAttempt(acquisitionStopwatch.Elapsed, acquired: false);
            LogHighContentionIfNeeded(cartId, checkoutId, correlationId);
            _logger.LogWarning(
                "Checkout execution lock already held | CheckoutId: {CheckoutId} | CorrelationId: {CorrelationId}",
                checkoutId,
                correlationId);
            return null;
        }

        var cartLockAcquired = false;

        try
        {
            var cartLockResult = DistributedLockResult.InfrastructureFailure();
            try
            {
                cartLockResult = await _distributedLockService.TryAcquireLockAsync(
                    ownedLocks[1],
                    lockToken,
                    ttl);
            }
            catch (Exception ex)
            {
                acquisitionStopwatch.Stop();
                _telemetry.RecordInfrastructureFailure();
                _logger.LogError(
                    ex,
                    "Cart lock acquisition threw exception | CartId: {CartId} | CheckoutId: {CheckoutId} | CorrelationId: {CorrelationId}",
                    cartId,
                    checkoutId,
                    correlationId);
                throw new CheckoutLockInfrastructureException(cartId);
            }

            if (cartLockResult.IsInfrastructureFailure)
            {
                acquisitionStopwatch.Stop();
                _telemetry.RecordInfrastructureFailure();
                _logger.LogError(
                    "Cart lock infrastructure failure | CartId: {CartId} | CheckoutId: {CheckoutId} | CorrelationId: {CorrelationId}",
                    cartId,
                    checkoutId,
                    correlationId);
                throw new CheckoutLockInfrastructureException(cartId);
            }

            cartLockAcquired = cartLockResult.IsAcquired;
            if (!cartLockAcquired)
            {
                acquisitionStopwatch.Stop();
                _telemetry.RecordAcquisitionAttempt(acquisitionStopwatch.Elapsed, acquired: false);
                LogHighContentionIfNeeded(cartId, checkoutId, correlationId);
                _logger.LogWarning(
                    "Cart execution lock already held | CartId: {CartId} | CheckoutId: {CheckoutId} | CorrelationId: {CorrelationId}",
                    cartId,
                    checkoutId,
                    correlationId);
                return null;
            }

            acquisitionStopwatch.Stop();
            _telemetry.RecordAcquisitionAttempt(acquisitionStopwatch.Elapsed, acquired: true);
            _logger.LogInformation(
                "Checkout locks acquired | CartId: {CartId} | CheckoutId: {CheckoutId} | AcquisitionMs: {AcquisitionMs} | CorrelationId: {CorrelationId}",
                cartId,
                checkoutId,
                acquisitionStopwatch.Elapsed.TotalMilliseconds,
                correlationId);

            return new CheckoutExecutionLockLease(
                _distributedLockService,
                _telemetry,
                _logger,
                cartId,
                checkoutId,
                lockToken,
                ownedLocks,
                ttl,
                heartbeatInterval,
                maxExecutionTime,
                correlationId);
        }
        finally
        {
            if (!cartLockAcquired)
            {
                // Best-effort release: do not let exceptions mask the original failure
                try
                {
                    await _distributedLockService.ReleaseLockAsync(ownedLocks[0], lockToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to release checkout lock during cleanup | CheckoutId: {CheckoutId} | CorrelationId: {CorrelationId}",
                        checkoutId,
                        correlationId);
                }
            }
        }
    }

    internal static string BuildCheckoutLockKey(Guid tenantId, string checkoutId) =>
        $"lock:checkout:{tenantId:N}:{checkoutId}";

    internal static string BuildCartLockKey(Guid tenantId, Guid cartId) =>
        $"lock:checkout:cart:{tenantId:N}:{cartId:N}";

    private static TimeSpan NormalizeLockTtl(TimeSpan ttl) =>
        ttl <= TimeSpan.Zero ? TimeSpan.FromMinutes(2) : ttl;

    private static TimeSpan NormalizeMaxExecutionTime(TimeSpan maxExecutionTime) =>
        maxExecutionTime <= TimeSpan.Zero ? TimeSpan.FromMinutes(5) : maxExecutionTime;

    private static TimeSpan NormalizeHeartbeatInterval(TimeSpan ttl, TimeSpan requestedHeartbeat)
    {
        if (requestedHeartbeat <= TimeSpan.Zero)
        {
            return TimeSpan.FromMilliseconds(Math.Max(1, ttl.TotalMilliseconds / 3d));
        }

        var maxHeartbeat = TimeSpan.FromMilliseconds(Math.Max(1, ttl.TotalMilliseconds / 2d));
        return requestedHeartbeat < maxHeartbeat ? requestedHeartbeat : maxHeartbeat;
    }

    private void LogHighContentionIfNeeded(Guid cartId, string checkoutId, string correlationId)
    {
        var threshold = _options.HighContentionWarningThreshold is > 0d and <= 1d
            ? _options.HighContentionWarningThreshold
            : 0.25d;
        var minimumAttempts = _options.HighContentionMinimumAttempts > 0
            ? _options.HighContentionMinimumAttempts
            : 10;

        if (_telemetry.ShouldWarnHighContention(threshold, minimumAttempts))
        {
            _logger.LogWarning(
                "Checkout lock contention rate high | CartId: {CartId} | CheckoutId: {CheckoutId} | ContentionRate: {ContentionRate:P1} | Attempts: {Attempts} | CorrelationId: {CorrelationId}",
                cartId,
                checkoutId,
                _telemetry.CurrentContentionRate,
                _telemetry.AttemptTotal,
                correlationId);
        }
    }

    private sealed class CheckoutExecutionLockLease : ICheckoutExecutionLockLease
    {
        private readonly IDistributedLockService _distributedLockService;
        private readonly CheckoutLockTelemetry _telemetry;
        private readonly ILogger _logger;
        private readonly string _lockToken;
        private readonly string[] _lockKeys;
        private readonly TimeSpan _lockTtl;
        private readonly TimeSpan _maxExecutionTime;
        private readonly string _correlationId;
        private readonly CancellationTokenSource _heartbeatCancellationSource;
        private readonly Task _heartbeatTask;
        private readonly Stopwatch _executionStopwatch;
        private int _ownershipLost;
        private int _infrastructureFailure;
        private int _disposed;

        public CheckoutExecutionLockLease(
            IDistributedLockService distributedLockService,
            CheckoutLockTelemetry telemetry,
            ILogger logger,
            Guid cartId,
            string checkoutId,
            string lockToken,
            string[] lockKeys,
            TimeSpan lockTtl,
            TimeSpan heartbeatInterval,
            TimeSpan maxExecutionTime,
            string correlationId)
        {
            _distributedLockService = distributedLockService;
            _telemetry = telemetry;
            _logger = logger;
            _lockToken = lockToken;
            _lockKeys = lockKeys;
            _lockTtl = lockTtl;
            _maxExecutionTime = maxExecutionTime;
            _correlationId = correlationId;
            _heartbeatCancellationSource = new CancellationTokenSource();
            _executionStopwatch = Stopwatch.StartNew();
            CartId = cartId;
            CheckoutId = checkoutId;
            _heartbeatTask = Task.Run(
                () => RunHeartbeatAsync(heartbeatInterval, _heartbeatCancellationSource.Token),
                CancellationToken.None);
        }

        public Guid CartId { get; }

        public string CheckoutId { get; }

        public bool OwnershipLost => Volatile.Read(ref _ownershipLost) == 1;

        public void ThrowIfOwnershipLost()
        {
            if (OwnershipLost)
            {
                if (Volatile.Read(ref _infrastructureFailure) == 1)
                {
                    throw new CheckoutLockInfrastructureException(CartId);
                }

                throw new CheckoutLockException(CartId);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return;
            }

            _heartbeatCancellationSource.Cancel();

            try
            {
                await _heartbeatTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _heartbeatCancellationSource.Dispose();
            }

            // Best-effort release: iterate all locks and suppress exceptions
            for (var index = _lockKeys.Length - 1; index >= 0; index--)
            {
                try
                {
                    await _distributedLockService.ReleaseLockAsync(_lockKeys[index], _lockToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to release lock during disposal | LockKey: {LockKey} | CartId: {CartId} | CheckoutId: {CheckoutId} | CorrelationId: {CorrelationId}",
                        _lockKeys[index],
                        CartId,
                        CheckoutId,
                        _correlationId);
                }
            }

            _logger.LogInformation(
                "Checkout locks released | CartId: {CartId} | CheckoutId: {CheckoutId} | CorrelationId: {CorrelationId}",
                CartId,
                CheckoutId,
                _correlationId);
        }

        private async Task RunHeartbeatAsync(TimeSpan heartbeatInterval, CancellationToken cancellationToken)
        {
            using var timer = new PeriodicTimer(heartbeatInterval);

            try
            {
                while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (_executionStopwatch.Elapsed >= _maxExecutionTime)
                    {
                        _telemetry.RecordExecutionTimeout();
                        Interlocked.Exchange(ref _ownershipLost, 1);
                        _logger.LogWarning(
                            "Checkout lock max execution time exceeded | CartId: {CartId} | CheckoutId: {CheckoutId} | MaxExecutionTimeSeconds: {MaxExecutionTimeSeconds} | CorrelationId: {CorrelationId}",
                            CartId,
                            CheckoutId,
                            _maxExecutionTime.TotalSeconds,
                            _correlationId);
                        return;
                    }

                    foreach (var lockKey in _lockKeys)
                    {
                        var refreshResult = await _distributedLockService.RefreshLockAsync(
                            lockKey,
                            _lockToken,
                            _lockTtl);

                        if (refreshResult.IsInfrastructureFailure)
                        {
                            _telemetry.RecordInfrastructureFailure();
                            Interlocked.Exchange(ref _infrastructureFailure, 1);
                            Interlocked.Exchange(ref _ownershipLost, 1);
                            _logger.LogError(
                                "Checkout lock heartbeat infrastructure failure | Key: {LockKey} | CartId: {CartId} | CheckoutId: {CheckoutId} | CorrelationId: {CorrelationId}",
                                lockKey,
                                CartId,
                                CheckoutId,
                                _correlationId);
                            return;
                        }

                        if (!refreshResult.IsAcquired)
                        {
                            Interlocked.Exchange(ref _ownershipLost, 1);
                            _logger.LogError(
                                "Checkout lock ownership lost during heartbeat | Key: {LockKey} | CartId: {CartId} | CheckoutId: {CheckoutId} | CorrelationId: {CorrelationId}",
                                lockKey,
                                CartId,
                                CheckoutId,
                                _correlationId);
                            return;
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _telemetry.RecordInfrastructureFailure();
                Interlocked.Exchange(ref _infrastructureFailure, 1);
                Interlocked.Exchange(ref _ownershipLost, 1);
                _logger.LogError(
                    ex,
                    "Checkout lock heartbeat failed | CartId: {CartId} | CheckoutId: {CheckoutId} | CorrelationId: {CorrelationId}",
                    CartId,
                    CheckoutId,
                    _correlationId);
            }
        }
    }
}
