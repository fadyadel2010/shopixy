using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Infrastructure.Idempotency;
using PointOfSale.API.Services.ProductAuthoring;
using PointOfSale.API.Services.ProductAuthoring.Bulk;
using PointOfSale.Core.Application.ProductAuthoring;
using PointOfSale.Core.DTOs.Bulk;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Bulk;
using PointOfSale.Core.Interfaces.ProductAuthoring;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.Bulk;

public sealed class BulkIdempotencyRecoveryTests
{
    private static readonly TimeSpan Lease = BulkIdempotencyService.DefaultClaimLeaseDuration;

    [Fact]
    public async Task StaleAbandonedClaim_IsRecoveredExactlyOnceAndLiveClaimsStayFenced()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var clock = new MutableTimeProvider(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var inner = new ProductMutationIdempotencyService(harness.Context, clock);
        var service = new BulkIdempotencyService(inner, harness.TenantContext, clock);
        const string key = "recovery-key";
        var item = new NormalizedBulkProductItem(Guid.NewGuid(), 7);
        var fingerprint = service.BuildFingerprint(BulkOperationType.Activate, [item]);

        var claim = await service.TryClaimAsync(BulkOperationType.Activate, key, fingerprint);
        Assert.NotNull(claim.Record);
        Assert.Null(claim.Replay);

        // A live lease must stay fenced: takeover before expiry changes nothing.
        clock.Set(clock.UtcNow.AddSeconds(1));
        var liveAttempt = await RecoverAsync(inner, harness, key, fingerprint, clock);
        Assert.Equal(ProductMutationIdempotencyRecoveryKind.NotRecoverable, liveAttempt.Kind);
        Assert.Equal(1, await CountRowsAsync(harness));

        // After the bounded lease expires the abandoned claim is recoverable.
        clock.Set(clock.UtcNow.Add(Lease).AddSeconds(5));
        var recovered = await RecoverAsync(inner, harness, key, fingerprint, clock);
        Assert.Equal(ProductMutationIdempotencyRecoveryKind.Recovered, recovered.Kind);
        Assert.NotNull(recovered.Record);

        // The refreshed lease fences every other caller, including the previous
        // owner whose cached lease timestamp predates the takeover.
        var secondAttempt = await RecoverAsync(inner, harness, key, fingerprint, clock);
        Assert.Equal(ProductMutationIdempotencyRecoveryKind.NotRecoverable, secondAttempt.Kind);
        Assert.False(await service.RenewClaimLeaseAsync(claim.Record));
    }

    [Fact]
    public async Task RecoveredClaim_FencesFormerOwnerFromCompletingOrFailingClaim()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var clock = new MutableTimeProvider(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var inner = new ProductMutationIdempotencyService(harness.Context, clock);
        var service = new BulkIdempotencyService(inner, harness.TenantContext, clock);
        var fingerprint = service.BuildFingerprint(
            BulkOperationType.Publish,
            [new NormalizedBulkProductItem(Guid.NewGuid(), 4)]);

        var originalOwner = await service.TryClaimAsync(
            BulkOperationType.Publish,
            "fenced-completion-key",
            fingerprint);
        Assert.NotNull(originalOwner.Record);

        clock.Set(clock.UtcNow.Add(Lease).AddSeconds(1));
        var recoveredOwner = await service.TryClaimAsync(
            BulkOperationType.Publish,
            "fenced-completion-key",
            fingerprint);
        Assert.NotNull(recoveredOwner.Record);
        Assert.Null(recoveredOwner.Replay);
        Assert.True(recoveredOwner.Recovered);

        var staleAggregate = BulkOperationResult.Empty(1, 1, "stale-owner");
        Assert.Null(await service.TryCompleteAndVerifyAsync(originalOwner.Record, staleAggregate));
        Assert.False(await service.FinalizeFailureAsync(originalOwner.Record, "stale owner"));
        Assert.False(await service.TryMarkInterruptedAsync(originalOwner.Record));

        var stillIncomplete = await SingleRowAsync(harness);
        Assert.Null(stillIncomplete.ResponseStatusCode);
        Assert.Null(stillIncomplete.ResponseJson);

        Assert.True(await service.TryMarkInterruptedAsync(recoveredOwner.Record));
        var interrupted = await SingleRowAsync(harness);
        Assert.True(service.IsInterruptedClaim(interrupted));

        var recoveredAggregate = BulkOperationResult.Empty(1, 1, "recovered-owner");
        var completed = await service.TryCompleteAndVerifyAsync(recoveredOwner.Record, recoveredAggregate);
        Assert.NotNull(completed);
        Assert.Equal("recovered-owner", completed.CorrelationId);
    }

    [Fact]
    public async Task RenewClaimLeaseAsync_ExtendsLiveLeaseAndStopsAfterCompletion()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var clock = new MutableTimeProvider(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var inner = new ProductMutationIdempotencyService(harness.Context, clock);
        var service = new BulkIdempotencyService(inner, harness.TenantContext, clock);
        const string key = "lease-key";
        var fingerprint = service.BuildFingerprint(
            BulkOperationType.Publish,
            [new NormalizedBulkProductItem(Guid.NewGuid(), 3)]);

        var claim = await service.TryClaimAsync(BulkOperationType.Publish, key, fingerprint);
        Assert.NotNull(claim.Record);
        var originalUpdatedAt = claim.Record.UpdatedAt;

        clock.Set(clock.UtcNow.AddSeconds(10));
        Assert.True(await service.RenewClaimLeaseAsync(claim.Record));
        Assert.True(claim.Record.UpdatedAt > originalUpdatedAt);

        var aggregate = BulkOperationResult.Empty(0, 0, "lease-correlation");
        var completed = await service.TryCompleteAndVerifyAsync(claim.Record, aggregate);
        Assert.NotNull(completed);

        // Completion ends ownership: neither renewal nor recovery applies.
        clock.Set(clock.UtcNow.AddSeconds(10));
        Assert.False(await service.RenewClaimLeaseAsync(claim.Record));
        var afterCompletion = await RecoverAsync(inner, harness, key, fingerprint, clock);
        Assert.Equal(ProductMutationIdempotencyRecoveryKind.NotRecoverable, afterCompletion.Kind);
    }

