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
using PointOfSale.Tests.Infrastructure;
using Xunit.Sdk;

namespace PointOfSale.Tests.Bulk;

public sealed class BulkPostCommitCancellationPostgresTests
{
    [Fact]
    public async Task Delete_PostCommitCancellation_Postgres_ReconcilesAndReplaysWithoutDuplicateDestruction()
    {
        if (!RealPostgresTestSupport.TryCreateScope(
                out var databaseScope,
                out var tenantId,
                out var tenantContext) ||
            databaseScope is null ||
            tenantContext is null)
        {
            throw SkipException.ForSkip(
                "Requires a real PostgreSQL ConnectionStrings:DefaultConnection value.");
        }

        await using var ownedScope = databaseScope;
        await using var context = ownedScope.CreateDbContext(tenantContext);
        var category = new Category
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Bulk cancellation PostgreSQL"
        };
        var product = new Product
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CategoryId = category.Id,
            Name = "PostgreSQL destructive cancellation product",
            Unit = "Piece",
            WeightUnit = "kg",
            DimensionUnit = "cm",
            HasVariants = true,
            IsActive = true,
            PublishStatus = PublishStatus.Published,
            IsOnlineVisible = true,
            IsSearchable = true,
            Images = "postgres-delete.jpg"
        };
        context.Categories.Add(category);
        context.Products.Add(product);
        context.ProductVariants.AddRange(
            Variant(product, tenantId, "PG-DELETE-1", isDefault: true),
            Variant(product, tenantId, "PG-DELETE-2", isDefault: false));
        await context.SaveChangesAsync();
        await context.Entry(product).ReloadAsync();
        var expectedVersion = product.RowVersion;
        context.ChangeTracker.Clear();

        using var cancellation = new CancellationTokenSource();
        var service = CreateService(
            context,
            tenantContext,
            new CancelAfterCommitObserver(cancellation));
        var request = new BulkProductRequest(
        [
            new BulkProductRequestItem(
                product.Id.ToString(),
                expectedVersion.ToString(System.Globalization.CultureInfo.InvariantCulture))
        ]);
        const string key = "postgres-post-commit-cancel-delete";

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ExecuteLifecycleAsync(BulkOperationType.Delete, request, key, cancellation.Token));

        context.ChangeTracker.Clear();
        var outerRow = await context.ProductRequestIdempotencyRecords
            .AsNoTracking()
            .SingleAsync(record => record.Operation == BulkIdempotencyService.OuterClaimStorageOperation);
        Assert.Equal(StatusCodes.Status200OK, outerRow.ResponseStatusCode);
        Assert.NotNull(outerRow.ResponseJson);
        Assert.Equal(2, await context.ProductRequestIdempotencyRecords.AsNoTracking().CountAsync());

        var deletedProduct = await context.Products
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(item => item.Id == product.Id);
        Assert.True(deletedProduct.IsDeleted);
        var deletedVariants = await context.ProductVariants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(variant => variant.ProductId == product.Id)
            .ToListAsync();
        Assert.Equal(2, deletedVariants.Count);
        Assert.All(deletedVariants, variant => Assert.True(variant.IsDeleted));

        var outboxTypes = await context.OutboxMessages
            .AsNoTracking()
            .Select(message => message.EventType)
            .ToListAsync();
        Assert.Equal(1, outboxTypes.Count(type => type == typeof(ProductDeletedEvent).FullName));
        Assert.Equal(2, outboxTypes.Count(type => type == typeof(ProductVariantDeletedEvent).FullName));
        Assert.Equal(2, outboxTypes.Count(type => type == typeof(VariantDeletedEvent).FullName));
        Assert.Single(await context.AuditLogs.AsNoTracking().ToListAsync());
        var marketplaceCount = await context.MarketplaceEventOutboxMessages.AsNoTracking().CountAsync();
        Assert.True(marketplaceCount > 0);
        var outboxCount = outboxTypes.Count;

        var firstReplay = await service.ExecuteLifecycleAsync(BulkOperationType.Delete, request, key, default);
        var secondReplay = await service.ExecuteLifecycleAsync(BulkOperationType.Delete, request, key, default);

        Assert.Equal(BulkLifecycleOutcomeKind.Success, firstReplay.Kind);
        Assert.NotNull(firstReplay.OperationResult);
        Assert.Equal(1, firstReplay.OperationResult.Requested);
        Assert.Equal(1, firstReplay.OperationResult.Accepted);
        Assert.Equal(1, firstReplay.OperationResult.Processed);
        Assert.Equal(0, firstReplay.OperationResult.Unchanged);
        Assert.Equal(0, firstReplay.OperationResult.Failed);
        Assert.Equal(BulkItemStatus.Processed, Assert.Single(firstReplay.OperationResult.Results).Status);
        Assert.Equal(
            JsonSerializer.Serialize(firstReplay.OperationResult),
            JsonSerializer.Serialize(secondReplay.OperationResult));
        Assert.Equal(2, await context.ProductRequestIdempotencyRecords.AsNoTracking().CountAsync());
        Assert.Equal(outboxCount, await context.OutboxMessages.AsNoTracking().CountAsync());
        Assert.Equal(marketplaceCount, await context.MarketplaceEventOutboxMessages.AsNoTracking().CountAsync());
        Assert.Single(await context.AuditLogs.AsNoTracking().ToListAsync());
    }

    private static BulkProductLifecycleService CreateService(
        ApplicationDbContext context,
        PointOfSale.Tests.Infrastructure.TestTenantContext tenantContext,
        IProductAuthoringTransactionObserver observer)
    {
        var httpContextAccessor = new HttpContextAccessor();
        var eventPublisher = new PersistedEventPublisher(context);
        var variantService = new ProductVariantService(
            context,
            eventPublisher,
            NullLogger<ProductVariantService>.Instance);
        var orchestrator = new ProductAuthoringOrchestrator(
            new ProductAuthoringPolicy(new ProductAuthoringValidator()),
            context,
            tenantContext,
            variantService,
            eventPublisher,
            new MarketplaceEventOutboxService(
                context,
                NullLogger<MarketplaceEventOutboxService>.Instance),
            NullLogger<ProductAuthoringOrchestrator>.Instance,
            httpContextAccessor,
            new ProductMutationIdempotencyService(context),
            observer,
            new ProductAuthoringReconstructionService(context, tenantContext));
        var validation = new Mock<IValidationService>();
        validation.Setup(service => service.CanDeleteProductAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidationResult.Success());
        var loader = new BulkProductLoader(context, tenantContext);

        return new BulkProductLifecycleService(
            new BulkPreparationService(
                new BulkProductRequestValidator(),
                loader,
                new StaticCorrelationIdAccessor("bulk-postgres-cancellation")),
            new BulkIdempotencyService(
                new ProductMutationIdempotencyService(context),
                tenantContext),
            new BulkOperationResultAggregator(),
            loader,
            orchestrator,
            new ProductAuthoringCommandMapper(tenantContext, httpContextAccessor),
            validation.Object,
            context,
            tenantContext,
            NullLogger<BulkProductLifecycleService>.Instance);
    }

    private static ProductVariant Variant(
        Product product,
        Guid tenantId,
        string sku,
        bool isDefault) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProductId = product.Id,
            Product = product,
            SKU = sku,
            Price = 12m,
            OnlinePrice = 12m,
            CostPrice = 5m,
            IsActive = true,
            IsDefault = isDefault
        };

    private sealed class CancelAfterCommitObserver(CancellationTokenSource cancellation)
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
            throw new OperationCanceledException(cancellation.Token);
        }
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
