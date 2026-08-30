using MassTransit;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using PointOfSale.Core.Events;
using PointOfSale.Core.Events.Affiliate;
using PointOfSale.Core.Payments;
using PointOfSale.Infrastructure.MongoDB.Documents.Affiliate;

namespace PointOfSale.Infrastructure.Consumers.Affiliate;

/// <summary>
/// AF-3 + AF-21: Projection consumer for AffiliateAttributedEvent.
///
/// Creates two documents when an order is attributed to an affiliate:
/// 1. AffiliateDashboardDocument (if doesn't exist) - for dashboard stats
/// 2. AffiliateOrderDocument (placeholder) - so order appears immediately in portal
///
/// AF-21: The order document is created with "Pending" status immediately,
/// then updated with commission details when AffiliateCommissionEarnedEvent fires.
/// This ensures affiliates see their orders immediately after placement.
///
/// IDEMPOTENCY: Upsert with SetOnInsert — if documents already exist, no-op.
/// </summary>
public class AffiliateAttributedProjectionConsumer : IConsumer<AffiliateAttributedEvent>
{
    private readonly IMongoCollection<AffiliateDashboardDocument> _dashboards;
    private readonly IMongoCollection<AffiliateOrderDocument> _orders;
    private readonly ILogger<AffiliateAttributedProjectionConsumer> _logger;

    public AffiliateAttributedProjectionConsumer(
        IMongoDatabase mongoDatabase,
        ILogger<AffiliateAttributedProjectionConsumer> logger)
    {
        _dashboards = mongoDatabase.GetCollection<AffiliateDashboardDocument>("affiliate_dashboards");
        _orders = mongoDatabase.GetCollection<AffiliateOrderDocument>("affiliate_orders");
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<AffiliateAttributedEvent> context)
    {
        var evt = context.Message;
        var ct = context.CancellationToken;
        var eventId = evt.EventId.ToString();
        var tenantId = evt.StoreTenantId != Guid.Empty
            ? evt.StoreTenantId
            : evt.TenantId ?? Guid.Empty;

        if (!evt.TenantId.HasValue || evt.TenantId.Value == Guid.Empty || tenantId == Guid.Empty)
        {
            _logger.LogError(
                "[Affiliate:Projection:Attributed] Rejected event missing store tenant authority | OrderId: {OrderId} | EventId: {EventId}",
                evt.OrderId, evt.EventId);
            throw new InvalidOperationException(
                $"AffiliateAttributedEvent missing store tenant authority. OrderId={evt.OrderId}; EventId={evt.EventId}");
        }

        if (evt.TenantId.Value != tenantId || evt.EventVersion <= 0)
        {
            _logger.LogError(
                "[Affiliate:Projection:Attributed] Rejected ambiguous/stale authority event | OrderId: {OrderId} | EventId: {EventId} | TenantId: {TenantId} | StoreTenantId: {StoreTenantId} | EventVersion: {EventVersion}",
                evt.OrderId, evt.EventId, evt.TenantId, evt.StoreTenantId, evt.EventVersion);
            throw new InvalidOperationException(
                $"AffiliateAttributedEvent has invalid authority metadata. OrderId={evt.OrderId}; EventId={evt.EventId}; TenantId={evt.TenantId}; StoreTenantId={evt.StoreTenantId}; EventVersion={evt.EventVersion}");
        }

        _logger.LogInformation(
            "[Affiliate:Projection:Attributed] OrderId: {OrderId} | AffiliateId: {AffiliateId}",
            evt.OrderId, evt.AffiliateAccountId);

        // ─── 1. UPSERT DASHBOARD ────────────────────────────────────────
        var dashFilter = Builders<AffiliateDashboardDocument>.Filter.And(
            Builders<AffiliateDashboardDocument>.Filter.Eq(d => d.TenantId, tenantId),
            Builders<AffiliateDashboardDocument>.Filter.Eq(d => d.AffiliateAccountId, evt.AffiliateAccountId));

        var dashUpdate = Builders<AffiliateDashboardDocument>.Update
            .SetOnInsert(d => d.Id, Guid.NewGuid())
            .SetOnInsert(d => d.TenantId, tenantId)
            .SetOnInsert(d => d.AffiliateAccountId, evt.AffiliateAccountId)
            .SetOnInsert(d => d.ReferralCode, evt.ReferralCode)
            .SetOnInsert(d => d.TotalEarned, 0m)
            .SetOnInsert(d => d.TotalReversed, 0m)
            .SetOnInsert(d => d.NetEarnings, 0m)
            .SetOnInsert(d => d.TotalOrders, 0)
            .Set(d => d.UpdatedAtUtc, DateTime.UtcNow);

        await _dashboards.UpdateOneAsync(dashFilter, dashUpdate, new UpdateOptions { IsUpsert = true }, ct);

        // ─── 2. CREATE PLACEHOLDER ORDER DOCUMENT (AF-21) ───────────────
        // This ensures the order appears in affiliate portal immediately,
        // even before payment is captured and commission is earned.
        
        // Check if document already exists and has processed this event (idempotency)
        var existingDoc = await _orders.Find(d => 
            d.TenantId == tenantId && 
            d.OrderId == evt.OrderId).FirstOrDefaultAsync(ct);

        if (existingDoc != null && existingDoc.ProcessedEventIds.Contains(eventId))
        {
            _logger.LogInformation(
                "[Affiliate:Projection:Attributed] Event already processed | OrderId: {OrderId} | EventId: {EventId}",
                evt.OrderId, eventId);
            return;
        }

        // Simple filter: just match by TenantId and OrderId
        var orderFilter = Builders<AffiliateOrderDocument>.Filter.And(
            Builders<AffiliateOrderDocument>.Filter.Eq(d => d.TenantId, tenantId),
            Builders<AffiliateOrderDocument>.Filter.Eq(d => d.OrderId, evt.OrderId));

        var orderUpdate = Builders<AffiliateOrderDocument>.Update
            .SetOnInsert(d => d.Id, Guid.NewGuid())
            .SetOnInsert(d => d.OrderId, evt.OrderId)
            .SetOnInsert(d => d.OrderNumber, evt.OrderNumber)
            .SetOnInsert(d => d.AffiliateAccountId, evt.AffiliateAccountId)
            .SetOnInsert(d => d.TenantId, tenantId)
            .SetOnInsert(d => d.Currency, string.Empty) // Will be set by CommissionEarned or OrderCreated
            .SetOnInsert(d => d.TotalCommission, 0m)
            .SetOnInsert(d => d.TotalReversed, 0m)
            .SetOnInsert(d => d.TotalApproved, 0m)
            .SetOnInsert(d => d.TotalPaid, 0m)
            .SetOnInsert(d => d.Status, "Pending") // Pending until commission is earned
            .SetOnInsert(d => d.OrderStatus, string.Empty) // Will be set by OrderCreated event
            .SetOnInsert(d => d.FulfillmentStatus, string.Empty)
            .SetOnInsert(d => d.PaymentMethod, string.Empty)
            .SetOnInsert(d => d.PaidAmount, 0m)
            .SetOnInsert(d => d.Items, new List<AffiliateOrderItemSubDocument>())
            .SetOnInsert(d => d.CreatedAtUtc, DateTime.UtcNow)
            .Set(d => d.UpdatedAtUtc, DateTime.UtcNow)
            .AddToSet(d => d.ProcessedEventIds, eventId); // AddToSet will create the array on insert

        var orderResult = await _orders.UpdateOneAsync(orderFilter, orderUpdate, new UpdateOptions { IsUpsert = true }, ct);

        if (orderResult.UpsertedId != null)
        {
            _logger.LogInformation(
                "[Affiliate:Projection:Attributed] Created placeholder order document | OrderId: {OrderId} | AffiliateId: {AffiliateId}",
                evt.OrderId, evt.AffiliateAccountId);
        }

        _logger.LogInformation(
            "[Affiliate:Projection:Attributed] Dashboard and order ensured | AffiliateId: {AffiliateId}",
            evt.AffiliateAccountId);
    }
}

/// <summary>
/// AF-3: Projection consumer for AffiliateCommissionEarnedEvent.
///
/// Updates three documents atomically:
/// 1. AffiliateOrderDocument — upsert with commission items
/// 2. AffiliateDashboardDocument — increment totals
/// 3. AffiliateEarningsSummaryDocument — increment currency-specific totals
///
/// IDEMPOTENCY: Uses AffiliateProjectionInboxDocument to track processed events.
/// Each projection type (Dashboard, Summary) has independent idempotency tracking
/// to prevent unbounded ProcessedEventIds array growth in aggregate documents.
///
/// DURABLE: Inbox entries are not evicted; stale event replays cannot re-increment money counters.
/// </summary>
public class AffiliateCommissionEarnedProjectionConsumer : IConsumer<AffiliateCommissionEarnedEvent>
{

    private readonly IMongoCollection<AffiliateOrderDocument> _orders;
    private readonly IMongoCollection<AffiliateDashboardDocument> _dashboards;
    private readonly IMongoCollection<AffiliateEarningsSummaryDocument> _summaries;
    private readonly ILogger<AffiliateCommissionEarnedProjectionConsumer> _logger;