    [Fact]
    public async Task DurableCompletion_PersistsEvenWhenEntityWasAttachedWithoutDifferences()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var inner = new ProductMutationIdempotencyService(harness.Context);
        var service = new BulkIdempotencyService(inner, harness.TenantContext);
        var fingerprint = service.BuildFingerprint(
            BulkOperationType.Unpublish,
            [new NormalizedBulkProductItem(Guid.NewGuid(), 5)]);

        var claim = await service.TryClaimAsync(BulkOperationType.Unpublish, "durable-key", fingerprint);
        Assert.NotNull(claim.Record);
        var aggregate = BulkOperationResult.Empty(1, 1, "durable-correlation");

        // Reproduce the legacy no-op conditions: values were already mutated in
        // memory, the tracker was cleared, and the entity was reattached so its
        // current values became the "originals". A tracked save would no-op.
        service.Complete(claim.Record, 200, aggregate);
        harness.Context.ChangeTracker.Clear();
        harness.Context.Attach(claim.Record);

        var verified = await service.TryCompleteAndVerifyAsync(claim.Record, aggregate);
        Assert.NotNull(verified);

        harness.Context.ChangeTracker.Clear();
        var storedRow = await SingleRowAsync(harness);
        Assert.Equal(200, storedRow.ResponseStatusCode);
        Assert.NotNull(storedRow.CompletedAtUtc);
        Assert.True(service.StoredResponseMatches(storedRow, aggregate));
        Assert.Equal("durable-correlation", verified.CorrelationId);

