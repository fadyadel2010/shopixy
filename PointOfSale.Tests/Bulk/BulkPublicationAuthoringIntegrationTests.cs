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
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Eventing;

namespace PointOfSale.Tests.Bulk;

public sealed class BulkPublicationAuthoringIntegrationTests
{
    [Fact]
    public async Task Publish_ReadySimpleProduct_BecomesPublishedVisibleWithSingleSideEffectsAndExactReplay()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var seeded = await SeedSimpleAsync(
            harness, isActive: true, PublishStatus.Draft, isOnlineVisible: false, withUnits: true);
        var service = CreateService(harness);

        var outcome = await service.ExecuteLifecycleAsync(
            BulkOperationType.Publish,
            Request(seeded.ProductId, seeded.RowVersion),
            "real-publish-key");

        Assert.Equal(BulkLifecycleOutcomeKind.Success, outcome.Kind);
        var item = Assert.Single(outcome.OperationResult!.Results);
        Assert.Equal(BulkItemStatus.Processed, item.Status);

        var product = await harness.Context.Products.AsNoTracking().SingleAsync(p => p.Id == seeded.ProductId);
        Assert.True(product.IsActive);
        Assert.Equal(PublishStatus.Published, product.PublishStatus);
        Assert.True(product.IsOnlineVisible);
        Assert.Equal("simple.jpg", product.Images);
        Assert.Equal(4m, product.PurchasePrice);
        Assert.Equal(9.5m, product.SalePrice);
        Assert.Null(product.OnlinePrice);

        Assert.Equal("Update", Assert.Single(await harness.Context.AuditLogs.AsNoTracking().ToListAsync()).ActionType);
        Assert.Single(await harness.Context.MarketplaceEventOutboxMessages.AsNoTracking().ToListAsync());
        var events = await harness.Context.OutboxMessages.AsNoTracking().Select(message => message.EventType).ToListAsync();
        Assert.Contains(typeof(ProductUpdatedEvent).FullName, events);
        Assert.Contains(typeof(ProductPublishStatusChangedEvent).FullName, events);
        Assert.DoesNotContain(typeof(ProductArchivedEvent).FullName, events);
        var productUpdatedPayload = await harness.Context.OutboxMessages
            .AsNoTracking()
            .Where(message => message.EventType == typeof(ProductUpdatedEvent).FullName)
            .Select(message => message.EventPayload)
            .SingleAsync();
        var productUpdated = JsonSerializer.Deserialize<ProductUpdatedEvent>(productUpdatedPayload)!;
        Assert.NotNull(productUpdated.DefaultUnit);
        Assert.Single(productUpdated.Units);
        Assert.Equal(9.5m, productUpdated.DefaultUnit.SalePrice);

        var outboxCount = await harness.Context.OutboxMessages.CountAsync();
        var marketplaceCount = await harness.Context.MarketplaceEventOutboxMessages.CountAsync();
        var auditCount = await harness.Context.AuditLogs.CountAsync();

        var replay = await service.ExecuteLifecycleAsync(
            BulkOperationType.Publish,
            Request(seeded.ProductId, seeded.RowVersion),
            "real-publish-key");

        Assert.Equal(
            JsonSerializer.Serialize(outcome.OperationResult),
            JsonSerializer.Serialize(replay.OperationResult));
        Assert.Equal(outboxCount, await harness.Context.OutboxMessages.CountAsync());
        Assert.Equal(marketplaceCount, await harness.Context.MarketplaceEventOutboxMessages.CountAsync());
        Assert.Equal(auditCount, await harness.Context.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task Publish_PublishedHiddenProduct_ProcessesThroughAuthoritativeUpdateToVisible()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var seeded = await SeedSimpleAsync(
            harness, isActive: true, PublishStatus.Published, isOnlineVisible: false, withUnits: true);
        var service = CreateService(harness);

        var outcome = await service.ExecuteLifecycleAsync(
            BulkOperationType.Publish,
            Request(seeded.ProductId, seeded.RowVersion),
            "real-publish-hidden-key");

        Assert.Equal(BulkLifecycleOutcomeKind.Success, outcome.Kind);
        Assert.Equal(BulkItemStatus.Processed, Assert.Single(outcome.OperationResult!.Results).Status);

        var product = await harness.Context.Products.AsNoTracking().SingleAsync(p => p.Id == seeded.ProductId);
        Assert.True(product.IsActive);
        Assert.Equal(PublishStatus.Published, product.PublishStatus);
        Assert.True(product.IsOnlineVisible);
    }

