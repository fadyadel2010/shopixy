using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Events;
using PointOfSale.Core.Pricing;
using MarketplaceEvents = PointOfSale.Core.Events.Marketplace;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Reliability;
using PointOfSale.Infrastructure.Utilities;

namespace PointOfSale.Infrastructure.Eventing;

public sealed record ProjectionOutboxTerminalFailure(
    string Pipeline,
    Guid MessageId,
    Guid EventId,
    string EventType,
    Guid? TenantId,
    string Error,
    DateTime FailedAtUtc,
    int AttemptCount);

public interface IProjectionFailureCompensationService
{
    Task HandleTerminalFailureAsync(ProjectionOutboxTerminalFailure failure, CancellationToken cancellationToken = default);
}

/// <summary>
/// Compensates irreparable projection payloads by rebuilding the failed outbox message from
/// authoritative SQL state whenever possible, instead of silently dropping the backbone event.
/// </summary>
public sealed class ProjectionFailureCompensationService : IProjectionFailureCompensationService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<ProjectionFailureCompensationService> _logger;

    public ProjectionFailureCompensationService(
        ApplicationDbContext db,
        ILogger<ProjectionFailureCompensationService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task HandleTerminalFailureAsync(
        ProjectionOutboxTerminalFailure failure,
        CancellationToken cancellationToken = default)
    {
        switch (failure.Pipeline)
        {
            case "marketplace-outbox":
                await CompensateMarketplaceFailureAsync(failure, cancellationToken);
                break;

            case "stock-sync-outbox":
                await CompensateStockSyncFailureAsync(failure, cancellationToken);
                break;

            default:
                _logger.LogCritical(
                    "Projection terminal failure has no compensation pipeline | Pipeline: {Pipeline} | MessageId: {MessageId} | EventType: {EventType} | Error: {Error}",
                    failure.Pipeline,
                    failure.MessageId,
                    failure.EventType,
                    failure.Error);
                break;
        }
    }

    private async Task CompensateMarketplaceFailureAsync(
        ProjectionOutboxTerminalFailure failure,
        CancellationToken cancellationToken)
    {
        var message = await _db.Set<MarketplaceEventOutboxMessage>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(candidate => candidate.Id == failure.MessageId, cancellationToken);

        if (message == null || message.PublishedAtUtc != null)
        {
            return;
        }

        var replay = TryBuildOriginalMarketplaceReplay(message) ??
            await BuildMarketplaceReplayAsync(message, cancellationToken);
        message.RefreshForAuthoritativeRetry(
            replay.EventType,
            replay.ClrType,
            replay.PayloadJson,
            $"Authoritative marketplace backbone replay prepared after terminal publish failure. Original error: {failure.Error}");
        AppendCompensationAudit(
            message.TenantId,
            "ProjectionCompensation",
            "MarketplaceProjectionOutbox",
            message.Id.ToString(),
            $"Marketplace event terminal failure was compensated by rebuilding the failed backbone message from authoritative SQL state. Original event type {failure.EventType}.",
            new
            {
                failure.Pipeline,
                failure.MessageId,
                failure.EventId,
                failure.EventType,
                failure.AttemptCount,
                replay.AggregateId,
                replay.AuthorityEntityType,
                failure.Error
            });

        _logger.LogError(
            "Compensated marketplace outbox terminal failure by rebuilding the authoritative backbone event for retry | MessageId: {MessageId} | AggregateId: {AggregateId} | AuthorityEntityType: {AuthorityEntityType} | EventType: {EventType} | Attempts: {AttemptCount}",
            failure.MessageId,
            replay.AggregateId,
            replay.AuthorityEntityType,
            failure.EventType,
            failure.AttemptCount);

        await _db.SaveChangesAsync(cancellationToken);
    }

    private static MarketplaceReplayEnvelope? TryBuildOriginalMarketplaceReplay(
        MarketplaceEventOutboxMessage message)
    {
        var aggregateId = message.AggregateRootId != Guid.Empty
            ? message.AggregateRootId
            : message.EntityId;

        return message.EventType switch
        {
            nameof(MarketplaceEvents.OrderCreatedEvent)
                when TryDeserializeExistingPayload<MarketplaceEvents.OrderCreatedEvent>(message) != null =>
                BuildOriginalPayloadReplay(message, aggregateId, nameof(Order)),
            nameof(MarketplaceEvents.PaymentCompletedEvent)
                when TryDeserializeExistingPayload<MarketplaceEvents.PaymentCompletedEvent>(message) != null =>
                BuildOriginalPayloadReplay(message, aggregateId, nameof(Order)),
            nameof(MarketplaceEvents.OrderDeliveredEvent)
                when TryDeserializeExistingPayload<MarketplaceEvents.OrderDeliveredEvent>(message) != null =>
                BuildOriginalPayloadReplay(message, aggregateId, nameof(Shipment)),
            nameof(MarketplaceEvents.RefundProcessedEvent)
                when TryDeserializeExistingPayload<MarketplaceEvents.RefundProcessedEvent>(message) != null =>
                BuildOriginalPayloadReplay(message, aggregateId, nameof(OrderRefund)),
            nameof(MarketplaceEvents.ProductUpdatedEvent)
                when TryDeserializeExistingPayload<MarketplaceEvents.ProductUpdatedEvent>(message) != null =>
                BuildOriginalPayloadReplay(message, aggregateId, nameof(Product)),
            _ => null
        };
    }

    private async Task CompensateStockSyncFailureAsync(
        ProjectionOutboxTerminalFailure failure,
        CancellationToken cancellationToken)
    {
        var message = await _db.Set<StockSyncOutboxMessage>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(candidate => candidate.Id == failure.MessageId, cancellationToken);

        if (message == null || message.PublishedAtUtc != null)
        {
            return;
        }

        var variant = await _db.ProductVariants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(candidate =>
                !candidate.IsDeleted &&
                candidate.TenantId == message.TenantId &&
                candidate.Id == message.VariantId,
                cancellationToken);

        if (variant == null)
        {
            await MarkStockCompensationCompletedWithoutReplayAsync(
                message,
                failure,
                "The authoritative variant no longer exists, so replay was impossible and the failed stock row was quarantined to unblock later stock events.",
                cancellationToken);
            return;
        }

        var warehouseAvailable = await _db.ProductVariantStocks
            .IgnoreQueryFilters()
            .WhereCurrentInventoryStockOwner(message.TenantId)
            .Where(candidate =>
                candidate.VariantId == message.VariantId &&
                candidate.WarehouseId == message.WarehouseId)
            .Select(candidate => candidate.Quantity - candidate.ReservedQuantity)
            .DefaultIfEmpty()
            .SumAsync(cancellationToken);

        var warehouseName = await _db.Warehouses
            .IgnoreQueryFilters()
            .Where(candidate =>
                !candidate.IsDeleted &&
                candidate.TenantId == message.TenantId &&
                candidate.Id == message.WarehouseId)
            .Select(candidate => candidate.Name)
            .FirstOrDefaultAsync(cancellationToken);

        var totalAvailable = await _db.ProductVariantStocks
            .IgnoreQueryFilters()
            .WhereCurrentInventoryStockOwner(message.TenantId)
            .Where(candidate => candidate.VariantId == message.VariantId)
            .Select(candidate => candidate.Quantity - candidate.ReservedQuantity)
            .DefaultIfEmpty()
            .SumAsync(cancellationToken);
        warehouseAvailable = Math.Max(0m, warehouseAvailable);
        var nextStockTimestamp = StockProjectionTimestamp.Next(variant.StockUpdatedAtUtc);

        variant.StockUpdatedAtUtc = nextStockTimestamp;
        variant.UpdatedAt = nextStockTimestamp;
        variant.AdvanceProjectionMutationVersion(variant.ProjectionMutationVersion <= 0
            ? 1L
            : variant.ProjectionMutationVersion + 1L);

        var compensationEvent = new ProductVariantStockChangedEvent
        {
            EventId = BuildDeterministicEventId("stock-sync-compensation", failure.MessageId),
            TenantId = message.TenantId,
            VariantId = message.VariantId,
            ProductId = message.ProductId,
            SKU = variant.SKU,
            WarehouseId = message.WarehouseId,
            WarehouseName = warehouseName,
            OldQuantity = warehouseAvailable,
            NewQuantity = warehouseAvailable,
            Reason = "ProjectionCompensationReplay",
            ReferenceId = failure.MessageId,
            ReferenceType = "ProjectionCompensation",
            MutationSource = "SystemCompensation",
            TotalStockQuantity = Math.Max(0m, totalAvailable),
            CorrelationId = failure.MessageId.ToString("N"),
            OccurredAtUtc = nextStockTimestamp,
            StockUpdatedAtUtc = nextStockTimestamp,
            EventVersion = PointOfSale.Core.Helpers.ProductEventVersionHelper.ToProductEventVersion(
                variant.ProjectionMutationVersion)
        };

        var payload = JsonSerializer.Serialize(compensationEvent, typeof(ProductVariantStockChangedEvent));
        _db.StockSyncOutboxMessages.Add(StockSyncOutboxMessage.Create(compensationEvent, payload));

        message.MarkCompensated(
            $"Compensated after terminal publish failure. A fresh stock snapshot event was emitted from SQL authority. Original error: {failure.Error}");
        AppendCompensationAudit(
            message.TenantId,
            "ProjectionCompensation",
            "StockSyncOutbox",
            message.Id.ToString(),
            $"Stock sync terminal failure was compensated by emitting a fresh SQL snapshot for variant {message.VariantId} in warehouse {message.WarehouseId}. Original event type {failure.EventType}.",
            new
            {
                failure.Pipeline,
                failure.MessageId,
                failure.EventId,
                failure.EventType,
                failure.AttemptCount,
                message.VariantId,
                message.WarehouseId,
                failure.Error
            });

        _logger.LogError(
            "Compensated stock sync terminal failure by emitting a fresh SQL stock snapshot and unblocking the stream | MessageId: {MessageId} | VariantId: {VariantId} | WarehouseId: {WarehouseId} | Attempts: {AttemptCount}",
            failure.MessageId,
            message.VariantId,
            message.WarehouseId,
            failure.AttemptCount);

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkStockCompensationCompletedWithoutReplayAsync(
        StockSyncOutboxMessage message,
        ProjectionOutboxTerminalFailure failure,
        string reason,
        CancellationToken cancellationToken)
    {
        message.MarkCompensated(
            $"Compensated after terminal publish failure without replay. {reason} Original error: {failure.Error}");
        AppendCompensationAudit(
            message.TenantId,
            "ProjectionCompensation",
            "StockSyncOutbox",
            message.Id.ToString(),
            $"Stock sync terminal failure was quarantined without replay. {reason} Original event type {failure.EventType}.",
            new
            {
                failure.Pipeline,
                failure.MessageId,
                failure.EventId,
                failure.EventType,
                failure.AttemptCount,
                message.VariantId,
                message.WarehouseId,
                failure.Error
            });

        _logger.LogCritical(
            "Stock sync terminal failure was marked compensated without replay to unblock the warehouse-scoped stream | MessageId: {MessageId} | TenantId: {TenantId} | VariantId: {VariantId} | WarehouseId: {WarehouseId} | Reason: {Reason}",
            failure.MessageId,
            message.TenantId,
            message.VariantId,
            message.WarehouseId,
            reason);

        await _db.SaveChangesAsync(cancellationToken);
    }

    private void AppendCompensationAudit(
        Guid? tenantId,
        string actionType,
        string entityType,
        string entityId,
        string description,
        object afterData)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenantId,
            TimestampUtc = DateTime.UtcNow,
            ActionType = actionType,
            EntityType = entityType,
            EntityId = entityId,
            Description = description,
            AfterDataJson = JsonSerializer.Serialize(afterData),
            Source = "Background",
            SuccessFlag = true // Compensation/recovery completed successfully
        });
    }

    private static Guid BuildDeterministicEventId(string purpose, Guid sourceId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{purpose}:{sourceId:N}"));
        return new Guid(hash[..16]);
    }

    private async Task<MarketplaceReplayEnvelope> BuildMarketplaceReplayAsync(
        MarketplaceEventOutboxMessage message,
        CancellationToken cancellationToken)
    {
        var aggregateId = message.AggregateRootId != Guid.Empty
            ? message.AggregateRootId
            : message.EntityId;

        return message.EventType switch
        {
            nameof(MarketplaceEvents.OrderCreatedEvent) => await BuildOrderCreatedReplayAsync(message, aggregateId, cancellationToken),
            nameof(MarketplaceEvents.PaymentCompletedEvent) => await BuildPaymentCompletedReplayAsync(message, aggregateId, cancellationToken),
            nameof(MarketplaceEvents.OrderDeliveredEvent) => await BuildOrderDeliveredReplayAsync(message, aggregateId, cancellationToken),
            nameof(MarketplaceEvents.RefundProcessedEvent) => await BuildRefundProcessedReplayAsync(message, aggregateId, cancellationToken),
            nameof(MarketplaceEvents.ProductUpdatedEvent) => await BuildProductUpdatedReplayAsync(message, aggregateId, cancellationToken),
            _ => BuildOriginalPayloadReplay(message, aggregateId, "MarketplaceEventOutboxMessage")
        };
    }

    private async Task<MarketplaceReplayEnvelope> BuildOrderCreatedReplayAsync(
        MarketplaceEventOutboxMessage message,
        Guid aggregateId,
        CancellationToken cancellationToken)
    {
        var order = await _db.Orders
            .IgnoreQueryFilters()
            .Include(candidate => candidate.Items)
            .FirstOrDefaultAsync(candidate => !candidate.IsDeleted && candidate.Id == aggregateId, cancellationToken);

        if (order == null)
        {
            return BuildOriginalPayloadReplay(message, aggregateId, nameof(Order));
        }

        var @event = new MarketplaceEvents.OrderCreatedEvent
        {
            EventId = message.EventId,
            OccurredAtUtc = message.OccurredAtUtc,
            CorrelationId = message.CorrelationId,
            EntityId = order.Id,
            AggregateRootId = order.Id,
            TenantId = order.TenantId,
            Version = message.Version,
            OrderNumber = order.OrderNumber,
            CustomerId = order.CustomerId,
            OnlineCustomerId = order.OnlineCustomerId,
            TotalAmount = order.Total,
            SubtotalAmount = order.Subtotal,
            Currency = order.Currency,
            PaymentMethodCode = order.PaymentMethodCode,
            PaymentMethodName = order.PaymentMethodName,
            IsPaid = order.IsPaid,
            Items = order.Items
                .Where(item => !item.IsDeleted)
                .OrderBy(item => item.CreatedAt)
                .Select(item => new MarketplaceEvents.MarketplaceOrderItem
                {
                    ProductId = item.ProductId,
                    VariantId = item.VariantId,
                    Sku = item.SKU,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice
                })
                .ToList()
        };

        return BuildReplayEnvelope(@event, nameof(Order), order.Id);
    }

    private async Task<MarketplaceReplayEnvelope> BuildPaymentCompletedReplayAsync(
        MarketplaceEventOutboxMessage message,
        Guid aggregateId,
        CancellationToken cancellationToken)
    {
        var order = await _db.Orders
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(candidate => !candidate.IsDeleted && candidate.Id == aggregateId, cancellationToken);

        if (order == null)
        {
            return BuildOriginalPayloadReplay(message, aggregateId, nameof(Order));
        }

        var completedAtUtc = order.PaidAt ?? message.OccurredAtUtc;
        var @event = new MarketplaceEvents.PaymentCompletedEvent
        {
            EventId = message.EventId,
            OccurredAtUtc = completedAtUtc,
            CorrelationId = message.CorrelationId,
            EntityId = order.Id,
            AggregateRootId = order.Id,
            TenantId = order.TenantId,
            Version = message.Version,
            OrderNumber = order.OrderNumber,
            PaymentMethod = order.PaymentMethodName ?? order.PaymentMethodCode,
            ExternalReference = order.PaymentReference,
            Amount = order.Total,
            Currency = order.Currency,
            CompletionSource = "AuthoritativeReplay",
            CompletedAtUtc = completedAtUtc
        };

        return BuildReplayEnvelope(@event, nameof(Order), order.Id);
    }

    private async Task<MarketplaceReplayEnvelope> BuildOrderDeliveredReplayAsync(
        MarketplaceEventOutboxMessage message,
        Guid aggregateId,
        CancellationToken cancellationToken)
    {
        var shipment = await _db.Shipments
            .IgnoreQueryFilters()
            .Include(candidate => candidate.Order)
            .FirstOrDefaultAsync(candidate => !candidate.IsDeleted && candidate.OrderId == aggregateId, cancellationToken);

        if (shipment?.Order == null)
        {
            return BuildOriginalPayloadReplay(message, aggregateId, nameof(Shipment));
        }

        var deliveredAtUtc = shipment.DeliveredAtUtc ?? shipment.Order.DeliveredAt ?? message.OccurredAtUtc;
        var @event = new MarketplaceEvents.OrderDeliveredEvent
        {
            EventId = message.EventId,
            OccurredAtUtc = deliveredAtUtc,
            CorrelationId = message.CorrelationId,
            EntityId = shipment.OrderId,
            AggregateRootId = shipment.OrderId,
            TenantId = shipment.TenantId,
            Version = message.Version,
            OrderNumber = shipment.Order.OrderNumber,
            DeliveredAtUtc = deliveredAtUtc
        };

        return BuildReplayEnvelope(@event, nameof(Shipment), shipment.OrderId);
    }

    private async Task<MarketplaceReplayEnvelope> BuildRefundProcessedReplayAsync(
        MarketplaceEventOutboxMessage message,
        Guid aggregateId,
        CancellationToken cancellationToken)
    {
        var existingPayload = TryDeserializeExistingPayload<MarketplaceEvents.RefundProcessedEvent>(message);
        var refund = await _db.OrderRefunds
            .IgnoreQueryFilters()
            .Include(candidate => candidate.Order)
            .FirstOrDefaultAsync(candidate =>
                !candidate.IsDeleted &&
                candidate.OrderId == aggregateId &&
                (existingPayload == null || candidate.RefundId == existingPayload.RefundId),
                cancellationToken);

        if (refund?.Order == null)
        {
            return BuildOriginalPayloadReplay(message, aggregateId, nameof(OrderRefund));
        }

        var processedAtUtc = refund.RefundedAtUtc == default
            ? message.OccurredAtUtc
            : refund.RefundedAtUtc;
        var @event = new MarketplaceEvents.RefundProcessedEvent
        {
            EventId = message.EventId,
            OccurredAtUtc = processedAtUtc,
            CorrelationId = message.CorrelationId,
            EntityId = refund.OrderId,
            AggregateRootId = refund.OrderId,
            TenantId = refund.TenantId,
            Version = message.Version,
            OrderNumber = refund.Order.OrderNumber,
            RefundId = refund.RefundId,
            Amount = refund.Amount,
            Currency = refund.Currency,
            IsFullRefund = refund.IsFullRefund,
            Reason = refund.Reason,
            PaymentMethod = refund.PaymentMethod,
            ProcessedAtUtc = processedAtUtc
        };

        return BuildReplayEnvelope(@event, nameof(OrderRefund), refund.Id);
    }

    private async Task<MarketplaceReplayEnvelope> BuildProductUpdatedReplayAsync(
        MarketplaceEventOutboxMessage message,
        Guid aggregateId,
        CancellationToken cancellationToken)
    {
        var product = await _db.Products
            .IgnoreQueryFilters()
            .Include(candidate => candidate.ProductUnits)
            .FirstOrDefaultAsync(candidate => !candidate.IsDeleted && candidate.Id == aggregateId, cancellationToken);

        if (product == null)
        {
            return BuildOriginalPayloadReplay(message, aggregateId, nameof(Product));
        }

        var existingPayload = TryDeserializeExistingPayload<MarketplaceEvents.ProductUpdatedEvent>(message);
        var defaultUnit = product.ProductUnits
            .FirstOrDefault(unit => !unit.IsDeleted && unit.IsDefault)
            ?? product.ProductUnits.FirstOrDefault(unit => !unit.IsDeleted);
        var @event = new MarketplaceEvents.ProductUpdatedEvent
        {
            EventId = message.EventId,
            OccurredAtUtc = message.OccurredAtUtc,
            CorrelationId = message.CorrelationId,
            EntityId = product.Id,
            AggregateRootId = product.Id,
            TenantId = product.TenantId,
            Version = message.Version,
            ChangedFields = existingPayload?.ChangedFields ?? ["AuthoritativeReplay"],
            Name = product.Name,
            Sku = product.SKU,
            Slug = product.Slug,
            SalePrice = CatalogPricingAuthority.ResolveSimpleSellingPrice(defaultUnit),
            OnlinePrice = null,
            CategoryId = product.CategoryId,
            IsOnlineVisible = product.IsOnlineVisible,
            HasVariants = product.HasVariants
        };

        return BuildReplayEnvelope(@event, nameof(Product), product.Id);
    }

    private static MarketplaceReplayEnvelope BuildOriginalPayloadReplay(
        MarketplaceEventOutboxMessage message,
        Guid aggregateId,
        string authorityEntityType) =>
        new(
            message.EventType,
            message.ClrType,
            message.PayloadJson,
            authorityEntityType,
            aggregateId);

    private static MarketplaceReplayEnvelope BuildReplayEnvelope<TEvent>(
        TEvent @event,
        string authorityEntityType,
        Guid aggregateId)
        where TEvent : class
    {
        var clrType = typeof(TEvent).AssemblyQualifiedName ?? typeof(TEvent).FullName ?? typeof(TEvent).Name;
        var payloadJson = JsonSerializer.Serialize(@event, typeof(TEvent));
        return new MarketplaceReplayEnvelope(
            typeof(TEvent).Name,
            clrType,
            payloadJson,
            authorityEntityType,
            aggregateId);
    }

    private static TEvent? TryDeserializeExistingPayload<TEvent>(MarketplaceEventOutboxMessage message)
        where TEvent : class
    {
        try
        {
            return JsonSerializer.Deserialize<TEvent>(message.PayloadJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record MarketplaceReplayEnvelope(
        string EventType,
        string ClrType,
        string PayloadJson,
        string AuthorityEntityType,
        Guid AggregateId);
}
