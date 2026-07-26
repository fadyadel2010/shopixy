-- ═══════════════════════════════════════════════════════════════════════════
-- AF-21: Backfill Order Status Data to MongoDB
-- ═══════════════════════════════════════════════════════════════════════════
--
-- PURPOSE:
-- This script generates synthetic events to backfill order lifecycle data
-- into existing AffiliateOrderDocument records in MongoDB.
--
-- STRATEGY:
-- 1. Query PostgreSQL for orders with affiliate commissions
-- 2. For each order, publish OrderCreatedEvent with current order state
-- 3. Let OrderLifecycleAffiliateProjectionConsumer update MongoDB
--
-- EXECUTION:
-- Run this in a C# console app or admin endpoint that has access to:
-- - ApplicationDbContext (PostgreSQL)
-- - IEventPublisher (MassTransit)
--
-- SAFETY:
-- - Idempotent: ProcessedEventIds prevents duplicate updates
-- - Tenant-safe: TenantId filter enforced in consumer
-- - No data loss: Only updates existing documents
--
-- ═══════════════════════════════════════════════════════════════════════════

-- STEP 1: Identify orders that need backfill
-- These are orders with affiliate commissions but missing lifecycle data in MongoDB

SELECT DISTINCT
    o.Id AS OrderId,
    o.OrderNumber,
    o.TenantId,
    o.Status AS OrderStatus,
    o.FulfillmentStatus,
    o.PaymentMethodCode AS PaymentMethod,
    o.Total AS PaidAmount,
    o.Currency,
    o.CreatedAt
FROM Orders o
INNER JOIN AffiliateCommissionLedger acl ON acl.OrderId = o.Id
WHERE 
    o.IsDeleted = FALSE
    AND acl.EntryType = 'Earned'
ORDER BY o.CreatedAt DESC;

-- ═══════════════════════════════════════════════════════════════════════════
-- C# BACKFILL IMPLEMENTATION
-- ═══════════════════════════════════════════════════════════════════════════
/*

using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;

public class AF21BackfillService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<AF21BackfillService> _logger;

    public AF21BackfillService(
        ApplicationDbContext dbContext,
        IEventPublisher eventPublisher,
        ILogger<AF21BackfillService> logger)
    {
        _dbContext = dbContext;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public async Task BackfillOrderStatusAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("[AF-21:Backfill] Starting order status backfill...");

        // Query orders with affiliate commissions
        var orders = await _dbContext.Orders
            .Where(o => !o.IsDeleted)
            .Where(o => _dbContext.AffiliateCommissionLedger
                .Any(acl => acl.OrderId == o.Id && acl.EntryType == "Earned"))
            .Select(o => new
            {
                o.Id,
                o.OrderNumber,
                o.TenantId,
                OrderStatus = o.Status,
                FulfillmentStatus = o.FulfillmentStatus,
                o.PaymentMethodCode,
                o.Total,
                o.Currency,
                o.CreatedAt
            })
            .ToListAsync(ct);

        _logger.LogInformation("[AF-21:Backfill] Found {Count} orders to backfill", orders.Count);

        int successCount = 0;
        int errorCount = 0;

        foreach (var order in orders)
        {
            try
            {
                // Publish synthetic OrderCreatedEvent
                var evt = new OrderCreatedEvent
                {
                    EventId = Guid.NewGuid(),
                    TenantId = order.TenantId,
                    OrderId = order.Id,
                    OrderNumber = order.OrderNumber,
                    Status = order.OrderStatus,
                    PaymentMethodCode = order.PaymentMethodCode ?? "Unknown",
                    Total = order.Total,
                    Currency = order.Currency,
                    OccurredAtUtc = order.CreatedAt
                };

                await _eventPublisher.PublishAsync(evt, ct);

                // If order is completed, also publish completion event
                if (order.FulfillmentStatus == FulfillmentStatus.Completed)
                {
                    var completedEvt = new OrderCompletedEvent
                    {
                        EventId = Guid.NewGuid(),
                        TenantId = order.TenantId,
                        OrderId = order.Id,
                        OrderNumber = order.OrderNumber,
                        OldStatus = FulfillmentStatus.Delivered,
                        NewStatus = FulfillmentStatus.Completed,
                        TransitionedAt = order.CreatedAt,
                        Source = FulfillmentRequestSource.System,
                        OccurredAtUtc = order.CreatedAt
                    };

                    await _eventPublisher.PublishAsync(completedEvt, ct);
                }

                // If order is delivered, publish delivery event
                else if (order.FulfillmentStatus == FulfillmentStatus.Delivered)
                {
                    var deliveredEvt = new FulfillmentDeliveredEvent
                    {
                        EventId = Guid.NewGuid(),
                        TenantId = order.TenantId,
                        OrderId = order.Id,
                        OrderNumber = order.OrderNumber,
                        OldStatus = FulfillmentStatus.OutForDelivery,
                        NewStatus = FulfillmentStatus.Delivered,
                        PaymentMethod = order.PaymentMethodCode ?? "Unknown",
                        PaymentStatus = PaymentStatus.Pending,
                        OrderTotal = order.Total,
                        TransitionedAt = order.CreatedAt,
                        Source = FulfillmentRequestSource.System,
                        OccurredAtUtc = order.CreatedAt
                    };

                    await _eventPublisher.PublishAsync(deliveredEvt, ct);
                }

                // If order is cancelled, publish cancellation event
                else if (order.OrderStatus == OrderStatus.Cancelled)
                {
                    var cancelledEvt = new OrderCancelledEvent
                    {
                        EventId = Guid.NewGuid(),
                        TenantId = order.TenantId,
                        OrderId = order.Id,
                        OrderNumber = order.OrderNumber,
                        CancellationReason = "Backfill - order was cancelled",
                        CancelledAt = order.CreatedAt,
                        RestoreInventory = false,
                        OccurredAtUtc = order.CreatedAt
                    };

                    await _eventPublisher.PublishAsync(cancelledEvt, ct);
                }

                successCount++;

                if (successCount % 100 == 0)
                {
                    _logger.LogInformation("[AF-21:Backfill] Progress: {Success} orders backfilled", successCount);
                }
            }
            catch (Exception ex)
            {
                errorCount++;
                _logger.LogError(ex, "[AF-21:Backfill] Failed to backfill order {OrderId}", order.Id);
            }
        }

        _logger.LogInformation(
            "[AF-21:Backfill] Completed | Success: {Success} | Errors: {Errors}",
            successCount, errorCount);
    }
}

*/

