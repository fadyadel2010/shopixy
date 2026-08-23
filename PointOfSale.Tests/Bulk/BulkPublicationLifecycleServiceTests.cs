using Microsoft.EntityFrameworkCore;
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

namespace PointOfSale.Tests.Bulk;

public sealed class BulkPublicationLifecycleServiceTests
{
    // ------------------------------------------------------------------
    // Publish semantics
    // ------------------------------------------------------------------

    [Fact]
    public async Task Publish_ActiveReadyDraft_ProcessesThroughAuthoritativeUpdateToPublishedVisible()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var product = await SeedProductAsync(
            harness, isActive: true, PublishStatus.Draft, isOnlineVisible: false,
            isSearchable: true, isFeatured: true);
        ProductAuthoringCommand? captured = null;
        var (service, orchestrator) = CreateService(harness, out _);
        orchestrator.Setup(item => item.AuthorAsync(It.IsAny<ProductAuthoringCommand>(), It.IsAny<CancellationToken>()))
            .Callback<ProductAuthoringCommand, CancellationToken>((command, _) => captured = command)
            .ReturnsAsync(ProductAuthoringResult.Accepted(product.Id, PublishStatus.Published, "bulk-publish", 31));

        var outcome = await service.ExecuteLifecycleAsync(
            BulkOperationType.Publish,
            Request(product),
            "publish-ready-key",
            default);

        Assert.Equal(BulkLifecycleOutcomeKind.Success, outcome.Kind);
        var item = Assert.Single(outcome.OperationResult!.Results);
        Assert.Equal(BulkItemStatus.Processed, item.Status);
        Assert.Equal(product.RowVersion.ToString(), item.NewVersion);