    public AffiliateCommissionEarnedProjectionConsumer(
        IMongoDatabase mongoDatabase,
        ILogger<AffiliateCommissionEarnedProjectionConsumer> logger)
    {
        _orders = mongoDatabase.GetCollection<AffiliateOrderDocument>("affiliate_orders");
        _dashboards = mongoDatabase.GetCollection<AffiliateDashboardDocument>("affiliate_dashboards");
        _summaries = mongoDatabase.GetCollection<AffiliateEarningsSummaryDocument>("affiliate_earnings_summaries");
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<AffiliateCommissionEarnedEvent> context)
    {
        var evt = context.Message;
        var ct = context.CancellationToken;
        var eventId = evt.EventId.ToString();
        var tenantId = AffiliateMoneyProjectionConsistency.ValidateAuthorityMetadata(
            evt.StoreTenantId,
            evt.TenantId,
            evt.EventVersion,
            evt.OrderId,
            nameof(AffiliateCommissionEarnedEvent));

        _logger.LogInformation(
            "[Affiliate:Projection:Earned] OrderId: {OrderId} | AffiliateId: {AffiliateId} | " +
            "Commission: {Commission} {Currency} | Items: {ItemCount}",
            evt.OrderId, evt.AffiliateAccountId, evt.TotalCommission, evt.Currency, evt.ItemCount);

        // ─── 1. UPSERT ORDER DOCUMENT ───────────────────────────────────
        // PATCH D: Per-document $nin filter — no early return that could skip dashboard/summary
        {
            var items = evt.Items.Select(i => new AffiliateOrderItemSubDocument
            {
                OrderItemId = i.OrderItemId,
                ProductId = i.ProductId,
                ProductName = i.ProductName,
                VariantName = i.VariantName,
                PaidAmount = i.PaidAmount,
                CommissionAmount = i.CommissionAmount,
                IsReversed = false
            }).ToList();

            var orderFilter = AffiliateMoneyProjectionConsistency.BuildEarnedFreshFilter(
                tenantId,
                evt.OrderId,
                evt.EventVersion);

            var orderUpdate = BuildOrderUpdate(evt, tenantId, eventId, items);

            var orderResult = await _orders.UpdateOneAsync(orderFilter, orderUpdate, new UpdateOptions { IsUpsert = true }, ct);

            if (orderResult.ModifiedCount == 0 && orderResult.UpsertedId == null)
            {
                _logger.LogInformation(
                    "[Affiliate:Projection:Earned] Order doc already has EventId {EventId} — skipping order update",
                    eventId);
            }
        }

        // ─── 2. UPDATE DASHBOARD ────────────────────────────────────────
        // Counter update and idempotency marker must be atomic on the dashboard document.
        {
            var dashFilter = Builders<AffiliateDashboardDocument>.Filter.And(
                Builders<AffiliateDashboardDocument>.Filter.Eq(d => d.TenantId, tenantId),
                Builders<AffiliateDashboardDocument>.Filter.Eq(d => d.AffiliateAccountId, evt.AffiliateAccountId),
                !Builders<AffiliateDashboardDocument>.Filter.AnyEq(d => d.ProcessedEventIds, eventId));

            var dashUpdate = Builders<AffiliateDashboardDocument>.Update
                .SetOnInsert(d => d.Id, Guid.NewGuid())
                .SetOnInsert(d => d.TenantId, tenantId)
                .SetOnInsert(d => d.AffiliateAccountId, evt.AffiliateAccountId)
                .Set(d => d.ReferralCode, evt.ReferralCode)
                .Inc(d => d.TotalEarned, evt.TotalCommission)
                .Inc(d => d.NetEarnings, evt.TotalCommission)
                .Inc(d => d.TotalOrders, 1)
                .Set(d => d.LastCommissionAtUtc, DateTime.UtcNow)
                .Set(d => d.UpdatedAtUtc, DateTime.UtcNow)
                .AddToSet(d => d.ProcessedEventIds, eventId);

            try
            {
                var dashResult = await _dashboards.UpdateOneAsync(dashFilter, dashUpdate, new UpdateOptions { IsUpsert = true }, ct);
                if (dashResult.ModifiedCount == 0 && dashResult.UpsertedId == null)
                {
                    _logger.LogInformation(
                        "[Affiliate:Projection:Earned] Dashboard already processed EventId {EventId} - skipping (idempotent retry)",
                        eventId);
                }
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                _logger.LogInformation(
                    "[Affiliate:Projection:Earned] Dashboard already processed EventId {EventId} — skipping (idempotent retry)",
                    eventId);
            }
        }

        // ─── 3. UPDATE EARNINGS SUMMARY ─────────────────────────────────
        // Counter update and idempotency marker must be atomic on the summary document.
        {
            var summaryFilter = Builders<AffiliateEarningsSummaryDocument>.Filter.And(
                Builders<AffiliateEarningsSummaryDocument>.Filter.Eq(d => d.TenantId, tenantId),
                Builders<AffiliateEarningsSummaryDocument>.Filter.Eq(d => d.AffiliateAccountId, evt.AffiliateAccountId),
                Builders<AffiliateEarningsSummaryDocument>.Filter.Eq(d => d.Currency, evt.Currency),
                !Builders<AffiliateEarningsSummaryDocument>.Filter.AnyEq(d => d.ProcessedEventIds, eventId));

            var summaryUpdate = Builders<AffiliateEarningsSummaryDocument>.Update
                .SetOnInsert(d => d.Id, Guid.NewGuid())
                .SetOnInsert(d => d.TenantId, tenantId)
                .SetOnInsert(d => d.AffiliateAccountId, evt.AffiliateAccountId)
                .SetOnInsert(d => d.Currency, evt.Currency)
                .Inc(d => d.TotalEarned, evt.TotalCommission)
                .Inc(d => d.NetEarnings, evt.TotalCommission)
                .Set(d => d.UpdatedAtUtc, DateTime.UtcNow)
                .AddToSet(d => d.ProcessedEventIds, eventId);

            try
            {
                var summaryResult = await _summaries.UpdateOneAsync(summaryFilter, summaryUpdate, new UpdateOptions { IsUpsert = true }, ct);
                if (summaryResult.ModifiedCount == 0 && summaryResult.UpsertedId == null)
                {
                    _logger.LogInformation(
                        "[Affiliate:Projection:Earned] Summary already processed EventId {EventId} - skipping (idempotent retry)",
                        eventId);
                }
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                _logger.LogInformation(
                    "[Affiliate:Projection:Earned] Summary already processed EventId {EventId} — skipping (idempotent retry)",
                    eventId);
            }
        }

        await AffiliateMoneyProjectionConsistency.RecomputeAffiliateMoneyAggregatesAsync(
            _orders,
            _dashboards,
            _summaries,
            tenantId,
            evt.AffiliateAccountId,
            evt.ReferralCode,
            ct);

        _logger.LogInformation(
            "[Affiliate:Projection:Earned] All projections updated | OrderId: {OrderId} | AffiliateId: {AffiliateId}",
            evt.OrderId, evt.AffiliateAccountId);
    }

    private static UpdateDefinition<AffiliateOrderDocument> BuildOrderUpdate(
        AffiliateCommissionEarnedEvent evt,
        Guid tenantId,
        string eventId,
        List<AffiliateOrderItemSubDocument> items)
    {
        return Builders<AffiliateOrderDocument>.Update
            .SetOnInsert(d => d.Id, Guid.NewGuid())
            .SetOnInsert(d => d.OrderId, evt.OrderId)
            .SetOnInsert(d => d.TenantId, tenantId)
            .SetOnInsert(d => d.CreatedAtUtc, DateTime.UtcNow)
            .Set(d => d.OrderNumber, evt.OrderNumber)
            .Set(d => d.AffiliateAccountId, evt.AffiliateAccountId)
            .Set(d => d.Currency, evt.Currency)
            .Set(d => d.TotalCommission, evt.TotalCommission)
            .Set(d => d.Status, "Earned")
            .Set(d => d.Items, items)
            .Set(d => d.UpdatedAtUtc, DateTime.UtcNow)
            .Set(d => d.LastCommissionEarnedEventOccurredAtUtc, evt.OccurredAtUtc)
            .Set(d => d.LastCommissionEarnedEventVersion, evt.EventVersion)
            .AddToSet(d => d.ProcessedEventIds, eventId);
    }
}

/// <summary>
/// AF-3: Projection consumer for AffiliateCommissionReversedEvent.
///
/// Updates three documents:
/// 1. AffiliateOrderDocument — marks item as reversed, adjusts totals and status
/// 2. AffiliateDashboardDocument — increments TotalReversed, decrements NetEarnings
/// 3. AffiliateEarningsSummaryDocument — increments TotalReversed, decrements NetEarnings
///
/// STATUS LOGIC:
/// - If ALL items are reversed → status = "Reversed"
/// - If SOME items are reversed → status = "PartiallyReversed"
/// - If NO items are reversed → status = "Earned"
///
/// IDEMPOTENCY: Uses AffiliateProjectionInboxDocument for dashboard and summary.
/// Order documents retain ProcessedEventIds (bounded lifetime).
/// </summary>
public class AffiliateCommissionReversedProjectionConsumer : IConsumer<AffiliateCommissionReversedEvent>
{

    private readonly IMongoCollection<AffiliateOrderDocument> _orders;
    private readonly IMongoCollection<AffiliateDashboardDocument> _dashboards;
    private readonly IMongoCollection<AffiliateEarningsSummaryDocument> _summaries;
    private readonly ILogger<AffiliateCommissionReversedProjectionConsumer> _logger;

    public AffiliateCommissionReversedProjectionConsumer(
        IMongoDatabase mongoDatabase,
        ILogger<AffiliateCommissionReversedProjectionConsumer> logger)
    {
        _orders = mongoDatabase.GetCollection<AffiliateOrderDocument>("affiliate_orders");
        _dashboards = mongoDatabase.GetCollection<AffiliateDashboardDocument>("affiliate_dashboards");
        _summaries = mongoDatabase.GetCollection<AffiliateEarningsSummaryDocument>("affiliate_earnings_summaries");
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<AffiliateCommissionReversedEvent> context)
    {
        var evt = context.Message;
        var ct = context.CancellationToken;
        var eventId = evt.EventId.ToString();
        var tenantId = AffiliateMoneyProjectionConsistency.ValidateAuthorityMetadata(
            evt.StoreTenantId,
            evt.TenantId,
            evt.EventVersion,
            evt.OrderId,
            nameof(AffiliateCommissionReversedEvent));

        _logger.LogInformation(
            "[Affiliate:Projection:Reversed] OrderId: {OrderId} | OrderItemId: {ItemId} | " +
            "Amount: {Amount} {Currency}",
            evt.OrderId, evt.OrderItemId, evt.ReversedAmount, evt.Currency);

        // ─── 1. UPDATE ORDER DOCUMENT ───────────────────────────────────
        // PATCH D: Per-document $nin filter instead of early-return
        {
            var currentOrder = await _orders.Find(
                    Builders<AffiliateOrderDocument>.Filter.And(
                        Builders<AffiliateOrderDocument>.Filter.Eq(d => d.TenantId, tenantId),
                        Builders<AffiliateOrderDocument>.Filter.Eq(d => d.OrderId, evt.OrderId)))
                .FirstOrDefaultAsync(ct);
            if (currentOrder == null)
            {
                throw new InvalidOperationException(
                    $"AffiliateCommissionReversedEvent cannot be acknowledged before its earned order projection exists. OrderId={evt.OrderId}; EventId={evt.EventId}.");
            }

            if (currentOrder.ProcessedEventIds.Contains(eventId))
            {
                await AffiliateMoneyProjectionConsistency.RecomputeAffiliateMoneyAggregatesAsync(
                    _orders, _dashboards, _summaries, tenantId, evt.AffiliateAccountId, referralCode: null, ct);
                return;
            }

            var currentItem = currentOrder.Items.SingleOrDefault(item => item.OrderItemId == evt.OrderItemId);
            var nextState = CalculateReversalState(currentOrder, currentItem, evt.ReversedAmount, evt.OrderId, evt.OrderItemId);

            var itemFilter = AffiliateMoneyProjectionConsistency.BuildReversalFreshFilter(
                tenantId,
                evt.OrderId,
                evt.OrderItemId,
                eventId,
                currentItem!.ReversedAmount);

            var itemUpdate = Builders<AffiliateOrderDocument>.Update
                .Set("items.$.reversedAmount", nextState.ItemReversedAmount)
                .Set("items.$.isReversed", nextState.IsItemFullyReversed)
                .Set(d => d.TotalReversed, nextState.OrderReversedAmount)
                .Set(d => d.Status, nextState.OrderStatus)
                .Set(d => d.UpdatedAtUtc, DateTime.UtcNow)
                .Max(d => d.LastCommissionReversedEventOccurredAtUtc, evt.OccurredAtUtc)
                .Max(d => d.LastCommissionReversedEventVersion, evt.EventVersion)
                .AddToSet(d => d.ProcessedEventIds, eventId);

            var itemResult = await _orders.UpdateOneAsync(itemFilter, itemUpdate, cancellationToken: ct);

            if (itemResult.ModifiedCount == 0)
            {
                var order = await _orders.Find(
                    Builders<AffiliateOrderDocument>.Filter.And(
                        Builders<AffiliateOrderDocument>.Filter.Eq(d => d.TenantId, tenantId),
                        Builders<AffiliateOrderDocument>.Filter.Eq(d => d.OrderId, evt.OrderId)
                    )).FirstOrDefaultAsync(ct);

                if (order == null)
                {
                    throw new InvalidOperationException(
                        $"AffiliateCommissionReversedEvent cannot be acknowledged before its earned order projection exists. OrderId={evt.OrderId}; EventId={evt.EventId}.");
                }

                var isDuplicate = order.ProcessedEventIds.Contains(eventId);
                if (!isDuplicate)
                {
                    throw new InvalidOperationException(
                        $"AffiliateCommissionReversedEvent lost its optimistic projection fence and must be retried. OrderId={evt.OrderId}; OrderItemId={evt.OrderItemId}; EventId={evt.EventId}.");
                }
            }
        }

        // ─── 2. UPDATE DASHBOARD ────────────────────────────────────────
        // Counter update and idempotency marker must be atomic on the dashboard document.
        {
            var dashFilter = Builders<AffiliateDashboardDocument>.Filter.And(
                Builders<AffiliateDashboardDocument>.Filter.Eq(d => d.TenantId, tenantId),
                Builders<AffiliateDashboardDocument>.Filter.Eq(d => d.AffiliateAccountId, evt.AffiliateAccountId),
                !Builders<AffiliateDashboardDocument>.Filter.AnyEq(d => d.ProcessedEventIds, eventId));

            var dashUpdate = Builders<AffiliateDashboardDocument>.Update
                .SetOnInsert(d => d.Id, Guid.NewGuid())
                .SetOnInsert(d => d.TenantId, tenantId)
                .SetOnInsert(d => d.AffiliateAccountId, evt.AffiliateAccountId)
                .Inc(d => d.TotalReversed, evt.ReversedAmount)
                .Inc(d => d.NetEarnings, -evt.ReversedAmount)
                .Set(d => d.UpdatedAtUtc, DateTime.UtcNow)
                .AddToSet(d => d.ProcessedEventIds, eventId);

            try
            {
                var dashResult = await _dashboards.UpdateOneAsync(dashFilter, dashUpdate, new UpdateOptions { IsUpsert = true }, ct);
                if (dashResult.ModifiedCount == 0 && dashResult.UpsertedId == null)
                {
                    _logger.LogInformation(
                        "[Affiliate:Projection:Reversed] Dashboard already processed EventId {EventId} - skipping (idempotent retry)",
                        eventId);
                }
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                _logger.LogInformation(
                    "[Affiliate:Projection:Reversed] Dashboard already processed EventId {EventId} — skipping (idempotent retry)",
                    eventId);
            }
        }

        // ─── 3. UPDATE EARNINGS SUMMARY ─────────────────────────────────
        // Counter update and idempotency marker must be atomic on the summary document.
        {
            var summaryFilter = Builders<AffiliateEarningsSummaryDocument>.Filter.And(
                Builders<AffiliateEarningsSummaryDocument>.Filter.Eq(d => d.TenantId, tenantId),
                Builders<AffiliateEarningsSummaryDocument>.Filter.Eq(d => d.AffiliateAccountId, evt.AffiliateAccountId),
                Builders<AffiliateEarningsSummaryDocument>.Filter.Eq(d => d.Currency, evt.Currency),
                !Builders<AffiliateEarningsSummaryDocument>.Filter.AnyEq(d => d.ProcessedEventIds, eventId));

            var summaryUpdate = Builders<AffiliateEarningsSummaryDocument>.Update
                .SetOnInsert(d => d.Id, Guid.NewGuid())
                .SetOnInsert(d => d.TenantId, tenantId)
                .SetOnInsert(d => d.AffiliateAccountId, evt.AffiliateAccountId)
                .SetOnInsert(d => d.Currency, evt.Currency)
                .Inc(d => d.TotalReversed, evt.ReversedAmount)
                .Inc(d => d.NetEarnings, -evt.ReversedAmount)
                .Set(d => d.UpdatedAtUtc, DateTime.UtcNow)
                .AddToSet(d => d.ProcessedEventIds, eventId);

            try
            {
                var summaryResult = await _summaries.UpdateOneAsync(summaryFilter, summaryUpdate, new UpdateOptions { IsUpsert = true }, ct);
                if (summaryResult.ModifiedCount == 0 && summaryResult.UpsertedId == null)
                {
                    _logger.LogInformation(
                        "[Affiliate:Projection:Reversed] Summary already processed EventId {EventId} - skipping (idempotent retry)",
                        eventId);
                }
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                _logger.LogInformation(
                    "[Affiliate:Projection:Reversed] Summary already processed EventId {EventId} — skipping (idempotent retry)",
                    eventId);
            }
        }

        await AffiliateMoneyProjectionConsistency.RecomputeAffiliateMoneyAggregatesAsync(
            _orders,
            _dashboards,
            _summaries,
            tenantId,
            evt.AffiliateAccountId,
            referralCode: null,
            ct);

        _logger.LogInformation(
            "[Affiliate:Projection:Reversed] All projections updated | OrderId: {OrderId} | ItemId: {ItemId}",
            evt.OrderId, evt.OrderItemId);
    }

