using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.CommerceHub;

public sealed record CommerceHubInventoryReconciliationResult(
    string Status,
    int InvalidStockRowCount,
    int MissingDeliveryCommitCount,
    int MissingReturnReceiptCount,
    int DuplicateEventReplayCount,
    int OpenReservationsOnCompletedOrdersCount,
    int ExpiredReservationsReleasedCount,
    DateTime ExecutedAtUtc);

public sealed class CommerceHubInventoryReconciliationJob
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IInventoryConsistencyService _inventoryConsistencyService;
    private readonly ILogger<CommerceHubInventoryReconciliationJob> _logger;

    public CommerceHubInventoryReconciliationJob(
        ApplicationDbContext dbContext,
        IInventoryConsistencyService inventoryConsistencyService,
        ILogger<CommerceHubInventoryReconciliationJob> logger)
    {
        _dbContext = dbContext;
        _inventoryConsistencyService = inventoryConsistencyService;
        _logger = logger;
    }

    public async Task<CommerceHubInventoryReconciliationResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var nowUtc = DateTime.UtcNow;
        var hasFailures = false;

        // Repair missing delivery commits (isolated to prevent aborting entire reconciliation)
        int repairedMissingDeliveryCommits = 0;
        try
        {
            repairedMissingDeliveryCommits = await RepairMissingDeliveryCommitsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // CANCELLATION HANDLING: Don't swallow cancellation requests
            if (ex is OperationCanceledException)
                throw;
            
            hasFailures = true;
            _logger.LogError(ex,
                "Failed to repair missing delivery commits during inventory reconciliation. Continuing with other repairs.");
        }

        // Repair missing return receipts (isolated to prevent aborting entire reconciliation)
        int repairedMissingReturnReceipts = 0;
        try
        {
            repairedMissingReturnReceipts = await RepairMissingReturnReceiptsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // CANCELLATION HANDLING: Don't swallow cancellation requests
            if (ex is OperationCanceledException)
                throw;
            
            hasFailures = true;
            _logger.LogError(ex,
                "Failed to repair missing return receipts during inventory reconciliation. Continuing with other repairs.");
        }

        // Repair open reservations on completed orders (isolated to prevent aborting entire reconciliation)
        int repairedOpenCompletedReservations = 0;
        try
        {
            repairedOpenCompletedReservations = await RepairOpenReservationsOnCompletedOrdersAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // CANCELLATION HANDLING: Don't swallow cancellation requests
            if (ex is OperationCanceledException)
                throw;
            
            hasFailures = true;
            _logger.LogError(ex,
                "Failed to repair open reservations on completed orders during inventory reconciliation. Continuing with other repairs.");
        }

        var expiredReservations = await _dbContext.InventoryReservations
            .AsNoTracking()
            .Where(reservation =>
                !reservation.IsDeleted &&
                reservation.ExpiresAtUtc.HasValue &&
                reservation.ExpiresAtUtc.Value <= nowUtc &&
                reservation.ReservedQuantity > reservation.CommittedQuantity + reservation.ReleasedQuantity &&
                // The shared reservation table also holds Storefront orders.
                // Their payment/fulfillment-aware cleanup owns their expiry;
                // a CommerceHub worker must never release them by TTL alone.
                _dbContext.CommerceHubMarketplaceOrders.Any(order => order.Id == reservation.OrderId && !order.IsDeleted) &&
                !_dbContext.CommerceHubSubOrderItems.Any(item =>
                    !item.IsDeleted &&
                    !item.SubOrder.IsDeleted &&
                    item.Id == reservation.OrderItemId &&
                    item.Status != CommerceHubOrderItemStatus.Cancelled &&
                    item.SubOrder.Status != OrderStatus.Cancelled &&
                    item.SubOrder.Status != OrderStatus.Refunded &&
                    item.SubOrder.PaymentMethod == CommerceHubPaymentMethod.CashOnDelivery))
            .Select(reservation => new
            {
                reservation.Id,
                reservation.TenantId,
                OpenQuantity = reservation.ReservedQuantity - reservation.CommittedQuantity - reservation.ReleasedQuantity
            })
            .ToListAsync(cancellationToken);

        foreach (var reservation in expiredReservations)
        {
            await _inventoryConsistencyService.ReleaseReservationAsync(
                reservation.TenantId,
                reservation.Id,
                reservation.OpenQuantity,
                InventoryEventIdFactory.Create("inventory-reservation-expired", reservation.Id),
                correlationId: "commercehub-inventory-reconciliation",
                source: "CommerceHubInventoryReconciliationJob",
                cancellationToken);
        }

        var invalidStockRowCount = await _dbContext.ProductVariantStocks
            .AsNoTracking()
            .CountAsync(stock =>
                !stock.IsDeleted &&
                (stock.Quantity < 0m ||
                 stock.ReservedQuantity < 0m ||
                 stock.DamagedQuantity < 0m ||
                 stock.Quantity < stock.ReservedQuantity),
                cancellationToken);

        var missingDeliveryCommitCount = await _dbContext.CommerceHubSubOrderItems
            .AsNoTracking()
            .Where(item => !item.IsDeleted && item.Status == CommerceHubOrderItemStatus.Delivered)
            .CountAsync(item =>
                !_dbContext.StockMovements.Any(movement =>
                    movement.ReferenceLineId == item.Id &&
                    movement.InventoryEventType == InventoryEventType.StockDecreased),
                cancellationToken);

        var missingReturnReceiptCount = await _dbContext.CommerceHubReturnRequests
            .AsNoTracking()
            .Where(item => !item.IsDeleted && item.Status == CommerceHubReturnStatus.Confirmed)
            .CountAsync(item =>
                !_dbContext.StockMovements.Any(movement =>
                    movement.ReferenceId == item.Id &&
                    (movement.InventoryEventType == InventoryEventType.StockIncreased ||
                     movement.InventoryEventType == InventoryEventType.StockDamaged)),
                cancellationToken);

        var duplicateEventReplayCount = await _dbContext.InventoryEventInbox
            .AsNoTracking()
            .Where(inbox => inbox.ReplayCount > 0)
            .SumAsync(inbox => inbox.ReplayCount, cancellationToken);

        var openReservationsOnCompletedOrdersCount = await (
            from reservation in _dbContext.InventoryReservations.AsNoTracking()
            join item in _dbContext.CommerceHubSubOrderItems.AsNoTracking()
                on reservation.OrderItemId equals item.Id
            where !reservation.IsDeleted &&
                  !item.IsDeleted &&
                  reservation.ReservedQuantity > reservation.CommittedQuantity + reservation.ReleasedQuantity &&
                  (item.Status == CommerceHubOrderItemStatus.Delivered ||
                   item.Status == CommerceHubOrderItemStatus.Cancelled)
            select reservation.Id)
            .CountAsync(cancellationToken);

        var status =
            !hasFailures &&
            invalidStockRowCount == 0 &&
            missingDeliveryCommitCount == 0 &&
            missingReturnReceiptCount == 0 &&
            openReservationsOnCompletedOrdersCount == 0
                ? "OK"
                : "MISMATCH";

        var result = new CommerceHubInventoryReconciliationResult(
            Status: status,
            InvalidStockRowCount: invalidStockRowCount,
            MissingDeliveryCommitCount: missingDeliveryCommitCount,
            MissingReturnReceiptCount: missingReturnReceiptCount,
            DuplicateEventReplayCount: duplicateEventReplayCount,
            OpenReservationsOnCompletedOrdersCount: openReservationsOnCompletedOrdersCount,
            ExpiredReservationsReleasedCount: expiredReservations.Count,
            ExecutedAtUtc: nowUtc);

        var description =
            $"InvalidStockRows={result.InvalidStockRowCount} | MissingDeliveryCommit={result.MissingDeliveryCommitCount} | " +
            $"MissingReturnReceipt={result.MissingReturnReceiptCount} | DuplicateReplays={result.DuplicateEventReplayCount} | " +
            $"OpenReservationsOnCompletedOrders={result.OpenReservationsOnCompletedOrdersCount} | " +
            $"ExpiredReservationsReleased={result.ExpiredReservationsReleasedCount} | " +
            $"RepairedDeliveryCommits={repairedMissingDeliveryCommits} | RepairedReturnReceipts={repairedMissingReturnReceipts} | " +
            $"RepairedOpenCompletedReservations={repairedOpenCompletedReservations} | " +
            $"RepairFailures={hasFailures}";

        if (status == "OK")
        {
            _logger.LogInformation("CommerceHub inventory reconciliation OK | {Description}", description);
        }
        else
        {
            _logger.LogWarning("CommerceHub inventory reconciliation mismatch | {Description}", description);
        }

        return result;
    }

    private async Task<int> RepairMissingDeliveryCommitsAsync(CancellationToken cancellationToken)
    {
        var candidates = await (
            from item in _dbContext.CommerceHubSubOrderItems.AsNoTracking()
            join subOrder in _dbContext.CommerceHubSubOrders.AsNoTracking()
                on item.SubOrderId equals subOrder.Id
            where !item.IsDeleted &&
                  !subOrder.IsDeleted &&
                  item.Status == CommerceHubOrderItemStatus.Delivered &&
                  !_dbContext.StockMovements.Any(movement =>
                      movement.ReferenceLineId == item.Id &&
                      movement.InventoryEventType == InventoryEventType.StockDecreased)
            select new
            {
                subOrder.StoreId,
                subOrder.MarketplaceOrderId,
                SubOrderId = subOrder.Id,
                OrderItemId = item.Id,
                item.ProductId,
                item.VariantId,
                Quantity = (decimal)item.Quantity
            })
            .ToListAsync(cancellationToken);

        var repaired = 0;
        foreach (var candidate in candidates)
        {
            try
            {
                await _inventoryConsistencyService.CommitDeliveryAsync(
                    new InventoryDeliveryCommitRequest(
                        TenantId: candidate.StoreId,
                        OrderId: candidate.MarketplaceOrderId,
                        SubOrderId: candidate.SubOrderId,
                        OrderItemId: candidate.OrderItemId,
                        ProductId: candidate.ProductId,
                        VariantId: candidate.VariantId,
                        Quantity: candidate.Quantity,
                        EventId: InventoryEventIdFactory.Create("commercehub-reconcile-delivery-commit", candidate.OrderItemId),
                        CorrelationId: "commercehub-inventory-reconciliation",
                        Source: "CommerceHubInventoryReconciliationJob"),
                    cancellationToken);
                repaired++;
            }
            catch (Exception ex)
            {
                // CANCELLATION HANDLING: Don't swallow cancellation requests
                if (ex is OperationCanceledException)
                    throw;
                
                _logger.LogWarning(
                    ex,
                    "CommerceHub inventory reconciliation could not repair missing delivery commit | OrderItemId: {OrderItemId}",
                    candidate.OrderItemId);
            }
        }

        return repaired;
    }

    private async Task<int> RepairMissingReturnReceiptsAsync(CancellationToken cancellationToken)
    {
        var refundRequests = _dbContext.Set<PointOfSale.Core.Entities.CommerceHubRefundRequest>();
        var candidates = await (
            from returnRequest in _dbContext.CommerceHubReturnRequests.AsNoTracking()
            join refund in refundRequests.AsNoTracking()
                on returnRequest.RefundId equals refund.Id
            join item in _dbContext.CommerceHubSubOrderItems.AsNoTracking()
                on returnRequest.OrderItemId equals item.Id
            where !returnRequest.IsDeleted &&
                  !refund.IsDeleted &&
                  !item.IsDeleted &&
                  returnRequest.Status == CommerceHubReturnStatus.Confirmed &&
                  !_dbContext.StockMovements.Any(movement =>
                      movement.ReferenceId == returnRequest.Id &&
                      (movement.InventoryEventType == InventoryEventType.StockIncreased ||
                       movement.InventoryEventType == InventoryEventType.StockDamaged))
            select new
            {
                returnRequest.Id,
                returnRequest.VendorId,
                returnRequest.RefundId,
                refund.OrderId,
                returnRequest.OrderItemId,
                item.ProductId,
                item.VariantId,
                DeliveredQuantity = (decimal)item.Quantity,
                ResellableQuantity = (decimal)returnRequest.ReceivedResellableQuantity,
                DamagedQuantity = (decimal)returnRequest.ReceivedDamagedQuantity
            })
            .ToListAsync(cancellationToken);

        var repaired = 0;
        foreach (var candidate in candidates)
        {
            try
            {
                await _inventoryConsistencyService.ReceiveReturnAsync(
                    new InventoryReturnReceiptRequest(
                        TenantId: candidate.VendorId,
                        ReturnId: candidate.Id,
                        RefundId: candidate.RefundId,
                        OrderId: candidate.OrderId,
                        OrderItemId: candidate.OrderItemId,
                        ProductId: candidate.ProductId,
                        VariantId: candidate.VariantId,
                        DeliveredQuantity: candidate.DeliveredQuantity,
                        ResellableQuantity: candidate.ResellableQuantity,
                        DamagedQuantity: candidate.DamagedQuantity,
                        EventId: InventoryEventIdFactory.Create("commercehub-reconcile-return-receipt", candidate.Id),
                        CorrelationId: "commercehub-inventory-reconciliation",
                        Source: "CommerceHubInventoryReconciliationJob"),
                    cancellationToken);
                repaired++;
            }
            catch (Exception ex)
            {
                // CANCELLATION HANDLING: Don't swallow cancellation requests
                if (ex is OperationCanceledException)
                    throw;
                
                _logger.LogWarning(
                    ex,
                    "CommerceHub inventory reconciliation could not repair missing return receipt | ReturnId: {ReturnId}",
                    candidate.Id);
            }
        }

        return repaired;
    }

    private async Task<int> RepairOpenReservationsOnCompletedOrdersAsync(CancellationToken cancellationToken)
    {
        var candidates = await (
            from reservation in _dbContext.InventoryReservations.AsNoTracking()
            join item in _dbContext.CommerceHubSubOrderItems.AsNoTracking()
                on reservation.OrderItemId equals item.Id
            join subOrder in _dbContext.CommerceHubSubOrders.AsNoTracking()
                on item.SubOrderId equals subOrder.Id
            where !reservation.IsDeleted &&
                  !item.IsDeleted &&
                  !subOrder.IsDeleted &&
                  reservation.ReservedQuantity > reservation.CommittedQuantity + reservation.ReleasedQuantity &&
                  (item.Status == CommerceHubOrderItemStatus.Delivered ||
                   item.Status == CommerceHubOrderItemStatus.Cancelled)
            select new
            {
                subOrder.StoreId,
                subOrder.MarketplaceOrderId,
                SubOrderId = subOrder.Id,
                OrderItemId = item.Id,
                item.ProductId,
                item.VariantId,
                Quantity = (decimal)item.Quantity,
                item.Status
            })
            .Distinct()
            .ToListAsync(cancellationToken);

        var repaired = 0;
        foreach (var candidate in candidates)
        {
            try
            {
                if (candidate.Status == CommerceHubOrderItemStatus.Cancelled)
                {
                    await _inventoryConsistencyService.ReleaseOrderItemReservationsAsync(
                        candidate.StoreId,
                        candidate.OrderItemId,
                        quantity: null,
                        eventId: InventoryEventIdFactory.Create("commercehub-reconcile-cancelled-release", candidate.OrderItemId),
                        correlationId: "commercehub-inventory-reconciliation",
                        source: "CommerceHubInventoryReconciliationJob",
                        cancellationToken: cancellationToken);
                }
                else
                {
                    await _inventoryConsistencyService.CommitDeliveryAsync(
                        new InventoryDeliveryCommitRequest(
                            TenantId: candidate.StoreId,
                            OrderId: candidate.MarketplaceOrderId,
                            SubOrderId: candidate.SubOrderId,
                            OrderItemId: candidate.OrderItemId,
                            ProductId: candidate.ProductId,
                            VariantId: candidate.VariantId,
                            Quantity: candidate.Quantity,
                            EventId: InventoryEventIdFactory.Create("commercehub-reconcile-open-delivery-commit", candidate.OrderItemId),
                            CorrelationId: "commercehub-inventory-reconciliation",
                            Source: "CommerceHubInventoryReconciliationJob"),
                        cancellationToken);
                }

                repaired++;
            }
            catch (Exception ex)
            {
                // CANCELLATION HANDLING: Don't swallow cancellation requests
                if (ex is OperationCanceledException)
                    throw;
                
                _logger.LogWarning(
                    ex,
                    "CommerceHub inventory reconciliation could not repair open reservation on completed order item | OrderItemId: {OrderItemId} | Status: {Status}",
                    candidate.OrderItemId,
                    candidate.Status);
            }
        }

        return repaired;
    }
}
