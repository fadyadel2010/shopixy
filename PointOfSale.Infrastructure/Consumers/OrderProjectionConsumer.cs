using MassTransit;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using MongoDB.Bson;
using MongoDB.Driver;
using PointOfSale.Core.Events;
using PointOfSale.Core.Events.Affiliate;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Projection;
using PointOfSale.Infrastructure.Reliability;
using StackExchange.Redis;

namespace PointOfSale.Infrastructure.Consumers;

/// <summary>
/// Projects Order events from PostgreSQL (Write Model) to MongoDB (Read Model).
/// 
/// PHASE 10.5.A: Order Lifecycle State Machine
/// 
/// ARCHITECTURAL RULES:
/// - Handles all Order lifecycle events
/// - Idempotent projections (safe to replay)
/// - Tenant isolation enforced - Guid.Empty rejected
/// - Invalidates Redis cache on changes
/// - Uses $set + $push Timeline for atomic updates
/// 
/// Consumed Events:
/// - OrderCreatedEvent → Insert new OrderDocument
/// - OrderStatusChangedEvent → Update Status + Timeline
/// - OrderPaymentStatusChangedEvent → Update PaymentStatus + Timeline
/// - ShipmentStatusChangedEvent → Update ShippingStatus + Timeline
/// - OrderShippedEvent → Update tracking info + Timeline
/// - OrderDeliveredEvent → Update delivery info + Timeline
/// - OrderCancelledEvent → Update Status to Cancelled + Timeline
/// - Fulfillment events → Rebuild shipping visibility from current SQL authority
/// </summary>
public class OrderProjectionConsumer :
    IConsumer<OrderCreatedEvent>,
    IConsumer<OrderStatusChangedEvent>,
    IConsumer<OrderPaymentStatusChangedEvent>,
    IConsumer<ShipmentStatusChangedEvent>,
    IConsumer<OrderShippedEvent>,
    IConsumer<OrderDeliveredEvent>,
    IConsumer<OrderCancelledEvent>,
    IConsumer<FulfillmentConfirmedEvent>,
    IConsumer<OrderPackedEvent>,
    IConsumer<OrderOutForDeliveryEvent>,
    IConsumer<FulfillmentDeliveredEvent>,
    IConsumer<OrderCompletedEvent>,
    IConsumer<OrderReturnedEvent>,
    IConsumer<OrderDeliveryFailedEvent>,
    IConsumer<OrderFulfillmentCancelledEvent>,
    IConsumer<AffiliateAttributedEvent>,       // AF-35: Mark order as affiliate-attributed
    IConsumer<AffiliateCommissionEarnedEvent>, // AF-35: Update commission amount
    IConsumer<AffiliateCommissionReversedEvent>
{
    private readonly IMongoCollection<OrderDocument> _collection;
    private readonly ApplicationDbContext _dbContext;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<OrderProjectionConsumer> _logger;

    private const string CacheKeyPrefix = "storefront:tenant:{0}:order:{1}";
    private const string CustomerOrdersCacheKeyPrefix = "storefront:tenant:{0}:customer:{1}:orders";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    public OrderProjectionConsumer(
        IMongoDatabase mongoDatabase,
        ApplicationDbContext dbContext,
        IConnectionMultiplexer redis,
        ILogger<OrderProjectionConsumer> logger)
    {
        _collection = mongoDatabase.GetCollection<OrderDocument>("orders");
        _dbContext = dbContext;
        _redis = redis;
        _logger = logger;

        _logger.LogInformation("[Order Projection Consumer] STARTED - Ready to process order events via RabbitMQ");

        EnsureIndexes();
    }

    private void EnsureIndexes()
    {
        try
        {
            var indexKeys = Builders<OrderDocument>.IndexKeys;

            // Unique composite index on (TenantId, OrderId)
            var uniqueIndex = new CreateIndexModel<OrderDocument>(
                indexKeys.Ascending(x => x.TenantId).Ascending(x => x.OrderId),
                new CreateIndexOptions { Unique = true, Name = "IX_TenantId_OrderId" });

            // Storefront order history is keyed by OnlineCustomerId. CustomerId is POS-only.
            var onlineCustomerIndex = new CreateIndexModel<OrderDocument>(
                indexKeys.Ascending(x => x.TenantId).Ascending(x => x.OnlineCustomerId),
                new CreateIndexOptions { Name = "IX_TenantId_OnlineCustomerId" });

            // Index on (TenantId, OrderNumber) for lookup
            var orderNumberIndex = new CreateIndexModel<OrderDocument>(
                indexKeys.Ascending(x => x.TenantId).Ascending(x => x.OrderNumber),
                new CreateIndexOptions { Unique = true, Name = "IX_TenantId_OrderNumber" });

            // Index on (TenantId, CreatedAt) for sorting
            var createdAtIndex = new CreateIndexModel<OrderDocument>(
                indexKeys.Ascending(x => x.TenantId).Descending(x => x.CreatedAt),
                new CreateIndexOptions { Name = "IX_TenantId_CreatedAt" });

            // Index on (TenantId, Status) for filtering
            var statusIndex = new CreateIndexModel<OrderDocument>(
                indexKeys.Ascending(x => x.TenantId).Ascending(x => x.Status),
                new CreateIndexOptions { Name = "IX_TenantId_Status" });

            var freshnessIndex = new CreateIndexModel<OrderDocument>(
                indexKeys.Ascending(x => x.TenantId).Ascending(x => x.OrderId).Descending(x => x.LastEventVersion).Descending(x => x.LastEventOccurredAtUtc),
                new CreateIndexOptions { Name = "IX_TenantId_OrderId_LastEventVersion_LastEventOccurredAtUtc" });

            var statusFreshnessIndex = new CreateIndexModel<OrderDocument>(
                indexKeys.Ascending(x => x.TenantId).Ascending(x => x.OrderId).Descending(x => x.LastStatusEventVersion).Descending(x => x.LastStatusEventOccurredAtUtc),
                new CreateIndexOptions { Name = "IX_TenantId_OrderId_LastStatusEventVersion_LastStatusEventOccurredAtUtc" });

            var paymentFreshnessIndex = new CreateIndexModel<OrderDocument>(
                indexKeys.Ascending(x => x.TenantId).Ascending(x => x.OrderId).Descending(x => x.LastPaymentEventVersion).Descending(x => x.LastPaymentEventOccurredAtUtc),
                new CreateIndexOptions { Name = "IX_TenantId_OrderId_LastPaymentEventVersion_LastPaymentEventOccurredAtUtc" });

            var shipmentFreshnessIndex = new CreateIndexModel<OrderDocument>(
                indexKeys.Ascending(x => x.TenantId).Ascending(x => x.OrderId).Descending(x => x.LastShipmentEventVersion).Descending(x => x.LastShipmentEventOccurredAtUtc),
                new CreateIndexOptions { Name = "IX_TenantId_OrderId_LastShipmentEventVersion_LastShipmentEventOccurredAtUtc" });

            _collection.Indexes.CreateMany(new[]
            {
                uniqueIndex,
                onlineCustomerIndex,
                orderNumberIndex,
                createdAtIndex,
                statusIndex,
                freshnessIndex,
                statusFreshnessIndex,
                paymentFreshnessIndex,
                shipmentFreshnessIndex
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Order] Failed to ensure MongoDB indexes for orders collection");
        }
    }

    #region OrderCreatedEvent

    public async Task Consume(ConsumeContext<OrderCreatedEvent> context)
    {
        var @event = context.Message;
        var cancellationToken = context.CancellationToken;

        if (!ValidateTenantId(@event.TenantId, @event.OrderId, nameof(OrderCreatedEvent)))
            return;

        // SECURITY: Validate EventVersion is positive before creating projection
        if (@event.EventVersion <= 0)
        {
            _logger.LogError(
                "[Order] REJECTED OrderCreatedEvent with non-positive EventVersion | TenantId: {TenantId} | OrderId: {OrderId} | EventId: {EventId} | EventVersion: {EventVersion}",
                @event.TenantId, @event.OrderId, @event.EventId, @event.EventVersion);
            throw new InvalidOperationException(
                $"Event {@event.EventId} has invalid EventVersion {@event.EventVersion}. EventVersion must be positive.");
        }

        _logger.LogInformation(
            "[Order] Processing {Action} | TenantId: {TenantId} | OrderId: {OrderId} | OrderNumber: {OrderNumber} | TraceId: {TraceId}",
            "ORDER_CREATED",
            @event.TenantId,
            @event.OrderId,
            @event.OrderNumber,
            context.MessageId);

        try
        {
            var filter = BuildOrderIdentityFilter(
                @event.TenantId!.Value,
                @event.OrderId);

            // Check if order already exists (idempotency)
            var exists = await _collection.Find(filter).AnyAsync(cancellationToken);
            if (exists)
            {
                _logger.LogDebug(
                    "[Order] Order already exists (idempotent) | TenantId: {TenantId} | OrderId: {OrderId}",
                    @event.TenantId, @event.OrderId);
                return;
            }

            var now = DateTime.UtcNow;
            var doc = new OrderDocument
            {
                TenantId = @event.TenantId!.Value,
                OrderId = @event.OrderId,
                OrderNumber = @event.OrderNumber,
                CustomerId = @event.CustomerId,
                OnlineCustomerId = @event.OnlineCustomerId,
                SessionId = @event.SessionId,
                CustomerEmail = @event.CustomerEmail,
                CustomerName = @event.CustomerName,
                Status = @event.Status.ToString(),
                PaymentStatus = @event.PaymentStatus.ToString(),
                ShippingStatus = "Pending",
                Subtotal = @event.Subtotal,
                PromotionDiscountAmount = @event.PromotionDiscountAmount,
                DiscountAmount = @event.DiscountAmount,
                TaxAmount = @event.TaxAmount,
                ShippingCost = @event.ShippingCost,
                Total = @event.Total,
                Currency = @event.Currency,
                // PHASE 10.5.A: Additional fields from enhanced OrderCreatedEvent
                ShippingMethodName = @event.ShippingMethodName,
                PaymentMethodCode = @event.PaymentMethodCode,
                PaymentMethodName = @event.PaymentMethodName,
                // FULFILLMENT OWNERSHIP SNAPSHOT
                FulfillmentMode = @event.FulfillmentMode?.ToString() ?? "Manual",
                FulfillmentCarrierCode = @event.FulfillmentCarrierCode,
                // PHASE 10.5.A2: Shipping address for order tracking
                ShippingAddress = new OrderAddressDocument
                {
                    Street = @event.ShippingStreet ?? string.Empty,
                    Street2 = @event.ShippingStreet2,
                    City = @event.ShippingCity,
                    State = @event.ShippingState,
                    ZipCode = @event.ShippingZipCode,
                    Country = @event.ShippingCountry,
                    Phone = @event.ShippingPhone
                },
                Items = @event.Items.Select(i => new OrderItemDocument
                {
                    ProductId = i.ProductId,
                    // PHASE 11.7: Include VariantId for SKU-level tracking
                    VariantId = i.VariantId,
                    SKU = i.SKU,
                    ProductName = i.ProductName,
                    VariantName = i.VariantName,
                    Quantity = i.Quantity,
                    UnitPrice = i.UnitPrice,
                    Total = i.Total
                }).ToList(),
                Timeline = new List<OrderTimelineItem>
                {
                    new()
                    {
                        At = @event.OccurredAtUtc,
                        Type = "OrderCreated",
                        From = "",
                        To = @event.Status.ToString(),
                        Description = "Order placed"
                    }
                },
                CreatedAt = @event.OccurredAtUtc,
                UpdatedAt = now,
                LastEventId = @event.EventId,
                LastEventOccurredAtUtc = @event.OccurredAtUtc,
                LastEventVersion = @event.EventVersion,
                LastStatusEventId = @event.EventId,
                LastStatusEventOccurredAtUtc = @event.OccurredAtUtc,
                LastStatusEventVersion = @event.EventVersion,
                LastPaymentEventId = @event.EventId,
                LastPaymentEventOccurredAtUtc = @event.OccurredAtUtc,
                LastPaymentEventVersion = @event.EventVersion,
                LastShipmentEventId = @event.EventId,
                LastShipmentEventOccurredAtUtc = @event.OccurredAtUtc,
                LastShipmentEventVersion = @event.EventVersion
            };

            await _collection.InsertOneAsync(doc, cancellationToken: cancellationToken);

            await InvalidateCacheAsync(@event.TenantId!.Value, @event.OrderId, @event.OnlineCustomerId);

            _logger.LogInformation(
                "[Order] {Action} projected successfully | TenantId: {TenantId} | OrderId: {OrderId} | OrderNumber: {OrderNumber}",
                "ORDER_CREATED",
                @event.TenantId,
                @event.OrderId,
                @event.OrderNumber);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // Idempotent - order already exists
            _logger.LogDebug(
                "[Order] Duplicate key (idempotent) | TenantId: {TenantId} | OrderId: {OrderId}",
                @event.TenantId, @event.OrderId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Order] Failed to project {Action} | TenantId: {TenantId} | OrderId: {OrderId}",
                "ORDER_CREATED",
                @event.TenantId,
                @event.OrderId);
            throw;
        }
    }

    #endregion

    #region OrderStatusChangedEvent

    public async Task Consume(ConsumeContext<OrderStatusChangedEvent> context)
    {
        var @event = context.Message;
        var cancellationToken = context.CancellationToken;

        if (!ValidateTenantId(@event.TenantId, @event.OrderId, nameof(OrderStatusChangedEvent)))
            return;

        _logger.LogInformation(
            "[Order] Processing {Action} | TenantId: {TenantId} | OrderId: {OrderId} | {From} → {To} | TraceId: {TraceId}",
            "STATUS_CHANGED",
            @event.TenantId,
            @event.OrderId,
            @event.OldStatus,
            @event.NewStatus,
            context.MessageId);

        try
        {
            var filter = BuildFreshFacetEventFilter(
                @event.TenantId!.Value,
                @event.OrderId,
                @event.OccurredAtUtc,
                @event.EventVersion,
                @event.EventId,
                nameof(OrderDocument.LastStatusEventVersion),
                nameof(OrderDocument.LastStatusEventOccurredAtUtc));

            var timelineEntry = new OrderTimelineItem
            {
                At = @event.OccurredAtUtc,
                Type = "StatusChanged",
                From = @event.OldStatus.ToString(),
                To = @event.NewStatus.ToString(),
                Description = @event.Reason ?? $"Status changed from {getStatusLabel(@event.OldStatus.ToString())} to {getStatusLabel(@event.NewStatus.ToString())}"
            };

            var update = Builders<OrderDocument>.Update
                .Set(x => x.Status, @event.NewStatus.ToString())
                .Set(x => x.UpdatedAt, DateTime.UtcNow)
                .Set(x => x.LastEventId, @event.EventId)
                .Max(x => x.LastEventOccurredAtUtc, @event.OccurredAtUtc)
                .Max(x => x.LastEventVersion, @event.EventVersion)
                .Set(x => x.LastStatusEventId, @event.EventId)
                .Set(x => x.LastStatusEventOccurredAtUtc, @event.OccurredAtUtc)
                .Set(x => x.LastStatusEventVersion, @event.EventVersion)
                .Push(x => x.Timeline, timelineEntry);

            var result = await _collection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);

            if (result.MatchedCount == 0)
            {
                await HandleNoOrderProjectionMutationMatchAsync(
                    @event.TenantId!.Value,
                    @event.OrderId,
                    @event.EventId,
                    @event.OccurredAtUtc,
                    "STATUS_CHANGED",
                    cancellationToken);
                return;
            }

            // Get customer ID for cache invalidation
            var doc = await _collection.Find(BuildOrderIdentityFilter(@event.TenantId!.Value, @event.OrderId)).Project(x => new { x.OnlineCustomerId }).FirstOrDefaultAsync(cancellationToken);
            await InvalidateCacheAsync(@event.TenantId!.Value, @event.OrderId, doc?.OnlineCustomerId);

            _logger.LogInformation(
                "[Order] {Action} projected successfully | TenantId: {TenantId} | OrderId: {OrderId} | {From} → {To}",
                "STATUS_CHANGED",
                @event.TenantId,
                @event.OrderId,
                @event.OldStatus,
                @event.NewStatus);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Order] Failed to project {Action} | TenantId: {TenantId} | OrderId: {OrderId}",
                "STATUS_CHANGED",
                @event.TenantId,
                @event.OrderId);
            throw;
        }
    }

    #endregion

    #region OrderPaymentStatusChangedEvent

    public async Task Consume(ConsumeContext<OrderPaymentStatusChangedEvent> context)
    {
        var @event = context.Message;
        var cancellationToken = context.CancellationToken;

        if (!ValidateTenantId(@event.TenantId, @event.OrderId, nameof(OrderPaymentStatusChangedEvent)))
            return;

        _logger.LogInformation(
            "[Order] Processing {Action} | TenantId: {TenantId} | OrderId: {OrderId} | {From} → {To} | TraceId: {TraceId}",
            "PAYMENT_CHANGED",
            @event.TenantId,
            @event.OrderId,
            @event.OldStatus,
            @event.NewStatus,
            context.MessageId);

        try
        {
            var paymentAuthority = await OrderPaymentLineageAuthority.LoadCurrentVisibilityAsync(
                _dbContext,
                @event.TenantId!.Value,
                @event.OrderId,
                cancellationToken);

            if (paymentAuthority == null)
            {
                _logger.LogWarning(
                    "[Order] Ignored payment projection replay without current SQL order payment lineage | TenantId: {TenantId} | OrderId: {OrderId} | EventId: {EventId}",
                    @event.TenantId,
                    @event.OrderId,
                    @event.EventId);
                await DeletePaymentProjectionWithoutSqlAuthorityAsync(
                    @event.TenantId.Value,
                    @event.OrderId,
                    cancellationToken);
                return;
            }

            var eventMatchesCurrentPaymentTruth = paymentAuthority.PaymentStatus == @event.NewStatus;
            if (!eventMatchesCurrentPaymentTruth)
            {
                _logger.LogWarning(
                    "[Order] Collapsed replayed payment facet payload against current SQL lineage | TenantId: {TenantId} | OrderId: {OrderId} | ReplayedPaymentStatus: {ReplayedPaymentStatus} | SqlPaymentStatus: {SqlPaymentStatus}",
                    @event.TenantId,
                    @event.OrderId,
                    @event.NewStatus,
                    paymentAuthority.PaymentStatus);
            }

            var filter = BuildFreshFacetEventFilter(
                @event.TenantId!.Value,
                @event.OrderId,
                @event.OccurredAtUtc,
                @event.EventVersion,
                @event.EventId,
                nameof(OrderDocument.LastPaymentEventVersion),
                nameof(OrderDocument.LastPaymentEventOccurredAtUtc));

            var description = eventMatchesCurrentPaymentTruth && !string.IsNullOrEmpty(@event.Description)
                ? @event.Description 
                : paymentAuthority.PaymentStatus.ToString() switch
                {
                    "Paid" => $"Payment received{(@event.AmountPaid.HasValue ? $" ({@event.AmountPaid.Value:C})" : "")}",
                    "Failed" => "Payment failed",
                    "Refunded" => "Payment refunded",
                    "PartiallyPaid" => $"Partial payment received{(@event.AmountPaid.HasValue ? $" ({@event.AmountPaid.Value:C})" : "")}",
                    _ => $"Payment status changed to {getStatusLabel(paymentAuthority.PaymentStatus.ToString())}"
                };

            var timelineEntry = new OrderTimelineItem
            {
                At = @event.OccurredAtUtc,
                Type = "PaymentChanged",
                From = @event.OldStatus.ToString(),
                To = paymentAuthority.PaymentStatus.ToString(),
                Description = description
            };

            UpdateDefinition<OrderDocument> update = Builders<OrderDocument>.Update
                .Set(x => x.PaymentStatus, paymentAuthority.PaymentStatus.ToString())
                .Set(x => x.UpdatedAt, DateTime.UtcNow)
                .Set(x => x.LastEventId, @event.EventId)
                .Max(x => x.LastEventOccurredAtUtc, @event.OccurredAtUtc)
                .Max(x => x.LastEventVersion, @event.EventVersion)
                .Set(x => x.LastPaymentEventId, @event.EventId)
                .Set(x => x.LastPaymentEventOccurredAtUtc, @event.OccurredAtUtc)
                .Set(x => x.LastPaymentEventVersion, @event.EventVersion);

            if (eventMatchesCurrentPaymentTruth)
            {
                update = Builders<OrderDocument>.Update.Combine(
                    update,
                    Builders<OrderDocument>.Update.Push(x => x.Timeline, timelineEntry));
            }

            var result = await _collection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);

            if (result.MatchedCount == 0)
            {
                await HandleNoOrderProjectionMutationMatchAsync(
                    @event.TenantId!.Value,
                    @event.OrderId,
                    @event.EventId,
                    @event.OccurredAtUtc,
                    "PAYMENT_CHANGED",
                    cancellationToken);
                return;
            }

            var doc = await _collection.Find(BuildOrderIdentityFilter(@event.TenantId!.Value, @event.OrderId)).Project(x => new { x.OnlineCustomerId }).FirstOrDefaultAsync(cancellationToken);
            await InvalidateCacheAsync(@event.TenantId!.Value, @event.OrderId, doc?.OnlineCustomerId);

            _logger.LogInformation(
                "[Order] {Action} projected successfully | TenantId: {TenantId} | OrderId: {OrderId} | {From} → {To}",
                "PAYMENT_CHANGED",
                @event.TenantId,
                @event.OrderId,
                @event.OldStatus,
                paymentAuthority.PaymentStatus);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Order] Failed to project {Action} | TenantId: {TenantId} | OrderId: {OrderId}",
                "PAYMENT_CHANGED",
                @event.TenantId,
                @event.OrderId);
            throw;
        }
    }

    #endregion

    #region ShipmentStatusChangedEvent

    public async Task Consume(ConsumeContext<ShipmentStatusChangedEvent> context)
    {
        var @event = context.Message;
        var cancellationToken = context.CancellationToken;

        if (!ValidateTenantId(@event.TenantId, @event.OrderId, nameof(ShipmentStatusChangedEvent)))
            return;

        _logger.LogInformation(
            "[Order] Processing {Action} | TenantId: {TenantId} | OrderId: {OrderId} | {From} → {To} | TraceId: {TraceId}",
            "SHIPMENT_CHANGED",
            @event.TenantId,
            @event.OrderId,
            @event.PreviousStatus,
            @event.NewStatus,
            context.MessageId);

        try
        {
            var shipmentAuthority = await OrderFulfillmentLineageAuthority.LoadCurrentShipmentAsync(
                _dbContext,
                @event.TenantId!.Value,
                @event.OrderId,
                @event.ShipmentId,
                cancellationToken);

            if (shipmentAuthority == null)
            {
                _logger.LogWarning(
                    "[Order] Ignored shipment projection replay without current SQL order/shipment lineage | TenantId: {TenantId} | OrderId: {OrderId} | ShipmentId: {ShipmentId} | EventId: {EventId}",
                    @event.TenantId,
                    @event.OrderId,
                    @event.ShipmentId,
                    @event.EventId);
                await TryRehydrateOrderProjectionAsync(
                    @event.TenantId.Value,
                    @event.OrderId,
                    @event.EventId,
                    @event.OccurredAtUtc,
                    cancellationToken);
                return;
            }

            if (shipmentAuthority.ShipmentStatus != @event.NewStatus)
            {
                _logger.LogWarning(
                    "[Order] Collapsed replayed shipment facet payload against current SQL lineage | TenantId: {TenantId} | OrderId: {OrderId} | ShipmentId: {ShipmentId} | ReplayedShipmentStatus: {ReplayedShipmentStatus} | SqlShipmentStatus: {SqlShipmentStatus}",
                    @event.TenantId,
                    @event.OrderId,
                    @event.ShipmentId,
                    @event.NewStatus,
                    shipmentAuthority.ShipmentStatus);
            }

            var shippingStatus = OrderFulfillmentLineageAuthority.ToProjectionShippingStatus(
                shipmentAuthority.ShipmentStatus);
            var filter = BuildFreshFacetEventFilter(
                @event.TenantId!.Value,
                @event.OrderId,
                @event.OccurredAtUtc,
                @event.EventVersion,
                @event.EventId,
                nameof(OrderDocument.LastShipmentEventVersion),
                nameof(OrderDocument.LastShipmentEventOccurredAtUtc));

            var timelineEntry = new OrderTimelineItem
            {
                At = @event.OccurredAtUtc,
                Type = "ShipmentChanged",
                From = @event.PreviousStatus.ToString(),
                To = shipmentAuthority.ShipmentStatus.ToString(),
                Description = $"Shipment status: {getStatusLabel(shippingStatus)}"
            };

            var updateDef = Builders<OrderDocument>.Update
                .Set(x => x.ShippingStatus, shippingStatus)
                .Set(x => x.UpdatedAt, DateTime.UtcNow)
                .Set(x => x.LastEventId, @event.EventId)
                .Max(x => x.LastEventOccurredAtUtc, @event.OccurredAtUtc)
                .Max(x => x.LastEventVersion, @event.EventVersion)
                .Set(x => x.LastShipmentEventId, @event.EventId)
                .Set(x => x.LastShipmentEventOccurredAtUtc, @event.OccurredAtUtc)
                .Set(x => x.LastShipmentEventVersion, @event.EventVersion)
                .Push(x => x.Timeline, timelineEntry);

            // Update tracking info if available
            if (!string.IsNullOrEmpty(shipmentAuthority.TrackingNumber))
            {
                updateDef = updateDef.Set(x => x.TrackingNumber, shipmentAuthority.TrackingNumber);
            }
            if (!string.IsNullOrEmpty(shipmentAuthority.CarrierCode))
            {
                updateDef = updateDef.Set(x => x.CarrierName, shipmentAuthority.CarrierCode);
            }

            var result = await _collection.UpdateOneAsync(filter, updateDef, cancellationToken: cancellationToken);

            if (result.MatchedCount == 0)
            {
                await HandleNoOrderProjectionMutationMatchAsync(
                    @event.TenantId!.Value,
                    @event.OrderId,
                    @event.EventId,
                    @event.OccurredAtUtc,
                    "SHIPMENT_CHANGED",
                    cancellationToken);
                return;
            }

            var doc = await _collection.Find(BuildOrderIdentityFilter(@event.TenantId!.Value, @event.OrderId)).Project(x => new { x.OnlineCustomerId }).FirstOrDefaultAsync(cancellationToken);
            await InvalidateCacheAsync(@event.TenantId!.Value, @event.OrderId, doc?.OnlineCustomerId);

            _logger.LogInformation(
                "[Order] {Action} projected successfully | TenantId: {TenantId} | OrderId: {OrderId} | {From} → {To}",
                "SHIPMENT_CHANGED",
                @event.TenantId,
                @event.OrderId,
                @event.PreviousStatus,
                @event.NewStatus);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Order] Failed to project {Action} | TenantId: {TenantId} | OrderId: {OrderId}",
                "SHIPMENT_CHANGED",
                @event.TenantId,
                @event.OrderId);
            throw;
        }
    }

    #endregion

    #region OrderShippedEvent

    public async Task Consume(ConsumeContext<OrderShippedEvent> context)
    {
        var @event = context.Message;
        var cancellationToken = context.CancellationToken;

        if (!ValidateTenantId(@event.TenantId, @event.OrderId, nameof(OrderShippedEvent)))
            return;

        _logger.LogInformation(
            "[Order] Processing {Action} | TenantId: {TenantId} | OrderId: {OrderId} | Tracking: {TrackingNumber} | TraceId: {TraceId}",
            "ORDER_SHIPPED",
            @event.TenantId,
            @event.OrderId,
            @event.TrackingNumber,
            context.MessageId);

        try
        {
            var filter = BuildFreshFacetEventFilter(
                @event.TenantId!.Value,
                @event.OrderId,
                @event.OccurredAtUtc,
                @event.EventVersion,
                @event.EventId,
                nameof(OrderDocument.LastShipmentEventVersion),
                nameof(OrderDocument.LastShipmentEventOccurredAtUtc));

            var timelineEntry = new OrderTimelineItem
            {
                At = @event.ShippedAt,
                Type = "Shipped",
                From = "",
                To = "Shipped",
                Description = !string.IsNullOrEmpty(@event.TrackingNumber) 
                    ? $"Shipped via {@event.Carrier ?? "carrier"} - Tracking: {@event.TrackingNumber}"
                    : "Order shipped"
            };

            var update = Builders<OrderDocument>.Update
                .Set(x => x.ShippingStatus, "Shipped")
                .Set(x => x.TrackingNumber, @event.TrackingNumber)
                .Set(x => x.CarrierName, @event.Carrier)
                .Set(x => x.UpdatedAt, DateTime.UtcNow)
                .Set(x => x.LastEventId, @event.EventId)
                .Max(x => x.LastEventOccurredAtUtc, @event.OccurredAtUtc)
                .Max(x => x.LastEventVersion, @event.EventVersion)
                .Set(x => x.LastShipmentEventId, @event.EventId)
                .Set(x => x.LastShipmentEventOccurredAtUtc, @event.OccurredAtUtc)
                .Set(x => x.LastShipmentEventVersion, @event.EventVersion)
                .Push(x => x.Timeline, timelineEntry);

            var result = await _collection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);

            if (result.MatchedCount == 0)
            {
                await HandleNoOrderProjectionMutationMatchAsync(
                    @event.TenantId!.Value,
                    @event.OrderId,
                    @event.EventId,
                    @event.OccurredAtUtc,
                    "ORDER_SHIPPED",
                    cancellationToken);
                return;
            }

            var doc = await _collection.Find(BuildOrderIdentityFilter(@event.TenantId!.Value, @event.OrderId)).Project(x => new { x.OnlineCustomerId }).FirstOrDefaultAsync(cancellationToken);
            await InvalidateCacheAsync(@event.TenantId!.Value, @event.OrderId, doc?.OnlineCustomerId);

            _logger.LogInformation(
                "[Order] {Action} projected successfully | TenantId: {TenantId} | OrderId: {OrderId}",
                "ORDER_SHIPPED",
                @event.TenantId,
                @event.OrderId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Order] Failed to project {Action} | TenantId: {TenantId} | OrderId: {OrderId}",
                "ORDER_SHIPPED",
                @event.TenantId,
                @event.OrderId);
            throw;
        }
    }

    #endregion

    #region OrderDeliveredEvent

    public async Task Consume(ConsumeContext<OrderDeliveredEvent> context)
    {
        var @event = context.Message;
        var cancellationToken = context.CancellationToken;

        if (!ValidateTenantId(@event.TenantId, @event.OrderId, nameof(OrderDeliveredEvent)))
            return;

        _logger.LogInformation(
            "[Order] Processing {Action} | TenantId: {TenantId} | OrderId: {OrderId} | TraceId: {TraceId}",
            "ORDER_DELIVERED",
            @event.TenantId,
            @event.OrderId,
            context.MessageId);

        try
        {
            // FACET GUARD: This event updates both status and shipment facets, so we must
            // validate freshness for both to prevent regression of either facet.
            var statusFacetFilter = BuildFreshFacetEventFilter(
                @event.TenantId!.Value,
                @event.OrderId,
                @event.OccurredAtUtc,
                @event.EventVersion,
                @event.EventId,
                nameof(OrderDocument.LastStatusEventVersion),
                nameof(OrderDocument.LastStatusEventOccurredAtUtc));

            var shipmentFacetFilter = BuildFreshFacetEventFilter(
                @event.TenantId!.Value,
                @event.OrderId,
                @event.OccurredAtUtc,
                @event.EventVersion,
                @event.EventId,
                nameof(OrderDocument.LastShipmentEventVersion),
                nameof(OrderDocument.LastShipmentEventOccurredAtUtc));

            var filter = Builders<OrderDocument>.Filter.And(statusFacetFilter, shipmentFacetFilter);

            var timelineEntry = new OrderTimelineItem
            {
                At = @event.DeliveredAt,
                Type = "Delivered",
                From = "",
                To = "Delivered",
                Description = "Order delivered"
            };

            var update = Builders<OrderDocument>.Update
                .Set(x => x.Status, "Delivered")
                .Set(x => x.ShippingStatus, "Delivered")
                .Set(x => x.DeliveredAt, @event.DeliveredAt)
                .Set(x => x.UpdatedAt, DateTime.UtcNow)
                .Set(x => x.LastEventId, @event.EventId)
                .Max(x => x.LastEventOccurredAtUtc, @event.OccurredAtUtc)
                .Max(x => x.LastEventVersion, @event.EventVersion)
                .Set(x => x.LastStatusEventId, @event.EventId)
                .Set(x => x.LastStatusEventOccurredAtUtc, @event.OccurredAtUtc)
                .Set(x => x.LastStatusEventVersion, @event.EventVersion)
                .Set(x => x.LastShipmentEventId, @event.EventId)
                .Set(x => x.LastShipmentEventOccurredAtUtc, @event.OccurredAtUtc)
                .Set(x => x.LastShipmentEventVersion, @event.EventVersion)
                .Push(x => x.Timeline, timelineEntry);

            var result = await _collection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);

            if (result.MatchedCount == 0)
            {
                await HandleNoOrderProjectionMutationMatchAsync(
                    @event.TenantId!.Value,
                    @event.OrderId,
                    @event.EventId,
                    @event.OccurredAtUtc,
                    "ORDER_DELIVERED",
                    cancellationToken);
                return;
            }

            var doc = await _collection.Find(BuildOrderIdentityFilter(@event.TenantId!.Value, @event.OrderId)).Project(x => new { x.OnlineCustomerId }).FirstOrDefaultAsync(cancellationToken);
            await InvalidateCacheAsync(@event.TenantId!.Value, @event.OrderId, doc?.OnlineCustomerId);

            _logger.LogInformation(
                "[Order] {Action} projected successfully | TenantId: {TenantId} | OrderId: {OrderId}",
                "ORDER_DELIVERED",
                @event.TenantId,
                @event.OrderId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Order] Failed to project {Action} | TenantId: {TenantId} | OrderId: {OrderId}",
                "ORDER_DELIVERED",
                @event.TenantId,
                @event.OrderId);
            throw;
        }
    }

    #endregion

    #region OrderCancelledEvent

    public async Task Consume(ConsumeContext<OrderCancelledEvent> context)
    {
        var @event = context.Message;
        var cancellationToken = context.CancellationToken;

        if (!ValidateTenantId(@event.TenantId, @event.OrderId, nameof(OrderCancelledEvent)))
            return;

        _logger.LogInformation(
            "[Order] Processing {Action} | TenantId: {TenantId} | OrderId: {OrderId} | Reason: {Reason} | TraceId: {TraceId}",
            "ORDER_CANCELLED",
            @event.TenantId,
            @event.OrderId,
            @event.CancellationReason,
            context.MessageId);

        try
        {
            var filter = BuildFreshFacetEventFilter(
                @event.TenantId!.Value,
                @event.OrderId,
                @event.OccurredAtUtc,
                @event.EventVersion,
                @event.EventId,
                nameof(OrderDocument.LastStatusEventVersion),
                nameof(OrderDocument.LastStatusEventOccurredAtUtc));

            var timelineEntry = new OrderTimelineItem
            {
                At = @event.CancelledAt,
                Type = "Cancelled",
                From = "",
                To = "Cancelled",
                Description = !string.IsNullOrEmpty(@event.CancellationReason) 
                    ? $"Order cancelled: {@event.CancellationReason}"
                    : "Order cancelled"
            };

            var update = Builders<OrderDocument>.Update
                .Set(x => x.Status, "Cancelled")
                .Set(x => x.CancellationReason, @event.CancellationReason)
                .Set(x => x.UpdatedAt, DateTime.UtcNow)
                .Set(x => x.LastEventId, @event.EventId)
                .Max(x => x.LastEventOccurredAtUtc, @event.OccurredAtUtc)
                .Max(x => x.LastEventVersion, @event.EventVersion)
                .Set(x => x.LastStatusEventId, @event.EventId)
                .Set(x => x.LastStatusEventOccurredAtUtc, @event.OccurredAtUtc)
                .Set(x => x.LastStatusEventVersion, @event.EventVersion)
                .Push(x => x.Timeline, timelineEntry);

            var result = await _collection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);

            if (result.MatchedCount == 0)
            {
                await HandleNoOrderProjectionMutationMatchAsync(
                    @event.TenantId!.Value,
                    @event.OrderId,
                    @event.EventId,
                    @event.OccurredAtUtc,
                    "ORDER_CANCELLED",
                    cancellationToken);
                return;
            }

            var doc = await _collection.Find(BuildOrderIdentityFilter(@event.TenantId!.Value, @event.OrderId)).Project(x => new { x.OnlineCustomerId }).FirstOrDefaultAsync(cancellationToken);
            await InvalidateCacheAsync(@event.TenantId!.Value, @event.OrderId, doc?.OnlineCustomerId);

            _logger.LogInformation(
                "[Order] {Action} projected successfully | TenantId: {TenantId} | OrderId: {OrderId}",
                "ORDER_CANCELLED",
                @event.TenantId,
                @event.OrderId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Order] Failed to project {Action} | TenantId: {TenantId} | OrderId: {OrderId}",
                "ORDER_CANCELLED",
                @event.TenantId,
                @event.OrderId);
            throw;
        }
    }

    #endregion

    #region Fulfillment Events

    public Task Consume(ConsumeContext<FulfillmentConfirmedEvent> context) =>
        ConsumeFulfillmentEventAsync(context.Message, nameof(FulfillmentConfirmedEvent), context.CancellationToken);

    public Task Consume(ConsumeContext<OrderPackedEvent> context) =>
        ConsumeFulfillmentEventAsync(context.Message, nameof(OrderPackedEvent), context.CancellationToken);

    public Task Consume(ConsumeContext<OrderOutForDeliveryEvent> context) =>
        ConsumeFulfillmentEventAsync(context.Message, nameof(OrderOutForDeliveryEvent), context.CancellationToken);

    public Task Consume(ConsumeContext<FulfillmentDeliveredEvent> context) =>
        ConsumeFulfillmentEventAsync(context.Message, nameof(FulfillmentDeliveredEvent), context.CancellationToken);

    public Task Consume(ConsumeContext<OrderCompletedEvent> context) =>
        ConsumeFulfillmentEventAsync(context.Message, nameof(OrderCompletedEvent), context.CancellationToken);

    public Task Consume(ConsumeContext<OrderReturnedEvent> context) =>
        ConsumeFulfillmentEventAsync(context.Message, nameof(OrderReturnedEvent), context.CancellationToken);

    public Task Consume(ConsumeContext<OrderDeliveryFailedEvent> context) =>
        ConsumeFulfillmentEventAsync(context.Message, nameof(OrderDeliveryFailedEvent), context.CancellationToken);

    public Task Consume(ConsumeContext<OrderFulfillmentCancelledEvent> context) =>
        ConsumeFulfillmentEventAsync(context.Message, nameof(OrderFulfillmentCancelledEvent), context.CancellationToken);

    private async Task ConsumeFulfillmentEventAsync(
        OrderFulfillmentEventBase @event,
        string eventName,
        CancellationToken cancellationToken)
    {
        if (!ValidateTenantId(@event.TenantId, @event.OrderId, eventName))
        {
            return;
        }

        var rehydrated = await TryRehydrateOrderProjectionAsync(
            @event.TenantId!.Value,
            @event.OrderId,
            @event.EventId,
            @event.OccurredAtUtc,
            cancellationToken);

        if (!rehydrated)
        {
            throw new InvalidOperationException(
                $"Order projection could not apply {eventName} because SQL source order {@event.OrderId} for tenant {@event.TenantId} was not found.");
        }

        _logger.LogInformation(
            "[Order] Fulfillment visibility synchronized from SQL authority | EventType: {EventType} | TenantId: {TenantId} | OrderId: {OrderId} | FulfillmentStatus: {FulfillmentStatus}",
            eventName,
            @event.TenantId,
            @event.OrderId,
            @event.NewStatus);
    }

    #endregion

    // ═══════════════════════════════════════════════════════════════════
    // AF-35: AFFILIATE ORDER ATTRIBUTION PROJECTION
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Builds a filter that accepts only "fresh" events that haven't been processed yet.
    /// Uses composite comparison (EventVersion, OccurredAtUtc) to reject stale
    /// and same-tick out-of-order lifecycle events.
    /// 
    /// Accepts event if:
    /// 1. No previous event exists (first event for this order)
    /// 2. Event carries a strictly newer aggregate mutation version
    /// 3. Event has the same version but a newer timestamp
    /// 
    /// Rejects event if:
    /// - Event version is older
    /// - Event version equals last event and timestamp is not newer (duplicate/replay/stale same-tick event)
    /// - Event version is non-positive (malformed event)
    /// 
    /// CRITICAL: eventVersion must be positive. Non-positive values indicate malformed events
    /// and are rejected to prevent version downgrade attacks where eventVersion=0 could
    /// match documents at version 1 but then persist 0, allowing future stale events through.
    /// </summary>
    private FilterDefinition<OrderDocument> BuildFreshEventFilter(
        Guid tenantId,
        Guid orderId,
        DateTime occurredAtUtc,
        int eventVersion,
        Guid eventId)
    {
        // SECURITY: Reject non-positive eventVersion early to prevent version downgrade
        if (eventVersion <= 0)
        {
            _logger.LogError(
                "[Order] REJECTED malformed event with non-positive EventVersion | TenantId: {TenantId} | OrderId: {OrderId} | EventId: {EventId} | EventVersion: {EventVersion}",
                tenantId, orderId, eventId, eventVersion);
            throw new InvalidOperationException(
                $"Event {eventId} has invalid EventVersion {eventVersion}. EventVersion must be positive.");
        }

        var sourceTimestamp = occurredAtUtc;
        var filter = Builders<OrderDocument>.Filter;
        return filter.And(
            BuildOrderIdentityFilter(tenantId, orderId),
            filter.Or(
                // Case 1: Event version is strictly newer (primary ordering)
                filter.Lt(x => x.LastEventVersion, eventVersion),
                // Case 2: Same version but newer timestamp (tie-breaker)
                filter.And(
                    filter.Eq(x => x.LastEventVersion, eventVersion),
                    filter.Lt(x => x.LastEventOccurredAtUtc, sourceTimestamp))));
    }

    private FilterDefinition<OrderDocument> BuildFreshFacetEventFilter(
        Guid tenantId,
        Guid orderId,
        DateTime occurredAtUtc,
        int eventVersion,
        Guid eventId,
        string eventVersionField,
        string eventOccurredAtField)
    {
        if (eventVersion <= 0)
        {
            _logger.LogError(
                "[Order] REJECTED malformed facet event with non-positive EventVersion | TenantId: {TenantId} | OrderId: {OrderId} | EventId: {EventId} | EventVersion: {EventVersion} | VersionField: {VersionField}",
                tenantId, orderId, eventId, eventVersion, eventVersionField);
            throw new InvalidOperationException(
                $"Event {eventId} has invalid EventVersion {eventVersion}. EventVersion must be positive.");
        }

        var filter = Builders<OrderDocument>.Filter;
        var versionField = new StringFieldDefinition<OrderDocument, int>(eventVersionField);
        var occurredAtField = new StringFieldDefinition<OrderDocument, DateTime?>(eventOccurredAtField);
        DateTime? sourceTimestamp = occurredAtUtc;

        return filter.And(
            BuildOrderIdentityFilter(tenantId, orderId),
            filter.Or(
                filter.Exists(eventVersionField, false),
                filter.Eq(versionField, 0),
                filter.Lt(versionField, eventVersion),
                filter.And(
                    filter.Eq(versionField, eventVersion),
                    filter.Or(
                        filter.Exists(eventOccurredAtField, false),
                        filter.Lt(occurredAtField, sourceTimestamp)))));
    }

    private static FilterDefinition<OrderDocument> BuildOrderIdentityFilter(Guid tenantId, Guid orderId)
    {
        var filter = Builders<OrderDocument>.Filter;
        return filter.And(
            filter.Eq(x => x.TenantId, tenantId),
            filter.Eq(x => x.OrderId, orderId));
    }

    /// <summary>
    /// Builds a freshness filter for affiliate commission events using event-version authority.
    /// Same-version writes are accepted only when the event id is identical, preventing a
    /// second same-version commission event from overwriting money fields.
    /// </summary>
    private static FilterDefinition<OrderDocument> BuildFreshAffiliateCommissionEventFilter(
        Guid tenantId,
        Guid orderId,
        Guid eventId,
        int eventVersion)
    {
        var filter = Builders<OrderDocument>.Filter;
        return filter.And(
            filter.Eq(x => x.TenantId, tenantId),
            filter.Eq(x => x.OrderId, orderId),
            filter.Or(
                // Case 1: No version tracked yet (first commission event)
                filter.Exists(x => x.LastAffiliateCommissionEventVersion, false),
                filter.Eq(x => x.LastAffiliateCommissionEventVersion, 0),
                // Case 2: Event version is strictly newer (primary ordering)
                filter.Lt(x => x.LastAffiliateCommissionEventVersion, eventVersion)),
            // Ensure this specific event hasn't been processed yet
            filter.Ne(x => x.LastAffiliateCommissionEventId, eventId));
    }

    private async Task HandleNoOrderProjectionMutationMatchAsync(
        Guid tenantId,
        Guid orderId,
        Guid eventId,
        DateTime occurredAtUtc,
        string action,
        CancellationToken cancellationToken)
    {
        var existing = await _collection
            .Find(BuildOrderIdentityFilter(tenantId, orderId))
            .Project(x => new { x.OnlineCustomerId, x.LastEventId, x.LastEventOccurredAtUtc })
            .FirstOrDefaultAsync(cancellationToken);

        if (existing != null)
        {
            _logger.LogInformation(
                "[Order] {Action} projection rejected stale or duplicate event | TenantId: {TenantId} | OrderId: {OrderId} | EventId: {EventId} | EventOccurredAtUtc: {EventOccurredAtUtc} | LastEventId: {LastEventId} | LastEventOccurredAtUtc: {LastEventOccurredAtUtc}",
                action,
                tenantId,
                orderId,
                eventId,
                occurredAtUtc,
                existing.LastEventId,
                existing.LastEventOccurredAtUtc);
            return;
        }

        var rehydrated = await TryRehydrateOrderProjectionAsync(
            tenantId,
            orderId,
            eventId,
            occurredAtUtc,
            cancellationToken);

        if (rehydrated)
        {
            _logger.LogWarning(
                "[Order] {Action} rebuilt missing projection from SQL source of truth | TenantId: {TenantId} | OrderId: {OrderId} | EventId: {EventId}",
                action,
                tenantId,
                orderId,
                eventId);
            return;
        }

        throw new InvalidOperationException(
            $"Order projection could not apply {action} because SQL source order {orderId} for tenant {tenantId} was not found.");
    }

    private async Task<bool> TryRehydrateOrderProjectionAsync(
        Guid tenantId,
        Guid orderId,
        Guid triggeringEventId,
        DateTime triggeringEventOccurredAtUtc,
        CancellationToken cancellationToken)
    {
        var order = await _dbContext.Orders
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(item => item.Customer)
            .Include(item => item.OnlineCustomer)
            .Include(item => item.Items)
            .FirstOrDefaultAsync(
                item => item.TenantId == tenantId &&
                        item.Id == orderId &&
                        !item.IsDeleted,
                cancellationToken);

        if (order == null)
        {
            return false;
        }

        var sourceTimestamp = StorefrontOrderProjectionBuilder.ResolveSourceTimestamp(order, triggeringEventOccurredAtUtc);
        var sourceVersion = order.ProjectionMutationVersion;
        var authoritySnapshot = await StorefrontOrderProjectionAuthoritySnapshotLoader.LoadAsync(
            _dbContext,
            tenantId,
            orderId,
            cancellationToken);
        
        // Clamp sourceVersion to int.MaxValue to align with how LastEventVersion is stored
        var clampedSourceVersion = sourceVersion > int.MaxValue ? int.MaxValue : (int)sourceVersion;
        
        var document = StorefrontOrderProjectionBuilder.Build(
            order, 
            triggeringEventId, 
            sourceTimestamp,
            sourceVersion,
            authoritySnapshot: authoritySnapshot);
        var filter = Builders<OrderDocument>.Filter;
        var freshnessFilter = filter.And(
            BuildOrderIdentityFilter(tenantId, orderId),
            filter.Or(
                // Legacy documents without LastEventVersion (treat as older)
                filter.Exists(x => x.LastEventVersion, false),
                // Event version is strictly greater (primary ordering)
                filter.Lt(x => x.LastEventVersion, clampedSourceVersion),
                // Event version equals, use timestamp as tie-breaker (secondary ordering)
                filter.And(
                    filter.Eq(x => x.LastEventVersion, clampedSourceVersion),
                    filter.Lt(x => x.LastEventOccurredAtUtc, sourceTimestamp))));

        try
        {
            var result = await _collection.ReplaceOneAsync(
                freshnessFilter,
                document,
                new ReplaceOptions { IsUpsert = true },
                cancellationToken);

            // DELIBERATE NO-OP SUCCESS CASE:
            // When MatchedCount == 0 && UpsertedId == null, it means the freshnessFilter
            // (which checks LastEventOccurredAtUtc < sourceTimestamp) prevented both the
            // update and the upsert. This occurs when the existing document in the database
            // already has fresher data (newer timestamp) than the incoming document.
            // This is a SUCCESS scenario - we correctly preserved the newer state and
            // rejected the stale event. Return true to acknowledge successful processing.
            if (result.MatchedCount == 0 && result.UpsertedId == null)
            {
                return true;
            }
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            _logger.LogInformation(
                "[Order] Rehydrate duplicate key treated as idempotent | TenantId: {TenantId} | OrderId: {OrderId}",
                tenantId,
                orderId);
        }

        await InvalidateCacheAsync(tenantId, orderId, document.OnlineCustomerId);
        return true;
    }

    private async Task DeletePaymentProjectionWithoutSqlAuthorityAsync(
        Guid tenantId,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var existing = await _collection
            .Find(BuildOrderIdentityFilter(tenantId, orderId))
            .Project(document => new { document.OnlineCustomerId })
            .FirstOrDefaultAsync(cancellationToken);

        if (existing == null)
        {
            return;
        }

        var result = await _collection.DeleteOneAsync(
            BuildOrderIdentityFilter(tenantId, orderId),
            cancellationToken);

        if (result.DeletedCount == 0)
        {
            return;
        }

        OperationalConvergenceTelemetry.RecordReplayDecision(
            OperationalConvergencePhase.Projection,
            "storefront_order_payment_projection",
            OperationalConvergenceAuthority.OrderPayment,
            OperationalConvergenceDecision.Deleted,
            OperationalConvergenceReason.MissingSqlAuthority,
            tenantId);

        await InvalidateCacheAsync(tenantId, orderId, existing.OnlineCustomerId);
        _logger.LogWarning(
            "[Order] Deleted stale payment projection without current SQL order payment lineage | TenantId: {TenantId} | OrderId: {OrderId}",
            tenantId,
            orderId);
    }

    #region AffiliateAttributedEvent

    /// <summary>
    /// AF-35: Marks the OrderDocument as an affiliate order.
    /// Called when AffiliateAttribution is recorded.
    ///
    /// Idempotency: UpdateOne with $set is safe to replay.
    /// No SQL join — all data comes from the event.
    /// </summary>
    public async Task Consume(ConsumeContext<AffiliateAttributedEvent> context)
    {
        var @event = context.Message;
        var ct = context.CancellationToken;

        var storeTenantId = @event.StoreTenantId != Guid.Empty
            ? @event.StoreTenantId
            : @event.TenantId ?? Guid.Empty;

        if (!@event.TenantId.HasValue || @event.TenantId.Value == Guid.Empty || storeTenantId == Guid.Empty)
        {
            _logger.LogWarning("[AF-35] AffiliateAttributedEvent missing TenantId | OrderId: {OrderId}", @event.OrderId);
            throw new InvalidOperationException(
                $"AffiliateAttributedEvent missing TenantId. OrderId={@event.OrderId}; EventId={@event.EventId}");
        }

        if (@event.TenantId.Value != storeTenantId)
        {
            _logger.LogError(
                "[AF-35] AffiliateAttributedEvent rejected because TenantId does not match StoreTenantId | TenantId: {TenantId} | StoreTenantId: {StoreTenantId} | OrderId: {OrderId} | EventId: {EventId}",
                @event.TenantId.Value, storeTenantId, @event.OrderId, @event.EventId);
            throw new InvalidOperationException(
                $"AffiliateAttributedEvent TenantId does not match StoreTenantId. TenantId={@event.TenantId.Value}; StoreTenantId={storeTenantId}; OrderId={@event.OrderId}; EventId={@event.EventId}");
        }

        if (@event.EventVersion <= 0)
        {
            _logger.LogError(
                "[AF-35] AffiliateAttributedEvent rejected due to invalid EventVersion | OrderId: {OrderId} | EventId: {EventId} | EventVersion: {EventVersion}",
                @event.OrderId, @event.EventId, @event.EventVersion);
            throw new InvalidOperationException(
                $"AffiliateAttributedEvent has invalid EventVersion. OrderId={@event.OrderId}; EventId={@event.EventId}; EventVersion={@event.EventVersion}");
        }

        _logger.LogInformation(
            "[AF-35] Projecting affiliate attribution | OrderId: {OrderId} | AffiliateId: {AffiliateId} | ReferralCode: {Code}",
            @event.OrderId, @event.AffiliateAccountId, @event.ReferralCode);

        try
        {
            var filter = BuildFreshFacetEventFilter(
                storeTenantId,
                @event.OrderId,
                @event.OccurredAtUtc,
                @event.EventVersion,
                @event.EventId,
                nameof(OrderDocument.LastAffiliateAttributionEventVersion),
                nameof(OrderDocument.LastAffiliateAttributionEventOccurredAtUtc));

            var update = Builders<OrderDocument>.Update
                .Set(x => x.IsAffiliateOrder, true)
                .Set(x => x.AffiliateId, @event.AffiliateAccountId)
                .Set(x => x.AffiliateReferralCode, @event.ReferralCode)
                .Set(x => x.AttributedAtUtc, @event.AttributedAtUtc)
                .Set(x => x.UpdatedAt, DateTime.UtcNow)
                .Set(x => x.LastEventId, @event.EventId)
                .Max(x => x.LastEventVersion, @event.EventVersion)
                .Max(x => x.LastEventOccurredAtUtc, @event.OccurredAtUtc)
                .Set(x => x.LastAffiliateAttributionEventId, @event.EventId)
                .Set(x => x.LastAffiliateAttributionEventVersion, @event.EventVersion)
                .Set(x => x.LastAffiliateAttributionEventOccurredAtUtc, @event.OccurredAtUtc)
                .Push(x => x.Timeline, new OrderTimelineItem
                {
                    At = @event.AttributedAtUtc,
                    Type = "AffiliateAttributed",
                    From = "",
                    To = "Attributed",
                    Description = $"Order attributed to affiliate (ref: {@event.ReferralCode})"
                });

            var result = await _collection.UpdateOneAsync(filter, update, cancellationToken: ct);

            if (result.MatchedCount == 0)
            {
                await HandleNoOrderProjectionMutationMatchAsync(
                    storeTenantId,
                    @event.OrderId,
                    @event.EventId,
                    @event.OccurredAtUtc,
                    "AFFILIATE_ATTRIBUTED",
                    ct);
                return;
            }

            _logger.LogInformation(
                "[AF-35] Affiliate attribution projected | OrderId: {OrderId} | AffiliateId: {AffiliateId}",
                @event.OrderId, @event.AffiliateAccountId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AF-35] Failed to project AffiliateAttributedEvent | OrderId: {OrderId}", @event.OrderId);
            throw;
        }
    }

    #endregion

    #region AffiliateCommissionEarnedEvent

    /// <summary>
    /// AF-35: Updates the OrderDocument with the total commission earned.
    /// Called when AffiliateCommissionEarnedEvent is processed.
    ///
    /// Idempotent: $set overwrites with same value on replay.
    /// </summary>
    public async Task Consume(ConsumeContext<AffiliateCommissionEarnedEvent> context)
    {
        var @event = context.Message;
        var ct = context.CancellationToken;

        var tenantId = @event.StoreTenantId;
        if (tenantId == Guid.Empty)
        {
            _logger.LogWarning("[AF-35] AffiliateCommissionEarnedEvent missing StoreTenantId | OrderId: {OrderId}", @event.OrderId);
            throw new InvalidOperationException(
                $"AffiliateCommissionEarnedEvent missing StoreTenantId. OrderId={@event.OrderId}; EventId={@event.EventId}");
        }

        if (@event.EventVersion <= 0)
        {
            _logger.LogError(
                "[AF-35] AffiliateCommissionEarnedEvent rejected due to invalid EventVersion | OrderId: {OrderId} | EventId: {EventId} | EventVersion: {EventVersion}",
                @event.OrderId, @event.EventId, @event.EventVersion);
            throw new InvalidOperationException(
                $"AffiliateCommissionEarnedEvent has invalid EventVersion. OrderId={@event.OrderId}; EventId={@event.EventId}; EventVersion={@event.EventVersion}");
        }

        _logger.LogInformation(
            "[AF-35] Projecting commission earned | OrderId: {OrderId} | Commission: {Amount}",
            @event.OrderId, @event.TotalCommission);

        try
        {
            var filter = BuildFreshAffiliateCommissionEventFilter(
                tenantId,
                @event.OrderId,
                @event.EventId,
                @event.EventVersion);

            var update = Builders<OrderDocument>.Update
                .Set(x => x.AffiliateCommissionAmount, @event.TotalCommission)
                .Set(x => x.IsAffiliateOrder, true) // Belt-and-suspenders guard
                .Set(x => x.UpdatedAt, DateTime.UtcNow)
                .Set(x => x.LastEventId, @event.EventId)
                .Set(x => x.LastAffiliateCommissionEventId, @event.EventId)
                .Set(x => x.LastAffiliateCommissionEventVersion, @event.EventVersion)
                .Set(x => x.LastAffiliateCommissionEventOccurredAtUtc, @event.OccurredAtUtc);

            var result = await _collection.UpdateOneAsync(filter, update, cancellationToken: ct);

            if (result.MatchedCount == 0)
            {
                await HandleNoOrderProjectionMutationMatchAsync(
                    tenantId,
                    @event.OrderId,
                    @event.EventId,
                    @event.OccurredAtUtc,
                    "AFFILIATE_COMMISSION_EARNED",
                    ct);
                return;
            }

            _logger.LogInformation(
                "[AF-35] Commission amount projected | OrderId: {OrderId} | Amount: {Amount}",
                @event.OrderId, @event.TotalCommission);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AF-35] Failed to project AffiliateCommissionEarnedEvent | OrderId: {OrderId}", @event.OrderId);
            throw;
        }
    }

    #endregion

    #region AffiliateCommissionReversedEvent

    public async Task Consume(ConsumeContext<AffiliateCommissionReversedEvent> context)
    {
        var @event = context.Message;
        var ct = context.CancellationToken;

        var tenantId = @event.StoreTenantId;
        if (tenantId == Guid.Empty)
        {
            _logger.LogWarning("[AF-35] AffiliateCommissionReversedEvent missing StoreTenantId | OrderId: {OrderId}", @event.OrderId);
            throw new InvalidOperationException(
                $"AffiliateCommissionReversedEvent missing StoreTenantId. OrderId={@event.OrderId}; EventId={@event.EventId}");
        }

        if (@event.EventVersion <= 0)
        {
            _logger.LogError(
                "[AF-35] AffiliateCommissionReversedEvent rejected due to invalid EventVersion | OrderId: {OrderId} | EventId: {EventId} | EventVersion: {EventVersion}",
                @event.OrderId, @event.EventId, @event.EventVersion);
            throw new InvalidOperationException(
                $"AffiliateCommissionReversedEvent has invalid EventVersion. OrderId={@event.OrderId}; EventId={@event.EventId}; EventVersion={@event.EventVersion}");
        }

        _logger.LogInformation(
            "[AF-35] Projecting commission reversal | OrderId: {OrderId} | ReversedAmount: {Amount}",
            @event.OrderId, @event.ReversedAmount);

        try
        {
            // Check if document exists first
            var exists = await _collection
                .Find(BuildOrderIdentityFilter(tenantId, @event.OrderId))
                .Project(document => document.OrderId)
                .FirstOrDefaultAsync(ct);

            if (exists == Guid.Empty)
            {
                await HandleNoOrderProjectionMutationMatchAsync(
                    tenantId,
                    @event.OrderId,
                    @event.EventId,
                    @event.OccurredAtUtc,
                    "AFFILIATE_COMMISSION_REVERSED",
                    ct);
                return;
            }

            var filter = BuildFreshAffiliateCommissionEventFilter(
                tenantId,
                @event.OrderId,
                @event.EventId,
                @event.EventVersion);

            // Use aggregation pipeline for atomic decrement with clamping to >= 0
            // This ensures the operation is atomic and idempotent
            var updatePipeline = PipelineDefinition<OrderDocument, OrderDocument>.Create(
                new[]
                {
                    new BsonDocument("$set", new BsonDocument
                    {
                        { "AffiliateCommissionAmount", new BsonDocument("$let", new BsonDocument
                            {
                                { "vars", new BsonDocument
                                    {
                                        { "currentAmount", new BsonDocument("$ifNull", new BsonArray { "$AffiliateCommissionAmount", 0 }) },
                                        { "reversedAmount", new BsonDecimal128(@event.ReversedAmount) }
                                    }
                                },
                                { "in", new BsonDocument("$cond", new BsonDocument
                                    {
                                        { "if", new BsonDocument("$lte", new BsonArray
                                            {
                                                new BsonDocument("$subtract", new BsonArray { "$$currentAmount", "$$reversedAmount" }),
                                                0
                                            })
                                        },
                                        { "then", 0 },
                                        { "else", new BsonDocument("$subtract", new BsonArray { "$$currentAmount", "$$reversedAmount" }) }
                                    })
                                }
                            })
                        },
                        { "IsAffiliateOrder", true },
                        { "UpdatedAt", DateTime.UtcNow },
                        // OrderDocument persists nullable event IDs as canonical strings while
                        // its compatibility serializer can still read historical UUID binaries.
                        { "LastEventId", @event.EventId.ToString("D") },
                        { "LastAffiliateCommissionEventId", @event.EventId.ToString("D") },
                        { "LastAffiliateCommissionEventVersion", @event.EventVersion },
                        { "LastAffiliateCommissionEventOccurredAtUtc", @event.OccurredAtUtc }
                    })
                });

            var result = await _collection.UpdateOneAsync(filter, updatePipeline, cancellationToken: ct);

            if (result.MatchedCount == 0)
            {
                await HandleNoOrderProjectionMutationMatchAsync(
                    tenantId,
                    @event.OrderId,
                    @event.EventId,
                    @event.OccurredAtUtc,
                    "AFFILIATE_COMMISSION_REVERSED",
                    ct);
                return;
            }

            _logger.LogInformation(
                "[AF-35] Commission reversal projected | OrderId: {OrderId} | ReversedAmount: {Amount}",
                @event.OrderId, @event.ReversedAmount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AF-35] Failed to project AffiliateCommissionReversedEvent | OrderId: {OrderId}", @event.OrderId);
            throw;
        }
    }

    #endregion

    #region Helper Methods

    private bool ValidateTenantId(Guid? tenantId, Guid orderId, string eventName)
    {
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            _logger.LogError(
                "[Order] SECURITY: {EventName} REJECTED - Missing or empty TenantId | OrderId: {OrderId}",
                eventName, orderId);
            return false;
        }
        return true;
    }

    private async Task InvalidateCacheAsync(Guid tenantId, Guid orderId, Guid? customerId)
    {
        try
        {
            var db = _redis.GetDatabase();
            var orderCacheKey = string.Format(CacheKeyPrefix, tenantId, orderId);
            await db.KeyDeleteAsync(orderCacheKey);

            if (customerId.HasValue)
            {
                var customerCacheKey = string.Format(CustomerOrdersCacheKeyPrefix, tenantId, customerId);
                await db.KeyDeleteAsync(customerCacheKey);
            }

            _logger.LogDebug(
                "[Order] Cache invalidated | TenantId: {TenantId} | OrderId: {OrderId}",
                tenantId, orderId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[Order] Cache invalidation failed (non-critical) | TenantId: {TenantId} | OrderId: {OrderId}",
                tenantId, orderId);
        }
    }

    private static string getStatusLabel(string status)
    {
        return status switch
        {
            "Pending" => "Pending",
            "Processing" => "Processing",
            "Confirmed" => "Confirmed",
            "Shipped" => "Shipped",
            "Delivered" => "Delivered",
            "Cancelled" => "Cancelled",
            "Paid" => "Paid",
            "Failed" => "Failed",
            "Refunded" => "Refunded",
            "PartiallyPaid" => "Partially Paid",
            _ => status
        };
    }

    #endregion
}

