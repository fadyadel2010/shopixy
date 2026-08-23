using Moq;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Infrastructure.Idempotency;
using PointOfSale.API.Services.ProductAuthoring.Bulk;
using PointOfSale.Core.DTOs.Bulk;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Tests.Bulk;

public sealed class BulkIdempotencyServiceTests
{
    [Fact]
    public void ValidateKey_RequiresKeyAndEnforcesMaximumLength()
    {
        var service = CreateFingerprintService();

        Assert.Equal(BulkErrorCodes.IdempotencyKeyRequired, service.ValidateKey(null).ErrorCode);
        Assert.Equal(BulkErrorCodes.IdempotencyKeyRequired, service.ValidateKey("  ").ErrorCode);
        Assert.Equal(
            BulkErrorCodes.IdempotencyKeyTooLong,
            service.ValidateKey(new string('x', BulkIdempotencyService.MaxIdempotencyKeyLength + 1)).ErrorCode);
        Assert.True(service.ValidateKey(new string('x', BulkIdempotencyService.MaxIdempotencyKeyLength)).IsValid);
    }

    [Fact]
    public void BuildFingerprint_IsOrderAndDuplicateInsensitiveButIncludesOperationVersionAndOptions()
    {
        var service = CreateFingerprintService();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var first = new[]
        {
            new NormalizedBulkProductItem(firstId, 10),
            new NormalizedBulkProductItem(secondId, 20),
            new NormalizedBulkProductItem(firstId, 10)
        };
        var reordered = new[]
        {
            new NormalizedBulkProductItem(secondId, 20),
            new NormalizedBulkProductItem(firstId, 10)
        };
        var optionsA = new Dictionary<string, string?> { ["mode"] = "strict", ["reason"] = "catalog" };
        var optionsReordered = new Dictionary<string, string?> { ["reason"] = "catalog", ["mode"] = "strict" };

        var fingerprint = service.BuildFingerprint(BulkOperationType.Publish, first, optionsA);

        Assert.Equal(
            fingerprint,
            service.BuildFingerprint(BulkOperationType.Publish, reordered, optionsReordered));
        Assert.NotEqual(
            fingerprint,
            service.BuildFingerprint(BulkOperationType.Unpublish, reordered, optionsA));
        Assert.NotEqual(
            fingerprint,
            service.BuildFingerprint(
                BulkOperationType.Publish,
                [new NormalizedBulkProductItem(firstId, 11), new NormalizedBulkProductItem(secondId, 20)],
                optionsA));
        Assert.NotEqual(
            fingerprint,
            service.BuildFingerprint(
                BulkOperationType.Publish,
                reordered,
                new Dictionary<string, string?> { ["mode"] = "lenient", ["reason"] = "catalog" }));
    }

