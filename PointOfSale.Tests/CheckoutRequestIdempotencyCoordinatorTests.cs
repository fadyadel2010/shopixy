using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PointOfSale.API.DTOs.Checkout;
using PointOfSale.API.Services.Checkout;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;
using System.Reflection;

namespace PointOfSale.Tests;

public sealed class CheckoutRequestIdempotencyCoordinatorTests
{
    [Fact]
    public async Task TryClaimAsync_WhenParallelSameCheckoutId_OnlyOneClaimIsGranted()
    {
        var tenantId = Guid.NewGuid();
        await using var store = await SqliteCheckoutIdempotencyStore.CreateAsync(tenantId);
        var checkoutId = Guid.NewGuid().ToString("D");
        const string requestHash = "hash-parallel";

        async Task<CheckoutIdempotencyClaimStatus> ClaimAsync()
        {
            await using var context = store.CreateDbContext();
            var coordinator = CreateCoordinator(context);
            var result = await coordinator.TryClaimAsync(tenantId, checkoutId, requestHash, requestHash, CancellationToken.None);
            return result.Status;
        }

        var statuses = await Task.WhenAll(
            Task.Run(ClaimAsync),
            Task.Run(ClaimAsync));

        Assert.Equal(1, statuses.Count(status => status == CheckoutIdempotencyClaimStatus.Claimed));
        Assert.Equal(1, statuses.Count(status => status == CheckoutIdempotencyClaimStatus.InProgress));

        await using var verificationContext = store.CreateDbContext();
        Assert.Equal(1, await verificationContext.OrderRequestIdempotencyRecords.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task TryClaimAsync_ReplaysStoredResponse_ForSameCheckoutIdAndPayload()
    {
        var tenantId = Guid.NewGuid();
        await using var store = await SqliteCheckoutIdempotencyStore.CreateAsync(tenantId);
        var checkoutId = Guid.NewGuid().ToString("D");
        Guid expectedOrderId = Guid.Empty;
        const string requestHash = "hash-replay";

        await using (var context = store.CreateDbContext())
        {
            var coordinator = CreateCoordinator(context);
            var claim = await coordinator.TryClaimAsync(tenantId, checkoutId, requestHash, requestHash, CancellationToken.None);

            Assert.Equal(CheckoutIdempotencyClaimStatus.Claimed, claim.Status);

            var response = new CheckoutResponseDto
            {
                OrderId = Guid.NewGuid(),
                OrderNumber = "ORD-REPLAY-001",
                TotalAmount = 125m,
                Currency = "EGP",
                CartId = Guid.NewGuid(),
                Status = "PendingPayment"
            };
            expectedOrderId = response.OrderId;

            await coordinator.MarkOrderCreatedAsync(claim.Record!, response.OrderId, CancellationToken.None);
            await coordinator.CompleteAsync(claim.Record!, response, CancellationToken.None);
        }

        await using (var replayContext = store.CreateDbContext())
        {
            var replayCoordinator = CreateCoordinator(replayContext);
            var replay = await replayCoordinator.TryClaimAsync(tenantId, checkoutId, requestHash, requestHash, CancellationToken.None);

            Assert.Equal(CheckoutIdempotencyClaimStatus.Replay, replay.Status);
            Assert.NotNull(replay.ReplayResponse);
            Assert.Equal(expectedOrderId, replay.ReplayResponse!.OrderId);
            Assert.Equal("ORD-REPLAY-001", replay.ReplayResponse!.OrderNumber);
            Assert.Equal(125m, replay.ReplayResponse.TotalAmount);
            Assert.Equal(1, await replayContext.OrderRequestIdempotencyRecords.IgnoreQueryFilters().CountAsync());
        }
    }

    [Fact]
    public async Task TryClaimAsync_WithNullSnapshotHash_RecoversStoredResponse_ByRequestHash()
    {
        var tenantId = Guid.NewGuid();
        await using var store = await SqliteCheckoutIdempotencyStore.CreateAsync(tenantId);
        var completedCheckoutId = $"chk_{Guid.NewGuid():N}";
        var preSnapshotLookupId = $"chk_lock_{Guid.NewGuid():N}";
        const string requestHash = "hash-pre-cart-replay";
        const string snapshotHash = "snapshot-before-cart-expiry";
        var orderId = Guid.NewGuid();
        var cartId = Guid.NewGuid();

        await using (var context = store.CreateDbContext())
        {
            var coordinator = CreateCoordinator(context);
            var claim = await coordinator.TryClaimAsync(tenantId, completedCheckoutId, requestHash, snapshotHash, CancellationToken.None);

            Assert.Equal(CheckoutIdempotencyClaimStatus.Claimed, claim.Status);

            await coordinator.MarkOrderCreatedAsync(claim.Record!, orderId, CancellationToken.None);
            await coordinator.CompleteAsync(
                claim.Record!,
                new CheckoutResponseDto
                {
                    OrderId = orderId,
                    OrderNumber = "ORD-PRE-CART-001",
                    TotalAmount = 250m,
                    Currency = "EGP",
                    CartId = cartId,
                    Status = "PendingPayment"
                },
                CancellationToken.None);
        }

        await using (var replayContext = store.CreateDbContext())
        {
            var coordinator = CreateCoordinator(replayContext);
            var replay = await coordinator.TryClaimAsync(
                tenantId,
                preSnapshotLookupId,
                requestHash,
                snapshotHash: null,
                CancellationToken.None);

            Assert.Equal(CheckoutIdempotencyClaimStatus.Replay, replay.Status);
            Assert.NotNull(replay.ReplayResponse);
            Assert.Equal(orderId, replay.ReplayResponse!.OrderId);
            Assert.Equal(completedCheckoutId, replay.CheckoutId);
        }
    }

    [Fact]
    public async Task TryClaimAsync_WithNullSnapshotHash_RecoversOrderBinding_ByRequestHash()
    {
        var tenantId = Guid.NewGuid();
        await using var store = await SqliteCheckoutIdempotencyStore.CreateAsync(tenantId);
        var completedCheckoutId = $"chk_{Guid.NewGuid():N}";
        var preSnapshotLookupId = $"chk_lock_{Guid.NewGuid():N}";
        const string requestHash = "hash-pre-cart-existing-order";
        const string snapshotHash = "snapshot-order-bound";
        var orderId = Guid.NewGuid();

        await using (var context = store.CreateDbContext())
        {
            var coordinator = CreateCoordinator(context);
            var claim = await coordinator.TryClaimAsync(tenantId, completedCheckoutId, requestHash, snapshotHash, CancellationToken.None);

            Assert.Equal(CheckoutIdempotencyClaimStatus.Claimed, claim.Status);

            await coordinator.MarkOrderCreatedAsync(claim.Record!, orderId, CancellationToken.None);
        }

        await using (var recoveryContext = store.CreateDbContext())
        {
            var coordinator = CreateCoordinator(recoveryContext);
            var recovery = await coordinator.TryClaimAsync(
                tenantId,
                preSnapshotLookupId,
                requestHash,
                snapshotHash: null,
                CancellationToken.None);

            Assert.Equal(CheckoutIdempotencyClaimStatus.ExistingOrder, recovery.Status);
            Assert.Equal(orderId, recovery.ExistingOrderId);
            Assert.Equal(completedCheckoutId, recovery.CheckoutId);
        }
    }

    [Fact]
    public async Task TryClaimAsync_RejectsConflictingPayloadReuse_ForSameCheckoutId()
    {
        var tenantId = Guid.NewGuid();
        await using var store = await SqliteCheckoutIdempotencyStore.CreateAsync(tenantId);
        var checkoutId = Guid.NewGuid().ToString("D");

        await using (var context = store.CreateDbContext())
        {
            var coordinator = CreateCoordinator(context);
            var claim = await coordinator.TryClaimAsync(tenantId, checkoutId, "hash-a", "snapshot-a", CancellationToken.None);
            Assert.Equal(CheckoutIdempotencyClaimStatus.Claimed, claim.Status);
        }

        await using (var conflictContext = store.CreateDbContext())
        {
            var coordinator = CreateCoordinator(conflictContext);
            var conflict = await coordinator.TryClaimAsync(tenantId, checkoutId, "hash-b", "snapshot-a", CancellationToken.None);
            Assert.Equal(CheckoutIdempotencyClaimStatus.PayloadConflict, conflict.Status);
            Assert.Contains("different checkout payload", conflict.ConflictMessage, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task TryClaimAsync_RejectsModifiedCartReuse_ForSameCheckoutId()
    {
        var tenantId = Guid.NewGuid();
        await using var store = await SqliteCheckoutIdempotencyStore.CreateAsync(tenantId);
        var checkoutId = Guid.NewGuid().ToString("D");

        await using (var context = store.CreateDbContext())
        {
            var coordinator = CreateCoordinator(context);
            var claim = await coordinator.TryClaimAsync(tenantId, checkoutId, "request-a", "snapshot-cart-a", CancellationToken.None);
            Assert.Equal(CheckoutIdempotencyClaimStatus.Claimed, claim.Status);
        }

        await using (var conflictContext = store.CreateDbContext())
        {
            var coordinator = CreateCoordinator(conflictContext);
            var conflict = await coordinator.TryClaimAsync(tenantId, checkoutId, "request-a", "snapshot-cart-b", CancellationToken.None);
            Assert.Equal(CheckoutIdempotencyClaimStatus.PayloadConflict, conflict.Status);
            Assert.Contains("different cart snapshot", conflict.ConflictMessage, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task TryClaimAsync_RejectsQuantityChange_ForSameCheckoutId()
    {
        var tenantId = Guid.NewGuid();
        await using var store = await SqliteCheckoutIdempotencyStore.CreateAsync(tenantId);
        var checkoutId = Guid.NewGuid().ToString("D");

        await using (var context = store.CreateDbContext())
        {
            var coordinator = CreateCoordinator(context);
            var claim = await coordinator.TryClaimAsync(tenantId, checkoutId, "request-a", "snapshot-qty-1", CancellationToken.None);
            Assert.Equal(CheckoutIdempotencyClaimStatus.Claimed, claim.Status);
        }

        await using (var conflictContext = store.CreateDbContext())
        {
            var coordinator = CreateCoordinator(conflictContext);
            var conflict = await coordinator.TryClaimAsync(tenantId, checkoutId, "request-a", "snapshot-qty-2", CancellationToken.None);
            Assert.Equal(CheckoutIdempotencyClaimStatus.PayloadConflict, conflict.Status);
            Assert.Contains("different cart snapshot", conflict.ConflictMessage, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task TryClaimAsync_WhenOrderExistsWithoutStoredResponse_ReturnsExistingOrderRecoverySignal()
    {
        var tenantId = Guid.NewGuid();
        await using var store = await SqliteCheckoutIdempotencyStore.CreateAsync(tenantId);
        var checkoutId = Guid.NewGuid().ToString("D");
        var orderId = Guid.NewGuid();

        await using (var context = store.CreateDbContext())
        {
            var coordinator = CreateCoordinator(context);
            var claim = await coordinator.TryClaimAsync(tenantId, checkoutId, "hash-existing-order", "hash-existing-order", CancellationToken.None);
            Assert.Equal(CheckoutIdempotencyClaimStatus.Claimed, claim.Status);

            await coordinator.MarkOrderCreatedAsync(claim.Record!, orderId, CancellationToken.None);
        }

        await using (var recoveryContext = store.CreateDbContext())
        {
            var coordinator = CreateCoordinator(recoveryContext);
            var recovery = await coordinator.TryClaimAsync(tenantId, checkoutId, "hash-existing-order", "hash-existing-order", CancellationToken.None);

            Assert.Equal(CheckoutIdempotencyClaimStatus.ExistingOrder, recovery.Status);
            Assert.Equal(orderId, recovery.ExistingOrderId);
        }
    }

    [Fact]
    public async Task DeleteClaimAsync_WhenOrderWasBound_DoesNotDeleteDurableRecord()
    {
        var tenantId = Guid.NewGuid();
        await using var store = await SqliteCheckoutIdempotencyStore.CreateAsync(tenantId);
        var checkoutId = Guid.NewGuid().ToString("D");
        var orderId = Guid.NewGuid();

        await using (var context = store.CreateDbContext())
        {
            var coordinator = CreateCoordinator(context);
            var claim = await coordinator.TryClaimAsync(tenantId, checkoutId, "hash-durable", "hash-durable", CancellationToken.None);

            Assert.Equal(CheckoutIdempotencyClaimStatus.Claimed, claim.Status);

            await coordinator.MarkOrderCreatedAsync(claim.Record!, orderId, CancellationToken.None);
            await coordinator.DeleteClaimAsync(claim.Record!, CancellationToken.None);
        }

        await using (var verificationContext = store.CreateDbContext())
        {
            var record = await verificationContext.OrderRequestIdempotencyRecords
                .IgnoreQueryFilters()
                .SingleAsync();

            Assert.Equal(orderId, record.OrderId);
            Assert.NotNull(record.CompletedAtUtc);
        }
    }

    [Fact]
    public async Task BindOrderCreatedForCommitAsync_WhenTrackerWasCleared_RehydratesTrackedClaimBeforeTransactionalSave()
    {
        var tenantId = Guid.NewGuid();
        await using var store = await SqliteCheckoutIdempotencyStore.CreateAsync(tenantId);
        var checkoutId = Guid.NewGuid().ToString("D");
        var orderId = Guid.NewGuid();

        await using (var context = store.CreateDbContext())
        {
            var coordinator = CreateCoordinator(context);
            var claim = await coordinator.TryClaimAsync(tenantId, checkoutId, "hash-rebind", "snapshot-rebind", CancellationToken.None);

            Assert.Equal(CheckoutIdempotencyClaimStatus.Claimed, claim.Status);

            context.ChangeTracker.Clear();

            var reboundRecord = await coordinator.BindOrderCreatedForCommitAsync(
                claim.Record!,
                orderId,
                CancellationToken.None);

            Assert.NotSame(claim.Record, reboundRecord);
            Assert.NotEqual(EntityState.Detached, context.Entry(reboundRecord).State);
            Assert.Equal(orderId, reboundRecord.OrderId);
            Assert.Equal(orderId, claim.Record!.OrderId);

            await context.SaveChangesAsync(CancellationToken.None);
        }

        await using (var verificationContext = store.CreateDbContext())
        {
            var record = await verificationContext.OrderRequestIdempotencyRecords
                .IgnoreQueryFilters()
                .SingleAsync();

            Assert.Equal(orderId, record.OrderId);
            Assert.NotNull(record.CompletedAtUtc);
        }
    }

    private static CheckoutRequestIdempotencyCoordinator CreateCoordinator(ApplicationDbContext context) =>
        new(
            context,
            Options.Create(new CheckoutExecutionLockOptions
            {
                LockTtl = TimeSpan.FromMinutes(2),
                HeartbeatInterval = TimeSpan.FromSeconds(30),
                MaxExecutionTime = TimeSpan.FromMinutes(5),
                HighContentionWarningThreshold = 0.25d,
                HighContentionMinimumAttempts = 10
            }),
            NullLogger<CheckoutRequestIdempotencyCoordinator>.Instance);

    private sealed class SqliteCheckoutIdempotencyStore : IAsyncDisposable
    {
        private readonly Guid _tenantId;
        private readonly string _databasePath;

        private SqliteCheckoutIdempotencyStore(Guid tenantId, string databasePath)
        {
            _tenantId = tenantId;
            _databasePath = databasePath;
        }

        public static async Task<SqliteCheckoutIdempotencyStore> CreateAsync(Guid tenantId)
        {
            var databasePath = Path.Combine(
                Path.GetTempPath(),
                $"checkout-idempotency-tests-{Guid.NewGuid():N}.db");

            var store = new SqliteCheckoutIdempotencyStore(tenantId, databasePath);

            await using var initializationContext = store.CreateDbContext();
            await initializationContext.Database.EnsureCreatedAsync();

            return store;
        }

        public ApplicationDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite($"Data Source={_databasePath}")
                .Options;

            return new CheckoutIdempotencyTestDbContext(options, new TestTenantContext(_tenantId));
        }

        public ValueTask DisposeAsync()
        {
            try
            {
                if (File.Exists(_databasePath))
                {
                    File.Delete(_databasePath);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class CheckoutIdempotencyTestDbContext : ApplicationDbContext
    {
        public CheckoutIdempotencyTestDbContext(
            DbContextOptions<ApplicationDbContext> options,
            TestTenantContext tenantContext)
            : base(options, tenantContext)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            foreach (var entityType in typeof(ApplicationDbContext)
                         .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                         .Where(property =>
                             property.PropertyType.IsGenericType &&
                             property.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
                         .Select(property => property.PropertyType.GetGenericArguments()[0])
                         .Where(entityType => entityType != typeof(OrderRequestIdempotency)))
            {
                modelBuilder.Ignore(entityType);
            }

            modelBuilder.Entity<OrderRequestIdempotency>(entity =>
            {
                entity.ToTable("OrderRequestIdempotency");
                entity.HasKey(record => record.Id);
                entity.HasIndex(record => new { record.TenantId, record.Key }).IsUnique();
                entity.Property(record => record.Key).HasMaxLength(200);
                entity.Property(record => record.RequestHash).HasMaxLength(64);
                entity.Property(record => record.SnapshotHash).HasMaxLength(64);
                entity.Property(record => record.ResponsePayload).HasColumnType("TEXT");
                entity.Property(record => record.ClaimToken).HasMaxLength(32);
            });
        }
    }
}
