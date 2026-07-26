using System.Diagnostics;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Events;
using PointOfSale.Core.Events.Affiliate;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Affiliate;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;

namespace PointOfSale.Infrastructure.Consumers.Affiliate;

/// <summary>
/// AF-2 + AF-2.5: Listens to OrderRefundedEvent and triggers per-item commission reversal.
///
/// AF-2.5 OBSERVABILITY:
/// - Structured logging with CorrelationId, TenantId
/// - Metrics: attempt, refund success, failed counters
/// - Event lag detection
/// - Processing duration histogram
///
/// BRIDGE PATTERN:
/// The existing refund flow is order-level (PaymentLifecycleService.RecordRefundAsync).
/// This consumer bridges it to the item-level granularity required by the
/// AffiliateCommissionLedger by iterating over the order's items.
///
/// CRITICAL RULES:
/// 1. If no earned commission exists for an item → AffiliateService exits silently
/// 2. Idempotent: AffiliateService catches DB unique-violation as success
/// 3. No commission calculation inside this consumer
/// </summary>
public class OrderRefundedAffiliateConsumer : IConsumer<OrderRefundedEvent>
{
    private const string EventType = "OrderRefund";

    private readonly IAffiliateService _affiliateService;
    private readonly ApplicationDbContext _db;
    private readonly AffiliateTelemetry _telemetry;
    private readonly ILogger<OrderRefundedAffiliateConsumer> _logger;

    public OrderRefundedAffiliateConsumer(
        IAffiliateService affiliateService,
        ApplicationDbContext db,
        AffiliateTelemetry telemetry,
        ILogger<OrderRefundedAffiliateConsumer> logger)
    {
        _affiliateService = affiliateService;
        _db = db;
        _telemetry = telemetry;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<OrderRefundedEvent> context)
    {
        var evt = context.Message;
        var ct = context.CancellationToken;
        var tenantId = evt.TenantId ?? Guid.Empty;
        var correlationId = evt.CorrelationId ?? evt.OrderId.ToString();
        var sw = Stopwatch.StartNew();

        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId,
            ["TenantId"] = tenantId,
            ["EventType"] = EventType,
            ["EventId"] = evt.EventId
        });

        AffiliateConsumerHelper.DetectAndRecordLag(
            _logger, _telemetry, tenantId, EventType, evt.OccurredAtUtc);

        _logger.LogInformation(
            "[Affiliate:Refund] Processing | OrderId: {OrderId} | RefundId: {RefundId} | " +
            "Amount: {Amount} | IsFullRefund: {IsFullRefund}",
            evt.OrderId, evt.RefundId, evt.RefundAmount, evt.IsFullRefund);

        _telemetry.RecordAttempt(tenantId, EventType);

        try
        {
            // Load order items to know which OrderItemIds to reverse
            var order = await _db.Orders
                .IgnoreQueryFilters()
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == evt.OrderId && !o.IsDeleted, ct);

            if (order == null)
            {
                _logger.LogWarning(
                    "[Affiliate:Refund] Order {OrderId} not found — skipping",
                    evt.OrderId);
                _telemetry.RecordSkipped(tenantId, "order_not_found");
                return;
            }

            var refund = await _db.OrderRefunds
                .IgnoreQueryFilters()
                .Include(item => item.Lines)
                .FirstOrDefaultAsync(item =>
                    item.OrderId == evt.OrderId &&
                    item.RefundId == evt.RefundId &&
                    !item.IsDeleted,
                    ct);

            var refundLinesByItemId = refund?.Lines
                .Where(line => line.Amount > 0m)
                .GroupBy(line => line.OrderItemId)
                .ToDictionary(group => group.Key, group => group.Sum(line => line.Amount))
                ?? new Dictionary<Guid, decimal>();

            var itemsToReverse = evt.IsFullRefund
                ? order.Items
                    .Select(item => new
                    {
                        Item = item,
                        Amount = refundLinesByItemId.TryGetValue(item.Id, out var amount)
                            ? amount
                            : item.AffiliateCommissionBaseAmountSnapshot ?? item.Total
                    })
                    .Where(item => item.Amount > 0m)
                    .ToList()
                : refundLinesByItemId
                    .Select(line =>
                    {
                        var item = order.Items.FirstOrDefault(orderItem => orderItem.Id == line.Key);
                        if (item == null)
                        {
                            throw new InvalidOperationException(
                                $"AFFILIATE_REFUND_LINE_ITEM_NOT_FOUND: Refund {evt.RefundId} references order item {line.Key} that is not present on order {evt.OrderId}.");
                        }

                        return new
                        {
                            Item = item,
                            Amount = line.Value
                        };
                    })
                    .ToList();

            if (!evt.IsFullRefund && itemsToReverse.Count == 0)
            {
                _logger.LogError(
                    "[Affiliate:Refund] INVARIANT FAILURE: Partial refund has no durable refunded item allocation | OrderId: {OrderId} | RefundId: {RefundId} | RefundAmount: {Amount} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                    evt.OrderId,
                    evt.RefundId,
                    evt.RefundAmount,
                    tenantId,
                    correlationId);
                _telemetry.RecordFailed(tenantId, "refund_item_allocation_missing");
                throw new InvalidOperationException(
                    $"AFFILIATE_REFUND_ITEM_ALLOCATION_REQUIRED: Partial refund {evt.RefundId} for order {evt.OrderId} has no durable OrderRefundLine allocation; commission reversal cannot be proven.");
            }

            foreach (var item in itemsToReverse)
            {
                await _affiliateService.ReverseCommissionForOrderItemAsync(
                    evt.OrderId,
                    item.Item.Id,
                    ct,
                    item.Amount,
                    BuildCommissionRefundReversalIdempotencyKey(evt.RefundId, item.Item.Id, item.Amount));
            }

            sw.Stop();
            _telemetry.RecordProcessingDuration(tenantId, EventType, sw.Elapsed.TotalMilliseconds);

            _logger.LogInformation(
                "[Affiliate:Refund] Reversal complete | OrderId: {OrderId} | Items: {Count} | Duration: {DurationMs}ms",
                evt.OrderId, itemsToReverse.Count, sw.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _telemetry.RecordFailed(tenantId, EventType);
            _telemetry.RecordProcessingDuration(tenantId, EventType, sw.Elapsed.TotalMilliseconds);

            _logger.LogError(ex,
                "[Affiliate:Refund] Failed | OrderId: {OrderId} | Duration: {DurationMs}ms",
                evt.OrderId, sw.Elapsed.TotalMilliseconds);
            throw; // Let MassTransit retry
        }
    }

    private static string BuildCommissionRefundReversalIdempotencyKey(
        string refundId,
        Guid orderItemId,
        decimal refundedBaseAmount) =>
        $"AFFILIATE_COMM:REVERSED:{refundId}:{orderItemId:N}:{refundedBaseAmount.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}";
}