/// <summary>
/// Consumer definition for OrderProjectionConsumer.
/// 
/// TOPOLOGY GOVERNANCE: This class is the SINGLE SOURCE OF TRUTH for queue configuration.
/// 
/// ARCHITECTURE: MassTransit Pub/Sub with Automatic Exchange Bindings
/// 
/// MassTransit automatically creates RabbitMQ exchange bindings when using ConfigureEndpoints
/// because the OrderProjectionConsumer implements IConsumer&lt;T&gt; for each event type.
/// 
/// When events are published via _publishEndpoint.Publish():
/// 1. Event goes to the message type exchange (e.g., "PointOfSale.Core.Events:OrderCreatedEvent")
/// 2. MassTransit creates a binding from that exchange to "order-projection-v3" queue
/// 3. OrderProjectionConsumer receives the event
/// 
/// This enables:
/// - Multiple consumers can subscribe to the same events (analytics, notifications, etc.)
/// - Loose coupling between producers and consumers
/// - True event-driven architecture
/// - Automatic binding creation for ALL IConsumer&lt;T&gt; interfaces
/// 
/// VERSIONING STRATEGY:
/// - v1: Initial implementation (6 events)
/// - v2: Added OrderCancelledEvent (topology drift issue discovered)
/// - v3: Fixed topology drift - removed manual ReceiveEndpoint override
/// 
/// WHEN TO INCREMENT VERSION:
/// ✅ Adding new IConsumer&lt;T&gt; interface → Increment version
/// ✅ Removing IConsumer&lt;T&gt; interface → Increment version
/// ✅ Breaking event schema changes → Increment version
/// ❌ Updating consumer logic only → Keep same version
/// 
/// CRITICAL RULES:
/// 1. Never manually override ReceiveEndpoint in Program.cs for this consumer
/// 2. This EndpointName is the authoritative queue name
/// 3. MassTransit auto-creates bindings - trust the framework
/// 4. Producers must use Publish(), NOT Send()
/// </summary>
// OrderProjectionConsumerDefinition moved to separate file: OrderProjectionConsumerDefinition.cs