    [Fact]
    public void BuildFingerprint_RejectsConflictingDuplicateVersionsAndUnknownOperation()
    {
        var service = CreateFingerprintService();
        var productId = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => service.BuildFingerprint(
            BulkOperationType.Publish,
            [new NormalizedBulkProductItem(productId, 1), new NormalizedBulkProductItem(productId, 2)]));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            service.BuildFingerprint((BulkOperationType)999, [new NormalizedBulkProductItem(productId, 1)]));
    }

    [Fact]
    public async Task ClaimCompleteAndReplay_UsesExistingTenantScopedStorage()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var service = new BulkIdempotencyService(
            new ProductMutationIdempotencyService(harness.Context),
            harness.TenantContext);
        var item = new NormalizedBulkProductItem(Guid.NewGuid(), 7);
        var fingerprint = service.BuildFingerprint(BulkOperationType.Activate, [item]);

        var claim = await service.TryClaimAsync(BulkOperationType.Activate, " bulk-replay-key ", fingerprint);

        Assert.NotNull(claim.Record);
        Assert.Null(claim.Replay);
        var response = new BulkOperationResult(
            1,
            1,
            1,
            0,
            0,
            "bulk-replay-correlation",
            [BulkItemResult.Processed(item.ProductId, item.ExpectedVersion, 8)]);
        service.Complete(claim.Record, 200, response);
        await harness.Context.SaveChangesAsync();
        harness.Context.ChangeTracker.Clear();

        var replay = await service.TryClaimAsync(BulkOperationType.Activate, "bulk-replay-key", fingerprint);

        Assert.Null(replay.Record);
        Assert.Equal(BulkIdempotencyReplayKind.Completed, replay.Replay?.Kind);
        var stored = service.ReadStoredResponse(replay.Replay?.Record);
        Assert.NotNull(stored);
        Assert.Equal("bulk-replay-correlation", stored.CorrelationId);
        Assert.Equal(BulkItemStatus.Processed, Assert.Single(stored.Results).Status);
        Assert.Equal(1, await harness.Context.ProductRequestIdempotencyRecords.CountAsync());
    }

    [Theory]
    [InlineData(BulkOperationType.Activate, "BulkProductActivate")]
    [InlineData(BulkOperationType.Deactivate, "BulkProductDeactivate")]
    [InlineData(BulkOperationType.Publish, "BulkProductPublish")]
    [InlineData(BulkOperationType.Unpublish, "BulkProductUnpublish")]
    [InlineData(BulkOperationType.Delete, "BulkProductDelete")]
    public void GetCanonicalOperationName_UsesCanonicalBulkProductNames(
        BulkOperationType operation,
        string expectedOperation)
    {
        Assert.Equal(expectedOperation, BulkIdempotencyService.GetCanonicalOperationName(operation));
    }

    [Fact]
    public async Task Claim_StoresOneOuterClaimAuthorityForEveryOperation()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var service = new BulkIdempotencyService(
            new ProductMutationIdempotencyService(harness.Context),
            harness.TenantContext);
        var item = new NormalizedBulkProductItem(Guid.NewGuid(), 7);
        var fingerprint = service.BuildFingerprint(BulkOperationType.Activate, [item]);

        var claim = await service.TryClaimAsync(BulkOperationType.Activate, "canonical-activate", fingerprint);

        Assert.NotNull(claim.Record);
        Assert.Null(claim.Replay);
        Assert.Equal(service.OuterClaimOperation, claim.Record.Operation);
    }

    [Theory]
    [InlineData(BulkOperationType.Deactivate)]
    [InlineData(BulkOperationType.Publish)]
    [InlineData(BulkOperationType.Unpublish)]
    [InlineData(BulkOperationType.Delete)]
    public async Task SameKeyAcrossDifferentOperations_SequentiallyConflictsWithOneOuterClaim(
        BulkOperationType otherOperation)
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var service = new BulkIdempotencyService(
            new ProductMutationIdempotencyService(harness.Context),
            harness.TenantContext);
        var item = new NormalizedBulkProductItem(Guid.NewGuid(), 7);
        var originalFingerprint = service.BuildFingerprint(BulkOperationType.Activate, [item]);

        var claim = await service.TryClaimAsync(
            BulkOperationType.Activate, "cross-operation-key", originalFingerprint);
        Assert.NotNull(claim.Record);

        var otherFingerprint = service.BuildFingerprint(otherOperation, [item]);
        var conflict = await service.TryClaimAsync(
            otherOperation, "cross-operation-key", otherFingerprint);

        Assert.Equal(BulkIdempotencyReplayKind.Conflict, conflict.Replay?.Kind);
        Assert.Equal(1, await harness.Context.ProductRequestIdempotencyRecords.CountAsync());
    }

    [Fact]
    public async Task SameKeyAndOperationWithDifferentSemantics_Conflicts()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var service = new BulkIdempotencyService(
            new ProductMutationIdempotencyService(harness.Context),
            harness.TenantContext);
        var productId = Guid.NewGuid();
        var original = service.BuildFingerprint(
            BulkOperationType.Delete,
            [new NormalizedBulkProductItem(productId, 1)]);
        var changed = service.BuildFingerprint(
            BulkOperationType.Delete,
            [new NormalizedBulkProductItem(productId, 2)]);

        var claim = await service.TryClaimAsync(BulkOperationType.Delete, "bulk-conflict-key", original);
        Assert.NotNull(claim.Record);

        var conflict = await service.TryClaimAsync(BulkOperationType.Delete, "bulk-conflict-key", changed);

        Assert.Equal(BulkIdempotencyReplayKind.Conflict, conflict.Replay?.Kind);
        Assert.Equal(1, await harness.Context.ProductRequestIdempotencyRecords.CountAsync());
    }

    [Fact]
    public async Task Resolve_DistinguishesMissingInProgressAndCompletedWithoutPayload()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var service = new BulkIdempotencyService(
            new ProductMutationIdempotencyService(harness.Context),
            harness.TenantContext);
        var fingerprint = service.BuildFingerprint(
            BulkOperationType.Deactivate,
            [new NormalizedBulkProductItem(Guid.NewGuid(), 3)]);

        var missing = await service.TryResolveAsync(
            BulkOperationType.Deactivate,
            "bulk-resolution-key",
            fingerprint);
        Assert.Equal(BulkIdempotencyReplayKind.NotFound, missing.Kind);

        var claim = await service.TryClaimAsync(
            BulkOperationType.Deactivate,
            "bulk-resolution-key",
            fingerprint);
        var inProgress = await service.TryResolveAsync(
            BulkOperationType.Deactivate,
            "bulk-resolution-key",
            fingerprint);
        Assert.Equal(BulkIdempotencyReplayKind.InProgress, inProgress.Kind);

        service.Complete(claim.Record, 204, null);
        await harness.Context.SaveChangesAsync();
        harness.Context.ChangeTracker.Clear();
        var completed = await service.TryResolveAsync(
            BulkOperationType.Deactivate,
            "bulk-resolution-key",
            fingerprint);
        Assert.Equal(BulkIdempotencyReplayKind.Completed, completed.Kind);
        Assert.Null(service.ReadStoredResponse(completed.Record));
    }

    [Fact]
    public async Task ClaimAndResolve_RejectInvalidKeysBeforeStorage()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var service = new BulkIdempotencyService(
            new ProductMutationIdempotencyService(harness.Context),
            harness.TenantContext);
        var fingerprint = service.BuildFingerprint(
            BulkOperationType.Publish,
            [new NormalizedBulkProductItem(Guid.NewGuid(), 1)]);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.TryClaimAsync(BulkOperationType.Publish, " ", fingerprint));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.TryResolveAsync(
                BulkOperationType.Publish,
                new string('x', BulkIdempotencyService.MaxIdempotencyKeyLength + 1),
                fingerprint));
        Assert.Equal(0, await harness.Context.ProductRequestIdempotencyRecords.CountAsync());
    }

    private static BulkIdempotencyService CreateFingerprintService() =>
        new(
            Mock.Of<IProductMutationIdempotencyService>(),
            new TestTenantContext(Guid.NewGuid()));
}
