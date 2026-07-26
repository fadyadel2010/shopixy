using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PointOfSale.API.Services.Checkout;
using PointOfSale.Infrastructure.Redis.Services;

namespace PointOfSale.Tests;

public sealed class CheckoutExecutionLockManagerTests
{
    [Fact]
    public async Task TryAcquireAsync_WhenParallelRequestsTargetSameCart_OnlyOneLeaseIsGranted()
    {
        var lockService = new InMemoryDistributedLockService();
        var manager = CreateManager(
            lockService,
            new CheckoutExecutionLockOptions
            {
                LockTtl = TimeSpan.FromSeconds(1),
                HeartbeatInterval = TimeSpan.FromMilliseconds(100)
            });

        var tenantId = Guid.NewGuid();
        var cartId = Guid.NewGuid();

        var acquisitionTasks = new[]
        {
            Task.Run(() => manager.TryAcquireAsync(
                tenantId,
                cartId,
                Guid.NewGuid().ToString("D"),
                "trace-parallel-1",
                CancellationToken.None)),
            Task.Run(() => manager.TryAcquireAsync(
                tenantId,
                cartId,
                Guid.NewGuid().ToString("D"),
                "trace-parallel-2",
                CancellationToken.None))
        };

        var leases = await Task.WhenAll(acquisitionTasks);

        Assert.Equal(1, leases.Count(lease => lease != null));
        Assert.Equal(1, leases.Count(lease => lease == null));

        foreach (var lease in leases.Where(lease => lease != null))
        {
            await lease!.DisposeAsync();
        }
    }

    [Fact]
    public async Task TryAcquireAsync_HeartbeatPreventsMidFlightExpiry_AndBlocksSecondCheckout()
    {
        var lockService = new InMemoryDistributedLockService();
        var manager = CreateManager(
            lockService,
            new CheckoutExecutionLockOptions
            {
                LockTtl = TimeSpan.FromMilliseconds(1000),
                HeartbeatInterval = TimeSpan.FromMilliseconds(200)
            });

        var tenantId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        await using var firstLease = await manager.TryAcquireAsync(
            tenantId,
            cartId,
            Guid.NewGuid().ToString("D"),
            "trace-1",
            CancellationToken.None);

        Assert.NotNull(firstLease);

        await Task.Delay(2000);

        var secondLease = await manager.TryAcquireAsync(
            tenantId,
            cartId,
            Guid.NewGuid().ToString("D"),
            "trace-2",
            CancellationToken.None);

        Assert.Null(secondLease);
        Assert.False(firstLease!.OwnershipLost);
    }

    [Fact]
    public async Task ThrowIfOwnershipLost_WhenHeartbeatCannotRefresh_ThrowsCheckoutLockException()
    {
        var lockService = new InMemoryDistributedLockService
        {
            FailRefreshAfterFirstSuccess = true
        };

        var manager = CreateManager(
            lockService,
            new CheckoutExecutionLockOptions
            {
                LockTtl = TimeSpan.FromMilliseconds(1000),
                HeartbeatInterval = TimeSpan.FromMilliseconds(200)
            });

        var tenantId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        await using var lease = await manager.TryAcquireAsync(
            tenantId,
            cartId,
            Guid.NewGuid().ToString("D"),
            "trace-lock-loss",
            CancellationToken.None);

        Assert.NotNull(lease);

        await Task.Delay(1500);

        Assert.True(lease!.OwnershipLost);
        Assert.Throws<CheckoutLockException>(() => lease.ThrowIfOwnershipLost());
    }

    [Fact]
    public async Task ThrowIfOwnershipLost_WhenMaxExecutionTimeExceeded_RecordsTimeout_AndThrowsCheckoutLockException()
    {
        var lockService = new InMemoryDistributedLockService();
        var telemetry = new CheckoutLockTelemetry();
        var manager = CreateManager(
            lockService,
            new CheckoutExecutionLockOptions
            {
                LockTtl = TimeSpan.FromMilliseconds(1000),
                HeartbeatInterval = TimeSpan.FromMilliseconds(200),
                MaxExecutionTime = TimeSpan.FromMilliseconds(800)
            },
            telemetry);

        var tenantId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        await using var lease = await manager.TryAcquireAsync(
            tenantId,
            cartId,
            Guid.NewGuid().ToString("D"),
            "trace-timeout",
            CancellationToken.None);

        Assert.NotNull(lease);

        await Task.Delay(1500);

        Assert.True(lease!.OwnershipLost);
        Assert.Throws<CheckoutLockException>(() => lease.ThrowIfOwnershipLost());
        Assert.Equal(1, telemetry.TimeoutTotal);
    }