    [Fact]
    public async Task Publish_SimpleProductWithoutUnitsOrPrice_FailsPublicationNotReadyWithoutSideEffects()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var seeded = await SeedSimpleAsync(
            harness, isActive: true, PublishStatus.Draft, isOnlineVisible: false, withUnits: true, sellingPrice: 0m);
        var service = CreateService(harness);

        var outcome = await service.ExecuteLifecycleAsync(
            BulkOperationType.Publish,
            Request(seeded.ProductId, seeded.RowVersion),
            "real-publish-not-ready-key");

        Assert.Equal(BulkLifecycleOutcomeKind.Success, outcome.Kind);
        var item = Assert.Single(outcome.OperationResult!.Results);
        Assert.Equal(BulkItemStatus.Failed, item.Status);
        Assert.Equal(BulkErrorCodes.PublicationNotReady, item.Code);
        Assert.Contains("positive selling price", item.Message, StringComparison.Ordinal);

        var product = await harness.Context.Products.AsNoTracking().SingleAsync(p => p.Id == seeded.ProductId);
        Assert.True(product.IsActive);
        Assert.Equal(PublishStatus.Draft, product.PublishStatus);
        Assert.False(product.IsOnlineVisible);

        Assert.Empty(await harness.Context.AuditLogs.AsNoTracking().ToListAsync());
        Assert.Empty(await harness.Context.OutboxMessages.AsNoTracking().ToListAsync());
        Assert.Empty(await harness.Context.MarketplaceEventOutboxMessages.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Publish_VariantProductWithoutActivePricedVariant_FailsPublicationNotReadyWithoutSideEffects()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var seeded = await SeedVariantAsync(harness, pricedVariantActive: false);
        var service = CreateService(harness);

        var outcome = await service.ExecuteLifecycleAsync(
            BulkOperationType.Publish,
            Request(seeded.ProductId, seeded.RowVersion),
            "real-publish-variant-key");

        Assert.Equal(BulkLifecycleOutcomeKind.Success, outcome.Kind);
        var item = Assert.Single(outcome.OperationResult!.Results);
        Assert.Equal(BulkItemStatus.Failed, item.Status);
        Assert.Equal(BulkErrorCodes.PublicationNotReady, item.Code);
        Assert.Contains("active priced variant", item.Message, StringComparison.Ordinal);

        var product = await harness.Context.Products.AsNoTracking().SingleAsync(p => p.Id == seeded.ProductId);
        Assert.True(product.IsActive);
        Assert.Equal(PublishStatus.Draft, product.PublishStatus);
        Assert.False(product.IsOnlineVisible);
        var variants = await harness.Context.ProductVariants.AsNoTracking()
            .Where(v => v.ProductId == product.Id)
            .ToDictionaryAsync(v => v.SKU);
        Assert.False(variants["BULKPUB-V1"].IsActive);
        Assert.True(variants["BULKPUB-V2"].IsActive);
        Assert.All(variants.Values, variant => Assert.False(variant.IsDeleted));

        Assert.Empty(await harness.Context.AuditLogs.AsNoTracking().ToListAsync());
        Assert.Empty(await harness.Context.OutboxMessages.AsNoTracking().ToListAsync());
        Assert.Empty(await harness.Context.MarketplaceEventOutboxMessages.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Publish_ReadyVariantProduct_PreservesPricingAndVariantState()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var seeded = await SeedVariantAsync(harness, pricedVariantActive: true);
        var variantsBefore = await harness.Context.ProductVariants
            .AsNoTracking()
            .Where(variant => variant.ProductId == seeded.ProductId)
            .ToDictionaryAsync(
                variant => variant.Id,
                variant => new
                {
                    variant.Price,
                    variant.OnlinePrice,
                    variant.CostPrice,
                    variant.IsActive,
                    variant.IsDefault,
                    variant.IsDeleted
                });
        var service = CreateService(harness);

        var outcome = await service.ExecuteLifecycleAsync(
            BulkOperationType.Publish,
            Request(seeded.ProductId, seeded.RowVersion),
            "real-publish-ready-variant-key");

        Assert.Equal(BulkItemStatus.Processed, Assert.Single(outcome.OperationResult!.Results).Status);
        var product = await harness.Context.Products.AsNoTracking()
            .SingleAsync(item => item.Id == seeded.ProductId);
        Assert.True(product.IsActive);
        Assert.Equal(PublishStatus.Published, product.PublishStatus);
        Assert.True(product.IsOnlineVisible);
        Assert.Equal(14m, product.PurchasePrice);
        Assert.Equal(15m, product.SalePrice);
        Assert.Equal(16m, product.OnlinePrice);

        var variantsAfter = await harness.Context.ProductVariants
            .AsNoTracking()
            .Where(variant => variant.ProductId == seeded.ProductId)
            .ToDictionaryAsync(
                variant => variant.Id,
                variant => new
                {
                    variant.Price,
                    variant.OnlinePrice,
                    variant.CostPrice,
                    variant.IsActive,
                    variant.IsDefault,
                    variant.IsDeleted
                });
        Assert.Equal(variantsBefore, variantsAfter);
    }

    [Fact]
    public async Task Unpublish_PublishedVisibleProduct_BecomesDraftHiddenPreservingActiveState()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var seeded = await SeedSimpleAsync(
            harness, isActive: true, PublishStatus.Published, isOnlineVisible: true, withUnits: true);
        var service = CreateService(harness);

        var outcome = await service.ExecuteLifecycleAsync(
            BulkOperationType.Unpublish,
            Request(seeded.ProductId, seeded.RowVersion),
            "real-unpublish-key");

        Assert.Equal(BulkLifecycleOutcomeKind.Success, outcome.Kind);
        Assert.Equal(BulkItemStatus.Processed, Assert.Single(outcome.OperationResult!.Results).Status);

        var product = await harness.Context.Products.AsNoTracking().SingleAsync(p => p.Id == seeded.ProductId);
        Assert.True(product.IsActive);
        Assert.Equal(PublishStatus.Draft, product.PublishStatus);
        Assert.False(product.IsOnlineVisible);
        Assert.Equal(4m, product.PurchasePrice);
        Assert.Equal(9.5m, product.SalePrice);
        Assert.Null(product.OnlinePrice);

        Assert.Equal("Update", Assert.Single(await harness.Context.AuditLogs.AsNoTracking().ToListAsync()).ActionType);
        var events = await harness.Context.OutboxMessages.AsNoTracking().Select(message => message.EventType).ToListAsync();
        Assert.Contains(typeof(ProductUpdatedEvent).FullName, events);
        Assert.Contains(typeof(ProductPublishStatusChangedEvent).FullName, events);
        Assert.DoesNotContain(typeof(ProductArchivedEvent).FullName, events);

        var replay = await service.ExecuteLifecycleAsync(
            BulkOperationType.Unpublish,
            Request(seeded.ProductId, seeded.RowVersion),
            "real-unpublish-key");

        Assert.Equal(
            JsonSerializer.Serialize(outcome.OperationResult),
            JsonSerializer.Serialize(replay.OperationResult));
        Assert.Equal(1, await harness.Context.AuditLogs.CountAsync());
        Assert.Equal(1, await harness.Context.MarketplaceEventOutboxMessages.CountAsync());
    }