-- ═══════════════════════════════════════════════════════════════════════════
-- ALTERNATIVE: DIRECT MONGODB UPDATE (NOT RECOMMENDED)
-- ═══════════════════════════════════════════════════════════════════════════
-- If event replay is not feasible, you can directly update MongoDB.
-- However, this bypasses the event-driven architecture and should be avoided.
--
-- MongoDB Shell Script:
/*

db.affiliate_orders.updateMany(
  {
    // Filter: documents missing order status fields
    $or: [
      { orderStatus: { $exists: false } },
      { orderStatus: "" }
    ]
  },
  [
    {
      $set: {
        orderStatus: "Unknown",
        fulfillmentStatus: "Unknown",
        paymentMethod: "Unknown",
        paidAmount: 0,
        updatedAtUtc: new Date()
      }
    }
  ]
);

*/

-- ═══════════════════════════════════════════════════════════════════════════
-- VERIFICATION QUERIES
-- ═══════════════════════════════════════════════════════════════════════════

-- Check how many orders need backfill
SELECT COUNT(DISTINCT o.Id) AS OrdersNeedingBackfill
FROM Orders o
INNER JOIN AffiliateCommissionLedger acl ON acl.OrderId = o.Id
WHERE 
    o.IsDeleted = FALSE
    AND acl.EntryType = 'Earned';

-- Sample orders for manual verification
SELECT 
    o.Id,
    o.OrderNumber,
    o.Status,
    o.FulfillmentStatus,
    o.PaymentMethodCode,
    o.Total,
    COUNT(acl.Id) AS CommissionEntries
FROM Orders o
INNER JOIN AffiliateCommissionLedger acl ON acl.OrderId = o.Id
WHERE 
    o.IsDeleted = FALSE
    AND acl.EntryType = 'Earned'
GROUP BY o.Id, o.OrderNumber, o.Status, o.FulfillmentStatus, o.PaymentMethodCode, o.Total
LIMIT 10;