    [Fact]
    public async Task TryAcquireAsync_WhenLockInfrastructureFails_ThrowsCheckoutLockInfrastructureException_AndRecordsTelemetry()
    {
        var lockService = new InMemoryDistributedLockService
        {
            FailAcquireWithInfrastructureFailure = true
        };
        var telemetry = new CheckoutLockTelemetry();
        var manager = CreateManager(
            lockService,
            new CheckoutExecutionLockOptions(),
            telemetry);

        var exception = await Assert.ThrowsAsync<CheckoutLockInfrastructureException>(() =>
            manager.TryAcquireAsync(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid().ToString("D"),
                "trace-infra",
                CancellationToken.None));

        Assert.Equal("CHECKOUT_LOCK_INFRASTRUCTURE_FAILURE", exception.ErrorCode);
        Assert.Equal(1, telemetry.InfrastructureFailureTotal);
    }

    private static CheckoutExecutionLockManager CreateManager(
        IDistributedLockService distributedLockService,
        CheckoutExecutionLockOptions options,
        CheckoutLockTelemetry? telemetry = null) =>
        new(
            distributedLockService,
            Options.Create(options),
            telemetry ?? new CheckoutLockTelemetry(),
            NullLogger<CheckoutExecutionLockManager>.Instance);

    private sealed class InMemoryDistributedLockService : IDistributedLockService
    {
        private readonly Lock _gate = new();
        private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
        private int _refreshSuccessCount;

        public bool FailAcquireWithInfrastructureFailure { get; set; }
        public bool FailRefreshAfterFirstSuccess { get; set; }
        public bool FailRefreshWithInfrastructureFailure { get; set; }

        public Task<DistributedLockResult> TryAcquireLockAsync(string lockKey, string lockValue, TimeSpan ttl)
        {
            lock (_gate)
            {
                if (FailAcquireWithInfrastructureFailure)
                {
                    return Task.FromResult(DistributedLockResult.InfrastructureFailure());
                }

                RemoveExpiredEntries();

                if (_entries.ContainsKey(lockKey))
                {
                    return Task.FromResult(DistributedLockResult.AlreadyHeld());
                }

                _entries[lockKey] = new Entry(lockValue, DateTime.UtcNow.Add(ttl));
                return Task.FromResult(DistributedLockResult.Acquired());
            }
        }

        public Task<DistributedLockResult> RefreshLockAsync(string lockKey, string lockValue, TimeSpan ttl)
        {
            lock (_gate)
            {
                if (FailRefreshWithInfrastructureFailure)
                {
                    return Task.FromResult(DistributedLockResult.InfrastructureFailure());
                }

                RemoveExpiredEntries();

                if (!_entries.TryGetValue(lockKey, out var entry) || !string.Equals(entry.Value, lockValue, StringComparison.Ordinal))
                {
                    return Task.FromResult(DistributedLockResult.AlreadyHeld());
                }

                var refreshAttempt = ++_refreshSuccessCount;
                if (FailRefreshAfterFirstSuccess && refreshAttempt > 1)
                {
                    _entries.Remove(lockKey);
                    return Task.FromResult(DistributedLockResult.AlreadyHeld());
                }

                entry.ExpiresAtUtc = DateTime.UtcNow.Add(ttl);
                return Task.FromResult(DistributedLockResult.Acquired());
            }
        }

        public Task<bool> ReleaseLockAsync(string lockKey, string lockValue)
        {
            lock (_gate)
            {
                RemoveExpiredEntries();

                if (_entries.TryGetValue(lockKey, out var entry) &&
                    string.Equals(entry.Value, lockValue, StringComparison.Ordinal))
                {
                    _entries.Remove(lockKey);
                    return Task.FromResult(true);
                }

                return Task.FromResult(false);
            }
        }

        public Task<bool> IsLockedAsync(string lockKey)
        {
            lock (_gate)
            {
                RemoveExpiredEntries();
                return Task.FromResult(_entries.ContainsKey(lockKey));
            }
        }

        private void RemoveExpiredEntries()
        {
            var now = DateTime.UtcNow;
            var expiredKeys = _entries
                .Where(pair => pair.Value.ExpiresAtUtc <= now)
                .Select(pair => pair.Key)
                .ToList();

            foreach (var expiredKey in expiredKeys)
            {
                _entries.Remove(expiredKey);
            }
        }

        private sealed class Entry
        {
            public Entry(string value, DateTime expiresAtUtc)
            {
                Value = value;
                ExpiresAtUtc = expiresAtUtc;
            }

            public string Value { get; }
            public DateTime ExpiresAtUtc { get; set; }
        }
    }
}