        // Re-verifying an already-persisted completion still replays exactly.
        var replayed = await service.TryCompleteAndVerifyAsync(claim.Record, aggregate);
        Assert.NotNull(replayed);
    }

    [Fact]
    public async Task Completed500_IsNeverReplayedAsSuccess()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var inner = new ProductMutationIdempotencyService(harness.Context);
        var service = new BulkIdempotencyService(inner, harness.TenantContext);
        var productId = Guid.NewGuid();
        var fingerprint = service.BuildFingerprint(
            BulkOperationType.Delete,
            [new NormalizedBulkProductItem(productId, 2)]);

        var claim = await service.TryClaimAsync(BulkOperationType.Delete, "failed-key", fingerprint);
        Assert.NotNull(claim.Record);
        Assert.True(await service.FinalizeFailureAsync(claim.Record, "interrupted."));
        harness.Context.ChangeTracker.Clear();

        var resolution = await service.TryResolveAsync(BulkOperationType.Delete, "failed-key", fingerprint);

        Assert.Equal(BulkIdempotencyReplayKind.Completed, resolution.Kind);
        Assert.Equal(500, resolution.Record?.ResponseStatusCode);
        Assert.Contains("interrupted", resolution.Record?.ResponseJson, StringComparison.Ordinal);
        Assert.Equal("interrupted.", service.ReadStoredFailureMessage(resolution.Record));

        var (lifecycle, orchestrator) = CreateLifecycleService(harness, out _);
        var replay = await lifecycle.ExecuteLifecycleAsync(
            BulkOperationType.Delete,
            Request(productId, "2"),
            "failed-key");

        Assert.Equal(BulkLifecycleOutcomeKind.Failed, replay.Kind);
        Assert.Equal("interrupted.", replay.Message);
        orchestrator.Verify(item => item.AuthorAsync(
            It.IsAny<ProductAuthoringCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteLifecycleAsync_FinalizedFailure_FirstResponseMatchesExactReplay()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: true);
        var aggregator = new Mock<IBulkOperationResultAggregator>();
        aggregator
            .Setup(item => item.Aggregate(
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<BulkItemResult>>()))
            .Throws(new InvalidOperationException("Injected aggregate failure."));

        var (lifecycle, orchestrator) = CreateLifecycleService(
            harness,
            out _,
            aggregatorOverride: aggregator.Object);
        var request = Request(harness.PreparedProductId, "7");

        var first = await lifecycle.ExecuteLifecycleAsync(
            BulkOperationType.Deactivate,
            request,
            "exact-failed-replay-key");
        var replay = await lifecycle.ExecuteLifecycleAsync(
            BulkOperationType.Deactivate,
            request,
            "exact-failed-replay-key");

        Assert.Equal(BulkLifecycleOutcomeKind.Failed, first.Kind);
        Assert.Equal(BulkLifecycleOutcomeKind.Failed, replay.Kind);
        Assert.Equal("Bulk product lifecycle processing was interrupted.", first.Message);
        Assert.Equal(first.Message, replay.Message);
        orchestrator.Verify(item => item.AuthorAsync(
            It.IsAny<ProductAuthoringCommand>(), It.IsAny<CancellationToken>()), Times.Once);

        var stored = await SingleOuterRowAsync(harness);
        Assert.Equal(StatusCodes.Status500InternalServerError, stored.ResponseStatusCode);
        Assert.Equal(first.Message, new BulkIdempotencyService(
            new ProductMutationIdempotencyService(harness.Context),
            harness.TenantContext).ReadStoredFailureMessage(stored));
    }

    [Fact]
    public async Task ExecuteLifecycleAsync_LiveInProgressClaim_IsFencedWithoutMutations()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: true);
        var clock = new MutableTimeProvider(DateTime.UtcNow);
        var seedingIdempotency = new BulkIdempotencyService(
            new ProductMutationIdempotencyService(harness.Context),
            harness.TenantContext,
            clock);
        var fingerprint = seedingIdempotency.BuildFingerprint(
            BulkOperationType.Deactivate,
            [new NormalizedBulkProductItem(harness.PreparedProductId, 7)]);
        var claim = await seedingIdempotency.TryClaimAsync(BulkOperationType.Deactivate, "live-key", fingerprint);
        Assert.NotNull(claim.Record);
        var leasedUpdatedAt = claim.Record.UpdatedAt;

        clock.Set(clock.UtcNow.AddSeconds(1));
        var (service, orchestrator) = CreateLifecycleService(harness, out _, timeProvider: clock);
        var outcome = await service.ExecuteLifecycleAsync(
            BulkOperationType.Deactivate,
            Request(harness.PreparedProductId, "7"),
            "live-key");

        Assert.Equal(BulkLifecycleOutcomeKind.IdempotencyInProgress, outcome.Kind);
        orchestrator.Verify(item => item.AuthorAsync(
            It.IsAny<ProductAuthoringCommand>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(1, await CountRowsAsync(harness));
        var storedRow = await SingleRowAsync(harness);
        Assert.Null(storedRow.ResponseStatusCode);
        Assert.Equal(leasedUpdatedAt, storedRow.UpdatedAt);
    }

    [Fact]
    public async Task ExecuteLifecycleAsync_StaleAbandonedClaim_RecoversWithoutDuplicateCommittedMutation()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: true);
        var clock = new MutableTimeProvider(DateTime.UtcNow);
        var seedingIdempotency = new BulkIdempotencyService(
            new ProductMutationIdempotencyService(harness.Context),
            harness.TenantContext,
            clock);
        const string key = "terminated-key";
        var fingerprint = seedingIdempotency.BuildFingerprint(
            BulkOperationType.Activate,
            [new NormalizedBulkProductItem(harness.PreparedProductId, 7)]);

        // Simulate a terminated process: outer claim inserted, the item's
        // mutation durably committed, and no completion ever recorded.
        var claim = await seedingIdempotency.TryClaimAsync(BulkOperationType.Activate, key, fingerprint);
        Assert.NotNull(claim.Record);

        var product = await harness.Context.Products.SingleAsync(p => p.Id == harness.PreparedProductId);
        product.IsActive = true;
        product.RowVersion += 1;
        await harness.Context.SaveChangesAsync();
        var committedRowVersion = product.RowVersion;
        harness.Context.ChangeTracker.Clear();

        await BackdateClaimAsync(harness, claim.Record, clock.UtcNow);

        clock.Set(clock.UtcNow.Add(Lease).AddSeconds(5));
        var (service, orchestrator) = CreateLifecycleService(harness, out _, timeProvider: clock);
        var outcome = await service.ExecuteLifecycleAsync(
            BulkOperationType.Activate,
            Request(harness.PreparedProductId, "7"),
            key);

        // The recovered run completes without re-mutating the committed item.
        Assert.Equal(BulkLifecycleOutcomeKind.Success, outcome.Kind);
        orchestrator.Verify(item => item.AuthorAsync(
            It.IsAny<ProductAuthoringCommand>(), It.IsAny<CancellationToken>()), Times.Never);

        harness.Context.ChangeTracker.Clear();
        var reloaded = await harness.Context.Products.AsNoTracking().SingleAsync(p => p.Id == harness.PreparedProductId);
        Assert.Equal(committedRowVersion, reloaded.RowVersion);
        Assert.True(reloaded.IsActive);

        var storedRow = await SingleRowAsync(harness);
        Assert.Equal(200, storedRow.ResponseStatusCode);
        Assert.NotNull(storedRow.ResponseJson);
        Assert.Equal(1, await CountRowsAsync(harness));

        var replay = await service.ExecuteLifecycleAsync(
            BulkOperationType.Activate,
            Request(harness.PreparedProductId, "7"),
            key);
        Assert.Equal(BulkLifecycleOutcomeKind.Success, replay.Kind);
        Assert.Equal(
            System.Text.Json.JsonSerializer.Serialize(outcome.OperationResult),
            System.Text.Json.JsonSerializer.Serialize(replay.OperationResult));
    }

    [Fact]
    public async Task ExecuteLifecycleAsync_StaleClaimWithNothingCommitted_ResumesProcessing()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var productId = Guid.NewGuid();
        var product = new Product
        {
            Id = productId,
            TenantId = harness.TenantContext.TenantId!.Value,
            CategoryId = Guid.NewGuid(),
            Name = "Resume Product",
            Unit = "Piece",
            WeightUnit = "kg",
            DimensionUnit = "cm",
            IsActive = false,
            PublishStatus = PublishStatus.Draft,
            IsOnlineVisible = false,
            RowVersion = 9
        };
        harness.Context.Products.Add(product);
        await harness.Context.SaveChangesAsync();
        var expectedVersion = product.RowVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);
        harness.Context.ChangeTracker.Clear();

        var clock = new MutableTimeProvider(DateTime.UtcNow);
        var seedingIdempotency = new BulkIdempotencyService(
            new ProductMutationIdempotencyService(harness.Context),
            harness.TenantContext,
            clock);
        const string key = "resume-key";
        var fingerprint = seedingIdempotency.BuildFingerprint(
            BulkOperationType.Deactivate,
            [new NormalizedBulkProductItem(productId, product.RowVersion)]);

        var claim = await seedingIdempotency.TryClaimAsync(BulkOperationType.Deactivate, key, fingerprint);
        Assert.NotNull(claim.Record);

        await BackdateClaimAsync(harness, claim.Record, clock.UtcNow);
        clock.Set(clock.UtcNow.Add(Lease).AddSeconds(5));

        var accepted = ProductAuthoringResult.Accepted(productId, PublishStatus.Archived, "resume-correlation", 11);
        var (service, orchestrator) = CreateLifecycleService(
            harness, out _, authoringResult: accepted, timeProvider: clock);
        var outcome = await service.ExecuteLifecycleAsync(
            BulkOperationType.Deactivate,
            Request(productId, expectedVersion),
            key);

        Assert.Equal(BulkLifecycleOutcomeKind.Success, outcome.Kind);
        Assert.Equal(BulkItemStatus.Processed, Assert.Single(outcome.OperationResult!.Results).Status);
        orchestrator.Verify(item => item.AuthorAsync(
            It.IsAny<ProductAuthoringCommand>(), It.IsAny<CancellationToken>()), Times.Once);

        var storedRow = await SingleRowAsync(harness);
        Assert.Equal(200, storedRow.ResponseStatusCode);
        Assert.Equal(1, await CountRowsAsync(harness));
    }

    [Fact]
    public async Task ExecuteLifecycleAsync_UnverifiableCompletion_PropagatesFailureAndLeavesClaimRecoverable()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: true);
        var tenantId = harness.TenantContext.TenantId!.Value;
        var claimRecord = new ProductRequestIdempotency
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Key = "bulk:unverifiable-key",
            Operation = "BulkProduct",
            ProductId = Guid.Empty,
            RequestFingerprint = "unverifiable-fingerprint"
        };

        var idempotencyMock = new Mock<IBulkIdempotencyService>();
        idempotencyMock
            .Setup(item => item.ValidateKey(It.IsAny<string?>()))
            .Returns(BulkIdempotencyKeyValidationResult.Valid());
        idempotencyMock
            .Setup(item => item.BuildFingerprint(
                It.IsAny<BulkOperationType>(),
                It.IsAny<IReadOnlyList<NormalizedBulkProductItem>>(),
                It.IsAny<IReadOnlyDictionary<string, string?>>()))
            .Returns(claimRecord.RequestFingerprint);
        idempotencyMock
            .Setup(item => item.TryClaimAsync(
                It.IsAny<BulkOperationType>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BulkIdempotencyClaim(claimRecord, null));
        idempotencyMock
            .Setup(item => item.RenewClaimLeaseAsync(It.IsAny<ProductRequestIdempotency>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        idempotencyMock
            .Setup(item => item.TryCompleteAndVerifyAsync(
                It.IsAny<ProductRequestIdempotency>(),
                It.IsAny<BulkOperationResult>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((BulkOperationResult?)null);
        idempotencyMock
            .Setup(item => item.FinalizeFailureAsync(
                It.IsAny<ProductRequestIdempotency>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var (lifecycle, _) = CreateLifecycleService(
            harness, out var orchestratorMock, idempotencyOverride: idempotencyMock.Object);

        var failure = await Assert.ThrowsAsync<BulkAggregateCompletionUnverifiedException>(() =>
            lifecycle.ExecuteLifecycleAsync(
                BulkOperationType.Publish,
                Request(harness.PreparedProductId, "7"),
                "unverifiable-key"));

        Assert.Contains("durably", failure.Message, StringComparison.Ordinal);
        orchestratorMock.Verify(item => item.AuthorAsync(
            It.IsAny<ProductAuthoringCommand>(), It.IsAny<CancellationToken>()), Times.Once);
        idempotencyMock.Verify(item => item.TryCompleteAndVerifyAsync(
            It.IsAny<ProductRequestIdempotency>(),
            It.IsAny<BulkOperationResult>(),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        idempotencyMock.Verify(item => item.FinalizeFailureAsync(
            It.IsAny<ProductRequestIdempotency>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ConcurrentCrossOperationClaims_ProduceExactlyOneOuterClaimAuthority()
    {
        var dbName = $"bulk-concurrent-{Guid.NewGuid():N}";
        var connectionString = $"Data Source=file:{dbName}?mode=memory&cache=shared";
        var tenantId = Guid.NewGuid();

        await using var keeper = new SqliteConnection(connectionString);
        keeper.Open();
        var bootstrapOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(keeper)
            .Options;
        await using (var bootstrap = new BulkFoundationDbContext(bootstrapOptions, new TestTenantContext(tenantId)))
        {
            bootstrap.Database.EnsureCreated();
        }

        var operations = new[]
        {
            BulkOperationType.Activate,
            BulkOperationType.Deactivate,
            BulkOperationType.Publish,
            BulkOperationType.Unpublish,
            BulkOperationType.Delete
        };

        var outcomes = await Task.WhenAll(operations.Select(operation => Task.Run(async () =>
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    await using var connection = new SqliteConnection(connectionString);
                    await connection.OpenAsync();
                    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                        .UseSqlite(connection)
                        .Options;
                    await using var context = new BulkFoundationDbContext(options, new TestTenantContext(tenantId));
                    var service = new BulkIdempotencyService(
                        new ProductMutationIdempotencyService(context),
                        new TestTenantContext(tenantId));
                    var fingerprint = service.BuildFingerprint(
                        operation,
                        [new NormalizedBulkProductItem(Guid.NewGuid(), 7)]);
                    var claim = await service.TryClaimAsync(operation, "concurrent-key", fingerprint);
                    return (Owned: claim.Record is not null, Kind: claim.Replay?.Kind);
                }
                catch (SqliteException exception) when (
                    attempt < 4 &&
                    exception.Message.Contains("locked", StringComparison.OrdinalIgnoreCase))
                {
                    await Task.Delay(20 * (attempt + 1));
                }
            }
        })));

        Assert.Equal(1, outcomes.Count(outcome => outcome.Owned));
        Assert.All(outcomes, outcome => Assert.NotEqual(
            BulkIdempotencyReplayKind.Completed, outcome.Kind));

        await using var verifyConnection = new SqliteConnection(connectionString);
        await verifyConnection.OpenAsync();
        var verifyOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(verifyConnection)
            .Options;
        await using var verifyContext = new BulkFoundationDbContext(verifyOptions, new TestTenantContext(tenantId));
        Assert.Equal(1, await verifyContext.ProductRequestIdempotencyRecords.CountAsync());
    }

    [Fact]
    public async Task ExecuteLifecycleAsync_RecoveredDeleteWithCommittedAcceptedMutation_ReconcilesProcessedAndReplaysExactly()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: true);
        var clock = new MutableTimeProvider(DateTime.UtcNow);
        var inner = new ProductMutationIdempotencyService(harness.Context, clock);
        var bulkIdempotency = new BulkIdempotencyService(inner, harness.TenantContext, clock);
        const string key = "recovered-delete-key";
        var productId = harness.PreparedProductId;
        var fingerprint = bulkIdempotency.BuildFingerprint(
            BulkOperationType.Delete,
            [new NormalizedBulkProductItem(productId, 7)]);

        var outerClaim = await bulkIdempotency.TryClaimAsync(BulkOperationType.Delete, key, fingerprint);
        Assert.NotNull(outerClaim.Record);

        // Simulate the terminated process: the Remove mutation committed
        // atomically with its accepted per-product authoring record. The
        // SQLite row-version interceptor bumps the version on update.
        var product = await harness.Context.Products.SingleAsync(p => p.Id == productId);
        product.IsDeleted = true;
        await harness.Context.SaveChangesAsync();
        var committedResult = ProductAuthoringResult.Accepted(
            productId, PublishStatus.Archived, "recovery-test-correlation", 8);
        await SeedBulkItemRecordAsync(
            harness, inner, bulkIdempotency, BulkOperationType.Delete, key, productId,
            StatusCodes.Status202Accepted, committedResult);

        await BackdateClaimAsync(harness, outerClaim.Record, clock.UtcNow);
        clock.Set(clock.UtcNow.Add(Lease).AddSeconds(5));

        var (service, orchestrator) = CreateLifecycleService(harness, out _, timeProvider: clock);
        var outcome = await service.ExecuteLifecycleAsync(
            BulkOperationType.Delete,
            Request(productId, "7"),
            key);

        // The recovered run reconciles the committed mutation instead of
        // permanently storing a contradicting NOT_FOUND result.
        Assert.Equal(BulkLifecycleOutcomeKind.Success, outcome.Kind);
        var itemResult = Assert.Single(outcome.OperationResult!.Results);
        Assert.Equal(BulkItemStatus.Processed, itemResult.Status);
        Assert.Equal("7", itemResult.PreviousVersion);
        orchestrator.Verify(item => item.AuthorAsync(
            It.IsAny<ProductAuthoringCommand>(), It.IsAny<CancellationToken>()), Times.Never);

        harness.Context.ChangeTracker.Clear();
        var reloaded = await harness.Context.Products
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(p => p.Id == productId);
        Assert.True(reloaded.IsDeleted);
        Assert.Equal(8u, reloaded.RowVersion);

        var outerRow = await SingleOuterRowAsync(harness);
        Assert.Equal(200, outerRow.ResponseStatusCode);
        Assert.NotNull(outerRow.ResponseJson);

        // The recovered aggregate replays exactly on subsequent calls.
        var replay = await service.ExecuteLifecycleAsync(
            BulkOperationType.Delete,
            Request(productId, "7"),
            key);
        Assert.Equal(BulkLifecycleOutcomeKind.Success, replay.Kind);
        Assert.Equal(
            System.Text.Json.JsonSerializer.Serialize(outcome.OperationResult),
            System.Text.Json.JsonSerializer.Serialize(replay.OperationResult));
    }

    [Fact]
    public async Task ExecuteLifecycleAsync_RecoveredActivateWithCommittedAcceptedMutation_ReconcilesConflictIntoProcessed()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var tenantId = harness.TenantContext.TenantId!.Value;
        var productId = Guid.NewGuid();
        harness.Context.Products.Add(new Product
        {
            Id = productId,
            TenantId = tenantId,
            CategoryId = Guid.NewGuid(),
            Name = "Recovered Activate Product",
            Unit = "Piece",
            WeightUnit = "kg",
            DimensionUnit = "cm",
            IsActive = false,
            PublishStatus = PublishStatus.Draft,
            IsOnlineVisible = false,
            RowVersion = 20
        });
        await harness.Context.SaveChangesAsync();
        harness.Context.ChangeTracker.Clear();
        const string expectedVersion = "21";

        var clock = new MutableTimeProvider(DateTime.UtcNow);
        var inner = new ProductMutationIdempotencyService(harness.Context, clock);
        var bulkIdempotency = new BulkIdempotencyService(inner, harness.TenantContext, clock);
        const string key = "recovered-activate-key";
        var fingerprint = bulkIdempotency.BuildFingerprint(
            BulkOperationType.Activate,
            [new NormalizedBulkProductItem(productId, 21)]);

        var outerClaim = await bulkIdempotency.TryClaimAsync(BulkOperationType.Activate, key, fingerprint);
        Assert.NotNull(outerClaim.Record);

        // The activation committed and advanced the row version, so the
        // recovered run now classifies the item as a concurrency conflict.
        var product = await harness.Context.Products.SingleAsync(p => p.Id == productId);
        product.IsActive = true;
        await harness.Context.SaveChangesAsync();
        var committedResult = ProductAuthoringResult.Accepted(
            productId, PublishStatus.Draft, "recovery-test-correlation", 22);
        await SeedBulkItemRecordAsync(
            harness, inner, bulkIdempotency, BulkOperationType.Activate, key, productId,
            StatusCodes.Status202Accepted, committedResult);

        await BackdateClaimAsync(harness, outerClaim.Record, clock.UtcNow);
        clock.Set(clock.UtcNow.Add(Lease).AddSeconds(5));

        var (service, orchestrator) = CreateLifecycleService(harness, out _, timeProvider: clock);
        var outcome = await service.ExecuteLifecycleAsync(
            BulkOperationType.Activate,
            Request(productId, expectedVersion),
            key);

        Assert.Equal(BulkLifecycleOutcomeKind.Success, outcome.Kind);
        var itemResult = Assert.Single(outcome.OperationResult!.Results);
        Assert.Equal(BulkItemStatus.Processed, itemResult.Status);
        Assert.Equal(expectedVersion, itemResult.PreviousVersion);
        orchestrator.Verify(item => item.AuthorAsync(
            It.IsAny<ProductAuthoringCommand>(), It.IsAny<CancellationToken>()), Times.Never);

        harness.Context.ChangeTracker.Clear();
        var reloaded = await harness.Context.Products.AsNoTracking().SingleAsync(p => p.Id == productId);
        Assert.Equal(22u, reloaded.RowVersion);
        Assert.True(reloaded.IsActive);

        var replay = await service.ExecuteLifecycleAsync(
            BulkOperationType.Activate,
            Request(productId, expectedVersion),
            key);
        Assert.Equal(
            System.Text.Json.JsonSerializer.Serialize(outcome.OperationResult),
            System.Text.Json.JsonSerializer.Serialize(replay.OperationResult));
    }

    [Theory]
    [InlineData(RecoveryRecordCase.Missing)]
    [InlineData(RecoveryRecordCase.InProgress)]
    [InlineData(RecoveryRecordCase.Malformed)]
    [InlineData(RecoveryRecordCase.Rejected)]
    public async Task ExecuteLifecycleAsync_RecoveredItemWithoutCommittedAcceptedMutation_PreservesConflictClassification(
        RecoveryRecordCase recordCase)
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var tenantId = harness.TenantContext.TenantId!.Value;
        var productId = Guid.NewGuid();
        harness.Context.Products.Add(new Product
        {
            Id = productId,
            TenantId = tenantId,
            CategoryId = Guid.NewGuid(),
            Name = "Preserved Conflict Product",
            Unit = "Piece",
            WeightUnit = "kg",
            DimensionUnit = "cm",
            IsActive = false,
            PublishStatus = PublishStatus.Draft,
            IsOnlineVisible = false,
            RowVersion = 30
        });
        await harness.Context.SaveChangesAsync();
        harness.Context.ChangeTracker.Clear();

        var clock = new MutableTimeProvider(DateTime.UtcNow);
        var inner = new ProductMutationIdempotencyService(harness.Context, clock);
        var bulkIdempotency = new BulkIdempotencyService(inner, harness.TenantContext, clock);
        var key = $"preserved-conflict-{recordCase}";
        var fingerprint = bulkIdempotency.BuildFingerprint(
            BulkOperationType.Activate,
            [new NormalizedBulkProductItem(productId, 31)]);

        var outerClaim = await bulkIdempotency.TryClaimAsync(BulkOperationType.Activate, key, fingerprint);
        Assert.NotNull(outerClaim.Record);

        var product = await harness.Context.Products.SingleAsync(p => p.Id == productId);
        product.IsActive = true;
        await harness.Context.SaveChangesAsync();

        switch (recordCase)
        {
            case RecoveryRecordCase.InProgress:
                await SeedBulkItemRecordAsync(
                    harness, inner, bulkIdempotency, BulkOperationType.Activate, key, productId,
                    null, null);
                break;
            case RecoveryRecordCase.Malformed:
                await SeedBulkItemRecordAsync(
                    harness, inner, bulkIdempotency, BulkOperationType.Activate, key, productId,
                    StatusCodes.Status202Accepted,
                    ProductAuthoringResult.Accepted(productId, PublishStatus.Draft, "recovery-test-correlation", 32),
                    record =>
                    {
                        record.ResponseStatusCode = StatusCodes.Status202Accepted;
                        record.ResponseJson = "{not-valid-json";
                        record.CompletedAtUtc = DateTime.UtcNow;
                    });
                break;
            case RecoveryRecordCase.Rejected:
                await SeedBulkItemRecordAsync(
                    harness, inner, bulkIdempotency, BulkOperationType.Activate, key, productId,
                    StatusCodes.Status400BadRequest,
                    new ProductAuthoringResult(
                        ProductAuthoringStatus.ValidationFailed,
                        productId,
                        null,
                        null,
                        [new ProductAuthoringValidationError("X", "expectedVersion", "rejected")],
                        "recovery-test-correlation",
                        new Dictionary<string, string>()));
                break;
        }

        await BackdateClaimAsync(harness, outerClaim.Record, clock.UtcNow);
        clock.Set(clock.UtcNow.Add(Lease).AddSeconds(5));

        var (service, orchestrator) = CreateLifecycleService(harness, out _, timeProvider: clock);
        var outcome = await service.ExecuteLifecycleAsync(
            BulkOperationType.Activate,
            Request(productId, "31"),
            key);

        // Without a durably accepted committed mutation, the current-state
        // classification stays authoritative and no authoring runs again.
        Assert.Equal(BulkLifecycleOutcomeKind.Success, outcome.Kind);
        var itemResult = Assert.Single(outcome.OperationResult!.Results);
        Assert.Equal(BulkItemStatus.Failed, itemResult.Status);
        Assert.Equal(BulkErrorCodes.ConcurrencyConflict, itemResult.Code);
        orchestrator.Verify(item => item.AuthorAsync(
            It.IsAny<ProductAuthoringCommand>(), It.IsAny<CancellationToken>()), Times.Never);

        var outerRow = await SingleOuterRowAsync(harness);
        Assert.Equal(200, outerRow.ResponseStatusCode);
    }

    [Fact]
    public async Task ExecuteLifecycleAsync_RecoveredDeleteWithoutCommittedMutation_PreservesNotFoundClassification()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: true);
        var clock = new MutableTimeProvider(DateTime.UtcNow);
        var inner = new ProductMutationIdempotencyService(harness.Context, clock);
        var bulkIdempotency = new BulkIdempotencyService(inner, harness.TenantContext, clock);
        const string key = "preserved-notfound-key";
        var productId = harness.ConflictProductId;
        var fingerprint = bulkIdempotency.BuildFingerprint(
            BulkOperationType.Delete,
            [new NormalizedBulkProductItem(productId, 7)]);

        var outerClaim = await bulkIdempotency.TryClaimAsync(BulkOperationType.Delete, key, fingerprint);
        Assert.NotNull(outerClaim.Record);

        // The product is removed without ever persisting a per-product
        // authoring record, so nothing proves a committed bulk mutation.
        var product = await harness.Context.Products.SingleAsync(p => p.Id == productId);
        product.IsDeleted = true;
        await harness.Context.SaveChangesAsync();

        await BackdateClaimAsync(harness, outerClaim.Record, clock.UtcNow);
        clock.Set(clock.UtcNow.Add(Lease).AddSeconds(5));

        var (service, orchestrator) = CreateLifecycleService(harness, out _, timeProvider: clock);
        var outcome = await service.ExecuteLifecycleAsync(
            BulkOperationType.Delete,
            Request(productId, "7"),
            key);

        Assert.Equal(BulkLifecycleOutcomeKind.Success, outcome.Kind);
        var itemResult = Assert.Single(outcome.OperationResult!.Results);
        Assert.Equal(BulkItemStatus.Failed, itemResult.Status);
        Assert.Equal(BulkErrorCodes.NotFound, itemResult.Code);
        orchestrator.Verify(item => item.AuthorAsync(
            It.IsAny<ProductAuthoringCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TryResolveCommittedBulkItemMutation_FailsClosedOnMismatchedIdentityOrNonAcceptedResult()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var tenantId = harness.TenantContext.TenantId!.Value;
        var productId = Guid.NewGuid();
        var clock = new MutableTimeProvider(DateTime.UtcNow);
        var inner = new ProductMutationIdempotencyService(harness.Context, clock);
        var bulkIdempotency = new BulkIdempotencyService(inner, harness.TenantContext, clock);
        const string key = "fail-closed-key";
        var accepted = ProductAuthoringResult.Accepted(
            productId, PublishStatus.Archived, "recovery-test-correlation", 5);

        await SeedBulkItemRecordAsync(
            harness, inner, bulkIdempotency, BulkOperationType.Delete, key, productId,
            StatusCodes.Status202Accepted, accepted);

        var resolved = await bulkIdempotency.TryResolveCommittedBulkItemMutationAsync(
            BulkOperationType.Delete, key, tenantId, productId);
        Assert.NotNull(resolved);
        Assert.Equal(ProductAuthoringStatus.Accepted, resolved.Status);

        // Foreign tenant: never proof of a committed mutation.
        Assert.Null(await bulkIdempotency.TryResolveCommittedBulkItemMutationAsync(
            BulkOperationType.Delete, key, Guid.NewGuid(), productId));

        // Mismatched product identity in the stored record: fail closed.
        Assert.Null(await bulkIdempotency.TryResolveCommittedBulkItemMutationAsync(
            BulkOperationType.Delete, key, tenantId, Guid.NewGuid()));

        // A record stored under a different operation mapping is not visible
        // to this item's deterministic identity.
        Assert.Null(await bulkIdempotency.TryResolveCommittedBulkItemMutationAsync(
            BulkOperationType.Activate, key, tenantId, productId));

        // Empty identity inputs fail closed.
        Assert.Null(await bulkIdempotency.TryResolveCommittedBulkItemMutationAsync(
            BulkOperationType.Delete, key, Guid.Empty, productId));
        Assert.Null(await bulkIdempotency.TryResolveCommittedBulkItemMutationAsync(
            BulkOperationType.Delete, " ", tenantId, productId));
    }

    public enum RecoveryRecordCase
    {
        Missing,
        InProgress,
        Malformed,
        Rejected
    }

    private static async Task SeedBulkItemRecordAsync(
        BulkFoundationTestHarness harness,
        ProductMutationIdempotencyService inner,
        BulkIdempotencyService bulkIdempotency,
        BulkOperationType operation,
        string key,
        Guid productId,
        int? statusCode,
        object? responsePayload,
        Action<ProductRequestIdempotency>? mutate = null)
    {
        var storageKey = inner.ScopeKey(
            BulkIdempotencyService.AuthoringScope,
            bulkIdempotency.BuildPerProductIdempotencyKey(operation, key, productId));
        var storageOperation = BulkIdempotencyService.BulkItemAuthoringOperation(operation) switch
        {
            ProductAuthoringOperation.Update => "AuthoringUpdate",
            ProductAuthoringOperation.Archive => "AuthoringArchive",
            ProductAuthoringOperation.Remove => "AuthoringRemove",
            _ => "AuthoringUnknown"
        };

        var claim = await inner.TryClaimAsync(new ProductMutationIdempotencyRequest(
            harness.TenantContext.TenantId!.Value,
            storageKey,
            storageOperation,
            productId,
            null,
            "seeded-item-fingerprint"));
        Assert.NotNull(claim.Record);

        if (statusCode.HasValue)
        {
            inner.Complete(claim.Record, statusCode.Value, responsePayload, productId);
        }

        mutate?.Invoke(claim.Record);
        await harness.Context.SaveChangesAsync();
        harness.Context.ChangeTracker.Clear();
    }

    private static Task<ProductRequestIdempotency> SingleOuterRowAsync(BulkFoundationTestHarness harness) =>
        harness.Context.ProductRequestIdempotencyRecords
            .AsNoTracking()
            .SingleAsync(row => row.Operation == BulkIdempotencyService.OuterClaimStorageOperation);

    private static async Task<ProductMutationIdempotencyRecoveryResult> RecoverAsync(
        ProductMutationIdempotencyService inner,
        BulkFoundationTestHarness harness,
        string key,
        string fingerprint,
        MutableTimeProvider clock) =>
        await inner.TryRecoverAbandonedClaimAsync(
            new ProductMutationIdempotencyRequest(
                harness.TenantContext.TenantId!.Value,
                inner.ScopeKey(BulkIdempotencyService.BulkScope, key),
                "BulkProduct",
                Guid.Empty,
                null,
                fingerprint),
            Lease,
            clock.UtcNow);

    private static async Task BackdateClaimAsync(
        BulkFoundationTestHarness harness,
        ProductRequestIdempotency record,
        DateTime utcNow)
    {
        var pastUtc = utcNow - TimeSpan.FromMinutes(5);
        var tracked = await harness.Context.ProductRequestIdempotencyRecords
            .SingleAsync(row => row.Id == record.Id);
        tracked.CreatedAt = pastUtc;
        tracked.UpdatedAt = pastUtc;
        await harness.Context.SaveChangesAsync();
        harness.Context.ChangeTracker.Clear();
    }

    private static Task<int> CountRowsAsync(BulkFoundationTestHarness harness) =>
        harness.Context.ProductRequestIdempotencyRecords.CountAsync();

    private static Task<ProductRequestIdempotency> SingleRowAsync(BulkFoundationTestHarness harness) =>
        harness.Context.ProductRequestIdempotencyRecords.AsNoTracking().SingleAsync();

    internal static BulkProductRequest Request(Guid productId, string version) =>
        new([new BulkProductRequestItem(productId.ToString(), version)]);

    internal static (IBulkProductLifecycleService Lifecycle, Mock<IProductAuthoringOrchestrator> Orchestrator)
        CreateLifecycleService(
            BulkFoundationTestHarness harness,
            out Mock<IProductAuthoringOrchestrator> orchestratorMock,
            ProductAuthoringResult? authoringResult = null,
            TimeProvider? timeProvider = null,
            IBulkIdempotencyService? idempotencyOverride = null,
            TimeSpan? leaseHeartbeatInterval = null,
            IServiceScopeFactory? leaseRenewalScopeFactory = null,
            IBulkOperationResultAggregator? aggregatorOverride = null)
    {
        orchestratorMock = new Mock<IProductAuthoringOrchestrator>();
        orchestratorMock
            .Setup(item => item.AuthorAsync(It.IsAny<ProductAuthoringCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(authoringResult ?? ProductAuthoringResult.InfrastructurePrepared("test-correlation"));

        var commandMapperMock = new Mock<IProductAuthoringCommandMapper>();
        commandMapperMock
            .Setup(item => item.FromLifecycleProduct(It.IsAny<ProductAuthoringOperation>(), It.IsAny<Product>()))
            .Returns((ProductAuthoringOperation operation, Product product) =>
                BuildTestCommand(operation, product));

        // The inner storage service must share the same injected clock so
        // claim lease timestamps and renewals stay on one time source.
        var idempotencyService = idempotencyOverride ?? new BulkIdempotencyService(
            new ProductMutationIdempotencyService(harness.Context, timeProvider),
            harness.TenantContext,
            timeProvider);
        var loader = new BulkProductLoader(harness.Context, harness.TenantContext);
        var preparationService = new BulkPreparationService(
            new BulkProductRequestValidator(),
            loader,
            new StaticCorrelationIdAccessor("recovery-test-correlation"));
        var aggregator = aggregatorOverride ?? new BulkOperationResultAggregator();

        var lifecycle = new BulkProductLifecycleService(
            preparationService,
            idempotencyService,
            aggregator,
            loader,
            orchestratorMock.Object,
            commandMapperMock.Object,
            BulkProductLifecycleServiceTests.CreateValidationMock().Object,
            harness.Context,
            harness.TenantContext,
            NullLogger<BulkProductLifecycleService>.Instance,
            leaseRenewalScopeFactory,
            leaseHeartbeatInterval);

        return (lifecycle, orchestratorMock);
    }

    private static ProductAuthoringCommand BuildTestCommand(ProductAuthoringOperation operation, Product product)
    {
        return new ProductAuthoringCommand(
            operation,
            new ProductAuthoringIdentity(
                product.TenantId,
                product.Id,
                product.CategoryId,
                null,
                null),
            new ProductAuthoringContent(
                product.Name,
                null, null, null, null, null,
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<ProductAuthoringFaq>()),
            new ProductAuthoringPricing(0, 0, null, false, 0),
            new ProductAuthoringPublication(product.PublishStatus, product.IsActive, product.IsOnlineVisible, true, false),
            new ProductAuthoringSeo(null, null, null, null),
            new ProductAuthoringShippingProfile(0, 0, 0, 0, "kg", "cm"),
            new ProductAuthoringInventoryProfile("Piece", 0, false, false, 0, false),
            new ProductAuthoringUnits(null, null, Array.Empty<ProductAuthoringUnit>()),
            Array.Empty<ProductAuthoringVariant>(),
            new ProductAuthoringMetadata(
                false, null, null, null, "test-correlation", null, null,
                new Dictionary<string, string>()));
    }

    internal sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public MutableTimeProvider(DateTime initialUtc)
        {
            _utcNow = new DateTimeOffset(DateTime.SpecifyKind(initialUtc, DateTimeKind.Utc));
        }

        public DateTime UtcNow => _utcNow.UtcDateTime;

        public void Set(DateTime utcNow) =>
            _utcNow = new DateTimeOffset(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc));

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
