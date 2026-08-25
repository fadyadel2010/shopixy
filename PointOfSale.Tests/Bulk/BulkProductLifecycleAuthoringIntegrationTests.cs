using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Infrastructure.Idempotency;
using PointOfSale.API.Services;
using PointOfSale.API.Services.ProductAuthoring;
using PointOfSale.API.Services.ProductAuthoring.Bulk;
using PointOfSale.Core.Application.ProductAuthoring;
using PointOfSale.Core.DTOs.Bulk;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Bulk;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Eventing;

namespace PointOfSale.Tests.Bulk;

public sealed class BulkProductLifecycleAuthoringIntegrationTests
{
    [Fact]
    public async Task Deactivate_RealAuthoringPath_ReconstructsPostgresJsonbLowercaseFaqs()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var seeded = await SeedAsync(
            harness,
            isActive: true,
            PublishStatus.Draft,
            isOnlineVisible: false,
            variantsActive: true,
            faqJson: "[{\"answer\":\" Persisted answer \",\"question\":\" Persisted question \"}]");
        var service = CreateService(harness);

        var outcome = await service.ExecuteLifecycleAsync(
            BulkOperationType.Deactivate,
            Request(seeded.ProductId, seeded.RowVersion),
            "lowercase-jsonb-faq-regression");

        Assert.Equal(BulkLifecycleOutcomeKind.Success, outcome.Kind);
        Assert.Equal(BulkItemStatus.Processed, Assert.Single(outcome.OperationResult!.Results).Status);
        var product = await harness.Context.Products.AsNoTracking().SingleAsync(p => p.Id == seeded.ProductId);
        Assert.False(product.IsActive);
        Assert.Equal(PublishStatus.Archived, product.PublishStatus);
        using var faqs = JsonDocument.Parse(product.FaqJson!);
        var faq = Assert.Single(faqs.RootElement.EnumerateArray());
        Assert.Equal(" Persisted question ", faq.GetProperty("question").GetString());
        Assert.Equal(" Persisted answer ", faq.GetProperty("answer").GetString());
        var audit = Assert.Single(await harness.Context.AuditLogs.AsNoTracking().ToListAsync());
        Assert.DoesNotContain("lowercase-jsonb-faq-regression", audit.AfterDataJson, StringComparison.Ordinal);
        Assert.DoesNotContain(harness.TenantContext.TenantId!.Value.ToString(), audit.AfterDataJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Activate_RealAuthoringPath_RestoresDraftWithoutReactivatingMultiVariants()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var seeded = await SeedAsync(
            harness,
            isActive: false,
            PublishStatus.Archived,
            isOnlineVisible: false,
            variantsActive: false);
        var service = CreateService(harness);

        var outcome = await service.ExecuteLifecycleAsync(
            BulkOperationType.Activate,
            Request(seeded.ProductId, seeded.RowVersion),
            "real-activate-key");

        Assert.Equal(BulkLifecycleOutcomeKind.Success, outcome.Kind);
        var item = Assert.Single(outcome.OperationResult!.Results);
        Assert.Equal(BulkItemStatus.Processed, item.Status);

        var product = await harness.Context.Products.AsNoTracking().SingleAsync(p => p.Id == seeded.ProductId);
        Assert.True(product.IsActive);
        Assert.Equal(PublishStatus.Draft, product.PublishStatus);
        Assert.False(product.IsOnlineVisible);
        Assert.Equal("primary.jpg,gallery.jpg", product.Images);
        Assert.Equal(product.RowVersion.ToString(), item.NewVersion);
        Assert.All(
            await harness.Context.ProductVariants.AsNoTracking().Where(v => v.ProductId == product.Id).ToListAsync(),
            variant => Assert.False(variant.IsActive));

        Assert.Equal("Update", Assert.Single(await harness.Context.AuditLogs.AsNoTracking().ToListAsync()).ActionType);
        Assert.Single(await harness.Context.MarketplaceEventOutboxMessages.AsNoTracking().ToListAsync());
        var events = await harness.Context.OutboxMessages.AsNoTracking().Select(message => message.EventType).ToListAsync();
        Assert.Contains(typeof(ProductUpdatedEvent).FullName, events);
        Assert.Contains(typeof(ProductPublishStatusChangedEvent).FullName, events);
        Assert.DoesNotContain(typeof(ProductArchivedEvent).FullName, events);
    }

    [Fact]
    public async Task Deactivate_RealAuthoringPath_PreservesVariantLifecycleAndReplayDoesNotDuplicateSideEffects()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var seeded = await SeedAsync(
            harness,
            isActive: true,
            PublishStatus.Published,
            isOnlineVisible: true,
            variantsActive: true);
        var initialVariants = await harness.Context.ProductVariants
            .AsNoTracking()
            .Where(variant => variant.ProductId == seeded.ProductId)
            .ToDictionaryAsync(variant => variant.Id, variant => (variant.RowVersion, variant.UpdatedAt));
        var service = CreateService(harness);
        var request = Request(seeded.ProductId, seeded.RowVersion);

        var first = await service.ExecuteLifecycleAsync(
            BulkOperationType.Deactivate,
            request,
            "real-deactivate-key");
        var outboxCount = await harness.Context.OutboxMessages.CountAsync();
        var marketplaceCount = await harness.Context.MarketplaceEventOutboxMessages.CountAsync();
        var auditCount = await harness.Context.AuditLogs.CountAsync();

        var replay = await service.ExecuteLifecycleAsync(
            BulkOperationType.Deactivate,
            request,
            "real-deactivate-key");

        Assert.Equal(
            JsonSerializer.Serialize(first.OperationResult),
            JsonSerializer.Serialize(replay.OperationResult));
        var product = await harness.Context.Products.AsNoTracking().SingleAsync(p => p.Id == seeded.ProductId);
        Assert.False(product.IsActive);
        Assert.Equal(PublishStatus.Archived, product.PublishStatus);
        Assert.False(product.IsOnlineVisible);
        Assert.All(
            await harness.Context.ProductVariants.AsNoTracking().Where(v => v.ProductId == product.Id).ToListAsync(),
            variant =>
            {
                Assert.True(variant.IsActive);
                Assert.Equal(initialVariants[variant.Id].RowVersion, variant.RowVersion);
                Assert.Equal(initialVariants[variant.Id].UpdatedAt, variant.UpdatedAt);
            });