        Assert.Equal(ProductAuthoringOperation.Update, captured!.Operation);
        Assert.Equal(PublishStatus.Published, captured.Publication.PublishStatus);
        Assert.True(captured.Publication.IsActive);
        Assert.True(captured.Publication.IsOnlineVisible);
        Assert.True(captured.Publication.IsSearchable);
        Assert.True(captured.Publication.IsFeatured);
        Assert.Equal(product.RowVersion, captured.Metadata.ExpectedVersion);
        Assert.StartsWith("bulk-item:", captured.Metadata.IdempotencyKey, StringComparison.Ordinal);
        Assert.DoesNotContain("publish-ready-key", captured.Metadata.IdempotencyKey, StringComparison.Ordinal);
        Assert.True(captured.Metadata.IdempotencyKey!.Length <= 200);
        Assert.Equal("true", captured.Metadata.AdditionalMetadata["preserveImages"]);
        Assert.Equal("true", captured.Metadata.AdditionalMetadata["preservePricingCompatibilityMirrors"]);
        Assert.Equal("true", captured.Metadata.AdditionalMetadata["preloadedProduct"]);
        Assert.Equal("true", captured.Metadata.AdditionalMetadata["stableOmittedCollectionsFingerprint"]);
    }

    [Fact]
    public async Task Publish_AlreadyPublishedVisible_ReturnsUnchangedWithoutOrchestratorCall()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var product = await SeedProductAsync(
            harness, isActive: true, PublishStatus.Published, isOnlineVisible: true);
        var (service, orchestrator) = CreateService(harness, out _);

        var outcome = await service.ExecuteLifecycleAsync(
            BulkOperationType.Publish,
            Request(product),
            "publish-already-key",
            default);

        Assert.Equal(BulkLifecycleOutcomeKind.Success, outcome.Kind);
        Assert.Equal(1, outcome.OperationResult!.Unchanged);
        Assert.Equal(0, outcome.OperationResult.Processed);
        var item = Assert.Single(outcome.OperationResult.Results);
        Assert.Equal(BulkItemStatus.Unchanged, item.Status);
        Assert.Equal(BulkErrorCodes.AlreadyPublished, item.Code);
        Assert.Equal(product.RowVersion.ToString(), item.NewVersion);
        orchestrator.Verify(x => x.AuthorAsync(
            It.IsAny<ProductAuthoringCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Publish_InactiveProduct_FailsProductInactiveWithoutOrchestratorCall()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var product = await SeedProductAsync(
            harness, isActive: false, PublishStatus.Draft, isOnlineVisible: false);
        var (service, orchestrator) = CreateService(harness, out _);

        var outcome = await service.ExecuteLifecycleAsync(
            BulkOperationType.Publish,
            Request(product),
            "publish-inactive-key",
            default);

        Assert.Equal(BulkLifecycleOutcomeKind.Success, outcome.Kind);
        Assert.Equal(1, outcome.OperationResult!.Failed);
        var item = Assert.Single(outcome.OperationResult.Results);
        Assert.Equal(BulkItemStatus.Failed, item.Status);
        Assert.Equal(BulkErrorCodes.ProductInactive, item.Code);
        Assert.Null(item.NewVersion);
        orchestrator.Verify(x => x.AuthorAsync(
            It.IsAny<ProductAuthoringCommand>(), It.IsAny<CancellationToken>()), Times.Never);

        var stored = await harness.Context.Products.AsNoTracking().SingleAsync(p => p.Id == product.Id);
        Assert.False(stored.IsActive);
    }

    [Fact]
    public async Task Publish_PublishedHidden_ProcessesThroughUpdateToBecomeVisible()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var product = await SeedProductAsync(
            harness, isActive: true, PublishStatus.Published, isOnlineVisible: false);
        ProductAuthoringCommand? captured = null;
        var (service, orchestrator) = CreateService(harness, out _);
        orchestrator.Setup(item => item.AuthorAsync(It.IsAny<ProductAuthoringCommand>(), It.IsAny<CancellationToken>()))
            .Callback<ProductAuthoringCommand, CancellationToken>((command, _) => captured = command)
            .ReturnsAsync(ProductAuthoringResult.Accepted(product.Id, PublishStatus.Published, "bulk-publish-hidden", 32));

        var outcome = await service.ExecuteLifecycleAsync(
            BulkOperationType.Publish,
            Request(product),
            "publish-hidden-key",
            default);

        Assert.Equal(BulkLifecycleOutcomeKind.Success, outcome.Kind);
        Assert.Equal(BulkItemStatus.Processed, Assert.Single(outcome.OperationResult!.Results).Status);
        Assert.Equal(ProductAuthoringOperation.Update, captured!.Operation);
        Assert.Equal(PublishStatus.Published, captured.Publication.PublishStatus);
        Assert.True(captured.Publication.IsActive);
        Assert.True(captured.Publication.IsOnlineVisible);
    }

    [Fact]
    public async Task Publish_NotReadyAuthoringResult_PreservesCanonicalMessageWithPublicationNotReadyCode()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var product = await SeedProductAsync(
            harness, isActive: true, PublishStatus.Draft, isOnlineVisible: false);
        var (service, orchestrator) = CreateService(harness, out _);
        orchestrator.Setup(item => item.AuthorAsync(It.IsAny<ProductAuthoringCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductAuthoringResult(
                ProductAuthoringStatus.NotReadyForPublication,
                product.Id,
                null,
                PublishStatus.Draft,
                [new ProductAuthoringValidationError(
                    "PRODUCT_DEFAULT_UNIT_PRICE_REQUIRED",
                    "units.sellingPrice",
                    "The default unit must have a positive selling price before publication.")],
                "bulk-not-ready",
                new Dictionary<string, string>()));

        var outcome = await service.ExecuteLifecycleAsync(
            BulkOperationType.Publish,
            Request(product),
            "publish-not-ready-key",
            default);

        Assert.Equal(BulkLifecycleOutcomeKind.Success, outcome.Kind);
        var item = Assert.Single(outcome.OperationResult!.Results);
        Assert.Equal(BulkItemStatus.Failed, item.Status);
        Assert.Equal(BulkErrorCodes.PublicationNotReady, item.Code);
        Assert.Equal(
            "The default unit must have a positive selling price before publication.",
            item.Message);
    }

    [Fact]
    public async Task Publish_ValidationFailedAuthoringResult_MapsValidationFailed()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var product = await SeedProductAsync(
            harness, isActive: true, PublishStatus.Draft, isOnlineVisible: false);
        var (service, orchestrator) = CreateService(harness, out _);
        orchestrator.Setup(item => item.AuthorAsync(It.IsAny<ProductAuthoringCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductAuthoringResult(
                ProductAuthoringStatus.ValidationFailed,
                product.Id,
                null,
                PublishStatus.Draft,
                [new ProductAuthoringValidationError(
                    "PRODUCT_CATEGORY_NOT_LIVE",
                    "categoryId",
                    "Product category must reference a live category.")],
                "bulk-validation",
                new Dictionary<string, string>()));

        var outcome = await service.ExecuteLifecycleAsync(
            BulkOperationType.Publish,
            Request(product),
            "publish-validation-key",
            default);

        var item = Assert.Single(outcome.OperationResult!.Results);
        Assert.Equal(BulkItemStatus.Failed, item.Status);
        Assert.Equal(BulkErrorCodes.ValidationFailed, item.Code);
        Assert.Equal("Product category must reference a live category.", item.Message);
    }

    // ------------------------------------------------------------------
    // Unpublish semantics
    // ------------------------------------------------------------------

    [Fact]
    public async Task Unpublish_PublishedVisible_ProcessesThroughUpdatePreservingActiveState()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var product = await SeedProductAsync(
            harness, isActive: true, PublishStatus.Published, isOnlineVisible: true,
            isSearchable: true, isFeatured: true);
        ProductAuthoringCommand? captured = null;
        var (service, orchestrator) = CreateService(harness, out _);
        orchestrator.Setup(item => item.AuthorAsync(It.IsAny<ProductAuthoringCommand>(), It.IsAny<CancellationToken>()))
            .Callback<ProductAuthoringCommand, CancellationToken>((command, _) => captured = command)
            .ReturnsAsync(ProductAuthoringResult.Accepted(product.Id, PublishStatus.Draft, "bulk-unpublish", 33));

        var outcome = await service.ExecuteLifecycleAsync(
            BulkOperationType.Unpublish,
            Request(product),
            "unpublish-visible-key",
            default);

        Assert.Equal(BulkLifecycleOutcomeKind.Success, outcome.Kind);
        Assert.Equal(BulkItemStatus.Processed, Assert.Single(outcome.OperationResult!.Results).Status);
        Assert.Equal(ProductAuthoringOperation.Update, captured!.Operation);
        Assert.Equal(PublishStatus.Draft, captured.Publication.PublishStatus);
        Assert.False(captured.Publication.IsOnlineVisible);
        Assert.True(captured.Publication.IsActive);
        Assert.True(captured.Publication.IsSearchable);
        Assert.True(captured.Publication.IsFeatured);
        Assert.Equal(product.RowVersion, captured.Metadata.ExpectedVersion);
        Assert.StartsWith("bulk-item:", captured.Metadata.IdempotencyKey, StringComparison.Ordinal);
        Assert.Equal("true", captured.Metadata.AdditionalMetadata["preserveImages"]);
        Assert.Equal("true", captured.Metadata.AdditionalMetadata["preservePricingCompatibilityMirrors"]);
    }

    [Theory]
    [InlineData(PublishStatus.Draft)]
    [InlineData(PublishStatus.Published)]
    public async Task Unpublish_HiddenDraftOrPublishedProduct_ReturnsAlreadyUnpublishedWithoutOrchestratorCall(
        PublishStatus publishStatus)
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var product = await SeedProductAsync(
            harness, isActive: true, publishStatus, isOnlineVisible: false);
        var (service, orchestrator) = CreateService(harness, out _);

        var outcome = await service.ExecuteLifecycleAsync(
            BulkOperationType.Unpublish,
            Request(product),
            "unpublish-already-key",
            default);

        Assert.Equal(BulkLifecycleOutcomeKind.Success, outcome.Kind);
        Assert.Equal(1, outcome.OperationResult!.Unchanged);
        Assert.Equal(0, outcome.OperationResult.Processed);
        var item = Assert.Single(outcome.OperationResult.Results);
        Assert.Equal(BulkItemStatus.Unchanged, item.Status);
        Assert.Equal(BulkErrorCodes.AlreadyUnpublished, item.Code);
        Assert.Equal(product.RowVersion.ToString(), item.NewVersion);
        orchestrator.Verify(x => x.AuthorAsync(
            It.IsAny<ProductAuthoringCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Unpublish_InactiveArchivedProduct_ProcessesThroughUpdatePreservingInactiveState()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var product = await SeedProductAsync(
            harness, isActive: false, PublishStatus.Archived, isOnlineVisible: false);
        ProductAuthoringCommand? captured = null;
        var (service, orchestrator) = CreateService(harness, out _);
        orchestrator.Setup(item => item.AuthorAsync(It.IsAny<ProductAuthoringCommand>(), It.IsAny<CancellationToken>()))
            .Callback<ProductAuthoringCommand, CancellationToken>((command, _) => captured = command)
            .ReturnsAsync(ProductAuthoringResult.Accepted(product.Id, PublishStatus.Draft, "bulk-unpublish-archived", 34));

        var outcome = await service.ExecuteLifecycleAsync(
            BulkOperationType.Unpublish,
            Request(product),
            "unpublish-archived-key",
            default);

        Assert.Equal(BulkLifecycleOutcomeKind.Success, outcome.Kind);
        Assert.Equal(BulkItemStatus.Processed, Assert.Single(outcome.OperationResult!.Results).Status);
        Assert.Equal(ProductAuthoringOperation.Update, captured!.Operation);
        Assert.Equal(PublishStatus.Draft, captured.Publication.PublishStatus);
        Assert.False(captured.Publication.IsOnlineVisible);
        Assert.False(captured.Publication.IsActive);
        orchestrator.Verify(x => x.AuthorAsync(
            It.Is<ProductAuthoringCommand>(command =>
                command.Operation == ProductAuthoringOperation.Archive),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    // ------------------------------------------------------------------
    // Cross-cutting invariants shared by publish and unpublish
    // ------------------------------------------------------------------

    [Fact]
    public async Task Publish_StaleVersionItem_FailsWithConflictWhileReadyItemContinues()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: true);
        var request = new BulkProductRequest(
        [
            new BulkProductRequestItem(harness.ConflictProductId.ToString(), "7"),
            new BulkProductRequestItem(harness.PreparedProductId.ToString(), "7")
        ]);
        var (service, orchestrator) = CreateService(harness, out _);
        orchestrator.Setup(item => item.AuthorAsync(It.IsAny<ProductAuthoringCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProductAuthoringResult.Accepted(
                harness.PreparedProductId, PublishStatus.Published, "publish-mixed", 35));

        var outcome = await service.ExecuteLifecycleAsync(
            BulkOperationType.Publish,
            request,
            "publish-mixed-key",
            default);

        Assert.Equal(BulkLifecycleOutcomeKind.Success, outcome.Kind);
        Assert.Equal(1, outcome.OperationResult!.Processed);
        Assert.Equal(1, outcome.OperationResult.Failed);
        Assert.Equal(BulkErrorCodes.ConcurrencyConflict, outcome.OperationResult.Results[0].Code);
        Assert.Equal(BulkItemStatus.Processed, outcome.OperationResult.Results[1].Status);
        orchestrator.Verify(item => item.AuthorAsync(
            It.Is<ProductAuthoringCommand>(command => command.Identity.ProductId == harness.PreparedProductId),
            It.IsAny<CancellationToken>()), Times.Once);
        orchestrator.Verify(item => item.AuthorAsync(
            It.Is<ProductAuthoringCommand>(command => command.Identity.ProductId == harness.ConflictProductId),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Publish_ForeignTenantAndMissingProducts_ClassifyAsNotFound()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: true);
        var request = new BulkProductRequest(
        [
            new BulkProductRequestItem(harness.ForeignProductId.ToString(), "11"),
            new BulkProductRequestItem(Guid.NewGuid().ToString(), "3")
        ]);
        var (service, orchestrator) = CreateService(harness, out _);

        var outcome = await service.ExecuteLifecycleAsync(
            BulkOperationType.Publish,
            request,
            "publish-foreign-key",
            default);

        Assert.Equal(BulkLifecycleOutcomeKind.Success, outcome.Kind);
        Assert.Equal(2, outcome.OperationResult!.Failed);
        Assert.All(outcome.OperationResult.Results, item =>
        {
            Assert.Equal(BulkItemStatus.Failed, item.Status);
            Assert.Equal(BulkErrorCodes.NotFound, item.Code);
        });
        orchestrator.Verify(x => x.AuthorAsync(
            It.IsAny<ProductAuthoringCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Unpublish_MixedStaleProcessedAndNotFound_ClassifiesEachItemIndependently()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: true);
        var publishedVisible = await SeedProductAsync(
            harness, isActive: true, PublishStatus.Published, isOnlineVisible: true);
        var request = new BulkProductRequest(
        [
            new BulkProductRequestItem(harness.ConflictProductId.ToString(), "7"),
            new BulkProductRequestItem(publishedVisible.Id.ToString(), publishedVisible.RowVersion.ToString()),
            new BulkProductRequestItem(harness.ForeignProductId.ToString(), "11")
        ]);
        var (service, orchestrator) = CreateService(harness, out _);
        orchestrator.Setup(item => item.AuthorAsync(It.IsAny<ProductAuthoringCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProductAuthoringResult.Accepted(
                publishedVisible.Id, PublishStatus.Draft, "unpublish-mixed", 36));

        var outcome = await service.ExecuteLifecycleAsync(
            BulkOperationType.Unpublish,
            request,
            "unpublish-mixed-key",
            default);

        Assert.Equal(BulkLifecycleOutcomeKind.Success, outcome.Kind);
        Assert.Equal(1, outcome.OperationResult!.Processed);
        Assert.Equal(2, outcome.OperationResult.Failed);
        Assert.Equal(BulkErrorCodes.ConcurrencyConflict, outcome.OperationResult.Results[0].Code);
        Assert.Equal(BulkItemStatus.Processed, outcome.OperationResult.Results[1].Status);
        Assert.Equal(BulkErrorCodes.NotFound, outcome.OperationResult.Results[2].Code);
    }

    [Theory]
    [InlineData(BulkOperationType.Publish)]
    [InlineData(BulkOperationType.Unpublish)]
    public async Task PublicationOperation_DuplicateItemsCollapseToSingleProcessedItem(BulkOperationType operation)
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var product = await SeedProductAsync(
            harness, isActive: true, PublishStatus.Draft, isOnlineVisible: true);
        var request = new BulkProductRequest(
        [
            new BulkProductRequestItem(product.Id.ToString(), product.RowVersion.ToString()),
            new BulkProductRequestItem(product.Id.ToString(), product.RowVersion.ToString())
        ]);
        var (service, orchestrator) = CreateService(harness, out _);
        orchestrator.Setup(item => item.AuthorAsync(It.IsAny<ProductAuthoringCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProductAuthoringResult.Accepted(
                product.Id, PublishStatus.Draft, "duplicate-collapse", 37));

        var outcome = await service.ExecuteLifecycleAsync(
            operation,
            request,
            $"duplicate-{operation}-key",
            default);

        Assert.Equal(BulkLifecycleOutcomeKind.Success, outcome.Kind);
        Assert.Equal(2, outcome.OperationResult!.Requested);
        Assert.Equal(1, outcome.OperationResult.Accepted);
        Assert.Equal(1, outcome.OperationResult.Processed);
        orchestrator.Verify(x => x.AuthorAsync(
            It.IsAny<ProductAuthoringCommand>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(BulkOperationType.Publish)]
    [InlineData(BulkOperationType.Unpublish)]
    public async Task PublicationOperation_ConflictingDuplicateVersions_RejectWholeRequestWithoutMutation(
        BulkOperationType operation)
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: true);
        var request = new BulkProductRequest(
        [
            new BulkProductRequestItem(harness.PreparedProductId.ToString(), "7"),
            new BulkProductRequestItem(harness.PreparedProductId.ToString(), "8")
        ]);
        var (service, orchestrator) = CreateService(harness, out _);

        var outcome = await service.ExecuteLifecycleAsync(
            operation,
            request,
            $"conflicting-{operation}-key",
            default);

        Assert.Equal(BulkLifecycleOutcomeKind.RequestValidationFailed, outcome.Kind);
        Assert.Contains(
            outcome.ValidationErrors!,
            error => error.Code == BulkErrorCodes.DuplicateProductConflict);
        orchestrator.Verify(x => x.AuthorAsync(
            It.IsAny<ProductAuthoringCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(BulkOperationType.Publish)]
    [InlineData(BulkOperationType.Unpublish)]
    public async Task PublicationOperation_BatchOverFifty_RejectedWithBatchTooLarge(BulkOperationType operation)
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var items = Enumerable.Range(0, 51)
            .Select(index => new BulkProductRequestItem(Guid.NewGuid().ToString(), "1"))
            .ToList();
        var (service, orchestrator) = CreateService(harness, out _);

        var outcome = await service.ExecuteLifecycleAsync(
            operation,
            new BulkProductRequest(items),
            $"too-large-{operation}-key",
            default);

        Assert.Equal(BulkLifecycleOutcomeKind.RequestValidationFailed, outcome.Kind);
        Assert.Contains(
            outcome.ValidationErrors!,
            error => error.Code == BulkErrorCodes.BatchTooLarge);
        orchestrator.Verify(x => x.AuthorAsync(
            It.IsAny<ProductAuthoringCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(BulkOperationType.Publish)]
    [InlineData(BulkOperationType.Unpublish)]
    public async Task PublicationOperation_IdenticalRequestReplaysStoredResponseWithoutSecondMutation(
        BulkOperationType operation)
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var product = await SeedProductAsync(
            harness, isActive: true, PublishStatus.Draft, isOnlineVisible: true);
        var request = new BulkProductRequest([
            new BulkProductRequestItem(product.Id.ToString(), product.RowVersion.ToString())
        ]);
        var (service, orchestrator) = CreateService(harness, out _);
        orchestrator.Setup(item => item.AuthorAsync(It.IsAny<ProductAuthoringCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProductAuthoringResult.Accepted(
                product.Id, PublishStatus.Draft, "publication-replay", 38));

        var first = await service.ExecuteLifecycleAsync(
            operation, request, $"replay-{operation}-key", default);
        var replay = await service.ExecuteLifecycleAsync(
            operation, request, $"replay-{operation}-key", default);

        Assert.Equal(
            System.Text.Json.JsonSerializer.Serialize(first.OperationResult),
            System.Text.Json.JsonSerializer.Serialize(replay.OperationResult));
        Assert.Equal(1, await harness.Context.ProductRequestIdempotencyRecords.CountAsync());
        orchestrator.Verify(x => x.AuthorAsync(
            It.IsAny<ProductAuthoringCommand>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(BulkOperationType.Publish)]
    [InlineData(BulkOperationType.Unpublish)]
    public async Task PublicationOperation_SameKeyWithChangedVersions_ConflictsWithoutMutation(
        BulkOperationType operation)
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var product = await SeedProductAsync(
            harness, isActive: true, PublishStatus.Draft, isOnlineVisible: true);
        var (service, orchestrator) = CreateService(harness, out _);
        var firstRequest = new BulkProductRequest([
            new BulkProductRequestItem(product.Id.ToString(), product.RowVersion.ToString())
        ]);
        var changedRequest = new BulkProductRequest([
            new BulkProductRequestItem(product.Id.ToString(), "6")
        ]);

        await service.ExecuteLifecycleAsync(
            operation, firstRequest, $"conflict-{operation}-key", default);
        var conflict = await service.ExecuteLifecycleAsync(
            operation, changedRequest, $"conflict-{operation}-key", default);

        Assert.Equal(BulkLifecycleOutcomeKind.IdempotencyConflict, conflict.Kind);
        Assert.Equal(1, await harness.Context.ProductRequestIdempotencyRecords.CountAsync());
        orchestrator.Verify(x => x.AuthorAsync(
            It.IsAny<ProductAuthoringCommand>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Publish_SameKeyAsUnpublish_ConflictsWithSingleOuterClaim()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var product = await SeedProductAsync(
            harness, isActive: true, PublishStatus.Draft, isOnlineVisible: false);
        var (service, orchestrator) = CreateService(harness, out _);
        orchestrator.Setup(item => item.AuthorAsync(It.IsAny<ProductAuthoringCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProductAuthoringResult.Accepted(product.Id, PublishStatus.Published, "operation-scoped", 39));

        var publishOutcome = await service.ExecuteLifecycleAsync(
            BulkOperationType.Publish, Request(product), "shared-operation-key", default);
        var unpublishOutcome = await service.ExecuteLifecycleAsync(
            BulkOperationType.Unpublish, Request(product), "shared-operation-key", default);

        Assert.Equal(BulkLifecycleOutcomeKind.Success, publishOutcome.Kind);
        Assert.Equal(BulkLifecycleOutcomeKind.IdempotencyConflict, unpublishOutcome.Kind);
        Assert.Null(unpublishOutcome.OperationResult);
        Assert.Equal(1, await harness.Context.ProductRequestIdempotencyRecords.CountAsync());
        orchestrator.Verify(x => x.AuthorAsync(
            It.IsAny<ProductAuthoringCommand>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static (IBulkProductLifecycleService, Mock<IProductAuthoringOrchestrator>) CreateService(
        BulkFoundationTestHarness harness,
        out Mock<IProductAuthoringOrchestrator> orchestratorMock)
    {
        orchestratorMock = new Mock<IProductAuthoringOrchestrator>();
        orchestratorMock.Setup(x => x.AuthorAsync(It.IsAny<ProductAuthoringCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProductAuthoringResult.InfrastructurePrepared("publication-test-correlation"));

        var commandMapperMock = new Mock<IProductAuthoringCommandMapper>();
        commandMapperMock.Setup(x => x.FromLifecycleProduct(It.IsAny<ProductAuthoringOperation>(), It.IsAny<Product>()))
            .Returns((ProductAuthoringOperation operation, Product product) =>
                BuildTestCommand(operation, product));

        var idempotencyService = new BulkIdempotencyService(
            new ProductMutationIdempotencyService(harness.Context),
            harness.TenantContext);

        var productLoader = new BulkProductLoader(harness.Context, harness.TenantContext);
        var preparationService = new BulkPreparationService(
            new BulkProductRequestValidator(),
            productLoader,
            new StaticCorrelationIdAccessor("publication-lifecycle-test"));

        var service = new BulkProductLifecycleService(
            preparationService,
            idempotencyService,
            new BulkOperationResultAggregator(),
            productLoader,
            orchestratorMock.Object,
            commandMapperMock.Object,
            Mock.Of<PointOfSale.API.Services.IValidationService>(),
            harness.Context,
            harness.TenantContext,
            NullLogger<BulkProductLifecycleService>.Instance);

        return (service, orchestratorMock);
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
            new ProductAuthoringPublication(product.PublishStatus, product.IsActive, product.IsOnlineVisible, product.IsSearchable, product.IsFeatured),
            new ProductAuthoringSeo(null, null, null, null),
            new ProductAuthoringShippingProfile(0, 0, 0, 0, "kg", "cm"),
            new ProductAuthoringInventoryProfile("Piece", 0, false, product.HasVariants, 0, false),
            new ProductAuthoringUnits(null, null, Array.Empty<ProductAuthoringUnit>()),
            Array.Empty<ProductAuthoringVariant>(),
            new ProductAuthoringMetadata(
                false, null, null, null, "publication-test-correlation", null, null,
                new Dictionary<string, string>()));
    }

    private static async Task<Product> SeedProductAsync(
        BulkFoundationTestHarness harness,
        bool isActive,
        PublishStatus publishStatus,
        bool isOnlineVisible,
        bool isSearchable = true,
        bool isFeatured = false)
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            TenantId = harness.TenantContext.TenantId!.Value,
            CategoryId = Guid.NewGuid(),
            Name = "Publication Product",
            Unit = "Piece",
            WeightUnit = "kg",
            DimensionUnit = "cm",
            IsActive = isActive,
            PublishStatus = publishStatus,
            IsOnlineVisible = isOnlineVisible,
            IsSearchable = isSearchable,
            IsFeatured = isFeatured,
            IsDeleted = false,
            RowVersion = 30
        };
        harness.Context.Products.Add(product);
        await harness.Context.SaveChangesAsync();
        return product;
    }

    private static BulkProductRequest Request(Product product) =>
        new([
            new BulkProductRequestItem(
                product.Id.ToString(),
                product.RowVersion.ToString(System.Globalization.CultureInfo.InvariantCulture))
        ]);
}