    [Fact]
    public async Task Unpublish_InactivePublishedProduct_PreservesInactiveStateThroughAuthoritativePath()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var seeded = await SeedSimpleAsync(
            harness, isActive: false, PublishStatus.Published, isOnlineVisible: true, withUnits: true);
        var service = CreateService(harness);

        var outcome = await service.ExecuteLifecycleAsync(
            BulkOperationType.Unpublish,
            Request(seeded.ProductId, seeded.RowVersion),
            "real-unpublish-inactive-key");

        Assert.Equal(BulkLifecycleOutcomeKind.Success, outcome.Kind);
        Assert.Equal(BulkItemStatus.Processed, Assert.Single(outcome.OperationResult!.Results).Status);

        var product = await harness.Context.Products.AsNoTracking().SingleAsync(p => p.Id == seeded.ProductId);
        Assert.False(product.IsActive);
        Assert.Equal(PublishStatus.Draft, product.PublishStatus);
        Assert.False(product.IsOnlineVisible);

        var events = await harness.Context.OutboxMessages.AsNoTracking().Select(message => message.EventType).ToListAsync();
        Assert.DoesNotContain(typeof(ProductArchivedEvent).FullName, events);
    }

    private static BulkProductLifecycleService CreateService(BulkFoundationTestHarness harness)
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
            NoOpProductAuthoringTransactionObserver.Instance,
            new ProductAuthoringReconstructionService(context, harness.TenantContext));

        var productLoader = new BulkProductLoader(context, harness.TenantContext);
        return new BulkProductLifecycleService(
            new BulkPreparationService(
                new BulkProductRequestValidator(),
                productLoader,
                new StaticCorrelationIdAccessor("bulk-publication-authoring")),
            new BulkIdempotencyService(
                new ProductMutationIdempotencyService(context),
                harness.TenantContext),
            new BulkOperationResultAggregator(),
            productLoader,
            orchestrator,
            new ProductAuthoringCommandMapper(harness.TenantContext, httpContextAccessor),
            Mock.Of<IValidationService>(),
            context,
            harness.TenantContext,
            NullLogger<BulkProductLifecycleService>.Instance);
    }

    private static async Task<SeededProduct> SeedSimpleAsync(
        BulkFoundationTestHarness harness,
        bool isActive,
        PublishStatus publishStatus,
        bool isOnlineVisible,
        bool withUnits,
        decimal sellingPrice = 9.5m)
    {
        var tenantId = harness.TenantContext.TenantId!.Value;
        var category = new Category
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Bulk publication"
        };
        var unit = new PointOfSale.Core.Entities.Unit
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Piece",
            Symbol = "pc",
            IsActive = true
        };
        var product = new Product
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CategoryId = category.Id,
            Name = "Bulk simple publication",
            SKU = $"BULK-PUB-{Guid.NewGuid():N}",
            Unit = "Piece",
            WeightUnit = "kg",
            DimensionUnit = "cm",
            HasVariants = false,
            IsActive = isActive,
            PublishStatus = publishStatus,
            IsOnlineVisible = isOnlineVisible,
            IsSearchable = true,
            Images = "simple.jpg",
            PurchasePrice = 4m,
            SalePrice = sellingPrice,
            OnlinePrice = null,
            RowVersion = 50
        };

        var productUnit = new ProductUnit
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            UnitId = unit.Id,
            ConversionFactor = 1m,
            PurchasePrice = 4m,
            SellingPrice = sellingPrice,
            IsDefault = true,
            IsBaseUnit = true,
            Product = product
        };

        harness.Context.Categories.Add(category);
        harness.Context.Units.Add(unit);
        harness.Context.Products.Add(product);
        if (withUnits)
        {
            harness.Context.ProductUnits.Add(productUnit);
        }
        await harness.Context.SaveChangesAsync();
        var rowVersion = product.RowVersion;
        harness.Context.ChangeTracker.Clear();
        return new SeededProduct(product.Id, rowVersion);
    }

    private static async Task<SeededProduct> SeedVariantAsync(
        BulkFoundationTestHarness harness,
        bool pricedVariantActive)
    {
        var tenantId = harness.TenantContext.TenantId!.Value;
        var category = new Category
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Bulk variant publication"
        };
        var product = new Product
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CategoryId = category.Id,
            Name = "Bulk variant publication",
            Unit = "Piece",
            WeightUnit = "kg",
            DimensionUnit = "cm",
            HasVariants = true,
            IsActive = true,
            PublishStatus = PublishStatus.Draft,
            IsOnlineVisible = false,
            IsSearchable = true,
            PurchasePrice = 14m,
            SalePrice = 15m,
            OnlinePrice = 16m,
            RowVersion = 60
        };
        harness.Context.Categories.Add(category);
        harness.Context.Products.Add(product);
        harness.Context.ProductVariants.AddRange(
            new ProductVariant
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProductId = product.Id,
                Product = product,
                SKU = "BULKPUB-V1",
                Price = 12m,
                CostPrice = 5m,
                IsActive = pricedVariantActive,
                IsDefault = true
            },
            new ProductVariant
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProductId = product.Id,
                Product = product,
                SKU = "BULKPUB-V2",
                Price = 0m,
                CostPrice = 5m,
                IsActive = true,
                IsDefault = false
            });
        await harness.Context.SaveChangesAsync();
        var rowVersion = product.RowVersion;
        harness.Context.ChangeTracker.Clear();
        return new SeededProduct(product.Id, rowVersion);
    }

    private static BulkProductRequest Request(Guid productId, uint rowVersion) =>
        new([
            new BulkProductRequestItem(productId.ToString(), rowVersion.ToString())
        ]);

    private sealed record SeededProduct(Guid ProductId, uint RowVersion);

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