/// <summary>
/// AF-2 + AF-2.5: Listens to the per-item OrderItemRefundedEvent for precise reversal.
///
/// AF-2.5 OBSERVABILITY:
/// - Structured logging with CorrelationId, TenantId
/// - Metrics: attempt, refund success, failed counters
/// - Event lag detection
///
/// CRITICAL RULES:
/// 1. If no earned commission exists → AffiliateService exits silently
/// 2. Idempotent via DB unique constraint on reversal idempotency key
/// </summary>
public class OrderItemRefundedAffiliateConsumer : IConsumer<OrderItemRefundedEvent>
{
    private const string EventType = "ItemRefund";

    private readonly IAffiliateService _affiliateService;
    private readonly AffiliateTelemetry _telemetry;
    private readonly ILogger<OrderItemRefundedAffiliateConsumer> _logger;

    public OrderItemRefundedAffiliateConsumer(
        IAffiliateService affiliateService,
        AffiliateTelemetry telemetry,
        ILogger<OrderItemRefundedAffiliateConsumer> logger)
    {
        _affiliateService = affiliateService;
        _telemetry = telemetry;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<OrderItemRefundedEvent> context)
    {
        var evt = context.Message;
        var ct = context.CancellationToken;
        var tenantId = evt.TenantId ?? Guid.Empty;
        var correlationId = evt.CorrelationId ?? evt.OrderId.ToString();
        var sw = Stopwatch.StartNew();

        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId,
            ["TenantId"] = tenantId,
            ["EventType"] = EventType,
            ["EventId"] = evt.EventId
        });

        AffiliateConsumerHelper.DetectAndRecordLag(
            _logger, _telemetry, tenantId, EventType, evt.OccurredAtUtc);

        _logger.LogInformation(
            "[Affiliate:ItemRefund] Processing | OrderId: {OrderId} | OrderItemId: {OrderItemId} | " +
            "Product: {Product} | TransactionAmount: {TransactionAmount} | CumulativeAmount: {CumulativeAmount}",
            evt.OrderId, evt.OrderItemId, evt.ProductName, evt.RefundedAmount, evt.CumulativeRefundedAmount);

        _telemetry.RecordAttempt(tenantId, EventType);

        try
        {
            await _affiliateService.ReverseCommissionForOrderItemAsync(
                evt.OrderId,
                evt.OrderItemId,
                ct,
                evt.RefundedAmount,
                BuildCommissionRefundReversalIdempotencyKey(evt, evt.RefundedAmount));

            sw.Stop();
            _telemetry.RecordProcessingDuration(tenantId, EventType, sw.Elapsed.TotalMilliseconds);

            _logger.LogInformation(
                "[Affiliate:ItemRefund] Completed | OrderItemId: {OrderItemId} | Duration: {DurationMs}ms",
                evt.OrderItemId, sw.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _telemetry.RecordFailed(tenantId, EventType);
            _telemetry.RecordProcessingDuration(tenantId, EventType, sw.Elapsed.TotalMilliseconds);

            _logger.LogError(ex,
                "[Affiliate:ItemRefund] Failed | OrderItemId: {OrderItemId} | Duration: {DurationMs}ms",
                evt.OrderItemId, sw.Elapsed.TotalMilliseconds);
            throw;
        }
    }

    private static string BuildCommissionRefundReversalIdempotencyKey(
        OrderItemRefundedEvent evt,
        decimal refundedBaseAmount)
    {
        var refundId = !string.IsNullOrWhiteSpace(evt.RefundId)
            ? evt.RefundId
            : evt.IdempotencyKey ?? evt.EventId.ToString("N");

        return $"AFFILIATE_COMM:REVERSED:{refundId}:{evt.OrderItemId:N}:{refundedBaseAmount.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}";
    }
}