    private static ReversalProjectionState CalculateReversalState(
        AffiliateOrderDocument order,
        AffiliateOrderItemSubDocument? item,
        decimal reversedAmount,
        Guid orderId,
        Guid orderItemId)
    {
        if (item == null)
        {
            throw new InvalidOperationException(
                $"AffiliateCommissionReversedEvent cannot be acknowledged before its earned item projection exists. OrderId={orderId}; OrderItemId={orderItemId}.");
        }

        if (reversedAmount <= 0m)
        {
            throw new InvalidOperationException(
                $"AffiliateCommissionReversedEvent has a non-positive reversal amount. OrderId={orderId}; OrderItemId={orderItemId}; ReversedAmount={reversedAmount}.");
        }

        var itemReversedAmount = item.ReversedAmount + reversedAmount;
        if (itemReversedAmount > item.CommissionAmount)
        {
            throw new InvalidOperationException(
                $"AffiliateCommissionReversedEvent exceeds projected earned commission. OrderId={orderId}; OrderItemId={orderItemId}; ProjectedEarned={item.CommissionAmount}; AttemptedReversed={itemReversedAmount}.");
        }

        var orderReversedAmount = order.TotalReversed + reversedAmount;
        if (orderReversedAmount > order.TotalCommission)
        {
            throw new InvalidOperationException(
                $"AffiliateCommissionReversedEvent exceeds projected order commission. OrderId={orderId}; ProjectedEarned={order.TotalCommission}; AttemptedReversed={orderReversedAmount}.");
        }

        return new ReversalProjectionState(
            itemReversedAmount,
            itemReversedAmount >= item.CommissionAmount,
            orderReversedAmount,
            orderReversedAmount >= order.TotalCommission ? "Reversed" : "PartiallyReversed");
    }

    private sealed record ReversalProjectionState(
        decimal ItemReversedAmount,
        bool IsItemFullyReversed,
        decimal OrderReversedAmount,
        string OrderStatus);
}

// ═══════════════════════════════════════════════════════════════════
// SETTLEMENT PROJECTIONS (AF-7)
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// AF-7: Projection consumer for AffiliateCommissionApprovedEvent.
///
/// Updates:
/// 1. AffiliateDashboardDocument — increments TotalApproved
/// 2. AffiliateOrderDocument — updates item settlementStatus, order status
/// 3. AffiliateEarningsSummaryDocument — increments TotalApproved
/// </summary>
public class AffiliateCommissionApprovedProjectionConsumer : IConsumer<AffiliateCommissionApprovedEvent>
{
    private readonly IMongoCollection<AffiliateOrderDocument> _orders;
    private readonly IMongoCollection<AffiliateDashboardDocument> _dashboards;
    private readonly IMongoCollection<AffiliateEarningsSummaryDocument> _summaries;
    private readonly ILogger<AffiliateCommissionApprovedProjectionConsumer> _logger;

    public AffiliateCommissionApprovedProjectionConsumer(
        IMongoDatabase mongoDatabase,
        ILogger<AffiliateCommissionApprovedProjectionConsumer> logger)
    {
        _orders = mongoDatabase.GetCollection<AffiliateOrderDocument>("affiliate_orders");
        _dashboards = mongoDatabase.GetCollection<AffiliateDashboardDocument>("affiliate_dashboards");
        _summaries = mongoDatabase.GetCollection<AffiliateEarningsSummaryDocument>("affiliate_earnings_summaries");
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<AffiliateCommissionApprovedEvent> context)
    {
        var evt = context.Message;
        var ct = context.CancellationToken;
        var eventId = evt.EventId.ToString();
        var tenantId = AffiliateMoneyProjectionConsistency.ValidateAuthorityMetadata(
            evt.StoreTenantId,
            evt.TenantId,
            evt.EventVersion,
            evt.OrderId,
            nameof(AffiliateCommissionApprovedEvent));

        _logger.LogInformation(
            "[Affiliate:Projection:Approved] OrderId: {OrderId} | TotalApproved: {Total} {Currency}",
            evt.OrderId, evt.TotalApproved, evt.Currency);

        // ─── 1. UPDATE DASHBOARD ────────────────────────────────────────
        // Counter update and idempotency marker must be atomic on the dashboard document.
        {
            var dashFilter = Builders<AffiliateDashboardDocument>.Filter.And(
                Builders<AffiliateDashboardDocument>.Filter.Eq(d => d.TenantId, tenantId),
                Builders<AffiliateDashboardDocument>.Filter.Eq(d => d.AffiliateAccountId, evt.AffiliateAccountId),
                !Builders<AffiliateDashboardDocument>.Filter.AnyEq(d => d.ProcessedEventIds, eventId));

            var dashUpdate = Builders<AffiliateDashboardDocument>.Update
                .SetOnInsert(d => d.Id, Guid.NewGuid())
                .SetOnInsert(d => d.TenantId, tenantId)
                .SetOnInsert(d => d.AffiliateAccountId, evt.AffiliateAccountId)
                .Inc(d => d.TotalApproved, evt.TotalApproved)
                .Set(d => d.UpdatedAtUtc, DateTime.UtcNow)
                .AddToSet(d => d.ProcessedEventIds, eventId);

            try
            {
                var dashResult = await _dashboards.UpdateOneAsync(dashFilter, dashUpdate, new UpdateOptions { IsUpsert = true }, ct);
                if (dashResult.ModifiedCount == 0 && dashResult.UpsertedId == null)
                {
                    _logger.LogInformation(
                        "[Affiliate:Projection:Approved] Dashboard already processed EventId {EventId} - skipping (idempotent retry)",
                        eventId);
                }
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                _logger.LogInformation(
                    "[Affiliate:Projection:Approved] Dashboard already processed EventId {EventId} — skipping (idempotent retry)",
                    eventId);
            }
        }

        // 2. Update order — set all non-reversed Earned items to Approved atomically
        // Use arrayFilters to avoid race conditions from read-modify-write
        var orderFilter = AffiliateMoneyProjectionConsistency.BuildApprovalFreshFilter(
            tenantId,
            evt.OrderId,
            evt.EventVersion,
            eventId);

        var orderUpdate = Builders<AffiliateOrderDocument>.Update
            .Set("items.$[elem].settlementStatus", "Approved")
            .Inc(d => d.TotalApproved, evt.TotalApproved)
            .Set(d => d.UpdatedAtUtc, DateTime.UtcNow)
            .Set(d => d.LastCommissionApprovedEventOccurredAtUtc, evt.OccurredAtUtc)
            .Set(d => d.LastCommissionApprovedEventVersion, evt.EventVersion)
            .AddToSet(d => d.ProcessedEventIds, eventId);

        var arrayFilters = new[]
        {
            new BsonDocumentArrayFilterDefinition<BsonDocument>(
                new BsonDocument
                {
                    { "elem.isReversed", false },
                    { "elem.settlementStatus", "Earned" }
                })
        };

        var updateOptions = new UpdateOptions { ArrayFilters = arrayFilters };
        var orderResult = await _orders.UpdateOneAsync(orderFilter, orderUpdate, updateOptions, ct);

        // After atomic update, determine new status by reading current state
        // AF-RACE-FIX: Use optimistic concurrency control to prevent status regression
        // with bounded retry loop for eventual consistency
        if (orderResult.ModifiedCount > 0)
        {
            const int maxRetries = 3;
            var retryCount = 0;
            var baseDelayMs = 50;
            var statusUpdated = false;

            while (retryCount < maxRetries && !statusUpdated)
            {
                var order = await _orders.Find(
                    Builders<AffiliateOrderDocument>.Filter.And(
                        Builders<AffiliateOrderDocument>.Filter.Eq(d => d.TenantId, tenantId),
                        Builders<AffiliateOrderDocument>.Filter.Eq(d => d.OrderId, evt.OrderId)
                    )).FirstOrDefaultAsync(ct);

                if (order != null)
                {
                    var readUpdatedAtUtc = order.UpdatedAtUtc;
                    var newStatus = DetermineOrderStatus(order.Items);
                    
                    // Include UpdatedAtUtc in filter to ensure we only update if document hasn't changed
                    var statusFilter = Builders<AffiliateOrderDocument>.Filter.And(
                        Builders<AffiliateOrderDocument>.Filter.Eq(d => d.Id, order.Id),
                        Builders<AffiliateOrderDocument>.Filter.Eq(d => d.UpdatedAtUtc, readUpdatedAtUtc));
                    
                    var statusUpdate = Builders<AffiliateOrderDocument>.Update
                        .Set(d => d.Status, newStatus)
                        .Set(d => d.UpdatedAtUtc, DateTime.UtcNow);
                    
                    var statusResult = await _orders.UpdateOneAsync(statusFilter, statusUpdate, cancellationToken: ct);
                    
                    if (statusResult.ModifiedCount > 0)
                    {
                        statusUpdated = true;
                    }
                    else
                    {
                        retryCount++;
                        if (retryCount < maxRetries)
                        {
                            // Exponential backoff: 50ms, 100ms, 200ms
                            var delayMs = baseDelayMs * (1 << (retryCount - 1));
                            _logger.LogInformation(
                                "[Affiliate:Projection:Approved] Status update contention detected, retrying ({Attempt}/{MaxRetries}) after {DelayMs}ms | OrderId: {OrderId}",
                                retryCount, maxRetries, delayMs, evt.OrderId);
                            await Task.Delay(delayMs, ct);
                        }
                    }
                }
                else
                {
                    // Order not found after item update - unexpected but break retry loop
                    break;
                }
            }

            if (!statusUpdated)
            {
                // After all retries, NACK the message to ensure eventual consistency
                // MassTransit will retry the entire message processing
                throw new InvalidOperationException(
                    $"[Affiliate:Projection:Approved] Failed to update order status after {maxRetries} attempts due to persistent concurrent modifications | OrderId: {evt.OrderId}");
            }
        }

        // ─── 3. UPDATE EARNINGS SUMMARY ─────────────────────────────────
        // Counter update and idempotency marker must be atomic on the summary document.
        {
            var summaryFilter = Builders<AffiliateEarningsSummaryDocument>.Filter.And(
                Builders<AffiliateEarningsSummaryDocument>.Filter.Eq(d => d.TenantId, tenantId),
                Builders<AffiliateEarningsSummaryDocument>.Filter.Eq(d => d.AffiliateAccountId, evt.AffiliateAccountId),
                Builders<AffiliateEarningsSummaryDocument>.Filter.Eq(d => d.Currency, evt.Currency),
                !Builders<AffiliateEarningsSummaryDocument>.Filter.AnyEq(d => d.ProcessedEventIds, eventId));

            var summaryUpdate = Builders<AffiliateEarningsSummaryDocument>.Update
                .SetOnInsert(d => d.Id, Guid.NewGuid())
                .SetOnInsert(d => d.TenantId, tenantId)
                .SetOnInsert(d => d.AffiliateAccountId, evt.AffiliateAccountId)
                .SetOnInsert(d => d.Currency, evt.Currency)
                .Inc(d => d.TotalApproved, evt.TotalApproved)
                .Set(d => d.UpdatedAtUtc, DateTime.UtcNow)
                .AddToSet(d => d.ProcessedEventIds, eventId);

            try
            {
                var summaryResult = await _summaries.UpdateOneAsync(summaryFilter, summaryUpdate, new UpdateOptions { IsUpsert = true }, ct);
                if (summaryResult.ModifiedCount == 0 && summaryResult.UpsertedId == null)
                {
                    _logger.LogInformation(
                        "[Affiliate:Projection:Approved] Summary already processed EventId {EventId} - skipping (idempotent retry)",
                        eventId);
                }
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                _logger.LogInformation(
                    "[Affiliate:Projection:Approved] Summary already processed EventId {EventId} — skipping (idempotent retry)",
                    eventId);
            }
        }

        await AffiliateMoneyProjectionConsistency.RecomputeAffiliateMoneyAggregatesAsync(
            _orders,
            _dashboards,
            _summaries,
            tenantId,
            evt.AffiliateAccountId,
            referralCode: null,
            ct);

        _logger.LogInformation(
            "[Affiliate:Projection:Approved] All projections updated | OrderId: {OrderId}", evt.OrderId);
    }

