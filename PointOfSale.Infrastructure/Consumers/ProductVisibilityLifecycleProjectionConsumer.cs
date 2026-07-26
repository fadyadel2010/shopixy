using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Helpers;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.Infrastructure.Consumers;

/// <summary>
/// Projects product delete/archive/publication lifecycle into the storefront product read model.
/// The event is only a trigger; current SQL product authority decides whether visibility is removed.
/// </summary>
public sealed class ProductVisibilityLifecycleProjectionConsumer :
    IConsumer<ProductDeletedEvent>,
    IConsumer<ProductArchivedEvent>,
    IConsumer<ProductPublishStatusChangedEvent>
{
    private const string ConsumerName = nameof(ProductVisibilityLifecycleProjectionConsumer);

    private readonly ApplicationDbContext _dbContext;
    private readonly IProductSqlAuthorityProjectionWriter _projectionWriter;
    private readonly ILogger<ProductVisibilityLifecycleProjectionConsumer> _logger;

    public ProductVisibilityLifecycleProjectionConsumer(
        ApplicationDbContext dbContext,
        IProductSqlAuthorityProjectionWriter projectionWriter,
        ILogger<ProductVisibilityLifecycleProjectionConsumer> logger)
    {
        _dbContext = dbContext;
        _projectionWriter = projectionWriter;
        _logger = logger;
    }

    public Task Consume(ConsumeContext<ProductDeletedEvent> context) =>
        ApplySqlVisibilityAuthorityAsync(
            context.Message,
            nameof(ProductDeletedEvent),
            context.Message.ProductId,
            context.CancellationToken);

    public Task Consume(ConsumeContext<ProductArchivedEvent> context) =>
        ApplySqlVisibilityAuthorityAsync(
            context.Message,
            nameof(ProductArchivedEvent),
            context.Message.ProductId,
            context.CancellationToken);

    public Task Consume(ConsumeContext<ProductPublishStatusChangedEvent> context) =>
        ApplySqlVisibilityAuthorityAsync(
            context.Message,
            nameof(ProductPublishStatusChangedEvent),
            context.Message.ProductId,
            context.CancellationToken);

    private async Task ApplySqlVisibilityAuthorityAsync(
        IntegrationEvent @event,
        string eventType,
        Guid productId,
        CancellationToken cancellationToken)
    {
        if (!@event.TenantId.HasValue || @event.TenantId.Value == Guid.Empty || productId == Guid.Empty)
        {
            _logger.LogWarning(
                "Product lifecycle visibility event rejected due to missing identity | EventType: {EventType} | EventId: {EventId} | ProductId: {ProductId} | TenantId: {TenantId}",
                eventType,
                @event.EventId,
                productId,
                @event.TenantId);
            return;
        }

        var tenantId = @event.TenantId.Value;
        var sqlProduct = await _dbContext.Products
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(product => product.TenantId == tenantId && product.Id == productId)
            .Select(product => new
            {
                product.Id,
                product.IsDeleted,
                product.IsActive,
                product.IsOnlineVisible,
                product.PublishStatus,
                product.UpdatedAt,
                product.CreatedAt,
                product.ProjectionMutationVersion
            })
            .FirstOrDefaultAsync(cancellationToken);

        _ = SqlTruthRebuildGuard.RequireSqlAuthority(
            SqlTruthRebuildTarget.Storefront,
            tenantId,
            productId,
            SqlAuthorityOverlayKind.Tombstone);

        var appliedVersion = sqlProduct == null
            ? @event.EventVersion
            : Math.Max(
                @event.EventVersion,
                ProductEventVersionHelper.ToProductEventVersion(sqlProduct.ProjectionMutationVersion));
        var appliedOccurredAtUtc = sqlProduct?.UpdatedAt ?? sqlProduct?.CreatedAt ?? @event.OccurredAtUtc;
        var fenceKey = new ProjectionEventFenceKey(
            ConsumerName,
            @event.GetType().FullName ?? eventType,
            productId);

        await using var fence = await ProjectionEventFence.TryAcquireAsync(
            _dbContext,
            @event,
            fenceKey,
            _logger,
            cancellationToken);

        if (fence == null)
        {
            _logger.LogInformation(
                "Duplicate or active product lifecycle storefront replay collapsed before side effects | EventType: {EventType} | EventId: {EventId} | ProductId: {ProductId} | TenantId: {TenantId}",
                eventType,
                @event.EventId,
                productId,
                tenantId);
            return;
        }

        if (sqlProduct is
            {
                IsDeleted: false,
                IsActive: true,
                IsOnlineVisible: true,
                PublishStatus: PublishStatus.Published
            })
        {
            var writeResult = await _projectionWriter.ApplyAsync(
                new ProductSqlAuthorityProjectionWriteRequest(
                    tenantId,
                    productId,
                    new ProductDocument
                    {
                        TenantId = tenantId,
                        ProductId = productId,
                        CreatedAtUtc = appliedOccurredAtUtc
                    },
                    @event.EventId,
                    appliedOccurredAtUtc,
                    appliedVersion),
                cancellationToken);

            await fence.CompleteAsync(@event, fenceKey, cancellationToken);

            if (writeResult.Tombstoned)
            {
                _logger.LogInformation(
                    "Product lifecycle visibility event converged to SQL-authoritative tombstone despite visible lifecycle trigger | EventType: {EventType} | EventId: {EventId} | ProductId: {ProductId} | TenantId: {TenantId} | AppliedVersion: {AppliedVersion} | Result: {Result}",
                    eventType,
                    @event.EventId,
                    productId,
                    tenantId,
                    appliedVersion,
                    writeResult.Disposition);
            }
            else
            {
                _logger.LogInformation(
                    "Product lifecycle visibility event recomposed visible storefront product from SQL authority | EventType: {EventType} | EventId: {EventId} | ProductId: {ProductId} | TenantId: {TenantId} | AppliedVersion: {AppliedVersion} | Result: {Result}",
                    eventType,
                    @event.EventId,
                    productId,
                    tenantId,
                    appliedVersion,
                    writeResult.Disposition);
            }
            return;
        }

        var tombstoneWriteResult = await _projectionWriter.ApplyAsync(
            new ProductSqlAuthorityProjectionWriteRequest(
                tenantId,
                productId,
                new ProductDocument
                {
                    TenantId = tenantId,
                    ProductId = productId,
                    CreatedAtUtc = appliedOccurredAtUtc
                },
                @event.EventId,
                appliedOccurredAtUtc,
                appliedVersion),
            cancellationToken);

        await fence.CompleteAsync(@event, fenceKey, cancellationToken);

        _logger.LogInformation(
            "Product lifecycle visibility tombstone {Result} from SQL authority | EventType: {EventType} | EventId: {EventId} | ProductId: {ProductId} | TenantId: {TenantId} | AppliedVersion: {AppliedVersion}",
            tombstoneWriteResult.Applied ? "applied" : "skipped",
            eventType,
            @event.EventId,
            productId,
            tenantId,
            appliedVersion);
    }
}
