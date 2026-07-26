using MassTransit;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Infrastructure.Consumers;

/// <summary>
/// Consumer for syncing OrderStatus from Shipment and Payment events.
/// 
/// PHASE 2: EVENT-DRIVEN ORDER STATUS SYNC
/// 
/// This consumer listens to:
/// - ShipmentStatusChangedEvent → Updates order status based on shipment lifecycle
/// - ShipmentCreatedEvent → Sets order to Processing
/// - ShipmentDeliveredEvent → Sets order to Delivered
/// - ShipmentCancelledEvent → Sets order to Cancelled
/// - ShipmentReturnedEvent → Sets order to Returned
/// - OrderPaymentStatusChangedEvent → Confirms pending orders when paid
/// 
/// ARCHITECTURAL RULES:
/// - Orders REACT to events, they don't control them
/// - One-way sync only (events → orders)
/// - All updates logged for audit
/// - Idempotent processing
/// </summary>
public class OrderStatusSyncConsumer :
    IConsumer<ShipmentStatusChangedEvent>,
    IConsumer<ShipmentCreatedEvent>,
    IConsumer<ShipmentDeliveredEvent>,
    IConsumer<ShipmentCancelledEvent>,
    IConsumer<ShipmentReturnedEvent>,
    IConsumer<OrderPaymentStatusChangedEvent>
{
    private readonly IOrderStatusSyncService _orderStatusSyncService;
    private readonly ILogger<OrderStatusSyncConsumer> _logger;

    public OrderStatusSyncConsumer(
        IOrderStatusSyncService orderStatusSyncService,
        ILogger<OrderStatusSyncConsumer> logger)
    {
        _orderStatusSyncService = orderStatusSyncService;
        _logger = logger;
    }

    #region ShipmentStatusChangedEvent

    /// <summary>
    /// Handle generic shipment status change events.
    /// This is the main handler for shipment lifecycle updates.
    /// </summary>
    public async Task Consume(ConsumeContext<ShipmentStatusChangedEvent> context)
    {
        var @event = context.Message;
        var cancellationToken = context.CancellationToken;

        if (!ValidateTenantId(@event.TenantId, @event.OrderId, nameof(ShipmentStatusChangedEvent)))
            return;

        _logger.LogInformation(
            "[OrderStatusSync] Received ShipmentStatusChangedEvent | OrderId: {OrderId} | ShipmentId: {ShipmentId} | {OldStatus} → {NewStatus}",
            @event.OrderId, @event.ShipmentId, @event.PreviousStatus, @event.NewStatus);

        var result = await _orderStatusSyncService.SyncFromShipmentAsync(
            @event.TenantId!.Value,
            @event.OrderId,
            @event.ShipmentId,
            @event.NewStatus,
            source: $"ShipmentStatusChangedEvent ({@event.PreviousStatus} → {@event.NewStatus})",
            cancellationToken);

        LogResult(@event.OrderId, @event.ShipmentId, result);
    }

    #endregion

    #region ShipmentCreatedEvent

    /// <summary>
    /// Handle shipment created events.
    /// Sets order to Processing status.
    /// </summary>
    public async Task Consume(ConsumeContext<ShipmentCreatedEvent> context)
    {
        var @event = context.Message;
        var cancellationToken = context.CancellationToken;

        if (!ValidateTenantId(@event.TenantId, @event.OrderId, nameof(ShipmentCreatedEvent)))
            return;

        _logger.LogInformation(
            "[OrderStatusSync] Received ShipmentCreatedEvent | OrderId: {OrderId} | ShipmentId: {ShipmentId}",
            @event.OrderId, @event.ShipmentId);

        // Newly created shipments typically start as ReadyForPickup
        var result = await _orderStatusSyncService.SyncFromShipmentAsync(
            @event.TenantId!.Value,
            @event.OrderId,
            @event.ShipmentId,
            ShipmentStatus.ReadyForPickup,
            source: "ShipmentCreatedEvent",
            cancellationToken);

        LogResult(@event.OrderId, @event.ShipmentId, result);
    }

    #endregion

    #region ShipmentDeliveredEvent

    /// <summary>
    /// Handle shipment delivered events.
    /// Sets order to Delivered status.
    /// </summary>
    public async Task Consume(ConsumeContext<ShipmentDeliveredEvent> context)
    {
        var @event = context.Message;
        var cancellationToken = context.CancellationToken;

        if (!ValidateTenantId(@event.TenantId, @event.OrderId, nameof(ShipmentDeliveredEvent)))
            return;

        _logger.LogInformation(
            "[OrderStatusSync] Received ShipmentDeliveredEvent | OrderId: {OrderId} | ShipmentId: {ShipmentId}",
            @event.OrderId, @event.ShipmentId);

        var result = await _orderStatusSyncService.SyncFromShipmentAsync(
            @event.TenantId!.Value,
            @event.OrderId,
            @event.ShipmentId,
            ShipmentStatus.Delivered,
            source: "ShipmentDeliveredEvent",
            cancellationToken);

        LogResult(@event.OrderId, @event.ShipmentId, result);
    }

    #endregion

    #region ShipmentCancelledEvent

    /// <summary>
    /// Handle shipment cancelled events.
    /// Sets order to Cancelled status.
    /// </summary>
    public async Task Consume(ConsumeContext<ShipmentCancelledEvent> context)
    {
        var @event = context.Message;
        var cancellationToken = context.CancellationToken;

        if (!ValidateTenantId(@event.TenantId, @event.OrderId, nameof(ShipmentCancelledEvent)))
            return;

        _logger.LogInformation(
            "[OrderStatusSync] Received ShipmentCancelledEvent | OrderId: {OrderId} | ShipmentId: {ShipmentId} | Reason: {Reason}",
            @event.OrderId, @event.ShipmentId, @event.CancellationReason);

        var result = await _orderStatusSyncService.SyncFromShipmentAsync(
            @event.TenantId!.Value,
            @event.OrderId,
            @event.ShipmentId,
            ShipmentStatus.Cancelled,
            source: $"ShipmentCancelledEvent (Reason: {@event.CancellationReason})",
            cancellationToken);

        LogResult(@event.OrderId, @event.ShipmentId, result);
    }

    #endregion

    #region ShipmentReturnedEvent

    /// <summary>
    /// Handle shipment returned events.
    /// Sets order to Returned status.
    /// </summary>
    public async Task Consume(ConsumeContext<ShipmentReturnedEvent> context)
    {
        var @event = context.Message;
        var cancellationToken = context.CancellationToken;

        if (!ValidateTenantId(@event.TenantId, @event.OrderId, nameof(ShipmentReturnedEvent)))
            return;

        _logger.LogInformation(
            "[OrderStatusSync] Received ShipmentReturnedEvent | OrderId: {OrderId} | ShipmentId: {ShipmentId} | Reason: {Reason}",
            @event.OrderId, @event.ShipmentId, @event.ReturnReason);

        var result = await _orderStatusSyncService.SyncFromShipmentAsync(
            @event.TenantId!.Value,
            @event.OrderId,
            @event.ShipmentId,
            ShipmentStatus.Returned,
            source: $"ShipmentReturnedEvent (Reason: {@event.ReturnReason})",
            cancellationToken);

        LogResult(@event.OrderId, @event.ShipmentId, result);
    }

    #endregion

    #region OrderPaymentStatusChangedEvent

    /// <summary>
    /// Handle payment status change events.
    /// If order is Pending and payment is confirmed → order becomes Confirmed.
    /// </summary>
    public async Task Consume(ConsumeContext<OrderPaymentStatusChangedEvent> context)
    {
        var @event = context.Message;
        var cancellationToken = context.CancellationToken;

        if (!ValidateTenantId(@event.TenantId, @event.OrderId, nameof(OrderPaymentStatusChangedEvent)))
            return;

        _logger.LogInformation(
            "[OrderStatusSync] Received OrderPaymentStatusChangedEvent | OrderId: {OrderId} | {OldStatus} → {NewStatus}",
            @event.OrderId, @event.OldStatus, @event.NewStatus);

        var result = await _orderStatusSyncService.SyncFromPaymentAsync(
            @event.TenantId!.Value,
            @event.OrderId,
            @event.NewStatus,
            source: $"OrderPaymentStatusChangedEvent ({@event.OldStatus} → {@event.NewStatus})",
            cancellationToken);

        _logger.LogInformation(
            "[OrderStatusSync] Payment sync result | OrderId: {OrderId} | Success: {Success} | StatusChanged: {Changed} | Message: {Message}",
            @event.OrderId, result.Success, result.StatusChanged, result.Message);
    }

    #endregion

    #region Helper Methods

    private bool ValidateTenantId(Guid? tenantId, Guid orderId, string eventName)
    {
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            _logger.LogError(
                "[OrderStatusSync] SECURITY: {EventName} REJECTED - Missing or empty TenantId | OrderId: {OrderId}",
                eventName, orderId);
            return false;
        }
        return true;
    }

    private void LogResult(Guid orderId, Guid shipmentId, OrderStatusSyncResult result)
    {
        if (result.StatusChanged)
        {
            _logger.LogInformation(
                "[OrderStatusSync] Order status synced | OrderId: {OrderId} | ShipmentId: {ShipmentId} | {OldStatus} → {NewStatus}",
                orderId, shipmentId, result.OldStatus, result.NewStatus);
        }
        else
        {
            _logger.LogDebug(
                "[OrderStatusSync] No status change | OrderId: {OrderId} | ShipmentId: {ShipmentId} | Message: {Message}",
                orderId, shipmentId, result.Message);
        }
    }

    #endregion
}

/// <summary>
/// Consumer definition for OrderStatusSyncConsumer.
/// 
/// Creates a dedicated queue "order-status-sync" that subscribes to:
/// - ShipmentStatusChangedEvent
/// - ShipmentCreatedEvent
/// - ShipmentDeliveredEvent
/// - ShipmentCancelledEvent  
/// - ShipmentReturnedEvent
/// - OrderPaymentStatusChangedEvent
/// </summary>
public class OrderStatusSyncConsumerDefinition : ConsumerDefinition<OrderStatusSyncConsumer>
{
    public OrderStatusSyncConsumerDefinition()
    {
        EndpointName = "order-status-sync";
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<OrderStatusSyncConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        // Configure retry policy for transient failures
        endpointConfigurator.UseMessageRetry(r => r.Intervals(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(15)));
    }
}