    private static string DetermineOrderStatus(List<AffiliateOrderItemSubDocument> items)
    {
        if (items.All(i => i.IsReversed)) return "Reversed";
        if (items.Any(i => i.IsReversed)) return "PartiallyReversed";
        if (items.Any(i => i.SettlementStatus == "Paid")) return "Paid";
        if (items.Any(i => i.SettlementStatus == "Approved")) return "Approved";
        return "Earned";
    }
}

/// <summary>
/// AF-7: Projection consumer for AffiliateCommissionPaidEvent.
///
/// Updates:
/// 1. AffiliateDashboardDocument — increments TotalPaid
/// 2. AffiliateOrderDocument — updates item settlementStatus, order status
/// 3. AffiliateEarningsSummaryDocument — increments TotalPaid
/// </summary>
public class AffiliateCommissionPaidProjectionConsumer : IConsumer<AffiliateCommissionPaidEvent>
{
    private readonly IMongoCollection<AffiliateOrderDocument> _orders;
    private readonly IMongoCollection<AffiliateDashboardDocument> _dashboards;
    private readonly IMongoCollection<AffiliateEarningsSummaryDocument> _summaries;
    private readonly ILogger<AffiliateCommissionPaidProjectionConsumer> _logger;

    public AffiliateCommissionPaidProjectionConsumer(
        IMongoDatabase mongoDatabase,
        ILogger<AffiliateCommissionPaidProjectionConsumer> logger)
    {
        _orders = mongoDatabase.GetCollection<AffiliateOrderDocument>("affiliate_orders");
        _dashboards = mongoDatabase.GetCollection<AffiliateDashboardDocument>("affiliate_dashboards");
        _summaries = mongoDatabase.GetCollection<AffiliateEarningsSummaryDocument>("affiliate_earnings_summaries");
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<AffiliateCommissionPaidEvent> context)
    {
        var evt = context.Message;
        var ct = context.CancellationToken;
        var eventId = evt.EventId.ToString();
        var tenantId = AffiliateMoneyProjectionConsistency.ValidateAuthorityMetadata(
            evt.StoreTenantId,
            evt.TenantId,
            evt.EventVersion,
            evt.OrderId,
            nameof(AffiliateCommissionPaidEvent));

        _logger.LogInformation(
            "[Affiliate:Projection:Paid] OrderId: {OrderId} | TotalPaid: {Total} {Currency}",
            evt.OrderId, evt.TotalPaid, evt.Currency);

        // ─── 1. UPDATE DASHBOARD ────────────────────────────────────────
        // Counter update and idempotency marker must be atomic on the dashboard document.
        {
            var dashFilter = Builders<AffiliateDashboardDocument>.Filter.And(
                Builders<AffiliateDashboardDocument>.Filter.Eq(d => d.TenantId, tenantId),
                Builders<AffiliateDashboardDocument>.Filter.Eq(d => d.AffiliateAccountId, evt.AffiliateAccountId),
                !Builders<AffiliateDashboardDocument>.Filter.AnyEq(d => d.ProcessedEventIds, eventId));

            var dashUpdate = Builders<AffiliateDashboardDocument>.Update
                .SetOnInsert(d => d.Id, Guid.NewGuid())
                .SetOnInsert(d => d.TenantId, tenantId)
                .SetOnInsert(d => d.AffiliateAccountId, evt.AffiliateAccountId)
                .Inc(d => d.TotalPaid, evt.TotalPaid)
                .Set(d => d.UpdatedAtUtc, DateTime.UtcNow)
                .AddToSet(d => d.ProcessedEventIds, eventId);

            try
            {
                var dashResult = await _dashboards.UpdateOneAsync(dashFilter, dashUpdate, new UpdateOptions { IsUpsert = true }, ct);
                if (dashResult.ModifiedCount == 0 && dashResult.UpsertedId == null)
                {
                    _logger.LogInformation(
                        "[Affiliate:Projection:Paid] Dashboard already processed EventId {EventId} - skipping (idempotent retry)",
                        eventId);
                }
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                _logger.LogInformation(
                    "[Affiliate:Projection:Paid] Dashboard already processed EventId {EventId} — skipping (idempotent retry)",
                    eventId);
            }
        }

        // 2. Update order — set all Approved items to Paid atomically
        // Use arrayFilters to avoid race conditions from read-modify-write
        var orderFilter = AffiliateMoneyProjectionConsistency.BuildPaidFreshFilter(
            tenantId,
            evt.OrderId,
            evt.EventVersion,
            eventId);

        var orderUpdate = Builders<AffiliateOrderDocument>.Update
            .Set("items.$[elem].settlementStatus", "Paid")
            .Inc(d => d.TotalPaid, evt.TotalPaid)
            .Set(d => d.UpdatedAtUtc, DateTime.UtcNow)
            .Set(d => d.LastCommissionPaidEventOccurredAtUtc, evt.OccurredAtUtc)
            .Set(d => d.LastCommissionPaidEventVersion, evt.EventVersion)
            .AddToSet(d => d.ProcessedEventIds, eventId);

        var arrayFilters = new[]
        {
            new BsonDocumentArrayFilterDefinition<BsonDocument>(
                new BsonDocument
                {
                    { "elem.settlementStatus", "Approved" }
                })
        };

        var updateOptions = new UpdateOptions { ArrayFilters = arrayFilters };
        var orderResult = await _orders.UpdateOneAsync(orderFilter, orderUpdate, updateOptions, ct);

        // After atomic update, determine new status by reading current state
        // AF-RACE-FIX: Use optimistic concurrency control to prevent status regression
        // with bounded retry loop for eventual consistency
        if (orderResult.ModifiedCount > 0)
        {
            const int maxRetries = 3;
            var retryCount = 0;
            var baseDelayMs = 50;
            var statusUpdated = false;

            while (retryCount < maxRetries && !statusUpdated)
            {
                var order = await _orders.Find(
                    Builders<AffiliateOrderDocument>.Filter.And(
                        Builders<AffiliateOrderDocument>.Filter.Eq(d => d.TenantId, tenantId),
                        Builders<AffiliateOrderDocument>.Filter.Eq(d => d.OrderId, evt.OrderId)
                    )).FirstOrDefaultAsync(ct);

                if (order != null)
                {
                    var readUpdatedAtUtc = order.UpdatedAtUtc;
                    var newStatus = DetermineOrderStatus(order.Items);
                    
                    // Include UpdatedAtUtc in filter to ensure we only update if document hasn't changed
                    var statusFilter = Builders<AffiliateOrderDocument>.Filter.And(
                        Builders<AffiliateOrderDocument>.Filter.Eq(d => d.Id, order.Id),
                        Builders<AffiliateOrderDocument>.Filter.Eq(d => d.UpdatedAtUtc, readUpdatedAtUtc));
                    
                    var statusUpdate = Builders<AffiliateOrderDocument>.Update
                        .Set(d => d.Status, newStatus)
                        .Set(d => d.UpdatedAtUtc, DateTime.UtcNow);
                    
                    var statusResult = await _orders.UpdateOneAsync(statusFilter, statusUpdate, cancellationToken: ct);
                    
                    if (statusResult.ModifiedCount > 0)
                    {
                        statusUpdated = true;
                    }
                    else
                    {
                        retryCount++;
                        if (retryCount < maxRetries)
                        {
                            // Exponential backoff: 50ms, 100ms, 200ms
                            var delayMs = baseDelayMs * (1 << (retryCount - 1));
                            _logger.LogInformation(
                                "[Affiliate:Projection:Paid] Status update contention detected, retrying ({Attempt}/{MaxRetries}) after {DelayMs}ms | OrderId: {OrderId}",
                                retryCount, maxRetries, delayMs, evt.OrderId);
                            await Task.Delay(delayMs, ct);
                        }
                    }
                }
                else
                {
                    // Order not found after item update - unexpected but break retry loop
                    break;
                }
            }

            if (!statusUpdated)
            {
                // After all retries, NACK the message to ensure eventual consistency
                // MassTransit will retry the entire message processing
                throw new InvalidOperationException(
                    $"[Affiliate:Projection:Paid] Failed to update order status after {maxRetries} attempts due to persistent concurrent modifications | OrderId: {evt.OrderId}");
            }
        }

        // ─── 3. UPDATE EARNINGS SUMMARY ─────────────────────────────────
        // Counter update and idempotency marker must be atomic on the summary document.
        {
            var summaryFilter = Builders<AffiliateEarningsSummaryDocument>.Filter.And(
                Builders<AffiliateEarningsSummaryDocument>.Filter.Eq(d => d.TenantId, tenantId),
                Builders<AffiliateEarningsSummaryDocument>.Filter.Eq(d => d.AffiliateAccountId, evt.AffiliateAccountId),
                Builders<AffiliateEarningsSummaryDocument>.Filter.Eq(d => d.Currency, evt.Currency),
                !Builders<AffiliateEarningsSummaryDocument>.Filter.AnyEq(d => d.ProcessedEventIds, eventId));

            var summaryUpdate = Builders<AffiliateEarningsSummaryDocument>.Update
                .SetOnInsert(d => d.Id, Guid.NewGuid())
                .SetOnInsert(d => d.TenantId, tenantId)
                .SetOnInsert(d => d.AffiliateAccountId, evt.AffiliateAccountId)
                .SetOnInsert(d => d.Currency, evt.Currency)
                .Inc(d => d.TotalPaid, evt.TotalPaid)
                .Set(d => d.UpdatedAtUtc, DateTime.UtcNow)
                .AddToSet(d => d.ProcessedEventIds, eventId);

            try
            {
                var summaryResult = await _summaries.UpdateOneAsync(summaryFilter, summaryUpdate, new UpdateOptions { IsUpsert = true }, ct);
                if (summaryResult.ModifiedCount == 0 && summaryResult.UpsertedId == null)
                {
                    _logger.LogInformation(
                        "[Affiliate:Projection:Paid] Summary already processed EventId {EventId} - skipping (idempotent retry)",
                        eventId);
                }
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                _logger.LogInformation(
                    "[Affiliate:Projection:Paid] Summary already processed EventId {EventId} — skipping (idempotent retry)",
                    eventId);
            }
        }

        await AffiliateMoneyProjectionConsistency.RecomputeAffiliateMoneyAggregatesAsync(
            _orders,
            _dashboards,
            _summaries,
            tenantId,
            evt.AffiliateAccountId,
            referralCode: null,
            ct);

        _logger.LogInformation(
            "[Affiliate:Projection:Paid] All projections updated | OrderId: {OrderId}", evt.OrderId);
    }

