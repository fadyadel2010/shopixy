using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Payments;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.API.Services;

/// <summary>
/// Implementation of OrderStatusSyncService.
/// 
/// PHASE 2: EVENT-DRIVEN ORDER STATUS SYNC
/// 
/// This service reacts to Shipment and Payment events to update Order status.
/// Orders do NOT control reality - they REFLECT reality.
/// 
/// SHIPMENT STATUS → ORDER STATUS MAPPING:
/// - Pending/ReadyForPickup → Processing
/// - PickedUp/InTransit/OutForDelivery → Shipped
/// - Delivered → Delivered
/// - Cancelled → Cancelled
/// - Returned → Returned
/// - Failed → (no change - order stays at current status)
/// 
/// PAYMENT STATUS → ORDER STATUS MAPPING:
/// - Paid (when order is Pending) → Confirmed
///
/// CONCURRENCY SAFETY (CRITICAL-2 FIX):
/// Both public methods now load the order row with a database-level exclusive lock
/// (FOR UPDATE on PostgreSQL, UPDLOCK+ROWLOCK+HOLDLOCK on SQL Server) inside an
/// explicit transaction.  This prevents concurrent shipment or payment webhooks from
/// racing through the load → validate → save cycle and bypassing state-machine guards.
/// 
/// PRODUCTION HARDENING - DEADLOCK PREVENTION:
/// This service uses EF Core execution strategy to automatically retry transient SQL
/// deadlock/serialization exceptions with exponential backoff.
/// 
/// SHARED LOCK-ORDER CONTRACT (with SaleFinancialIntegrityService):
/// 1. Orders table (locked first via LoadOrderForStatusMutationAsync)
/// 2. JournalEntries table (locked in SaleFinancialIntegrityService)
/// 3. AccountingOutboxMessages table (locked in SaleFinancialIntegrityService)
/// 4. Related entities (Payments, Shipments, etc.)
/// 
/// Both services MUST acquire locks in this order to prevent cyclical waits and deadlocks.
/// </summary>
public class OrderStatusSyncService : IOrderStatusSyncService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<OrderStatusSyncService> _logger;

    public OrderStatusSyncService(
        ApplicationDbContext dbContext,
        IEventPublisher eventPublisher,
        ILogger<OrderStatusSyncService> logger)
    {
        _dbContext = dbContext;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OrderStatusSyncResult> SyncFromShipmentAsync(
        Guid tenantId,
        Guid orderId,
        Guid shipmentId,
        ShipmentStatus newShipmentStatus,
        string source,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[OrderStatusSync] Processing shipment sync | TenantId: {TenantId} | OrderId: {OrderId} | ShipmentId: {ShipmentId} | ShipmentStatus: {ShipmentStatus} | Source: {Source}",
            tenantId, orderId, shipmentId, newShipmentStatus, source);

        try
        {
            var strategy = _dbContext.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
                try
                {
                    // CRITICAL-2 FIX: Lock the order row before validating and mutating status.
                    // This eliminates the race window between concurrent shipment webhooks.
                    // SECURITY FIX: Include tenantId in the lock query to prevent cross-tenant access.
                    var order = await LoadOrderForStatusMutationAsync(tenantId, orderId, cancellationToken);

                    if (order == null)
                    {
                        _logger.LogWarning(
                            "[OrderStatusSync] Order not found | TenantId: {TenantId} | OrderId: {OrderId}",
                            tenantId, orderId);
                        await transaction.RollbackAsync(cancellationToken);
                        return OrderStatusSyncResult.Failed($"Order {orderId} not found for tenant {tenantId}");
                    }

                    var oldStatus = order.Status;
                    var shipmentAuthority = await OrderFulfillmentLineageAuthority.LoadCurrentShipmentAsync(
                        _dbContext,
                        tenantId,
                        orderId,
                        shipmentId,
                        cancellationToken);

                    if (shipmentAuthority == null)
                    {
                        _logger.LogWarning(
                            "[OrderStatusSync] Ignored shipment sync without current SQL order/shipment lineage | TenantId: {TenantId} | OrderId: {OrderId} | ShipmentId: {ShipmentId} | ReplayedShipmentStatus: {ShipmentStatus} | Source: {Source}",
                            tenantId,
                            orderId,
                            shipmentId,
                            newShipmentStatus,
                            source);
                        await transaction.RollbackAsync(cancellationToken);
                        return OrderStatusSyncResult.NoChange(
                            oldStatus,
                            $"Shipment {shipmentId} is no longer current SQL lineage for order {orderId}.");
                    }

                    var authoritativeShipmentStatus = shipmentAuthority.ShipmentStatus;
                    if (authoritativeShipmentStatus != newShipmentStatus)
                    {
                        _logger.LogWarning(
                            "[OrderStatusSync] Collapsed replayed shipment status against current SQL lineage | TenantId: {TenantId} | OrderId: {OrderId} | ShipmentId: {ShipmentId} | ReplayedShipmentStatus: {ReplayedShipmentStatus} | SqlShipmentStatus: {SqlShipmentStatus} | Source: {Source}",
                            tenantId,
                            orderId,
                            shipmentId,
                            newShipmentStatus,
                            authoritativeShipmentStatus,
                            source);
                    }

                    var newOrderStatus = MapShipmentStatusToOrderStatus(authoritativeShipmentStatus, oldStatus);

                    if (!ShouldUpdateOrderStatus(oldStatus, newOrderStatus, authoritativeShipmentStatus))
                    {
                        _logger.LogDebug(
                            "[OrderStatusSync] No status change needed | OrderId: {OrderId} | CurrentStatus: {CurrentStatus} | ShipmentStatus: {ShipmentStatus}",
                            orderId, oldStatus, authoritativeShipmentStatus);
                        await transaction.RollbackAsync(cancellationToken);
                        return OrderStatusSyncResult.NoChange(oldStatus, $"Order already at {oldStatus}, no change from shipment {authoritativeShipmentStatus}");
                    }

                    if (IsTerminalStatus(oldStatus))
                    {
                        _logger.LogWarning(
                            "[OrderStatusSync] Cannot update terminal order status | OrderId: {OrderId} | CurrentStatus: {CurrentStatus} | AttemptedStatus: {AttemptedStatus}",
                            orderId, oldStatus, newOrderStatus);
                        await transaction.RollbackAsync(cancellationToken);
                        return OrderStatusSyncResult.NoChange(oldStatus, $"Cannot modify terminal order status {oldStatus}");
                    }

                    if (!IsValidStatusProgression(oldStatus, newOrderStatus))
                    {
                        _logger.LogWarning(
                            "[OrderStatusSync] Invalid status regression blocked | OrderId: {OrderId} | {OldStatus} → {NewStatus}",
                            orderId, oldStatus, newOrderStatus);
                        await transaction.RollbackAsync(cancellationToken);
                        return OrderStatusSyncResult.NoChange(oldStatus, $"Cannot regress from {oldStatus} to {newOrderStatus}");
                    }

                    if (oldStatus == OrderStatus.Pending && newOrderStatus != OrderStatus.Pending)
                    {
                        _logger.LogWarning(
                            "[OrderStatusSync] Blocked shipment-driven promotion of pending order | OrderId: {OrderId} | ShipmentStatus: {ShipmentStatus} | PaymentStatus: {PaymentStatus}",
                            orderId,
                            authoritativeShipmentStatus,
                            order.PaymentStatus);
                        await transaction.RollbackAsync(cancellationToken);
                        return OrderStatusSyncResult.NoChange(
                            oldStatus,
                            $"Pending orders cannot advance from shipment events before financial confirmation. Shipment status: {authoritativeShipmentStatus}");
                    }

                    if (OrderOperationalFinancialAuthority.IsOutboundOrderProgression(newOrderStatus))
                    {
                        var outboundFailureReason = OrderOperationalFinancialAuthority.GetOutboundProgressBlockReason(order);
                        if (outboundFailureReason != null)
                        {
                            _logger.LogCritical(
                                "[OrderStatusSync] Blocked shipment-driven operational promotion without financial authority | OrderId: {OrderId} | ShipmentId: {ShipmentId} | ShipmentStatus: {ShipmentStatus} | CurrentOrderStatus: {CurrentOrderStatus} | TargetOrderStatus: {TargetOrderStatus} | PaymentStatus: {PaymentStatus} | FinancialPostingStatus: {FinancialPostingStatus} | Reason: {Reason}",
                                orderId,
                                shipmentId,
                                authoritativeShipmentStatus,
                                oldStatus,
                                newOrderStatus,
                                order.PaymentStatus,
                                order.FinancialPostingStatus,
                                outboundFailureReason);
                            await transaction.RollbackAsync(cancellationToken);
                            return OrderStatusSyncResult.NoChange(
                                oldStatus,
                                $"{outboundFailureReason} Shipment status: {authoritativeShipmentStatus}");
                        }
                    }

                    using var orderStatusScope = OrderStatusSaveChangesInterceptor.BeginAuthorizedContext();
                    order.Status = newOrderStatus;
                    order.UpdatedAt = DateTime.UtcNow;
                    UpdateOrderTimestamps(order, newOrderStatus);

                    // RISK-3 FIX: Emit event BEFORE commit so it participates in the MassTransit EF outbox
                    await _eventPublisher.PublishAsync(new OrderStatusChangedEvent
                    {
                        TenantId = order.TenantId,
                        EventVersion = PointOfSale.Core.Helpers.ProductEventVersionHelper.ToProductEventVersion(order.ProjectionMutationVersion + 1),
                        OrderId = order.Id,
                        OrderNumber = order.OrderNumber,
                        OldStatus = oldStatus,
                        NewStatus = newOrderStatus,
                        Reason = $"Synced from SQL shipment authority {authoritativeShipmentStatus} (Source: {source})"
                    }, cancellationToken);

                    await _dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);

                    _logger.LogInformation(
                        "[OrderStatusSync] Order status updated | OrderId: {OrderId} | {OldStatus} → {NewStatus} | Source: {Source} | ShipmentId: {ShipmentId}",
                        orderId, oldStatus, newOrderStatus, source, shipmentId);

                    return OrderStatusSyncResult.Changed(oldStatus, newOrderStatus);
                }
                catch
                {
                    await transaction.RollbackAsync(cancellationToken);
                    throw;
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[OrderStatusSync] Failed to sync from shipment | OrderId: {OrderId} | ShipmentId: {ShipmentId}",
                orderId, shipmentId);
            return OrderStatusSyncResult.Failed(ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<OrderStatusSyncResult> SyncFromPaymentAsync(
        Guid tenantId,
        Guid orderId,
        PaymentStatus newPaymentStatus,
        string source,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[OrderStatusSync] Processing payment sync | TenantId: {TenantId} | OrderId: {OrderId} | PaymentStatus: {PaymentStatus} | Source: {Source}",
            tenantId, orderId, newPaymentStatus, source);

        try
        {
            var strategy = _dbContext.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
                try
                {
                    // CRITICAL-2 FIX: Lock the order row before mutating status.
                    // SECURITY FIX: Include tenantId in the lock query to prevent cross-tenant access.
                    var order = await LoadOrderForStatusMutationAsync(tenantId, orderId, cancellationToken);

                    if (order == null)
                    {
                        _logger.LogWarning(
                            "[OrderStatusSync] Order not found | TenantId: {TenantId} | OrderId: {OrderId}",
                            tenantId, orderId);
                        await transaction.RollbackAsync(cancellationToken);
                        return OrderStatusSyncResult.Failed($"Order {orderId} not found for tenant {tenantId}");
                    }

                    var oldStatus = order.Status;

                    if (oldStatus != OrderStatus.Pending)
                    {
                        _logger.LogDebug(
                            "[OrderStatusSync] Order not Pending, skipping payment sync | OrderId: {OrderId} | CurrentStatus: {CurrentStatus}",
                            orderId, oldStatus);
                        await transaction.RollbackAsync(cancellationToken);
                        return OrderStatusSyncResult.NoChange(oldStatus, $"Order is {oldStatus}, not Pending - payment sync skipped");
                    }

                    if (!PaymentStatusAuthority.IsPaidState(newPaymentStatus))
                    {
                        _logger.LogDebug(
                            "[OrderStatusSync] Payment not confirmed, skipping | OrderId: {OrderId} | PaymentStatus: {PaymentStatus}",
                            orderId, newPaymentStatus);
                        await transaction.RollbackAsync(cancellationToken);
                        return OrderStatusSyncResult.NoChange(oldStatus, $"Payment status is {newPaymentStatus}, not a canonical paid state");
                    }

                    var newStatus = OrderStatus.Confirmed;
                    using var orderStatusScope = OrderStatusSaveChangesInterceptor.BeginAuthorizedContext();
                    order.Status = newStatus;
                    order.UpdatedAt = DateTime.UtcNow;

                    // RISK-3 FIX: Emit event BEFORE commit so it participates in the MassTransit EF outbox
                    await _eventPublisher.PublishAsync(new OrderStatusChangedEvent
                    {
                        TenantId = order.TenantId,
                        EventVersion = PointOfSale.Core.Helpers.ProductEventVersionHelper.ToProductEventVersion(order.ProjectionMutationVersion + 1),
                        OrderId = order.Id,
                        OrderNumber = order.OrderNumber,
                        OldStatus = oldStatus,
                        NewStatus = newStatus,
                        Reason = $"Payment confirmed (Source: {source})"
                    }, cancellationToken);

                    await _dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);

                    _logger.LogInformation(
                        "[OrderStatusSync] Order status updated from payment | OrderId: {OrderId} | {OldStatus} → {NewStatus} | Source: {Source}",
                        orderId, oldStatus, newStatus, source);

                    return OrderStatusSyncResult.Changed(oldStatus, newStatus);
                }
                catch
                {
                    await transaction.RollbackAsync(cancellationToken);
                    throw;
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[OrderStatusSync] Failed to sync from payment | OrderId: {OrderId}",
                orderId);
            return OrderStatusSyncResult.Failed(ex.Message);
        }
    }

    #region Private Methods

    /// <summary>
    /// Loads an order with a database-level exclusive row lock, preventing concurrent
    /// shipment or payment webhooks from racing through the load → validate → save cycle.
    ///
    /// PostgreSQL : SELECT ... FOR UPDATE
    /// SQL Server : SELECT ... WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
    ///
    /// Must be called inside an active database transaction.
    /// 
    /// SECURITY: Filters by TenantId to prevent cross-tenant access.
    /// </summary>
    private async Task<Order?> LoadOrderForStatusMutationAsync(
        Guid tenantId,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var providerName = _dbContext.Database.ProviderName ?? string.Empty;

        if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            return await _dbContext.Orders
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM ""Orders""
                    WHERE ""Id"" = {orderId}
                      AND ""TenantId"" = {tenantId}
                      AND ""IsDeleted"" = FALSE
                    FOR UPDATE")
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("MicrosoftSqlServer", StringComparison.OrdinalIgnoreCase))
        {
            return await _dbContext.Orders
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM [Orders] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [Id] = {orderId}
                      AND [TenantId] = {tenantId}
                      AND [IsDeleted] = 0")
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(cancellationToken);
        }

        // Non-production provider (e.g. in-memory, SQLite) — plain query, no lock needed.
        return await _dbContext.Orders
            .FirstOrDefaultAsync(o => o.Id == orderId && o.TenantId == tenantId, cancellationToken);
    }

    /// <summary>
    /// Map ShipmentStatus to OrderStatus.
    /// 
    /// MANDATORY MAPPING RULES:
    /// - Pending/ReadyForPickup → Processing
    /// - PickedUp/InTransit/OutForDelivery → Shipped
    /// - Delivered → Delivered
    /// - Cancelled → Cancelled
    /// - Returned → Returned
    /// - Failed → NO CHANGE (return current status)
    /// </summary>
    private static OrderStatus MapShipmentStatusToOrderStatus(ShipmentStatus shipmentStatus, OrderStatus currentOrderStatus)
    {
        return shipmentStatus switch
        {
            ShipmentStatus.PendingExternalRegistration => OrderStatus.Processing,
            ShipmentStatus.Pending => OrderStatus.Processing,
            ShipmentStatus.ReadyForPickup => OrderStatus.Processing,

            ShipmentStatus.PickedUp => OrderStatus.Shipped,
            ShipmentStatus.InTransit => OrderStatus.Shipped,
            ShipmentStatus.OutForDelivery => OrderStatus.Shipped,

            ShipmentStatus.Delivered => OrderStatus.Delivered,

            ShipmentStatus.Cancelled => OrderStatus.Cancelled,

            ShipmentStatus.Returned => OrderStatus.Refunded, // Map to Refunded as per enum

            // CRITICAL: Failed shipment does NOT auto-cancel order
            // Leave order in current state for admin intervention
            ShipmentStatus.Failed => currentOrderStatus,

            _ => currentOrderStatus
        };
    }

    /// <summary>
    /// Check if we should update order status.
    /// - No change if new status equals old status
    /// - No change if shipment failed (handled separately)
    /// </summary>
    private static bool ShouldUpdateOrderStatus(OrderStatus oldStatus, OrderStatus newStatus, ShipmentStatus shipmentStatus)
    {
        if (oldStatus == newStatus)
            return false;

        if (shipmentStatus == ShipmentStatus.Failed)
            return false;

        return true;
    }

    /// <summary>
    /// Check if order status is terminal (cannot be changed).
    /// Terminal statuses: Cancelled, Refunded, Completed
    /// </summary>
    private static bool IsTerminalStatus(OrderStatus status)
    {
        return status is OrderStatus.Cancelled or OrderStatus.Refunded or OrderStatus.Completed;
    }

    /// <summary>
    /// Validate status progression is forward-only.
    /// 
    /// VALID FORWARD PROGRESSIONS:
    /// Pending → Confirmed → Processing → Shipped → Delivered
    /// Any status → Cancelled (via explicit action)
    /// Delivered → Refunded (via return flow)
    /// 
    /// INVALID (blocked):
    /// Shipped → Processing (regression)
    /// Delivered → Shipped (regression)
    /// </summary>
    private static bool IsValidStatusProgression(OrderStatus from, OrderStatus to)
    {
        // Terminal states can transition to terminal states
        if (to is OrderStatus.Cancelled or OrderStatus.Refunded)
            return true;

        // Define order lifecycle progression
        var progressionOrder = new Dictionary<OrderStatus, int>
        {
            { OrderStatus.Pending, 0 },
            { OrderStatus.Confirmed, 1 },
            { OrderStatus.Processing, 2 },
            { OrderStatus.Shipped, 3 },
            { OrderStatus.Delivered, 4 },
            { OrderStatus.Completed, 5 },
            { OrderStatus.OnHold, 2 }, // OnHold is at same level as Processing
        };

        if (!progressionOrder.TryGetValue(from, out var fromOrder) ||
            !progressionOrder.TryGetValue(to, out var toOrder))
        {
            return false;
        }

        // Only allow forward progression
        return toOrder >= fromOrder;
    }

    /// <summary>
    /// Update order timestamps based on new status.
    /// </summary>
    private static void UpdateOrderTimestamps(Order order, OrderStatus newStatus)
    {
        var now = DateTime.UtcNow;

        switch (newStatus)
        {
            case OrderStatus.Shipped:
                order.ShippedAt ??= now;
                break;
            case OrderStatus.Delivered:
                order.DeliveredAt ??= now;
                break;
            case OrderStatus.Cancelled:
                order.CancelledAt ??= now;
                break;
        }
    }

    #endregion
}
