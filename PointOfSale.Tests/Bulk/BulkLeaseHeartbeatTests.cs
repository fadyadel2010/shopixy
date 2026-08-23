using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PointOfSale.API.Infrastructure.Idempotency;
using PointOfSale.API.Services.ProductAuthoring.Bulk;
using PointOfSale.Core.Application.ProductAuthoring;
using PointOfSale.Core.DTOs.Bulk;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces.Bulk;
using PointOfSale.Infrastructure.Data;
using TenantContext = PointOfSale.Infrastructure.MultiTenancy.TenantContext;

namespace PointOfSale.Tests.Bulk;

public sealed class BulkLeaseHeartbeatTests
{
    private static readonly TimeSpan Lease = BulkIdempotencyService.DefaultClaimLeaseDuration;
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMilliseconds(40);

    [Fact]
    public async Task ExecuteLifecycleAsync_HeartbeatRenewsWhileSingleItemBlockedLongerThanLease_PreventsTakeover()
    {
        await using var harness = BulkFoundationTestHarness.Create(
            seedProducts: true, SharedConnectionString());
        var clock = new BulkIdempotencyRecoveryTests.MutableTimeProvider(DateTime.UtcNow);
        await using var renewalProvider = BuildIsolatedRenewalProvider(harness, clock, out var scopeFactory);
        var tenantId = harness.TenantContext.TenantId!.Value;
        var inner = new ProductMutationIdempotencyService(harness.Context, clock);
        var bulkIdempotency = new BulkIdempotencyService(inner, harness.TenantContext, clock);
        const string key = "heartbeat-live-key";
        var productId = harness.PreparedProductId;
        var fingerprint = bulkIdempotency.BuildFingerprint(
            BulkOperationType.Deactivate,
            [new NormalizedBulkProductItem(productId, 7)]);

        var authoringStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseAuthoring = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (service, orchestrator) = BulkIdempotencyRecoveryTests.CreateLifecycleService(
            harness,
            out _,
            authoringResult: ProductAuthoringResult.Accepted(
                productId, PublishStatus.Archived, "recovery-test-correlation", 9),
            timeProvider: clock,
            leaseHeartbeatInterval: HeartbeatInterval,
            leaseRenewalScopeFactory: scopeFactory);
        orchestrator
            .Setup(item => item.AuthorAsync(It.IsAny<ProductAuthoringCommand>(), It.IsAny<CancellationToken>()))
            .Returns(async (ProductAuthoringCommand _, CancellationToken itemToken) =>
            {
                authoringStarted.TrySetResult();
                await releaseAuthoring.Task.WaitAsync(TimeSpan.FromSeconds(20), itemToken);
                return ProductAuthoringResult.Accepted(
                    productId, PublishStatus.Archived, "recovery-test-correlation", 9);
            });

        // The execution owns the fresh outer claim itself.
        var executionTask = service.ExecuteLifecycleAsync(
            BulkOperationType.Deactivate,
            BulkIdempotencyRecoveryTests.Request(productId, "7"),
            key);
        await authoringStarted.Task.WaitAsync(TimeSpan.FromSeconds(20));
        var claimedRow = await OuterRowAsync(harness);

        // The lease fully expires while the single authoring call still runs.
        clock.Set(clock.UtcNow.Add(Lease).AddSeconds(5));

        // The isolated-context heartbeat must renew past the original claim
        // timestamp while item authoring remains blocked.
        Assert.True(await WaitForOuterRowUpdatedAtBeyondAsync(
            harness, claimedRow.Id, claimedRow.UpdatedAt!.Value, TimeSpan.FromSeconds(10)));

        // A takeover attempt at the advanced clock is fenced by the heartbeat.
        var takeover = await inner.TryRecoverAbandonedClaimAsync(
            new ProductMutationIdempotencyRequest(
                tenantId,
                inner.ScopeKey(BulkIdempotencyService.BulkScope, key),
                "BulkProduct",
                Guid.Empty,
                null,
                fingerprint),
            Lease,
            clock.UtcNow);
        Assert.Equal(ProductMutationIdempotencyRecoveryKind.NotRecoverable, takeover.Kind);

        releaseAuthoring.TrySetResult();
        var outcome = await executionTask.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(BulkLifecycleOutcomeKind.Success, outcome.Kind);
        Assert.Equal(BulkItemStatus.Processed, Assert.Single(outcome.OperationResult!.Results).Status);
        orchestrator.Verify(item => item.AuthorAsync(
            It.IsAny<ProductAuthoringCommand>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteLifecycleAsync_DeleteAfterTakeover_StaleExecutionCannotRetryCompleteFailOrRenewClaim()
    {
        await using var harness = BulkFoundationTestHarness.Create(
            seedProducts: true, SharedConnectionString());
        var clock = new BulkIdempotencyRecoveryTests.MutableTimeProvider(DateTime.UtcNow);
        await using var renewalProvider = BuildIsolatedRenewalProvider(harness, clock, out var scopeFactory);
        var inner = new ProductMutationIdempotencyService(harness.Context, clock);
        var bulkIdempotency = new BulkIdempotencyService(inner, harness.TenantContext, clock);
        const string key = "heartbeat-takeover-key";
        var productId = harness.PreparedProductId;

        var authoringStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseAuthoring = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (service, orchestrator) = BulkIdempotencyRecoveryTests.CreateLifecycleService(
            harness,
            out _,
            authoringResult: ProductAuthoringResult.Accepted(
                productId, PublishStatus.Archived, "recovery-test-correlation", 9),
            timeProvider: clock,
            leaseHeartbeatInterval: HeartbeatInterval,
            leaseRenewalScopeFactory: scopeFactory);
        orchestrator
            .Setup(item => item.AuthorAsync(It.IsAny<ProductAuthoringCommand>(), It.IsAny<CancellationToken>()))
            .Returns(async (ProductAuthoringCommand _, CancellationToken itemToken) =>
            {
                authoringStarted.TrySetResult();
                await releaseAuthoring.Task.WaitAsync(TimeSpan.FromSeconds(20), itemToken);
                return ProductAuthoringResult.Accepted(
                    productId, PublishStatus.Archived, "recovery-test-correlation", 9);
            });

        var executionTask = service.ExecuteLifecycleAsync(
            BulkOperationType.Delete,
            BulkIdempotencyRecoveryTests.Request(productId, "7"),
            key);
        await authoringStarted.Task.WaitAsync(TimeSpan.FromSeconds(20));
        var claimedRow = await OuterRowAsync(harness);
        var staleRecord = claimedRow;

        // A successor takes over the durable lease while the stale worker is
        // still inside its blocked authoring call.
        var successorToken = clock.UtcNow.Add(Lease).AddMinutes(3);
        var trackedRow = await harness.Context.ProductRequestIdempotencyRecords
            .SingleAsync(record => record.Id == claimedRow.Id);
        trackedRow.UpdatedAt = successorToken;
        await harness.Context.SaveChangesAsync();
        harness.Context.ChangeTracker.Clear();

        // SaveChanges stamps UpdatedAt itself; the persisted value is the
        // authoritative successor token the stale owner must fail against.
        var persistedSuccessorToken = (await OuterRowAsync(harness, claimedRow.Id)).UpdatedAt!.Value;

        // The stale heartbeat's next atomic renewal fails and interrupts the
        // item work; the stale execution surfaces the lease-lost failure.
        var failure = await Assert.ThrowsAnyAsync<BulkIdempotencyLeaseLostException>(
            () => executionTask.WaitAsync(TimeSpan.FromSeconds(20)));

        Assert.Contains("lease", failure.Message, StringComparison.OrdinalIgnoreCase);
        orchestrator.Verify(item => item.AuthorAsync(
            It.IsAny<ProductAuthoringCommand>(), It.IsAny<CancellationToken>()), Times.Once);

        var afterFailure = await OuterRowAsync(harness, claimedRow.Id);
        Assert.Null(afterFailure.ResponseStatusCode);
        Assert.Null(afterFailure.ResponseJson);
        Assert.Equal(persistedSuccessorToken, afterFailure.UpdatedAt);

        // The stale owner cannot renew, complete, or finalize the claim.
        Assert.False(await bulkIdempotency.RenewClaimLeaseAsync(staleRecord));
        Assert.Null(await bulkIdempotency.TryCompleteAndVerifyAsync(
            staleRecord, BulkOperationResult.Empty(1, 1, "stale-owner")));
        Assert.False(await bulkIdempotency.FinalizeFailureAsync(staleRecord, "stale owner"));

        // The heartbeat stopped: the claim stays untouched by the stale worker.
        clock.Set(clock.UtcNow.AddMinutes(5));
        await Task.Delay(TimeSpan.FromMilliseconds(HeartbeatInterval.TotalMilliseconds * 6));
        var untouched = await OuterRowAsync(harness, claimedRow.Id);
        Assert.Equal(persistedSuccessorToken, untouched.UpdatedAt);
        Assert.Null(untouched.ResponseStatusCode);

        // The successor owner can still complete the recovered claim exactly.
        var successorRecord = await OuterRowAsync(harness, claimedRow.Id);
        successorRecord.ClaimLeaseUpdatedAtUtc = successorRecord.UpdatedAt;
        var completed = await bulkIdempotency.TryCompleteAndVerifyAsync(
            successorRecord, BulkOperationResult.Empty(1, 1, "successor-owner"));
        Assert.NotNull(completed);
        Assert.Equal("successor-owner", completed.CorrelationId);

        releaseAuthoring.TrySetResult();
    }

    [Fact]
    public async Task ExecuteLifecycleAsync_HeartbeatStopsAfterCompletion_DoesNotContinueTouchingClaim()
    {
        await using var harness = BulkFoundationTestHarness.Create(
            seedProducts: true, SharedConnectionString());
        var clock = new BulkIdempotencyRecoveryTests.MutableTimeProvider(DateTime.UtcNow);
        await using var renewalProvider = BuildIsolatedRenewalProvider(harness, clock, out var scopeFactory);
        var inner = new ProductMutationIdempotencyService(harness.Context, clock);
        var bulkIdempotency = new BulkIdempotencyService(inner, harness.TenantContext, clock);
        const string key = "heartbeat-stop-key";
        var productId = harness.PreparedProductId;

        var (service, orchestrator) = BulkIdempotencyRecoveryTests.CreateLifecycleService(
            harness,
            out _,
            authoringResult: ProductAuthoringResult.Accepted(
                productId, PublishStatus.Archived, "recovery-test-correlation", 9),
            timeProvider: clock,
            leaseHeartbeatInterval: HeartbeatInterval,
            leaseRenewalScopeFactory: scopeFactory);

        var outcome = await service.ExecuteLifecycleAsync(
            BulkOperationType.Deactivate,
            BulkIdempotencyRecoveryTests.Request(productId, "7"),
            key);

        Assert.Equal(BulkLifecycleOutcomeKind.Success, outcome.Kind);
        var completedRow = await OuterRowAsync(harness);
        Assert.Equal(200, completedRow.ResponseStatusCode);
        var finalUpdatedAt = completedRow.UpdatedAt;

        // After completion no renewal may touch the claim again.
        clock.Set(clock.UtcNow.AddMinutes(5));
        await Task.Delay(TimeSpan.FromMilliseconds(HeartbeatInterval.TotalMilliseconds * 6));
        var afterWait = await OuterRowAsync(harness, completedRow.Id);
        Assert.Equal(finalUpdatedAt, afterWait.UpdatedAt);
        Assert.False(await bulkIdempotency.RenewClaimLeaseAsync(completedRow));
    }

    [Fact]
    public async Task ExecuteLifecycleAsync_HeartbeatStopsAfterContainedItemError_DoesNotContinueTouchingClaim()
    {
        await using var harness = BulkFoundationTestHarness.Create(
            seedProducts: true, SharedConnectionString());
        var clock = new BulkIdempotencyRecoveryTests.MutableTimeProvider(DateTime.UtcNow);
        await using var renewalProvider = BuildIsolatedRenewalProvider(harness, clock, out var scopeFactory);
        var inner = new ProductMutationIdempotencyService(harness.Context, clock);
        var bulkIdempotency = new BulkIdempotencyService(inner, harness.TenantContext, clock);
        const string key = "heartbeat-error-stop-key";
        var productId = harness.PreparedProductId;

        var (service, orchestrator) = BulkIdempotencyRecoveryTests.CreateLifecycleService(
            harness,
            out _,
            timeProvider: clock,
            leaseHeartbeatInterval: HeartbeatInterval,
            leaseRenewalScopeFactory: scopeFactory);
        orchestrator
            .Setup(item => item.AuthorAsync(It.IsAny<ProductAuthoringCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Injected authoring crash."));

        var outcome = await service.ExecuteLifecycleAsync(
            BulkOperationType.Deactivate,
            BulkIdempotencyRecoveryTests.Request(productId, "7"),
            key);

        // The contained item failure still completes the batch durably.
        Assert.Equal(BulkLifecycleOutcomeKind.Success, outcome.Kind);
        var completedRow = await OuterRowAsync(harness);
        Assert.Equal(200, completedRow.ResponseStatusCode);
        var finalUpdatedAt = completedRow.UpdatedAt;

        clock.Set(clock.UtcNow.AddMinutes(5));
        await Task.Delay(TimeSpan.FromMilliseconds(HeartbeatInterval.TotalMilliseconds * 6));
        var afterWait = await OuterRowAsync(harness, completedRow.Id);
        Assert.Equal(finalUpdatedAt, afterWait.UpdatedAt);
    }

    [Fact]
    public async Task ExecuteLifecycleAsync_CancellationReconciliation_KeepsHeartbeatUntilRecoveryFinishesThenStops()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: true);
        var tenantId = harness.TenantContext.TenantId!.Value;
        var claimRecord = new ProductRequestIdempotency
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Key = "bulk:cancellation-heartbeat-key",
            Operation = BulkIdempotencyService.OuterClaimStorageOperation,
            ProductId = Guid.Empty,
            RequestFingerprint = "cancellation-heartbeat-fingerprint",
            UpdatedAt = DateTime.UtcNow,
            ClaimLeaseUpdatedAtUtc = DateTime.UtcNow
        };
        var resolverStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseResolver = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var renewalCount = 0;
        var idempotency = new Mock<IBulkIdempotencyService>();
        idempotency.Setup(service => service.ValidateKey(It.IsAny<string?>()))
            .Returns(BulkIdempotencyKeyValidationResult.Valid());
        idempotency.Setup(service => service.BuildFingerprint(
                It.IsAny<BulkOperationType>(),
                It.IsAny<IReadOnlyList<NormalizedBulkProductItem>>(),
                It.IsAny<IReadOnlyDictionary<string, string?>?>()))
            .Returns(claimRecord.RequestFingerprint);
        idempotency.Setup(service => service.TryClaimAsync(
                It.IsAny<BulkOperationType>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BulkIdempotencyClaim(claimRecord, null));
        idempotency.Setup(service => service.BuildPerProductIdempotencyKey(
                It.IsAny<BulkOperationType>(), It.IsAny<string>(), It.IsAny<Guid>()))
            .Returns("bulk-item:cancellation-heartbeat");
        idempotency.Setup(service => service.RenewClaimLeaseAsync(
                It.IsAny<ProductRequestIdempotency>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                Interlocked.Increment(ref renewalCount);
                return true;
            });
        idempotency.Setup(service => service.TryMarkInterruptedAsync(
                It.IsAny<ProductRequestIdempotency>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        idempotency.Setup(service => service.TryResolveCommittedBulkItemMutationAsync(
                It.IsAny<BulkOperationType>(),
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                resolverStarted.TrySetResult();
                await releaseResolver.Task.WaitAsync(TimeSpan.FromSeconds(10));
                return null;
            });
        idempotency.Setup(service => service.TryCompleteAndVerifyAsync(
                It.IsAny<ProductRequestIdempotency>(),
                It.IsAny<BulkOperationResult>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductRequestIdempotency _, BulkOperationResult aggregate, CancellationToken _) => aggregate);

        var (service, orchestrator) = BulkIdempotencyRecoveryTests.CreateLifecycleService(
            harness,
            out _,
            idempotencyOverride: idempotency.Object,
            leaseHeartbeatInterval: HeartbeatInterval);
        using var cancellation = new CancellationTokenSource();
        orchestrator.Setup(item => item.AuthorAsync(
                It.IsAny<ProductAuthoringCommand>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromCanceled<ProductAuthoringResult>(cancellation.Token);
            });

        var execution = service.ExecuteLifecycleAsync(
            BulkOperationType.Deactivate,
            BulkIdempotencyRecoveryTests.Request(harness.PreparedProductId, "7"),
            "cancellation-heartbeat-key",
            cancellation.Token);
        await resolverStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (Volatile.Read(ref renewalCount) < 2 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.True(Volatile.Read(ref renewalCount) >= 2);
        releaseResolver.TrySetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);

        var finalRenewalCount = Volatile.Read(ref renewalCount);
        await Task.Delay(TimeSpan.FromMilliseconds(HeartbeatInterval.TotalMilliseconds * 6));
        Assert.Equal(finalRenewalCount, Volatile.Read(ref renewalCount));
        idempotency.Verify(service => service.FinalizeFailureAsync(
            It.IsAny<ProductRequestIdempotency>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private static string SharedConnectionString() =>
        $"Data Source=file:bulk-heartbeat-{Guid.NewGuid():N}?mode=memory&cache=shared";

    /// <summary>
    /// Builds an isolated DI container whose per-scope idempotency services run
    /// on their own connections/contexts over the same database, mirroring the
    /// production heartbeat renewal path.
    /// </summary>
    private static ServiceProvider BuildIsolatedRenewalProvider(
        BulkFoundationTestHarness harness,
        TimeProvider clock,
        out IServiceScopeFactory scopeFactory)
    {
        var tenantId = harness.TenantContext.TenantId!.Value;
        var sharedConnectionString = harness.Connection.ConnectionString;
        var services = new ServiceCollection();
        services.AddSingleton(new TenantContext());
        services.AddScoped(_ =>
        {
            var connection = new SqliteConnection(sharedConnectionString);
            connection.Open();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .Options;
            return new ProductMutationIdempotencyService(
                new BulkFoundationDbContext(options, new TestTenantContext(tenantId)),
                clock);
        });
        services.AddScoped<IBulkIdempotencyService>(sp => new BulkIdempotencyService(
            sp.GetRequiredService<ProductMutationIdempotencyService>(),
            new TestTenantContext(tenantId),
            clock));

        var provider = services.BuildServiceProvider();
        scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        return provider;
    }

    private static async Task<ProductRequestIdempotency> OuterRowAsync(
        BulkFoundationTestHarness harness,
        Guid? recordId = null)
    {
        harness.Context.ChangeTracker.Clear();
        return await harness.Context.ProductRequestIdempotencyRecords
            .AsNoTracking()
            .SingleAsync(record =>
                record.Operation == BulkIdempotencyService.OuterClaimStorageOperation &&
                (recordId == null || record.Id == recordId.Value));
    }

    private static async Task<bool> WaitForOuterRowUpdatedAtBeyondAsync(
        BulkFoundationTestHarness harness,
        Guid recordId,
        DateTime previousUpdatedAt,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var row = await OuterRowAsync(harness, recordId);
            if (row.UpdatedAt > previousUpdatedAt)
            {
                return true;
            }

            await Task.Delay(20);
        }

        return false;
    }
}