    private static string DetermineOrderStatus(List<AffiliateOrderItemSubDocument> items)
    {
        if (items.All(i => i.IsReversed)) return "Reversed";
        if (items.Any(i => i.IsReversed)) return "PartiallyReversed";
        if (items.Any(i => i.SettlementStatus == "Paid")) return "Paid";
        if (items.Any(i => i.SettlementStatus == "Approved")) return "Approved";
        return "Earned";
    }
}

internal static class AffiliateMoneyProjectionConsistency
{
    public static Guid ValidateAuthorityMetadata(
        Guid storeTenantId,
        Guid? eventTenantId,
        int eventVersion,
        Guid orderId,
        string eventName)
    {
        if (storeTenantId == Guid.Empty)
        {
            throw new InvalidOperationException(
                $"{eventName} is missing StoreTenantId. OrderId={orderId}.");
        }

        if (eventTenantId.HasValue &&
            eventTenantId.Value != Guid.Empty &&
            eventTenantId.Value != storeTenantId)
        {
            throw new InvalidOperationException(
                $"{eventName} has conflicting tenant authority. TenantId={eventTenantId.Value}; StoreTenantId={storeTenantId}; OrderId={orderId}.");
        }

        if (eventVersion <= 0)
        {
            throw new InvalidOperationException(
                $"{eventName} has invalid EventVersion. OrderId={orderId}; EventVersion={eventVersion}.");
        }

        return storeTenantId;
    }

    public static FilterDefinition<AffiliateOrderDocument> BuildEarnedFreshFilter(
        Guid tenantId,
        Guid orderId,
        int eventVersion) =>
        Builders<AffiliateOrderDocument>.Filter.And(
            Builders<AffiliateOrderDocument>.Filter.Eq(d => d.TenantId, tenantId),
            Builders<AffiliateOrderDocument>.Filter.Eq(d => d.OrderId, orderId),
            BuildFreshnessFilter(
                "lastCommissionEarnedEventVersion",
                d => d.LastCommissionEarnedEventVersion,
                eventVersion));

    public static FilterDefinition<AffiliateOrderDocument> BuildReversalFreshFilter(
        Guid tenantId,
        Guid orderId,
        Guid orderItemId,
        string eventId,
        decimal expectedReversedAmount) =>
        Builders<AffiliateOrderDocument>.Filter.And(
            Builders<AffiliateOrderDocument>.Filter.Eq(d => d.TenantId, tenantId),
            Builders<AffiliateOrderDocument>.Filter.Eq(d => d.OrderId, orderId),
            !Builders<AffiliateOrderDocument>.Filter.AnyEq(d => d.ProcessedEventIds, eventId),
            Builders<AffiliateOrderDocument>.Filter.ElemMatch(
                d => d.Items,
                i => i.OrderItemId == orderItemId &&
                     !i.IsReversed &&
                     i.ReversedAmount == expectedReversedAmount));

    public static FilterDefinition<AffiliateOrderDocument> BuildApprovalFreshFilter(
        Guid tenantId,
        Guid orderId,
        int eventVersion,
        string eventId) =>
        Builders<AffiliateOrderDocument>.Filter.And(
            Builders<AffiliateOrderDocument>.Filter.Eq(d => d.TenantId, tenantId),
            Builders<AffiliateOrderDocument>.Filter.Eq(d => d.OrderId, orderId),
            !Builders<AffiliateOrderDocument>.Filter.AnyEq(d => d.ProcessedEventIds, eventId),
            Builders<AffiliateOrderDocument>.Filter.ElemMatch(
                d => d.Items,
                i => !i.IsReversed && i.SettlementStatus == "Earned"),
            BuildFreshnessFilter(
                "lastCommissionApprovedEventVersion",
                d => d.LastCommissionApprovedEventVersion,
                eventVersion));

    public static FilterDefinition<AffiliateOrderDocument> BuildPaidFreshFilter(
        Guid tenantId,
        Guid orderId,
        int eventVersion,
        string eventId) =>
        Builders<AffiliateOrderDocument>.Filter.And(
            Builders<AffiliateOrderDocument>.Filter.Eq(d => d.TenantId, tenantId),
            Builders<AffiliateOrderDocument>.Filter.Eq(d => d.OrderId, orderId),
            !Builders<AffiliateOrderDocument>.Filter.AnyEq(d => d.ProcessedEventIds, eventId),
            Builders<AffiliateOrderDocument>.Filter.ElemMatch(
                d => d.Items,
                i => i.SettlementStatus == "Approved"),
            BuildFreshnessFilter(
                "lastCommissionPaidEventVersion",
                d => d.LastCommissionPaidEventVersion,
                eventVersion));

    public static async Task RecomputeAffiliateMoneyAggregatesAsync(
        IMongoCollection<AffiliateOrderDocument> orders,
        IMongoCollection<AffiliateDashboardDocument> dashboards,
        IMongoCollection<AffiliateEarningsSummaryDocument> summaries,
        Guid tenantId,
        Guid affiliateAccountId,
        string? referralCode,
        CancellationToken cancellationToken)
    {
        var orderRows = await orders.Find(
                Builders<AffiliateOrderDocument>.Filter.And(
                    Builders<AffiliateOrderDocument>.Filter.Eq(d => d.TenantId, tenantId),
                    Builders<AffiliateOrderDocument>.Filter.Eq(d => d.AffiliateAccountId, affiliateAccountId)))
            .ToListAsync(cancellationToken);

        var nowUtc = DateTime.UtcNow;
        var totalEarned = orderRows.Sum(row => row.TotalCommission);
        var totalReversed = orderRows.Sum(row => row.TotalReversed);
        var totalApproved = orderRows.Sum(row => row.TotalApproved);
        var totalPaid = orderRows.Sum(row => row.TotalPaid);

        var dashboardUpdate = Builders<AffiliateDashboardDocument>.Update
            .SetOnInsert(d => d.Id, Guid.NewGuid())
            .SetOnInsert(d => d.TenantId, tenantId)
            .SetOnInsert(d => d.AffiliateAccountId, affiliateAccountId)
            .Set(d => d.TotalEarned, totalEarned)
            .Set(d => d.TotalReversed, totalReversed)
            .Set(d => d.NetEarnings, totalEarned - totalReversed)
            .Set(d => d.TotalApproved, totalApproved)
            .Set(d => d.TotalPaid, totalPaid)
            .Set(d => d.TotalOrders, orderRows.Count(row => row.TotalCommission > 0m))
            .Set(d => d.LastCommissionAtUtc, nowUtc)
            .Set(d => d.UpdatedAtUtc, nowUtc);

        if (!string.IsNullOrWhiteSpace(referralCode))
        {
            dashboardUpdate = dashboardUpdate.Set(d => d.ReferralCode, referralCode);
        }

        await dashboards.UpdateOneAsync(
            Builders<AffiliateDashboardDocument>.Filter.And(
                Builders<AffiliateDashboardDocument>.Filter.Eq(d => d.TenantId, tenantId),
                Builders<AffiliateDashboardDocument>.Filter.Eq(d => d.AffiliateAccountId, affiliateAccountId)),
            dashboardUpdate,
            new UpdateOptions { IsUpsert = true },
            cancellationToken);

        var groups = orderRows
            .Where(row => !string.IsNullOrWhiteSpace(row.Currency))
            .GroupBy(row => row.Currency)
            .ToList();

        foreach (var group in groups)
        {
            var earned = group.Sum(row => row.TotalCommission);
            var reversed = group.Sum(row => row.TotalReversed);

            await summaries.UpdateOneAsync(
                Builders<AffiliateEarningsSummaryDocument>.Filter.And(
                    Builders<AffiliateEarningsSummaryDocument>.Filter.Eq(d => d.TenantId, tenantId),
                    Builders<AffiliateEarningsSummaryDocument>.Filter.Eq(d => d.AffiliateAccountId, affiliateAccountId),
                    Builders<AffiliateEarningsSummaryDocument>.Filter.Eq(d => d.Currency, group.Key)),
                Builders<AffiliateEarningsSummaryDocument>.Update
                    .SetOnInsert(d => d.Id, Guid.NewGuid())
                    .SetOnInsert(d => d.TenantId, tenantId)
                    .SetOnInsert(d => d.AffiliateAccountId, affiliateAccountId)
                    .SetOnInsert(d => d.Currency, group.Key)
                    .Set(d => d.TotalEarned, earned)
                    .Set(d => d.TotalReversed, reversed)
                    .Set(d => d.NetEarnings, earned - reversed)
                    .Set(d => d.TotalApproved, group.Sum(row => row.TotalApproved))
                    .Set(d => d.TotalPaid, group.Sum(row => row.TotalPaid))
                    .Set(d => d.UpdatedAtUtc, nowUtc),
                new UpdateOptions { IsUpsert = true },
                cancellationToken);
        }

        var activeCurrencies = groups.Select(group => group.Key).ToList();
        var staleCurrencyFilter = Builders<AffiliateEarningsSummaryDocument>.Filter.And(
            Builders<AffiliateEarningsSummaryDocument>.Filter.Eq(d => d.TenantId, tenantId),
            Builders<AffiliateEarningsSummaryDocument>.Filter.Eq(d => d.AffiliateAccountId, affiliateAccountId),
            activeCurrencies.Count == 0
                ? Builders<AffiliateEarningsSummaryDocument>.Filter.Empty
                : Builders<AffiliateEarningsSummaryDocument>.Filter.Nin(d => d.Currency, activeCurrencies));

        await summaries.UpdateManyAsync(
            staleCurrencyFilter,
            Builders<AffiliateEarningsSummaryDocument>.Update
                .Set(d => d.TotalEarned, 0m)
                .Set(d => d.TotalReversed, 0m)
                .Set(d => d.NetEarnings, 0m)
                .Set(d => d.TotalApproved, 0m)
                .Set(d => d.TotalPaid, 0m)
                .Set(d => d.UpdatedAtUtc, nowUtc),
            cancellationToken: cancellationToken);
    }

    private static FilterDefinition<AffiliateOrderDocument> BuildFreshnessFilter(
        string versionFieldName,
        System.Linq.Expressions.Expression<Func<AffiliateOrderDocument, int>> versionField,
        int eventVersion) =>
        Builders<AffiliateOrderDocument>.Filter.Or(
            Builders<AffiliateOrderDocument>.Filter.Exists(versionFieldName, false),
            Builders<AffiliateOrderDocument>.Filter.Eq(versionField, 0),
            Builders<AffiliateOrderDocument>.Filter.Lt(versionField, eventVersion));
}