        Assert.Equal("Archive", Assert.Single(await harness.Context.AuditLogs.AsNoTracking().ToListAsync()).ActionType);
        Assert.Equal(outboxCount, await harness.Context.OutboxMessages.CountAsync());
        Assert.Equal(marketplaceCount, await harness.Context.MarketplaceEventOutboxMessages.CountAsync());
        Assert.Equal(auditCount, await harness.Context.AuditLogs.CountAsync());
        var events = await harness.Context.OutboxMessages.AsNoTracking().Select(message => message.EventType).ToListAsync();
        Assert.Contains(typeof(ProductUpdatedEvent).FullName, events);
        Assert.Contains(typeof(ProductPublishStatusChangedEvent).FullName, events);
        Assert.Contains(typeof(ProductArchivedEvent).FullName, events);
    }

    [Fact]
    public async Task Activate_PostCommitObserverFailure_ReplaysCommittedSimpleProductWithoutDuplicateSideEffects()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var seeded = await SeedSimpleAsync(harness);
        var service = CreateService(harness, new ThrowOnceAfterCommitObserver());

        var outcome = await service.ExecuteLifecycleAsync(
            BulkOperationType.Activate,
            Request(seeded.ProductId, seeded.RowVersion),
            "real-ambiguous-activate-key");

        Assert.Equal(BulkLifecycleOutcomeKind.Success, outcome.Kind);
        Assert.Equal(BulkItemStatus.Processed, Assert.Single(outcome.OperationResult!.Results).Status);
        var product = await harness.Context.Products.AsNoTracking().SingleAsync(p => p.Id == seeded.ProductId);
        Assert.True(product.IsActive);
        Assert.Equal(PublishStatus.Draft, product.PublishStatus);
        Assert.Equal("simple.jpg", product.Images);
        Assert.Single(await harness.Context.ProductVariants.AsNoTracking().Where(v => v.ProductId == product.Id).ToListAsync());
        Assert.Single(await harness.Context.AuditLogs.AsNoTracking().ToListAsync());
        Assert.Equal(2, await harness.Context.ProductRequestIdempotencyRecords.CountAsync());
        var events = await harness.Context.OutboxMessages.AsNoTracking().Select(message => message.EventType).ToListAsync();
        Assert.Equal(1, events.Count(item => item == typeof(ProductUpdatedEvent).FullName));
        Assert.Equal(1, events.Count(item => item == typeof(ProductPublishStatusChangedEvent).FullName));
    }

    [Fact]
    public async Task Delete_RealAuthoringPath_SoftDeletesVariantsAndExactReplayDoesNotDuplicateSideEffects()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var seeded = await SeedAsync(
            harness,
            isActive: true,
            PublishStatus.Published,
            isOnlineVisible: true,
            variantsActive: true);
        var service = CreateService(harness);
        var request = Request(seeded.ProductId, seeded.RowVersion);

        var first = await service.ExecuteLifecycleAsync(
            BulkOperationType.Delete,
            request,
            "real-delete-key");
        var outboxCount = await harness.Context.OutboxMessages.CountAsync();
        var marketplaceCount = await harness.Context.MarketplaceEventOutboxMessages.CountAsync();
        var auditCount = await harness.Context.AuditLogs.CountAsync();

        var replay = await service.ExecuteLifecycleAsync(
            BulkOperationType.Delete,
            request,
            "real-delete-key");

        Assert.Equal(
            JsonSerializer.Serialize(first.OperationResult),
            JsonSerializer.Serialize(replay.OperationResult));
        Assert.Equal(BulkItemStatus.Processed, Assert.Single(first.OperationResult!.Results).Status);

        var product = await harness.Context.Products
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(item => item.Id == seeded.ProductId);
        Assert.True(product.IsDeleted);
        Assert.False(product.IsActive);
        Assert.False(product.IsOnlineVisible);
        Assert.Equal(PublishStatus.Archived, product.PublishStatus);

        var variants = await harness.Context.ProductVariants
            .AsNoTracking()
            .Where(variant => variant.ProductId == seeded.ProductId)
            .ToListAsync();
        Assert.Equal(2, variants.Count);
        Assert.All(variants, variant =>
        {
            Assert.True(variant.IsDeleted);
            Assert.False(variant.IsActive);
        });

        Assert.Equal("Delete", Assert.Single(await harness.Context.AuditLogs.AsNoTracking().ToListAsync()).ActionType);
        Assert.Equal(outboxCount, await harness.Context.OutboxMessages.CountAsync());
        Assert.Equal(marketplaceCount, await harness.Context.MarketplaceEventOutboxMessages.CountAsync());
        Assert.Equal(auditCount, await harness.Context.AuditLogs.CountAsync());
        var events = await harness.Context.OutboxMessages.AsNoTracking().Select(message => message.EventType).ToListAsync();
        Assert.Equal(1, events.Count(item => item == typeof(ProductDeletedEvent).FullName));
        Assert.Equal(2, events.Count(item => item == typeof(ProductVariantDeletedEvent).FullName));
        Assert.Equal(2, events.Count(item => item == typeof(VariantDeletedEvent).FullName));
    }

    [Fact]
    public async Task Delete_PostCommitObserverFailure_ReplaysCommittedRemoveWithoutSecondDelete()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var seeded = await SeedAsync(
            harness,
            isActive: true,
            PublishStatus.Published,
            isOnlineVisible: true,
            variantsActive: true);
        var service = CreateService(harness, new ThrowOnceAfterCommitObserver());

        var outcome = await service.ExecuteLifecycleAsync(
            BulkOperationType.Delete,
            Request(seeded.ProductId, seeded.RowVersion),
            "ambiguous-delete-key");

        Assert.Equal(BulkLifecycleOutcomeKind.Success, outcome.Kind);
        Assert.Equal(BulkItemStatus.Processed, Assert.Single(outcome.OperationResult!.Results).Status);
        Assert.True((await harness.Context.Products
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(product => product.Id == seeded.ProductId)).IsDeleted);
        Assert.Single(await harness.Context.AuditLogs.AsNoTracking().ToListAsync());
        Assert.Equal(1, await harness.Context.OutboxMessages.CountAsync(message =>
            message.EventType == typeof(ProductDeletedEvent).FullName));
    }

    [Fact]
    public async Task Delete_PostCommitCancellation_ReplaysCommittedRemoveWithoutSecondDelete()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var seeded = await SeedAsync(
            harness,
            isActive: true,
            PublishStatus.Published,
            isOnlineVisible: true,
            variantsActive: true);
        using var cancellation = new CancellationTokenSource();
        var service = CreateService(harness, new CancelOnceAfterCommitObserver(cancellation));
        var request = Request(seeded.ProductId, seeded.RowVersion);
        const string key = "post-commit-cancel-delete-key";

        // Cancellation after the committed remove propagates like every other
        // operation instead of silently retrying the authoring call.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ExecuteLifecycleAsync(
                BulkOperationType.Delete,
                request,
                key,
                cancellation.Token));

        var replay = await service.ExecuteLifecycleAsync(
            BulkOperationType.Delete,
            request,
            key,
            default);
        var secondReplay = await service.ExecuteLifecycleAsync(
            BulkOperationType.Delete,
            request,
            key,
            default);

        Assert.Equal(BulkLifecycleOutcomeKind.Success, replay.Kind);
        Assert.Equal(BulkItemStatus.Processed, Assert.Single(replay.OperationResult!.Results).Status);
        Assert.Equal(
            System.Text.Json.JsonSerializer.Serialize(replay.OperationResult),
            System.Text.Json.JsonSerializer.Serialize(secondReplay.OperationResult));
        Assert.True((await harness.Context.Products
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(product => product.Id == seeded.ProductId)).IsDeleted);
        Assert.Single(await harness.Context.AuditLogs.AsNoTracking().ToListAsync());
        Assert.Equal(1, await harness.Context.OutboxMessages.CountAsync(message =>
            message.EventType == typeof(ProductDeletedEvent).FullName));
    }

    [Theory]
    [InlineData(BulkOperationType.Activate)]
    [InlineData(BulkOperationType.Deactivate)]
    [InlineData(BulkOperationType.Publish)]
    [InlineData(BulkOperationType.Unpublish)]
    [InlineData(BulkOperationType.Delete)]
    public async Task PostCommitCancellation_RealAuthoringPath_RethrowsAndStoresTruthfulReplayableAggregate(
        BulkOperationType operation)
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var initial = InitialState(operation);
        var seeded = await SeedAsync(
            harness, initial.IsActive, initial.Status, initial.Visible, variantsActive: true);
        var key = $"post-commit-cancel-{operation}";
        using var cancellation = new CancellationTokenSource();
        var service = CreateService(harness, new CancelOnceAfterCommitObserver(cancellation));
        var request = Request(seeded.ProductId, seeded.RowVersion);

        // The original post-commit cancellation still reaches the caller.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ExecuteLifecycleAsync(operation, request, key, cancellation.Token));

        // The committed mutation is stored under a truthful HTTP 200 aggregate,
        // never a terminal 500, with exactly one mutation record set.
        harness.Context.ChangeTracker.Clear();
        var outerRow = await OuterRowAsync(harness);
        Assert.Equal(200, outerRow.ResponseStatusCode);
        Assert.NotNull(outerRow.ResponseJson);
        Assert.Equal(2, await harness.Context.ProductRequestIdempotencyRecords.CountAsync());
        Assert.Single(await harness.Context.AuditLogs.AsNoTracking().ToListAsync());
        await AssertMutatedStateAsync(harness, seeded.ProductId, operation);

        var outboxCount = await harness.Context.OutboxMessages.AsNoTracking().CountAsync();
        var marketplaceCount = await harness.Context.MarketplaceEventOutboxMessages.AsNoTracking().CountAsync();
        Assert.True(marketplaceCount > 0);

        var firstReplay = await service.ExecuteLifecycleAsync(operation, request, key, default);
        var secondReplay = await service.ExecuteLifecycleAsync(operation, request, key, default);

        Assert.Equal(BulkLifecycleOutcomeKind.Success, firstReplay.Kind);
        Assert.Equal(BulkLifecycleOutcomeKind.Success, secondReplay.Kind);
        Assert.NotNull(firstReplay.OperationResult);
        Assert.Equal(1, firstReplay.OperationResult.Requested);
        Assert.Equal(1, firstReplay.OperationResult.Accepted);
        Assert.Equal(1, firstReplay.OperationResult.Processed);
        Assert.Equal(0, firstReplay.OperationResult.Unchanged);
        Assert.Equal(0, firstReplay.OperationResult.Failed);

        var item = Assert.Single(firstReplay.OperationResult.Results);
        Assert.Equal(BulkItemStatus.Processed, item.Status);
        Assert.Null(item.Code);
        Assert.Equal(
            seeded.RowVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            item.PreviousVersion);
        Assert.Equal(
            System.Text.Json.JsonSerializer.Serialize(firstReplay.OperationResult),
            System.Text.Json.JsonSerializer.Serialize(secondReplay.OperationResult));

        // Neither replay duplicated authoring side effects.
        Assert.Equal(2, await harness.Context.ProductRequestIdempotencyRecords.CountAsync());
        Assert.Equal(outboxCount, await harness.Context.OutboxMessages.AsNoTracking().CountAsync());
        Assert.Equal(marketplaceCount, await harness.Context.MarketplaceEventOutboxMessages.AsNoTracking().CountAsync());
        Assert.Single(await harness.Context.AuditLogs.AsNoTracking().ToListAsync());
    }

    [Theory]
    [InlineData(BulkOperationType.Activate)]
    [InlineData(BulkOperationType.Deactivate)]
    [InlineData(BulkOperationType.Publish)]
    [InlineData(BulkOperationType.Unpublish)]
    [InlineData(BulkOperationType.Delete)]
    public async Task BetweenItemCancellation_CommitsProcessedItemsAndMarksRestNotProcessedWithExactReplays(
        BulkOperationType operation)
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var initial = InitialState(operation);
        var first = await SeedAsync(
            harness, initial.IsActive, initial.Status, initial.Visible, variantsActive: true);
        var second = await SeedAsync(
            harness, initial.IsActive, initial.Status, initial.Visible, variantsActive: true);
        var third = await SeedAsync(
            harness, initial.IsActive, initial.Status, initial.Visible, variantsActive: true);
        var key = $"cancel-mid-{operation}";
        using var cancellation = new CancellationTokenSource();
        var service = CreateService(harness, new CancelAtSecondCommitObserver(cancellation));
        var request = new BulkProductRequest(
        [
            new BulkProductRequestItem(first.ProductId.ToString(), first.RowVersion.ToString()),
            new BulkProductRequestItem(second.ProductId.ToString(), second.RowVersion.ToString()),
            new BulkProductRequestItem(third.ProductId.ToString(), third.RowVersion.ToString())
        ]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ExecuteLifecycleAsync(operation, request, key, cancellation.Token));

        // A and B committed durably together with their per-product records;
        // C was never started, so only the outer claim plus two items exist.
        harness.Context.ChangeTracker.Clear();
        var outerRow = await OuterRowAsync(harness);
        Assert.Equal(200, outerRow.ResponseStatusCode);
        Assert.NotNull(outerRow.ResponseJson);
        Assert.Equal(3, await harness.Context.ProductRequestIdempotencyRecords.CountAsync());
        Assert.Equal(2, await harness.Context.AuditLogs.AsNoTracking().CountAsync());

        var outboxCount = await harness.Context.OutboxMessages.AsNoTracking().CountAsync();
        var marketplaceCount = await harness.Context.MarketplaceEventOutboxMessages.AsNoTracking().CountAsync();

        var replay = await service.ExecuteLifecycleAsync(operation, request, key, default);
        var secondReplay = await service.ExecuteLifecycleAsync(operation, request, key, default);

        Assert.Equal(BulkLifecycleOutcomeKind.Success, replay.Kind);
        Assert.NotNull(replay.OperationResult);
        Assert.Equal(3, replay.OperationResult.Requested);
        Assert.Equal(3, replay.OperationResult.Accepted);
        Assert.Equal(2, replay.OperationResult.Processed);
        Assert.Equal(0, replay.OperationResult.Unchanged);
        Assert.Equal(1, replay.OperationResult.Failed);

        Assert.Equal(BulkItemStatus.Processed, replay.OperationResult.Results[0].Status);
        Assert.Equal(first.ProductId, replay.OperationResult.Results[0].ProductId);
        Assert.Equal(BulkItemStatus.Processed, replay.OperationResult.Results[1].Status);
        Assert.Equal(second.ProductId, replay.OperationResult.Results[1].ProductId);

        var notProcessed = replay.OperationResult.Results[2];
        Assert.Equal(third.ProductId, notProcessed.ProductId);
        Assert.Equal(BulkItemStatus.Failed, notProcessed.Status);
        Assert.Equal(BulkErrorCodes.RequestCancelled, notProcessed.Code);
        Assert.Equal(
            third.RowVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            notProcessed.PreviousVersion);
        Assert.Null(notProcessed.NewVersion);

        Assert.Equal(BulkLifecycleOutcomeKind.Success, secondReplay.Kind);
        Assert.Equal(
            System.Text.Json.JsonSerializer.Serialize(replay.OperationResult),
            System.Text.Json.JsonSerializer.Serialize(secondReplay.OperationResult));

        // No duplicate authoring or side effects across either replay.
        Assert.Equal(3, await harness.Context.ProductRequestIdempotencyRecords.CountAsync());
        Assert.Equal(outboxCount, await harness.Context.OutboxMessages.AsNoTracking().CountAsync());
        Assert.Equal(marketplaceCount, await harness.Context.MarketplaceEventOutboxMessages.AsNoTracking().CountAsync());
        Assert.Equal(2, await harness.Context.AuditLogs.AsNoTracking().CountAsync());

        await AssertMutatedStateAsync(harness, first.ProductId, operation);
        await AssertMutatedStateAsync(harness, second.ProductId, operation);
        var untouched = await harness.Context.Products.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(product => product.Id == third.ProductId);
        Assert.Equal(initial.IsActive, untouched.IsActive);
        Assert.Equal(initial.Status, untouched.PublishStatus);
        Assert.Equal(initial.Visible, untouched.IsOnlineVisible);
        Assert.False(untouched.IsDeleted);
    }

    [Theory]
    [InlineData(BulkOperationType.Activate)]
    [InlineData(BulkOperationType.Deactivate)]
    [InlineData(BulkOperationType.Publish)]
    [InlineData(BulkOperationType.Unpublish)]
    [InlineData(BulkOperationType.Delete)]
    public async Task CancellationAfterLastCommit_PersistsTruthfulReplayableAggregateWithoutFailure(
        BulkOperationType operation)
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var initial = InitialState(operation);
        var seeded = await SeedAsync(
            harness, initial.IsActive, initial.Status, initial.Visible, variantsActive: true);
        var key = $"cancel-at-completion-{operation}";
        using var cancellation = new CancellationTokenSource();
        var service = CreateService(harness, new CancelAfterCommitWithoutThrowingObserver(cancellation));
        var request = Request(seeded.ProductId, seeded.RowVersion);

        // Cancellation fires after the only commit; batch completion persists
        // the truthful aggregate anyway and reports success, never a failure.
        var outcome = await service.ExecuteLifecycleAsync(operation, request, key, cancellation.Token);

        Assert.Equal(BulkLifecycleOutcomeKind.Success, outcome.Kind);
        Assert.NotNull(outcome.OperationResult);
        Assert.Equal(1, outcome.OperationResult.Requested);
        Assert.Equal(1, outcome.OperationResult.Accepted);
        Assert.Equal(1, outcome.OperationResult.Processed);
        Assert.Equal(0, outcome.OperationResult.Unchanged);
        Assert.Equal(0, outcome.OperationResult.Failed);
        Assert.Equal(BulkItemStatus.Processed, Assert.Single(outcome.OperationResult.Results).Status);

        harness.Context.ChangeTracker.Clear();
        var outerRow = await OuterRowAsync(harness);
        Assert.Equal(200, outerRow.ResponseStatusCode);
        Assert.NotNull(outerRow.ResponseJson);
        Assert.Equal(2, await harness.Context.ProductRequestIdempotencyRecords.CountAsync());
        Assert.Single(await harness.Context.AuditLogs.AsNoTracking().ToListAsync());
        await AssertMutatedStateAsync(harness, seeded.ProductId, operation);

        var outboxCount = await harness.Context.OutboxMessages.AsNoTracking().CountAsync();
        var marketplaceCount = await harness.Context.MarketplaceEventOutboxMessages.AsNoTracking().CountAsync();

        var replay = await service.ExecuteLifecycleAsync(operation, request, key, default);

        Assert.Equal(BulkLifecycleOutcomeKind.Success, replay.Kind);
        Assert.Equal(
            System.Text.Json.JsonSerializer.Serialize(outcome.OperationResult),
            System.Text.Json.JsonSerializer.Serialize(replay.OperationResult));
        Assert.Equal(2, await harness.Context.ProductRequestIdempotencyRecords.CountAsync());
        Assert.Equal(outboxCount, await harness.Context.OutboxMessages.AsNoTracking().CountAsync());
        Assert.Equal(marketplaceCount, await harness.Context.MarketplaceEventOutboxMessages.AsNoTracking().CountAsync());
    }

    [Theory]
    [InlineData(BulkOperationType.Activate)]
    [InlineData(BulkOperationType.Deactivate)]
    [InlineData(BulkOperationType.Publish)]
    [InlineData(BulkOperationType.Unpublish)]
    [InlineData(BulkOperationType.Delete)]
    public async Task CancellationBeforeFirstCommit_RollsBackAndReplaysExactNotProcessedAggregate(
        BulkOperationType operation)
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var initial = InitialState(operation);
        var seeded = await SeedAsync(
            harness, initial.IsActive, initial.Status, initial.Visible, variantsActive: true);
        var key = $"cancel-before-first-commit-{operation}";
        using var cancellation = new CancellationTokenSource();
        var service = CreateService(harness, new CancelBeforeCommitObserver(cancellation));
        var request = Request(seeded.ProductId, seeded.RowVersion);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ExecuteLifecycleAsync(operation, request, key, cancellation.Token));

        harness.Context.ChangeTracker.Clear();
        var outerRow = await OuterRowAsync(harness);
        Assert.Equal(200, outerRow.ResponseStatusCode);
        Assert.NotNull(outerRow.ResponseJson);
        Assert.Equal(1, await harness.Context.ProductRequestIdempotencyRecords.CountAsync());
        Assert.Empty(await harness.Context.AuditLogs.AsNoTracking().ToListAsync());
        Assert.Empty(await harness.Context.OutboxMessages.AsNoTracking().ToListAsync());
        Assert.Empty(await harness.Context.MarketplaceEventOutboxMessages.AsNoTracking().ToListAsync());
        await AssertInitialStateAsync(harness, seeded.ProductId, initial, variantsActive: true);

        var firstReplay = await service.ExecuteLifecycleAsync(operation, request, key, default);
        var secondReplay = await service.ExecuteLifecycleAsync(operation, request, key, default);

        Assert.Equal(BulkLifecycleOutcomeKind.Success, firstReplay.Kind);
        Assert.NotNull(firstReplay.OperationResult);
        Assert.Equal(1, firstReplay.OperationResult.Requested);
        Assert.Equal(1, firstReplay.OperationResult.Accepted);
        Assert.Equal(0, firstReplay.OperationResult.Processed);
        Assert.Equal(0, firstReplay.OperationResult.Unchanged);
        Assert.Equal(1, firstReplay.OperationResult.Failed);
        var item = Assert.Single(firstReplay.OperationResult.Results);
        Assert.Equal(BulkItemStatus.Failed, item.Status);
        Assert.Equal(BulkErrorCodes.RequestCancelled, item.Code);
        Assert.Equal(
            seeded.RowVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            item.PreviousVersion);
        Assert.Null(item.NewVersion);
        Assert.Equal(
            JsonSerializer.Serialize(firstReplay.OperationResult),
            JsonSerializer.Serialize(secondReplay.OperationResult));

        Assert.Equal(1, await harness.Context.ProductRequestIdempotencyRecords.CountAsync());
        Assert.Empty(await harness.Context.AuditLogs.AsNoTracking().ToListAsync());
        Assert.Empty(await harness.Context.OutboxMessages.AsNoTracking().ToListAsync());
        Assert.Empty(await harness.Context.MarketplaceEventOutboxMessages.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task UnverifiableOuterCompletion_AfterCommittedMutation_LeavesClaimRecoverableWithout500()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var seeded = await SeedAsync(
            harness, isActive: true, PublishStatus.Draft, isOnlineVisible: false, variantsActive: true);
        var realBulkIdempotency = new BulkIdempotencyService(
            new ProductMutationIdempotencyService(harness.Context),
            harness.TenantContext);
        var idempotency = CreateDelegatingBulkIdempotencyMock(realBulkIdempotency);
        idempotency.Setup(service => service.TryCompleteAndVerifyAsync(
                It.IsAny<ProductRequestIdempotency>(),
                It.IsAny<BulkOperationResult>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((BulkOperationResult?)null);
        var service = CreateService(
            harness,
            bulkIdempotencyOverride: idempotency.Object);
        var request = Request(seeded.ProductId, seeded.RowVersion);
        const string key = "unverified-completion-after-commit";

        var failure = await Assert.ThrowsAsync<BulkAggregateCompletionUnverifiedException>(() =>
            service.ExecuteLifecycleAsync(BulkOperationType.Publish, request, key, default));

        Assert.Contains("durably", failure.Message, StringComparison.OrdinalIgnoreCase);
        harness.Context.ChangeTracker.Clear();
        var outerRow = await OuterRowAsync(harness);
        Assert.Null(outerRow.ResponseStatusCode);
        Assert.Null(outerRow.ResponseJson);
        Assert.Null(outerRow.CompletedAtUtc);
        Assert.Equal(2, await harness.Context.ProductRequestIdempotencyRecords.CountAsync());
        await AssertMutatedStateAsync(harness, seeded.ProductId, BulkOperationType.Publish);

        var outboxCount = await harness.Context.OutboxMessages.AsNoTracking().CountAsync();
        var marketplaceCount = await harness.Context.MarketplaceEventOutboxMessages.AsNoTracking().CountAsync();
        var auditCount = await harness.Context.AuditLogs.AsNoTracking().CountAsync();
        Assert.True(outboxCount > 0);
        Assert.True(marketplaceCount > 0);
        Assert.Equal(1, auditCount);

        var concurrentRetry = await service.ExecuteLifecycleAsync(
            BulkOperationType.Publish, request, key, default);
        Assert.Equal(BulkLifecycleOutcomeKind.IdempotencyInProgress, concurrentRetry.Kind);
        Assert.Equal(outboxCount, await harness.Context.OutboxMessages.AsNoTracking().CountAsync());
        Assert.Equal(marketplaceCount, await harness.Context.MarketplaceEventOutboxMessages.AsNoTracking().CountAsync());
        Assert.Equal(auditCount, await harness.Context.AuditLogs.AsNoTracking().CountAsync());
        idempotency.Verify(service => service.FinalizeFailureAsync(
            It.IsAny<ProductRequestIdempotency>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CancellationReconciliationReadFailure_MarksInterruptedAndStaleTakeoverReconcilesWithoutMutation()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var initial = InitialState(BulkOperationType.Activate);
        var seeded = await SeedAsync(
            harness, initial.IsActive, initial.Status, initial.Visible, variantsActive: true);
        var realBulkIdempotency = new BulkIdempotencyService(
            new ProductMutationIdempotencyService(harness.Context),
            harness.TenantContext);
        var idempotency = CreateDelegatingBulkIdempotencyMock(realBulkIdempotency);
        idempotency.Setup(service => service.TryResolveCommittedBulkItemMutationAsync(
                It.IsAny<BulkOperationType>(),
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Injected durable-result read failure."));
        using var cancellation = new CancellationTokenSource();
        var service = CreateService(
            harness,
            new CancelBeforeCommitObserver(cancellation),
            idempotency.Object);
        var request = Request(seeded.ProductId, seeded.RowVersion);
        const string key = "cancellation-reconciliation-read-failure";

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ExecuteLifecycleAsync(BulkOperationType.Activate, request, key, cancellation.Token));

        harness.Context.ChangeTracker.Clear();
        var outerRow = await OuterRowAsync(harness);
        Assert.Null(outerRow.ResponseStatusCode);
        Assert.True(realBulkIdempotency.IsInterruptedClaim(outerRow));
        Assert.Null(outerRow.CompletedAtUtc);
        Assert.Equal(1, await harness.Context.ProductRequestIdempotencyRecords.CountAsync());
        await AssertInitialStateAsync(harness, seeded.ProductId, initial, variantsActive: true);
        Assert.Empty(await harness.Context.AuditLogs.AsNoTracking().ToListAsync());
        Assert.Empty(await harness.Context.OutboxMessages.AsNoTracking().ToListAsync());
        Assert.Empty(await harness.Context.MarketplaceEventOutboxMessages.AsNoTracking().ToListAsync());
        idempotency.Verify(service => service.TryCompleteAndVerifyAsync(
            It.IsAny<ProductRequestIdempotency>(),
            It.IsAny<BulkOperationResult>(),
            It.IsAny<CancellationToken>()), Times.Never);
        idempotency.Verify(service => service.FinalizeFailureAsync(
            It.IsAny<ProductRequestIdempotency>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);

        // Once the lease is stale, takeover must use the durable interrupted
        // marker and reconcile the uncommitted item as cancelled. It must not
        // resume authoring merely because the first reconciliation read failed.
        harness.Context.ChangeTracker.Clear();
        var staleUpdatedAt = DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(2));
        await harness.Context.ProductRequestIdempotencyRecords
            .Where(record => record.Id == outerRow.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(
                record => record.UpdatedAt,
                staleUpdatedAt));
        harness.Context.ChangeTracker.Clear();
        idempotency.Setup(service => service.TryResolveCommittedBulkItemMutationAsync(
                It.IsAny<BulkOperationType>(),
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .Returns((BulkOperationType resolvedOperation,
                string resolvedKey,
                Guid tenantId,
                Guid productId,
                CancellationToken token) =>
                realBulkIdempotency.TryResolveCommittedBulkItemMutationAsync(
                    resolvedOperation, resolvedKey, tenantId, productId, token));

        var recoveryService = CreateService(
            harness,
            bulkIdempotencyOverride: idempotency.Object);
        var recovery = await recoveryService.ExecuteLifecycleAsync(
            BulkOperationType.Activate,
            request,
            key,
            default);

        Assert.Equal(BulkLifecycleOutcomeKind.Success, recovery.Kind);
        Assert.NotNull(recovery.OperationResult);
        Assert.Equal(1, recovery.OperationResult.Requested);
        Assert.Equal(1, recovery.OperationResult.Accepted);
        Assert.Equal(0, recovery.OperationResult.Processed);
        Assert.Equal(0, recovery.OperationResult.Unchanged);
        Assert.Equal(1, recovery.OperationResult.Failed);
        var cancelledItem = Assert.Single(recovery.OperationResult.Results);
        Assert.Equal(BulkItemStatus.Failed, cancelledItem.Status);
        Assert.Equal(BulkErrorCodes.RequestCancelled, cancelledItem.Code);
        await AssertInitialStateAsync(harness, seeded.ProductId, initial, variantsActive: true);
        Assert.Empty(await harness.Context.AuditLogs.AsNoTracking().ToListAsync());
        Assert.Empty(await harness.Context.OutboxMessages.AsNoTracking().ToListAsync());
        Assert.Empty(await harness.Context.MarketplaceEventOutboxMessages.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task RecoveredUnmarkedDeleteClaim_WithCommittedEvidence_ReconcilesRemainingItemWithoutSecondDelete()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var initial = InitialState(BulkOperationType.Delete);
        var first = await SeedAsync(
            harness, initial.IsActive, initial.Status, initial.Visible, variantsActive: true);
        var second = await SeedAsync(
            harness, initial.IsActive, initial.Status, initial.Visible, variantsActive: true);
        var realBulkIdempotency = new BulkIdempotencyService(
            new ProductMutationIdempotencyService(harness.Context),
            harness.TenantContext);
        var idempotency = CreateDelegatingBulkIdempotencyMock(realBulkIdempotency);
        idempotency.Setup(service => service.TryMarkInterruptedAsync(
                It.IsAny<ProductRequestIdempotency>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        using var cancellation = new CancellationTokenSource();
        var service = CreateService(
            harness,
            new CancelOnceAfterCommitObserver(cancellation),
            idempotency.Object);
        var request = new BulkProductRequest(
        [
            new BulkProductRequestItem(first.ProductId.ToString(), first.RowVersion.ToString()),
            new BulkProductRequestItem(second.ProductId.ToString(), second.RowVersion.ToString())
        ]);
        const string key = "recovered-unmarked-delete-cancellation";

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ExecuteLifecycleAsync(BulkOperationType.Delete, request, key, cancellation.Token));

        harness.Context.ChangeTracker.Clear();
        var outerRow = await OuterRowAsync(harness);
        Assert.Null(outerRow.ResponseStatusCode);
        Assert.Null(outerRow.ResponseJson);
        Assert.Equal(2, await harness.Context.ProductRequestIdempotencyRecords.CountAsync());
        await AssertMutatedStateAsync(harness, first.ProductId, BulkOperationType.Delete);
        await AssertInitialStateAsync(harness, second.ProductId, initial, variantsActive: true);
        var outboxCount = await harness.Context.OutboxMessages.AsNoTracking().CountAsync();
        var marketplaceCount = await harness.Context.MarketplaceEventOutboxMessages.AsNoTracking().CountAsync();
        var auditCount = await harness.Context.AuditLogs.AsNoTracking().CountAsync();
        Assert.True(outboxCount > 0);
        Assert.True(marketplaceCount > 0);
        Assert.Equal(1, auditCount);

        var staleUpdatedAt = DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(2));
        await harness.Context.ProductRequestIdempotencyRecords
            .Where(record => record.Id == outerRow.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(
                record => record.UpdatedAt,
                staleUpdatedAt));
        harness.Context.ChangeTracker.Clear();

        var recoveryService = CreateService(
            harness,
            bulkIdempotencyOverride: idempotency.Object);
        var recovery = await recoveryService.ExecuteLifecycleAsync(
            BulkOperationType.Delete,
            request,
            key,
            default);

        Assert.Equal(BulkLifecycleOutcomeKind.Success, recovery.Kind);
        Assert.NotNull(recovery.OperationResult);
        Assert.Equal(2, recovery.OperationResult.Requested);
        Assert.Equal(2, recovery.OperationResult.Accepted);
        Assert.Equal(1, recovery.OperationResult.Processed);
        Assert.Equal(0, recovery.OperationResult.Unchanged);
        Assert.Equal(1, recovery.OperationResult.Failed);
        Assert.Equal(BulkItemStatus.Processed, recovery.OperationResult.Results[0].Status);
        Assert.Equal(first.ProductId, recovery.OperationResult.Results[0].ProductId);
        Assert.Equal(BulkItemStatus.Failed, recovery.OperationResult.Results[1].Status);
        Assert.Equal(BulkErrorCodes.RequestCancelled, recovery.OperationResult.Results[1].Code);
        Assert.Equal(second.ProductId, recovery.OperationResult.Results[1].ProductId);
        await AssertMutatedStateAsync(harness, first.ProductId, BulkOperationType.Delete);
        await AssertInitialStateAsync(harness, second.ProductId, initial, variantsActive: true);
        var completedOuter = await OuterRowAsync(harness);
        Assert.Equal(StatusCodes.Status200OK, completedOuter.ResponseStatusCode);
        Assert.NotNull(completedOuter.ResponseJson);

        var replay = await recoveryService.ExecuteLifecycleAsync(
            BulkOperationType.Delete,
            request,
            key,
            default);
        Assert.Equal(BulkLifecycleOutcomeKind.Success, replay.Kind);
        Assert.NotNull(replay.OperationResult);
        Assert.Equal(
            JsonSerializer.Serialize(recovery.OperationResult),
            JsonSerializer.Serialize(replay.OperationResult));
        Assert.Equal(2, await harness.Context.ProductRequestIdempotencyRecords.CountAsync());
        Assert.Equal(outboxCount, await harness.Context.OutboxMessages.AsNoTracking().CountAsync());
        Assert.Equal(marketplaceCount, await harness.Context.MarketplaceEventOutboxMessages.AsNoTracking().CountAsync());
        Assert.Equal(auditCount, await harness.Context.AuditLogs.AsNoTracking().CountAsync());
    }

    private static (bool IsActive, PublishStatus Status, bool Visible) InitialState(
        BulkOperationType operation) =>
        operation switch
        {
            BulkOperationType.Activate => (false, PublishStatus.Archived, false),
            BulkOperationType.Deactivate => (true, PublishStatus.Published, true),
            BulkOperationType.Publish => (true, PublishStatus.Draft, false),
            BulkOperationType.Unpublish => (true, PublishStatus.Published, true),
            BulkOperationType.Delete => (true, PublishStatus.Published, true),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
        };

    private static async Task AssertMutatedStateAsync(
        BulkFoundationTestHarness harness,
        Guid productId,
        BulkOperationType operation)
    {
        var product = await harness.Context.Products
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(item => item.Id == productId);
        switch (operation)
        {
            case BulkOperationType.Activate:
                Assert.True(product.IsActive);
                Assert.Equal(PublishStatus.Draft, product.PublishStatus);
                Assert.False(product.IsOnlineVisible);
                break;
            case BulkOperationType.Deactivate:
                Assert.False(product.IsActive);
                Assert.Equal(PublishStatus.Archived, product.PublishStatus);
                Assert.False(product.IsOnlineVisible);
                break;
            case BulkOperationType.Publish:
                Assert.True(product.IsActive);
                Assert.Equal(PublishStatus.Published, product.PublishStatus);
                Assert.True(product.IsOnlineVisible);
                break;
            case BulkOperationType.Unpublish:
                Assert.True(product.IsActive);
                Assert.Equal(PublishStatus.Draft, product.PublishStatus);
                Assert.False(product.IsOnlineVisible);
                break;
            case BulkOperationType.Delete:
                Assert.True(product.IsDeleted);
                Assert.False(product.IsActive);
                Assert.False(product.IsOnlineVisible);
                Assert.All(
                    await harness.Context.ProductVariants
                        .IgnoreQueryFilters()
                        .AsNoTracking()
                        .Where(variant => variant.ProductId == productId)
                        .ToListAsync(),
                    variant => Assert.True(variant.IsDeleted));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation), operation, null);
        }
    }

    private static async Task AssertInitialStateAsync(
        BulkFoundationTestHarness harness,
        Guid productId,
        (bool IsActive, PublishStatus Status, bool Visible) initial,
        bool variantsActive)
    {
        var product = await harness.Context.Products
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(item => item.Id == productId);
        Assert.Equal(initial.IsActive, product.IsActive);
        Assert.Equal(initial.Status, product.PublishStatus);
        Assert.Equal(initial.Visible, product.IsOnlineVisible);
        Assert.False(product.IsDeleted);

        var variants = await harness.Context.ProductVariants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(variant => variant.ProductId == productId)
            .ToListAsync();
        Assert.Equal(2, variants.Count);
        Assert.All(variants, variant =>
        {
            Assert.Equal(variantsActive, variant.IsActive);
            Assert.False(variant.IsDeleted);
        });
    }

    private static async Task<ProductRequestIdempotency> OuterRowAsync(
        BulkFoundationTestHarness harness)
    {
        harness.Context.ChangeTracker.Clear();
        return await harness.Context.ProductRequestIdempotencyRecords
            .AsNoTracking()
            .SingleAsync(record =>
                record.Operation == BulkIdempotencyService.OuterClaimStorageOperation);
    }

    private static Mock<IBulkIdempotencyService> CreateDelegatingBulkIdempotencyMock(
        BulkIdempotencyService inner)
    {
        var mock = new Mock<IBulkIdempotencyService>();
        mock.Setup(service => service.ValidateKey(It.IsAny<string?>()))
            .Returns((string? key) => inner.ValidateKey(key));
        mock.Setup(service => service.BuildFingerprint(
                It.IsAny<BulkOperationType>(),
                It.IsAny<IReadOnlyList<NormalizedBulkProductItem>>(),
                It.IsAny<IReadOnlyDictionary<string, string?>?>()))
            .Returns((BulkOperationType operation,
                IReadOnlyList<NormalizedBulkProductItem> items,
                IReadOnlyDictionary<string, string?>? options) =>
                inner.BuildFingerprint(operation, items, options));
        mock.Setup(service => service.BuildPerProductIdempotencyKey(
                It.IsAny<BulkOperationType>(), It.IsAny<string>(), It.IsAny<Guid>()))
            .Returns((BulkOperationType operation, string key, Guid productId) =>
                inner.BuildPerProductIdempotencyKey(operation, key, productId));
        mock.Setup(service => service.TryClaimAsync(
                It.IsAny<BulkOperationType>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns((BulkOperationType operation, string key, string fingerprint, CancellationToken token) =>
                inner.TryClaimAsync(operation, key, fingerprint, token));
        mock.Setup(service => service.RenewClaimLeaseAsync(
                It.IsAny<ProductRequestIdempotency>(), It.IsAny<CancellationToken>()))
            .Returns((ProductRequestIdempotency record, CancellationToken token) =>
                inner.RenewClaimLeaseAsync(record, token));
        mock.Setup(service => service.IsInterruptedClaim(It.IsAny<ProductRequestIdempotency?>()))
            .Returns((ProductRequestIdempotency? record) => inner.IsInterruptedClaim(record));
        mock.Setup(service => service.TryMarkInterruptedAsync(
                It.IsAny<ProductRequestIdempotency>(), It.IsAny<CancellationToken>()))
            .Returns((ProductRequestIdempotency record, CancellationToken token) =>
                inner.TryMarkInterruptedAsync(record, token));
        mock.Setup(service => service.ReadStoredResponse(It.IsAny<ProductRequestIdempotency?>()))
            .Returns((ProductRequestIdempotency? record) => inner.ReadStoredResponse(record));
        mock.Setup(service => service.StoredResponseMatches(
                It.IsAny<ProductRequestIdempotency?>(), It.IsAny<BulkOperationResult>()))
            .Returns((ProductRequestIdempotency? record, BulkOperationResult aggregate) =>
                inner.StoredResponseMatches(record, aggregate));
        mock.Setup(service => service.TryCompleteAndVerifyAsync(
                It.IsAny<ProductRequestIdempotency>(),
                It.IsAny<BulkOperationResult>(),
                It.IsAny<CancellationToken>()))
            .Returns((ProductRequestIdempotency record, BulkOperationResult aggregate, CancellationToken token) =>
                inner.TryCompleteAndVerifyAsync(record, aggregate, token));
        mock.Setup(service => service.FinalizeFailureAsync(
                It.IsAny<ProductRequestIdempotency>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns((ProductRequestIdempotency record, string message, CancellationToken token) =>
                inner.FinalizeFailureAsync(record, message, token));
        mock.Setup(service => service.TryResolveCommittedBulkItemMutationAsync(
                It.IsAny<BulkOperationType>(),
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .Returns((BulkOperationType operation, string key, Guid tenantId, Guid productId, CancellationToken token) =>
                inner.TryResolveCommittedBulkItemMutationAsync(operation, key, tenantId, productId, token));
        return mock;
    }

    private static BulkProductLifecycleService CreateService(
        BulkFoundationTestHarness harness,
        IProductAuthoringTransactionObserver? transactionObserver = null,
        IBulkIdempotencyService? bulkIdempotencyOverride = null)
    {
        var context = harness.Context;
        var httpContextAccessor = new HttpContextAccessor();
        var eventPublisher = new PersistedEventPublisher(context);
        var variantService = new ProductVariantService(
            context,
            eventPublisher,
            NullLogger<ProductVariantService>.Instance);
        var orchestrator = new ProductAuthoringOrchestrator(
            new ProductAuthoringPolicy(new ProductAuthoringValidator()),
            context,
            harness.TenantContext,
            variantService,
            eventPublisher,
            new MarketplaceEventOutboxService(
                context,
                NullLogger<MarketplaceEventOutboxService>.Instance),
            NullLogger<ProductAuthoringOrchestrator>.Instance,
            httpContextAccessor,
            new ProductMutationIdempotencyService(context),
            transactionObserver ?? NoOpProductAuthoringTransactionObserver.Instance,
            new ProductAuthoringReconstructionService(context, harness.TenantContext));

        var validation = new Mock<IValidationService>();
        validation.Setup(service => service.CanDeleteProductAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidationResult.Success());

        var productLoader = new BulkProductLoader(context, harness.TenantContext);
        return new BulkProductLifecycleService(
            new BulkPreparationService(
                new BulkProductRequestValidator(),
                productLoader,
                new StaticCorrelationIdAccessor("bulk-real-authoring")),
            bulkIdempotencyOverride ?? new BulkIdempotencyService(
                new ProductMutationIdempotencyService(context),
                harness.TenantContext),
            new BulkOperationResultAggregator(),
            productLoader,
            orchestrator,
            new ProductAuthoringCommandMapper(harness.TenantContext, httpContextAccessor),
            validation.Object,
            context,
            harness.TenantContext,
            NullLogger<BulkProductLifecycleService>.Instance);
    }

    private static async Task<SeededProduct> SeedAsync(
        BulkFoundationTestHarness harness,
        bool isActive,
        PublishStatus publishStatus,
        bool isOnlineVisible,
        bool variantsActive,
        string? faqJson = null)
    {
        var tenantId = harness.TenantContext.TenantId!.Value;
        var category = new Category
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Bulk lifecycle"
        };
        var product = new Product
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CategoryId = category.Id,
            Name = "Bulk authored product",
            Unit = "Piece",
            WeightUnit = "kg",
            DimensionUnit = "cm",
            HasVariants = true,
            IsActive = isActive,
            PublishStatus = publishStatus,
            IsOnlineVisible = isOnlineVisible,
            IsSearchable = true,
            Images = "primary.jpg,gallery.jpg",
            FaqJson = faqJson,
            RowVersion = 30
        };
        harness.Context.Categories.Add(category);
        harness.Context.Products.Add(product);
        harness.Context.ProductVariants.AddRange(
            Variant(product, tenantId, variantsActive, isDefault: true, "BULK-1"),
            Variant(product, tenantId, variantsActive, isDefault: false, "BULK-2"));
        await harness.Context.SaveChangesAsync();
        var rowVersion = product.RowVersion;
        harness.Context.ChangeTracker.Clear();
        return new SeededProduct(product.Id, rowVersion);
    }

    private static async Task<SeededProduct> SeedSimpleAsync(BulkFoundationTestHarness harness)
    {
        var tenantId = harness.TenantContext.TenantId!.Value;
        var category = new Category
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Bulk simple lifecycle"
        };
        var product = new Product
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CategoryId = category.Id,
            Name = "Bulk simple product",
            SKU = "BULK-SIMPLE-1",
            Unit = "Piece",
            WeightUnit = "kg",
            DimensionUnit = "cm",
            HasVariants = false,
            IsActive = false,
            PublishStatus = PublishStatus.Archived,
            IsOnlineVisible = false,
            IsSearchable = true,
            Images = "simple.jpg",
            RowVersion = 40
        };
        harness.Context.Categories.Add(category);
        harness.Context.Products.Add(product);
        await harness.Context.SaveChangesAsync();
        var rowVersion = product.RowVersion;
        harness.Context.ChangeTracker.Clear();
        return new SeededProduct(product.Id, rowVersion);
    }

    private static ProductVariant Variant(
        Product product,
        Guid tenantId,
        bool isActive,
        bool isDefault,
        string sku) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProductId = product.Id,
            Product = product,
            SKU = sku,
            Price = 12m,
            CostPrice = 5m,
            IsActive = isActive,
            IsDefault = isDefault
        };

    private static BulkProductRequest Request(Guid productId, uint rowVersion) =>
        new([
            new BulkProductRequestItem(productId.ToString(), rowVersion.ToString())
        ]);

    private sealed record SeededProduct(Guid ProductId, uint RowVersion);

    private sealed class ThrowOnceAfterCommitObserver : IProductAuthoringTransactionObserver
    {
        private bool _hasThrown;

        public Task AfterUnitSynchronizationAsync(
            ProductAuthoringCommand command,
            Product product,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task AfterVariantSynchronizationAsync(
            ProductAuthoringCommand command,
            Product product,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task BeforeCommitAsync(
            ProductAuthoringCommand command,
            Product product,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task AfterCommitAsync(
            ProductAuthoringCommand command,
            Product product,
            CancellationToken cancellationToken)
        {
            if (!_hasThrown)
            {
                _hasThrown = true;
                throw new InvalidOperationException("Injected post-commit observer failure.");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class CancelOnceAfterCommitObserver(CancellationTokenSource cancellation)
        : IProductAuthoringTransactionObserver
    {
        private bool _hasCancelled;

        public Task AfterUnitSynchronizationAsync(
            ProductAuthoringCommand command,
            Product product,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task AfterVariantSynchronizationAsync(
            ProductAuthoringCommand command,
            Product product,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task BeforeCommitAsync(
            ProductAuthoringCommand command,
            Product product,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task AfterCommitAsync(
            ProductAuthoringCommand command,
            Product product,
            CancellationToken cancellationToken)
        {
            if (!_hasCancelled)
            {
                _hasCancelled = true;
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class CancelAtSecondCommitObserver(CancellationTokenSource cancellation)
        : IProductAuthoringTransactionObserver
    {
        private int _commits;

        public Task AfterUnitSynchronizationAsync(
            ProductAuthoringCommand command,
            Product product,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task AfterVariantSynchronizationAsync(
            ProductAuthoringCommand command,
            Product product,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task BeforeCommitAsync(
            ProductAuthoringCommand command,
            Product product,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task AfterCommitAsync(
            ProductAuthoringCommand command,
            Product product,
            CancellationToken cancellationToken)
        {
            // Cancel after the second committed mutation so item C never
            // starts while A and B stay durably committed with results.
            if (Interlocked.Increment(ref _commits) == 2)
            {
                cancellation.Cancel();
            }

            return Task.CompletedTask;
        }
    }

    private sealed class CancelAfterCommitWithoutThrowingObserver(CancellationTokenSource cancellation)
        : IProductAuthoringTransactionObserver
    {
        public Task AfterUnitSynchronizationAsync(
            ProductAuthoringCommand command,
            Product product,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task AfterVariantSynchronizationAsync(
            ProductAuthoringCommand command,
            Product product,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task BeforeCommitAsync(
            ProductAuthoringCommand command,
            Product product,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task AfterCommitAsync(
            ProductAuthoringCommand command,
            Product product,
            CancellationToken cancellationToken)
        {
            cancellation.Cancel();
            return Task.CompletedTask;
        }
    }

    private sealed class CancelBeforeCommitObserver(CancellationTokenSource cancellation)
        : IProductAuthoringTransactionObserver
    {
        public Task AfterUnitSynchronizationAsync(
            ProductAuthoringCommand command,
            Product product,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task AfterVariantSynchronizationAsync(
            ProductAuthoringCommand command,
            Product product,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task BeforeCommitAsync(
            ProductAuthoringCommand command,
            Product product,
            CancellationToken cancellationToken)
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        }

        public Task AfterCommitAsync(
            ProductAuthoringCommand command,
            Product product,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class PersistedEventPublisher(ApplicationDbContext context) : IEventPublisher
    {
        public Task PublishAsync<T>(T @event, CancellationToken cancellationToken = default)
            where T : IntegrationEvent
        {
            Persist(@event);
            return Task.CompletedTask;
        }

        public Task PublishBatchAsync<T>(IEnumerable<T> events, CancellationToken cancellationToken = default)
            where T : IntegrationEvent
        {
            foreach (var @event in events)
            {
                Persist(@event);
            }

            return Task.CompletedTask;
        }

        public Task PublishSensitiveAsync<T>(T @event, CancellationToken cancellationToken = default)
            where T : IntegrationEvent, ISensitiveIntegrationEvent =>
            PublishAsync(@event, cancellationToken);

        private void Persist(IntegrationEvent @event)
        {
            var type = @event.GetType();
            context.OutboxMessages.Add(new OutboxMessage
            {
                Id = Guid.NewGuid(),
                EventType = type.FullName ?? type.Name,
                EventPayload = JsonSerializer.Serialize(@event, type),
                TenantId = @event.TenantId,
                AggregateId = ReadGuid(@event, "ProductId") ?? ReadGuid(@event, "VariantId"),
                EventVersion = @event.EventVersion,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        private static Guid? ReadGuid(object value, string propertyName) =>
            value.GetType().GetProperty(propertyName)?.GetValue(value) is Guid guid ? guid : null;
    }
}