/// <summary>
/// AF-21: Order Lifecycle Projection Consumer
///
/// Listens to order lifecycle events and updates AffiliateOrderDocument with:
/// - OrderStatus (from OrderStatusChangedEvent, OrderCompletedEvent, OrderCancelledEvent)
/// - FulfillmentStatus (from FulfillmentDeliveredEvent, OrderCompletedEvent, OrderFulfillmentCancelledEvent)
/// - PaymentMethod + PaidAmount (from OrderCreatedEvent)
///
/// CRITICAL RULES:
/// 1. ONLY updates existing AffiliateOrderDocument (created by CommissionEarnedEvent)
/// 2. NEVER creates new documents (no upsert)
/// 3. ALWAYS filters by TenantId to enforce tenant isolation
/// 4. Idempotent via ProcessedEventIds
/// 5. NO SQL queries — pure event-driven projection
///
/// TENANT SAFETY:
/// - TenantId filter prevents cross-tenant updates
/// - StoreTenantId from commission events matches Order.TenantId
/// - Affiliate portal reads by AffiliateAccountId (no tenant filter)
/// - Admin portal reads by TenantId (tenant-scoped)
/// </summary>
public class OrderLifecycleAffiliateProjectionConsumer :
    IConsumer<OrderCreatedEvent>,
    IConsumer<OrderStatusChangedEvent>,
    IConsumer<OrderPaymentStatusChangedEvent>,
    IConsumer<OrderCompletedEvent>,
    IConsumer<OrderCancelledEvent>,
    IConsumer<FulfillmentConfirmedEvent>,
    IConsumer<OrderPackedEvent>,
    IConsumer<OrderOutForDeliveryEvent>,
    IConsumer<FulfillmentDeliveredEvent>,
    IConsumer<OrderReturnedEvent>,
    IConsumer<OrderDeliveryFailedEvent>,
    IConsumer<OrderFulfillmentCancelledEvent>
{
    private readonly IMongoCollection<AffiliateOrderDocument> _orders;
    private readonly ILogger<OrderLifecycleAffiliateProjectionConsumer> _logger;

    public OrderLifecycleAffiliateProjectionConsumer(
        IMongoDatabase mongoDatabase,
        ILogger<OrderLifecycleAffiliateProjectionConsumer> logger)
    {
        _orders = mongoDatabase.GetCollection<AffiliateOrderDocument>("affiliate_orders");
        _logger = logger;
    }

    /// <summary>
    /// Stamps PaymentMethod and PaidAmount when order is created.
    /// This runs BEFORE commission is earned, so we use UpdateMany (may be 0 docs).
    /// </summary>
    public async Task Consume(ConsumeContext<OrderCreatedEvent> context)
    {
        var evt = context.Message;
        var ct = context.CancellationToken;
        var eventId = evt.EventId.ToString();
        var tenantId = evt.TenantId ?? Guid.Empty;

        _logger.LogInformation(
            "[Affiliate:Lifecycle:OrderCreated] OrderId: {OrderId} | PaymentMethod: {PaymentMethod} | Total: {Total} | Status: {Status}",
            evt.OrderId, evt.PaymentMethodCode, evt.Total, evt.Status);

        // Check if already processed (idempotency)
        var existingDoc = await _orders.Find(d => 
            d.TenantId == tenantId && 
            d.OrderId == evt.OrderId).FirstOrDefaultAsync(ct);

        if (existingDoc != null && existingDoc.ProcessedEventIds.Contains(eventId))
        {
            _logger.LogDebug(
                "[Affiliate:Lifecycle:OrderCreated] Event already processed | OrderId: {OrderId} | EventId: {EventId}",
                evt.OrderId, eventId);
            return;
        }

        if (existingDoc == null)
        {
            _logger.LogWarning(
                "[Affiliate:Lifecycle:OrderCreated] No affiliate order document found | OrderId: {OrderId} | TenantId: {TenantId}",
                evt.OrderId, tenantId);
            return;
        }

        var baseFilter = BuildOrderStatusLifecycleFilter(tenantId, evt.OrderId, evt.OccurredAtUtc, evt.EventVersion);

        // Build base update without PaidAmount
        var baseUpdate = Builders<AffiliateOrderDocument>.Update
            .Set(d => d.PaymentMethod, evt.PaymentMethodCode ?? "Unknown")
            .Set(d => d.OrderStatus, evt.Status.ToString())
            .Set(d => d.LastOrderStatusEventOccurredAtUtc, evt.OccurredAtUtc)
            .Set(d => d.LastOrderStatusEventVersion, evt.EventVersion)
            .Set(d => d.UpdatedAtUtc, DateTime.UtcNow)
            .AddToSet(d => d.ProcessedEventIds, eventId);

        // AF-ATOMIC: Split into two atomic updates to prevent race conditions
        // Update 1: Documents where no payment event has been processed yet - include PaidAmount
        var filterWithPaymentGuard = Builders<AffiliateOrderDocument>.Filter.And(
            baseFilter,
            Builders<AffiliateOrderDocument>.Filter.Or(
                Builders<AffiliateOrderDocument>.Filter.Eq(d => d.LastPaymentStatusEventVersion, 0),
                Builders<AffiliateOrderDocument>.Filter.Eq(d => d.LastPaymentStatusEventOccurredAtUtc, null)));

        var updateWithPaidAmount = Builders<AffiliateOrderDocument>.Update.Combine(
            baseUpdate,
            Builders<AffiliateOrderDocument>.Update.Set(d => d.PaidAmount, evt.IsPaid ? evt.Total : 0m));

        var resultWithPayment = await _orders.UpdateManyAsync(filterWithPaymentGuard, updateWithPaidAmount, cancellationToken: ct);

        // Update 2: Documents where payment event has already been processed - exclude PaidAmount
        var filterWithoutPaymentGuard = Builders<AffiliateOrderDocument>.Filter.And(
            baseFilter,
            Builders<AffiliateOrderDocument>.Filter.Ne(d => d.LastPaymentStatusEventVersion, 0),
            Builders<AffiliateOrderDocument>.Filter.Ne(d => d.LastPaymentStatusEventOccurredAtUtc, null));

        var resultWithoutPayment = await _orders.UpdateManyAsync(filterWithoutPaymentGuard, baseUpdate, cancellationToken: ct);

        var result = new { ModifiedCount = resultWithPayment.ModifiedCount + resultWithoutPayment.ModifiedCount };

        if (result.ModifiedCount > 0)
        {
            _logger.LogInformation(
                "[Affiliate:Lifecycle:OrderCreated] Updated {Count} affiliate order docs | OrderId: {OrderId} | OrderStatus: {Status} | PaymentMethod: {PaymentMethod}",
                result.ModifiedCount, evt.OrderId, evt.Status, evt.PaymentMethodCode);
        }
        else
        {
            _logger.LogWarning(
                "[Affiliate:Lifecycle:OrderCreated] No documents updated | OrderId: {OrderId} | TenantId: {TenantId}",
                evt.OrderId, tenantId);
        }
    }

    /// <summary>
    /// Updates OrderStatus when order status changes.
    /// </summary>
    public async Task Consume(ConsumeContext<OrderStatusChangedEvent> context)
    {
        var evt = context.Message;
        var ct = context.CancellationToken;
        var eventId = evt.EventId.ToString();
        var tenantId = evt.TenantId ?? Guid.Empty;

        _logger.LogInformation(
            "[Affiliate:Lifecycle:StatusChanged] OrderId: {OrderId} | {OldStatus} → {NewStatus}",
            evt.OrderId, evt.OldStatus, evt.NewStatus);

        // Check if already processed (idempotency)
        var existingDoc = await _orders.Find(d => 
            d.TenantId == tenantId && 
            d.OrderId == evt.OrderId).FirstOrDefaultAsync(ct);

        if (existingDoc != null && existingDoc.ProcessedEventIds.Contains(eventId))
        {
            _logger.LogDebug(
                "[Affiliate:Lifecycle:StatusChanged] Event already processed | OrderId: {OrderId} | EventId: {EventId}",
                evt.OrderId, eventId);
            return;
        }

        var filter = BuildOrderStatusLifecycleFilter(tenantId, evt.OrderId, evt.OccurredAtUtc, evt.EventVersion);

        var update = Builders<AffiliateOrderDocument>.Update
            .Set(d => d.OrderStatus, evt.NewStatus.ToString())
            .Set(d => d.LastOrderStatusEventOccurredAtUtc, evt.OccurredAtUtc)
            .Set(d => d.LastOrderStatusEventVersion, evt.EventVersion)
            .Set(d => d.UpdatedAtUtc, DateTime.UtcNow)
            .AddToSet(d => d.ProcessedEventIds, eventId);

        var result = await _orders.UpdateManyAsync(filter, update, cancellationToken: ct);

        if (result.ModifiedCount > 0)
        {
            _logger.LogInformation(
                "[Affiliate:Lifecycle:StatusChanged] Updated {Count} affiliate order docs | OrderId: {OrderId} | NewStatus: {NewStatus}",
                result.ModifiedCount, evt.OrderId, evt.NewStatus);
        }
        else
        {
            _logger.LogWarning(
                "[Affiliate:Lifecycle:StatusChanged] No documents updated | OrderId: {OrderId} | TenantId: {TenantId} | NewStatus: {NewStatus}",
                evt.OrderId, tenantId, evt.NewStatus);
        }
    }

    /// <summary>
    /// Updates PaidAmount when payment authority confirms a lifecycle transition.
    /// </summary>
    public async Task Consume(ConsumeContext<OrderPaymentStatusChangedEvent> context)
    {
        var evt = context.Message;
        var ct = context.CancellationToken;
        var eventId = evt.EventId.ToString();
        var tenantId = evt.TenantId ?? Guid.Empty;

        _logger.LogInformation(
            "[Affiliate:Lifecycle:PaymentChanged] OrderId: {OrderId} | {OldStatus} → {NewStatus} | AmountPaid: {AmountPaid}",
            evt.OrderId, evt.OldStatus, evt.NewStatus, evt.AmountPaid);

        var existingDoc = await _orders.Find(d =>
            d.TenantId == tenantId &&
            d.OrderId == evt.OrderId).FirstOrDefaultAsync(ct);

        if (existingDoc != null && existingDoc.ProcessedEventIds.Contains(eventId))
        {
            _logger.LogDebug(
                "[Affiliate:Lifecycle:PaymentChanged] Event already processed | OrderId: {OrderId} | EventId: {EventId}",
                evt.OrderId, eventId);
            return;
        }

        if (existingDoc == null)
        {
            _logger.LogWarning(
                "[Affiliate:Lifecycle:PaymentChanged] No affiliate order document found | OrderId: {OrderId} | TenantId: {TenantId}",
                evt.OrderId, tenantId);
            return;
        }

        var filter = BuildPaymentStatusLifecycleFilter(tenantId, evt.OrderId, evt.OccurredAtUtc, evt.EventVersion);
        
        // Determine paid amount based on payment status
        decimal paidAmount;
        if (string.Equals(evt.NewStatus.ToString(), "PartiallyPaid", StringComparison.Ordinal))
        {
            // PartiallyPaid requires AmountPaid - fail message if missing
            if (!evt.AmountPaid.HasValue)
            {
                throw new InvalidOperationException(
                    $"[Affiliate:Lifecycle:PaymentChanged] PartiallyPaid status requires AmountPaid | OrderId: {evt.OrderId} | EventId: {eventId} | Status: {evt.NewStatus}");
            }
            paidAmount = evt.AmountPaid.Value;
        }
        else if (PaymentStatusAuthority.IsPaidState(evt.NewStatus))
        {
            // Other paid states require AmountPaid - fail message if missing
            if (!evt.AmountPaid.HasValue)
            {
                throw new InvalidOperationException(
                    $"[Affiliate:Lifecycle:PaymentChanged] Paid status requires AmountPaid | OrderId: {evt.OrderId} | EventId: {eventId} | Status: {evt.NewStatus}");
            }
            paidAmount = evt.AmountPaid.Value;
        }
        else
        {
            // Non-paid states: set to 0
            paidAmount = 0m;
        }

        var update = Builders<AffiliateOrderDocument>.Update
            .Set(d => d.PaidAmount, paidAmount)
            .Set(d => d.LastPaymentStatusEventOccurredAtUtc, evt.OccurredAtUtc)
            .Set(d => d.LastPaymentStatusEventVersion, evt.EventVersion)
            .Set(d => d.UpdatedAtUtc, DateTime.UtcNow)
            .AddToSet(d => d.ProcessedEventIds, eventId);

        var result = await _orders.UpdateManyAsync(filter, update, cancellationToken: ct);

        if (result.ModifiedCount > 0)
        {
            _logger.LogInformation(
                "[Affiliate:Lifecycle:PaymentChanged] Updated {Count} affiliate order docs | OrderId: {OrderId} | NewStatus: {NewStatus} | PaidAmount: {PaidAmount}",
                result.ModifiedCount, evt.OrderId, evt.NewStatus, paidAmount);
        }
        else
        {
            _logger.LogWarning(
                "[Affiliate:Lifecycle:PaymentChanged] No documents updated | OrderId: {OrderId} | TenantId: {TenantId}",
                evt.OrderId, tenantId);
        }
    }

    /// <summary>
    /// Updates FulfillmentStatus when order is confirmed.
    /// </summary>
    public async Task Consume(ConsumeContext<FulfillmentConfirmedEvent> context)
    {
        var evt = context.Message;
        var ct = context.CancellationToken;
        var eventId = evt.EventId.ToString();
        var tenantId = evt.TenantId ?? Guid.Empty;

        _logger.LogInformation(
            "[Affiliate:Lifecycle:Confirmed] OrderId: {OrderId} | FulfillmentStatus: {Status}",
            evt.OrderId, evt.NewStatus);

        // Check if already processed (idempotency)
        var existingDoc = await _orders.Find(d => 
            d.TenantId == tenantId && 
            d.OrderId == evt.OrderId).FirstOrDefaultAsync(ct);

        if (existingDoc != null && existingDoc.ProcessedEventIds.Contains(eventId))
        {
            _logger.LogDebug(
                "[Affiliate:Lifecycle:Confirmed] Event already processed | OrderId: {OrderId} | EventId: {EventId}",
                evt.OrderId, eventId);
            return;
        }

        var filter = BuildFulfillmentLifecycleFilter(tenantId, evt.OrderId, evt.OccurredAtUtc, evt.EventVersion);

        var update = Builders<AffiliateOrderDocument>.Update
            .Set(d => d.FulfillmentStatus, evt.NewStatus.ToString())
            .Set(d => d.LastFulfillmentStatusEventOccurredAtUtc, evt.OccurredAtUtc)
            .Set(d => d.LastFulfillmentStatusEventVersion, evt.EventVersion)
            .Set(d => d.UpdatedAtUtc, DateTime.UtcNow)
            .AddToSet(d => d.ProcessedEventIds, eventId);

        var result = await _orders.UpdateManyAsync(filter, update, cancellationToken: ct);

        if (result.ModifiedCount > 0)
        {
            _logger.LogInformation(
                "[Affiliate:Lifecycle:Confirmed] Updated {Count} affiliate order docs | OrderId: {OrderId} | FulfillmentStatus: {Status}",
                result.ModifiedCount, evt.OrderId, evt.NewStatus);
        }
        else
        {
            _logger.LogWarning(
                "[Affiliate:Lifecycle:Confirmed] No documents updated | OrderId: {OrderId} | TenantId: {TenantId}",
                evt.OrderId, tenantId);
        }
    }

    /// <summary>
    /// Updates FulfillmentStatus when order is packed.
    /// </summary>
    public async Task Consume(ConsumeContext<OrderPackedEvent> context)
    {
        var evt = context.Message;
        var ct = context.CancellationToken;
        var eventId = evt.EventId.ToString();
        var tenantId = evt.TenantId ?? Guid.Empty;

        _logger.LogInformation(
            "[Affiliate:Lifecycle:Packed] OrderId: {OrderId} | FulfillmentStatus: {Status}",
            evt.OrderId, evt.NewStatus);

        // Check if already processed (idempotency)
        var existingDoc = await _orders.Find(d => 
            d.TenantId == tenantId && 
            d.OrderId == evt.OrderId).FirstOrDefaultAsync(ct);

        if (existingDoc != null && existingDoc.ProcessedEventIds.Contains(eventId))
        {
            _logger.LogDebug(
                "[Affiliate:Lifecycle:Packed] Event already processed | OrderId: {OrderId} | EventId: {EventId}",
                evt.OrderId, eventId);
            return;
        }

        var filter = BuildFulfillmentLifecycleFilter(tenantId, evt.OrderId, evt.OccurredAtUtc, evt.EventVersion);

        var update = Builders<AffiliateOrderDocument>.Update
            .Set(d => d.FulfillmentStatus, evt.NewStatus.ToString())
            .Set(d => d.LastFulfillmentStatusEventOccurredAtUtc, evt.OccurredAtUtc)
            .Set(d => d.LastFulfillmentStatusEventVersion, evt.EventVersion)
            .Set(d => d.UpdatedAtUtc, DateTime.UtcNow)
            .AddToSet(d => d.ProcessedEventIds, eventId);

        var result = await _orders.UpdateManyAsync(filter, update, cancellationToken: ct);

        if (result.ModifiedCount > 0)
        {
            _logger.LogInformation(
                "[Affiliate:Lifecycle:Packed] Updated {Count} affiliate order docs | OrderId: {OrderId} | FulfillmentStatus: {Status}",
                result.ModifiedCount, evt.OrderId, evt.NewStatus);
        }
        else
        {
            _logger.LogWarning(
                "[Affiliate:Lifecycle:Packed] No documents updated | OrderId: {OrderId} | TenantId: {TenantId}",
                evt.OrderId, tenantId);
        }
    }

    /// <summary>
    /// Updates FulfillmentStatus when order is out for delivery.
    /// </summary>
    public async Task Consume(ConsumeContext<OrderOutForDeliveryEvent> context)
    {
        var evt = context.Message;
        var ct = context.CancellationToken;
        var eventId = evt.EventId.ToString();
        var tenantId = evt.TenantId ?? Guid.Empty;

        _logger.LogInformation(
            "[Affiliate:Lifecycle:OutForDelivery] OrderId: {OrderId} | FulfillmentStatus: {Status}",
            evt.OrderId, evt.NewStatus);

        // Check if already processed (idempotency)
        var existingDoc = await _orders.Find(d => 
            d.TenantId == tenantId && 
            d.OrderId == evt.OrderId).FirstOrDefaultAsync(ct);

        if (existingDoc != null && existingDoc.ProcessedEventIds.Contains(eventId))
        {
            _logger.LogDebug(
                "[Affiliate:Lifecycle:OutForDelivery] Event already processed | OrderId: {OrderId} | EventId: {EventId}",
                evt.OrderId, eventId);
            return;
        }

        var filter = BuildFulfillmentLifecycleFilter(tenantId, evt.OrderId, evt.OccurredAtUtc, evt.EventVersion);

        var update = Builders<AffiliateOrderDocument>.Update
            .Set(d => d.FulfillmentStatus, evt.NewStatus.ToString())
            .Set(d => d.LastFulfillmentStatusEventOccurredAtUtc, evt.OccurredAtUtc)
            .Set(d => d.LastFulfillmentStatusEventVersion, evt.EventVersion)
            .Set(d => d.UpdatedAtUtc, DateTime.UtcNow)
            .AddToSet(d => d.ProcessedEventIds, eventId);

        var result = await _orders.UpdateManyAsync(filter, update, cancellationToken: ct);

        if (result.ModifiedCount > 0)
        {
            _logger.LogInformation(
                "[Affiliate:Lifecycle:OutForDelivery] Updated {Count} affiliate order docs | OrderId: {OrderId} | FulfillmentStatus: {Status}",
                result.ModifiedCount, evt.OrderId, evt.NewStatus);
        }
        else
        {
            _logger.LogWarning(
                "[Affiliate:Lifecycle:OutForDelivery] No documents updated | OrderId: {OrderId} | TenantId: {TenantId}",
                evt.OrderId, tenantId);
        }
    }

    /// <summary>
    /// Updates FulfillmentStatus when order is completed.
    /// </summary>
    public async Task Consume(ConsumeContext<OrderCompletedEvent> context)
    {
        var evt = context.Message;
        var ct = context.CancellationToken;
        var eventId = evt.EventId.ToString();
        var tenantId = evt.TenantId ?? Guid.Empty;

        _logger.LogInformation(
            "[Affiliate:Lifecycle:Completed] OrderId: {OrderId} | FulfillmentStatus: {Status}",
            evt.OrderId, evt.NewStatus);

        // Check if already processed (idempotency)
        var existingDoc = await _orders.Find(d => 
            d.TenantId == tenantId && 
            d.OrderId == evt.OrderId).FirstOrDefaultAsync(ct);

        if (existingDoc != null && existingDoc.ProcessedEventIds.Contains(eventId))
        {
            _logger.LogDebug(
                "[Affiliate:Lifecycle:Completed] Event already processed | OrderId: {OrderId} | EventId: {EventId}",
                evt.OrderId, eventId);
            return;
        }

        var filter = BuildFulfillmentLifecycleFilter(tenantId, evt.OrderId, evt.OccurredAtUtc, evt.EventVersion);

        var update = Builders<AffiliateOrderDocument>.Update
            .Set(d => d.FulfillmentStatus, evt.NewStatus.ToString())
            .Set(d => d.LastFulfillmentStatusEventOccurredAtUtc, evt.OccurredAtUtc)
            .Set(d => d.LastFulfillmentStatusEventVersion, evt.EventVersion)
            .Set(d => d.UpdatedAtUtc, DateTime.UtcNow)
            .AddToSet(d => d.ProcessedEventIds, eventId);

        var result = await _orders.UpdateManyAsync(filter, update, cancellationToken: ct);

        if (result.ModifiedCount > 0)
        {
            _logger.LogInformation(
                "[Affiliate:Lifecycle:Completed] Updated {Count} affiliate order docs | OrderId: {OrderId} | FulfillmentStatus: {Status}",
                result.ModifiedCount, evt.OrderId, evt.NewStatus);
        }
        else
        {
            _logger.LogWarning(
                "[Affiliate:Lifecycle:Completed] No documents updated | OrderId: {OrderId} | TenantId: {TenantId}",
                evt.OrderId, tenantId);
        }
    }

    /// <summary>
    /// Updates OrderStatus and FulfillmentStatus when order is cancelled.
    /// </summary>
    public async Task Consume(ConsumeContext<OrderCancelledEvent> context)
    {
        var evt = context.Message;
        var ct = context.CancellationToken;
        var eventId = evt.EventId.ToString();
        var tenantId = evt.TenantId ?? Guid.Empty;

        _logger.LogInformation(
            "[Affiliate:Lifecycle:Cancelled] OrderId: {OrderId} | Reason: {Reason}",
            evt.OrderId, evt.CancellationReason);

        // Check if already processed (idempotency)
        var existingDoc = await _orders.Find(d => 
            d.TenantId == tenantId && 
            d.OrderId == evt.OrderId).FirstOrDefaultAsync(ct);

        if (existingDoc != null && existingDoc.ProcessedEventIds.Contains(eventId))
        {
            _logger.LogDebug(
                "[Affiliate:Lifecycle:Cancelled] Event already processed | OrderId: {OrderId} | EventId: {EventId}",
                evt.OrderId, eventId);
            return;
        }

        var filter = BuildOrderAndFulfillmentLifecycleFilter(tenantId, evt.OrderId, evt.OccurredAtUtc, evt.EventVersion);

        var update = Builders<AffiliateOrderDocument>.Update
            .Set(d => d.OrderStatus, "Cancelled")
            .Set(d => d.FulfillmentStatus, "Cancelled")
            .Set(d => d.LastOrderStatusEventOccurredAtUtc, evt.OccurredAtUtc)
            .Set(d => d.LastFulfillmentStatusEventOccurredAtUtc, evt.OccurredAtUtc)
            .Set(d => d.LastOrderStatusEventVersion, evt.EventVersion)
            .Set(d => d.LastFulfillmentStatusEventVersion, evt.EventVersion)
            .Set(d => d.UpdatedAtUtc, DateTime.UtcNow)
            .AddToSet(d => d.ProcessedEventIds, eventId);

        var result = await _orders.UpdateManyAsync(filter, update, cancellationToken: ct);

        if (result.ModifiedCount > 0)
        {
            _logger.LogInformation(
                "[Affiliate:Lifecycle:Cancelled] Updated {Count} affiliate order docs | OrderId: {OrderId}",
                result.ModifiedCount, evt.OrderId);
        }
        else
        {
            _logger.LogWarning(
                "[Affiliate:Lifecycle:Cancelled] No documents updated | OrderId: {OrderId} | TenantId: {TenantId}",
                evt.OrderId, tenantId);
        }
    }

    /// <summary>
    /// Updates FulfillmentStatus when order is delivered.
    /// </summary>
    public async Task Consume(ConsumeContext<FulfillmentDeliveredEvent> context)
    {
        var evt = context.Message;
        var ct = context.CancellationToken;
        var eventId = evt.EventId.ToString();
        var tenantId = evt.TenantId ?? Guid.Empty;

        _logger.LogInformation(
            "[Affiliate:Lifecycle:Delivered] OrderId: {OrderId} | FulfillmentStatus: {Status}",
            evt.OrderId, evt.NewStatus);

        // Check if already processed (idempotency)
        var existingDoc = await _orders.Find(d => 
            d.TenantId == tenantId && 
            d.OrderId == evt.OrderId).FirstOrDefaultAsync(ct);

        if (existingDoc != null && existingDoc.ProcessedEventIds.Contains(eventId))
        {
            _logger.LogDebug(
                "[Affiliate:Lifecycle:Delivered] Event already processed | OrderId: {OrderId} | EventId: {EventId}",
                evt.OrderId, eventId);
            return;
        }

        var filter = BuildFulfillmentLifecycleFilter(tenantId, evt.OrderId, evt.OccurredAtUtc, evt.EventVersion);

        var update = Builders<AffiliateOrderDocument>.Update
            .Set(d => d.FulfillmentStatus, evt.NewStatus.ToString())
            .Set(d => d.LastFulfillmentStatusEventOccurredAtUtc, evt.OccurredAtUtc)
            .Set(d => d.LastFulfillmentStatusEventVersion, evt.EventVersion)
            .Set(d => d.UpdatedAtUtc, DateTime.UtcNow)
            .AddToSet(d => d.ProcessedEventIds, eventId);

        var result = await _orders.UpdateManyAsync(filter, update, cancellationToken: ct);

        if (result.ModifiedCount > 0)
        {
            _logger.LogInformation(
                "[Affiliate:Lifecycle:Delivered] Updated {Count} affiliate order docs | OrderId: {OrderId} | FulfillmentStatus: {Status}",
                result.ModifiedCount, evt.OrderId, evt.NewStatus);
        }
        else
        {
            _logger.LogWarning(
                "[Affiliate:Lifecycle:Delivered] No documents updated | OrderId: {OrderId} | TenantId: {TenantId}",
                evt.OrderId, tenantId);
        }
    }

    public Task Consume(ConsumeContext<OrderReturnedEvent> context) =>
        ConsumeTerminalFulfillmentEventAsync(context.Message, nameof(OrderReturnedEvent), context.CancellationToken);

    public Task Consume(ConsumeContext<OrderDeliveryFailedEvent> context) =>
        ConsumeTerminalFulfillmentEventAsync(context.Message, nameof(OrderDeliveryFailedEvent), context.CancellationToken);

    private async Task ConsumeTerminalFulfillmentEventAsync(
        OrderFulfillmentEventBase evt,
        string eventName,
        CancellationToken cancellationToken)
    {
        var tenantId = evt.TenantId ?? Guid.Empty;
        if (tenantId == Guid.Empty || evt.EventVersion <= 0)
        {
            throw new InvalidOperationException(
                $"{eventName} requires tenant authority and a positive event version. OrderId={evt.OrderId}; EventId={evt.EventId}.");
        }

        var eventId = evt.EventId.ToString();
        var existingDoc = await _orders.Find(d =>
            d.TenantId == tenantId &&
            d.OrderId == evt.OrderId).FirstOrDefaultAsync(cancellationToken);

        if (existingDoc?.ProcessedEventIds.Contains(eventId) == true)
        {
            return;
        }

        var filter = BuildFulfillmentLifecycleFilter(
            tenantId,
            evt.OrderId,
            evt.OccurredAtUtc,
            evt.EventVersion);
        var update = Builders<AffiliateOrderDocument>.Update
            .Set(d => d.FulfillmentStatus, evt.NewStatus.ToString())
            .Set(d => d.LastFulfillmentStatusEventOccurredAtUtc, evt.OccurredAtUtc)
            .Set(d => d.LastFulfillmentStatusEventVersion, evt.EventVersion)
            .Set(d => d.UpdatedAtUtc, DateTime.UtcNow)
            .AddToSet(d => d.ProcessedEventIds, eventId);

        var result = await _orders.UpdateManyAsync(filter, update, cancellationToken: cancellationToken);
        _logger.LogInformation(
            "[Affiliate:Lifecycle:{EventType}] Updated {Count} affiliate order docs | TenantId: {TenantId} | OrderId: {OrderId} | FulfillmentStatus: {Status}",
            eventName,
            result.ModifiedCount,
            tenantId,
            evt.OrderId,
            evt.NewStatus);
    }

    /// <summary>
    /// Updates FulfillmentStatus when order fulfillment is cancelled.
    /// </summary>
    public async Task Consume(ConsumeContext<OrderFulfillmentCancelledEvent> context)
    {
        var evt = context.Message;
        var ct = context.CancellationToken;
        var eventId = evt.EventId.ToString();
        var tenantId = evt.TenantId ?? Guid.Empty;

        _logger.LogInformation(
            "[Affiliate:Lifecycle:FulfillmentCancelled] OrderId: {OrderId} | Reason: {Reason}",
            evt.OrderId, evt.Reason);

        // Check if already processed (idempotency)
        var existingDoc = await _orders.Find(d => 
            d.TenantId == tenantId && 
            d.OrderId == evt.OrderId).FirstOrDefaultAsync(ct);

        if (existingDoc != null && existingDoc.ProcessedEventIds.Contains(eventId))
        {
            _logger.LogDebug(
                "[Affiliate:Lifecycle:FulfillmentCancelled] Event already processed | OrderId: {OrderId} | EventId: {EventId}",
                evt.OrderId, eventId);
            return;
        }

        var filter = BuildFulfillmentLifecycleFilter(tenantId, evt.OrderId, evt.OccurredAtUtc, evt.EventVersion);

        var update = Builders<AffiliateOrderDocument>.Update
            .Set(d => d.FulfillmentStatus, evt.NewStatus.ToString())
            .Set(d => d.LastFulfillmentStatusEventOccurredAtUtc, evt.OccurredAtUtc)
            .Set(d => d.LastFulfillmentStatusEventVersion, evt.EventVersion)
            .Set(d => d.UpdatedAtUtc, DateTime.UtcNow)
            .AddToSet(d => d.ProcessedEventIds, eventId);

        var result = await _orders.UpdateManyAsync(filter, update, cancellationToken: ct);

        if (result.ModifiedCount > 0)
        {
            _logger.LogInformation(
                "[Affiliate:Lifecycle:FulfillmentCancelled] Updated {Count} affiliate order docs | OrderId: {OrderId} | FulfillmentStatus: {Status}",
                result.ModifiedCount, evt.OrderId, evt.NewStatus);
        }
        else
        {
            _logger.LogWarning(
                "[Affiliate:Lifecycle:FulfillmentCancelled] No documents updated | OrderId: {OrderId} | TenantId: {TenantId}",
                evt.OrderId, tenantId);
        }
    }

    private static FilterDefinition<AffiliateOrderDocument> BuildOrderStatusLifecycleFilter(
        Guid tenantId,
        Guid orderId,
        DateTime eventOccurredAtUtc,
        int eventVersion)
    {
        return Builders<AffiliateOrderDocument>.Filter.And(
            Builders<AffiliateOrderDocument>.Filter.Eq(d => d.TenantId, tenantId),
            Builders<AffiliateOrderDocument>.Filter.Eq(d => d.OrderId, orderId),
            Builders<AffiliateOrderDocument>.Filter.Or(
                // S38C: Use expression-based Exists to resolve correct BSON field name (camelCase)
                Builders<AffiliateOrderDocument>.Filter.Exists(d => d.LastOrderStatusEventVersion, false),
                Builders<AffiliateOrderDocument>.Filter.Eq(d => d.LastOrderStatusEventVersion, 0),
                Builders<AffiliateOrderDocument>.Filter.Lt(d => d.LastOrderStatusEventVersion, eventVersion),
                Builders<AffiliateOrderDocument>.Filter.And(
                    Builders<AffiliateOrderDocument>.Filter.Eq(d => d.LastOrderStatusEventVersion, eventVersion),
                    Builders<AffiliateOrderDocument>.Filter.Or(
                        Builders<AffiliateOrderDocument>.Filter.Exists(d => d.LastOrderStatusEventOccurredAtUtc, false),
                        Builders<AffiliateOrderDocument>.Filter.Eq(d => d.LastOrderStatusEventOccurredAtUtc, null),
                        Builders<AffiliateOrderDocument>.Filter.Lte(d => d.LastOrderStatusEventOccurredAtUtc, eventOccurredAtUtc)))));
    }

    private static FilterDefinition<AffiliateOrderDocument> BuildFulfillmentLifecycleFilter(
        Guid tenantId,
        Guid orderId,
        DateTime eventOccurredAtUtc,
        int eventVersion)
    {
        return Builders<AffiliateOrderDocument>.Filter.And(
            Builders<AffiliateOrderDocument>.Filter.Eq(d => d.TenantId, tenantId),
            Builders<AffiliateOrderDocument>.Filter.Eq(d => d.OrderId, orderId),
            Builders<AffiliateOrderDocument>.Filter.Or(
                // S38C: Use expression-based Exists to resolve correct BSON field name (camelCase)
                Builders<AffiliateOrderDocument>.Filter.Exists(d => d.LastFulfillmentStatusEventVersion, false),
                Builders<AffiliateOrderDocument>.Filter.Eq(d => d.LastFulfillmentStatusEventVersion, 0),
                Builders<AffiliateOrderDocument>.Filter.Lt(d => d.LastFulfillmentStatusEventVersion, eventVersion),
                Builders<AffiliateOrderDocument>.Filter.And(
                    Builders<AffiliateOrderDocument>.Filter.Eq(d => d.LastFulfillmentStatusEventVersion, eventVersion),
                    Builders<AffiliateOrderDocument>.Filter.Or(
                        Builders<AffiliateOrderDocument>.Filter.Exists(d => d.LastFulfillmentStatusEventOccurredAtUtc, false),
                        Builders<AffiliateOrderDocument>.Filter.Eq(d => d.LastFulfillmentStatusEventOccurredAtUtc, null),
                        Builders<AffiliateOrderDocument>.Filter.Lte(d => d.LastFulfillmentStatusEventOccurredAtUtc, eventOccurredAtUtc)))));
    }

    private static FilterDefinition<AffiliateOrderDocument> BuildPaymentStatusLifecycleFilter(
        Guid tenantId,
        Guid orderId,
        DateTime eventOccurredAtUtc,
        int eventVersion)
    {
        return Builders<AffiliateOrderDocument>.Filter.And(
            Builders<AffiliateOrderDocument>.Filter.Eq(d => d.TenantId, tenantId),
            Builders<AffiliateOrderDocument>.Filter.Eq(d => d.OrderId, orderId),
            Builders<AffiliateOrderDocument>.Filter.Or(
                Builders<AffiliateOrderDocument>.Filter.Exists(d => d.LastPaymentStatusEventVersion, false),
                Builders<AffiliateOrderDocument>.Filter.Eq(d => d.LastPaymentStatusEventVersion, 0),
                Builders<AffiliateOrderDocument>.Filter.Lt(d => d.LastPaymentStatusEventVersion, eventVersion),
                Builders<AffiliateOrderDocument>.Filter.And(
                    Builders<AffiliateOrderDocument>.Filter.Eq(d => d.LastPaymentStatusEventVersion, eventVersion),
                    Builders<AffiliateOrderDocument>.Filter.Or(
                        Builders<AffiliateOrderDocument>.Filter.Exists(d => d.LastPaymentStatusEventOccurredAtUtc, false),
                        Builders<AffiliateOrderDocument>.Filter.Eq(d => d.LastPaymentStatusEventOccurredAtUtc, null),
                        Builders<AffiliateOrderDocument>.Filter.Lte(d => d.LastPaymentStatusEventOccurredAtUtc, eventOccurredAtUtc)))));
    }

    private static FilterDefinition<AffiliateOrderDocument> BuildOrderAndFulfillmentLifecycleFilter(
        Guid tenantId,
        Guid orderId,
        DateTime eventOccurredAtUtc,
        int eventVersion)
    {
        return Builders<AffiliateOrderDocument>.Filter.And(
            BuildOrderStatusLifecycleFilter(tenantId, orderId, eventOccurredAtUtc, eventVersion),
            BuildFulfillmentLifecycleFilter(tenantId, orderId, eventOccurredAtUtc, eventVersion));
    }
}
