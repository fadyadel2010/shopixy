using MassTransit;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using PointOfSale.Core.Events.Affiliate;
using PointOfSale.Infrastructure.MongoDB.Documents.Affiliate;
using PointOfSale.Infrastructure.MongoDB.Services;

namespace PointOfSale.Infrastructure.Consumers.Affiliate;

// ═══════════════════════════════════════════════════════════════════
// AF-24: Daily Analytics Projection Consumers
//
// RULES:
// 1. Consume ONLY existing events — no new domain events
// 2. Upsert semantics via composite string ID
// 3. Idempotent via ProcessedEventIds (capped at 500)
// 4. Never query PostgreSQL
// 5. Never throw — log and skip corrupt events
// 6. Date = UtcNow.Date (event processing time)
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// AF-24: Consumes AffiliateLinkClickedEvent and increments daily click count.
/// </summary>
public class AffiliateAnalyticsDailyClickConsumer : IConsumer<AffiliateLinkClickedEvent>
{
    private readonly MongoDbContext _mongoDbContext;
    private readonly ILogger<AffiliateAnalyticsDailyClickConsumer> _logger;

    public AffiliateAnalyticsDailyClickConsumer(
        MongoDbContext mongoDbContext,
        ILogger<AffiliateAnalyticsDailyClickConsumer> logger)
    {
        _mongoDbContext = mongoDbContext;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<AffiliateLinkClickedEvent> context)
    {
        var msg = context.Message;

        // ─── Safety Guards ─────────────────────────────────────────────
        if (msg.AffiliateAccountId == Guid.Empty)
        {
            _logger.LogWarning("[AF-24:Click] Skipping — missing AffiliateAccountId | EventId: {EventId}", msg.EventId);
            return;
        }

        // Clicks use the event's TenantId (store where click happened)
        var tenantId = msg.TenantId ?? Guid.Empty;
        if (tenantId == Guid.Empty)
        {
            _logger.LogWarning("[AF-24:Click] Skipping — missing TenantId | EventId: {EventId}", msg.EventId);
            return;
        }

        var dateStr = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var docId = $"{msg.AffiliateAccountId}:{tenantId}:{dateStr}";
        var eventIdStr = msg.EventId.ToString();
        var receipt = new AffiliateAnalyticsProjectionReceipt(
            _mongoDbContext.AffiliateProjectionEventReceipts,
            nameof(AffiliateAnalyticsDailyClickConsumer),
            docId,
            eventIdStr);

        try
        {
            if (!await receipt.TryClaimAsync(context.CancellationToken))
            {
                _logger.LogDebug("[AF-24:Click] Event already durably processed or currently claimed | DocId: {DocId} | EventId: {EventId}", docId, eventIdStr);
                return;
            }

            var update = Builders<AffiliateAnalyticsDailyDocument>.Update
                .SetOnInsert(d => d.AffiliateAccountId, msg.AffiliateAccountId)
                .SetOnInsert(d => d.TenantId, tenantId)
                .SetOnInsert(d => d.Date, dateStr)
                .Inc(d => d.Clicks, 1)
                .Set(d => d.UpdatedAtUtc, DateTime.UtcNow)
                .AddToSet(d => d.ProcessedEventIds, eventIdStr);

            var applied = await AffiliateAnalyticsDailyAtomicUpdate.ApplyOnceAsync(
                _mongoDbContext.AffiliateAnalyticsDaily,
                docId,
                eventIdStr,
                update,
                context.CancellationToken);

            if (applied)
            {
                _logger.LogDebug("[AF-24:Click] Updated daily analytics | DocId: {DocId}", docId);
            }
            else
            {
                _logger.LogDebug("[AF-24:Click] Event already processed | DocId: {DocId} | EventId: {EventId}", docId, eventIdStr);
            }

            await receipt.MarkProcessedAsync(context.CancellationToken);
        }
        catch (Exception ex)
        {
            await receipt.MarkFailedAsync(ex, context.CancellationToken);
            _logger.LogError(ex, "[AF-24:Click] Failed to update daily analytics | DocId: {DocId}", docId);
            throw; // Let MassTransit retry
        }
    }
}

/// <summary>
/// AF-24: Consumes AffiliateCommissionEarnedEvent and increments daily orders + earnings.
/// Also tracks per-product earnings for top-products queries.
/// </summary>
public class AffiliateAnalyticsDailyEarningConsumer : IConsumer<AffiliateCommissionEarnedEvent>
{
    private readonly MongoDbContext _mongoDbContext;
    private readonly ILogger<AffiliateAnalyticsDailyEarningConsumer> _logger;

    public AffiliateAnalyticsDailyEarningConsumer(
        MongoDbContext mongoDbContext,
        ILogger<AffiliateAnalyticsDailyEarningConsumer> logger)
    {
        _mongoDbContext = mongoDbContext;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<AffiliateCommissionEarnedEvent> context)
    {
        var msg = context.Message;

        // ─── Safety Guards ─────────────────────────────────────────────
        if (msg.AffiliateAccountId == Guid.Empty)
        {
            _logger.LogWarning("[AF-24:Earning] Skipping — missing AffiliateAccountId | EventId: {EventId}", msg.EventId);
            return;
        }

        if (msg.StoreTenantId == Guid.Empty)
        {
            _logger.LogCritical("[AF-24:Earning] Skipping — missing StoreTenantId | EventId: {EventId}", msg.EventId);
            return;
        }

        // AF-24 HARDENING: Reject non-positive commission (corrupt upstream event)
        if (msg.TotalCommission <= 0)
        {
            _logger.LogWarning(
                "[AF-24:Earning] Ignoring non-positive commission amount | Amount: {Amount} | EventId: {EventId}",
                msg.TotalCommission, msg.EventId);
            return;
        }

        var dateStr = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var docId = $"{msg.AffiliateAccountId}:{msg.StoreTenantId}:{dateStr}";
        var eventIdStr = msg.EventId.ToString();
        var receipt = new AffiliateAnalyticsProjectionReceipt(
            _mongoDbContext.AffiliateProjectionEventReceipts,
            nameof(AffiliateAnalyticsDailyEarningConsumer),
            docId,
            eventIdStr);

        try
        {
            if (!await receipt.TryClaimAsync(context.CancellationToken))
            {
                _logger.LogDebug("[AF-24:Earning] Event already durably processed or currently claimed | DocId: {DocId} | EventId: {EventId}", docId, eventIdStr);
                return;
            }

            var update = Builders<AffiliateAnalyticsDailyDocument>.Update
                .SetOnInsert(d => d.AffiliateAccountId, msg.AffiliateAccountId)
                .SetOnInsert(d => d.TenantId, msg.StoreTenantId)
                .SetOnInsert(d => d.Date, dateStr)
                .Inc(d => d.Orders, 1)
                .Inc(d => d.Earnings, msg.TotalCommission)
                .Set(d => d.UpdatedAtUtc, DateTime.UtcNow)
                .AddToSet(d => d.ProcessedEventIds, eventIdStr);

            var applied = await AffiliateAnalyticsDailyAtomicUpdate.ApplyOnceAsync(
                _mongoDbContext.AffiliateAnalyticsDaily,
                docId,
                eventIdStr,
                update,
                context.CancellationToken);

            // AF-24 HARDENING: Always attempt per-product updates regardless of 'applied' flag
            // to ensure idempotent recovery if document-level update succeeded but item updates failed.
            // UpdateProductEarningAsync has its own idempotency via eventIdStr in ProcessedEventIds.
            //
            // AF-26 FIX: Aggregate items by ProductId before processing to handle orders with
            // multiple line items for the same product. Without aggregation, UpdateProductEarningAsync
            // deduplicates by (ProductId, EventId) and drops later duplicates, losing commission.
            bool anyProductRepaired = false;
            if (msg.Items?.Count > 0)
            {
                var aggregatedItems = msg.Items
                    .GroupBy(item => item.ProductId)
                    .Select(group => new CommissionLineItem
                    {
                        OrderItemId = group.First().OrderItemId, // Use first item's ID (not critical for analytics)
                        ProductId = group.Key,
                        ProductName = group.First().ProductName,
                        VariantName = group.First().VariantName, // Use first variant name if multiple
                        PaidAmount = group.Sum(item => item.PaidAmount),
                        CommissionRate = group.First().CommissionRate, // Assume same rate per product
                        CommissionAmount = group.Sum(item => item.CommissionAmount)
                    })
                    .ToList();

                foreach (var item in aggregatedItems)
                {
                    var repaired = await UpdateProductEarningAsync(docId, eventIdStr, item, context.CancellationToken);
                    anyProductRepaired = anyProductRepaired || repaired;
                }
            }

            if (applied)
            {
                _logger.LogDebug(
                    "[AF-24:Earning] Updated daily analytics | DocId: {DocId} | Orders: +1 | Earnings: +{Earnings}",
                    docId, msg.TotalCommission);
            }
            else
            {
                if (anyProductRepaired)
                {
                    _logger.LogDebug(
                        "[AF-24:Earning] Repaired missing ProductEarnings | DocId: {DocId} | EventId: {EventId} | Products: {ProductCount}",
                        docId, eventIdStr, msg.Items?.Count ?? 0);
                }
                else
                {
                    _logger.LogDebug("[AF-24:Earning] Event already processed | DocId: {DocId} | EventId: {EventId}", docId, eventIdStr);
                }
            }

            await receipt.MarkProcessedAsync(context.CancellationToken);
        }
        catch (Exception ex)
        {
            await receipt.MarkFailedAsync(ex, context.CancellationToken);
            _logger.LogError(ex, "[AF-24:Earning] Failed to update daily analytics | DocId: {DocId}", docId);
            throw; // Let MassTransit retry
        }
    }

    private async Task<bool> UpdateProductEarningAsync(
        string docId,
        string eventIdStr,
        CommissionLineItem item,
        CancellationToken cancellationToken)
    {
        // Try to increment existing product earnings only if this event hasn't been processed for this product
        var productFilter = Builders<AffiliateAnalyticsDailyDocument>.Filter.And(
            Builders<AffiliateAnalyticsDailyDocument>.Filter.Eq(d => d.Id, docId),
            Builders<AffiliateAnalyticsDailyDocument>.Filter.ElemMatch(
                d => d.ProductEarnings,
                pe => pe.ProductId == item.ProductId && 
                      (pe.ProcessedEventIds == null || !pe.ProcessedEventIds.Contains(eventIdStr))));

        var incrementProduct = Builders<AffiliateAnalyticsDailyDocument>.Update
            .Inc("productEarnings.$.earnings", item.CommissionAmount)
            .Inc("productEarnings.$.orders", 1)
            .AddToSet("productEarnings.$.processedEventIds", eventIdStr);

        var incrementResult = await _mongoDbContext.AffiliateAnalyticsDaily.UpdateOneAsync(
            productFilter,
            incrementProduct,
            cancellationToken: cancellationToken);

        if (incrementResult.ModifiedCount > 0)
        {
            return false; // Normal increment, not a repair
        }

        // Check if event was already processed for this product (idempotency check)
        var alreadyProcessedFilter = Builders<AffiliateAnalyticsDailyDocument>.Filter.And(
            Builders<AffiliateAnalyticsDailyDocument>.Filter.Eq(d => d.Id, docId),
            Builders<AffiliateAnalyticsDailyDocument>.Filter.ElemMatch(
                d => d.ProductEarnings,
                pe => pe.ProductId == item.ProductId && 
                      pe.ProcessedEventIds != null && 
                      pe.ProcessedEventIds.Contains(eventIdStr)));

        var alreadyProcessedDoc = await _mongoDbContext.AffiliateAnalyticsDaily
            .Find(alreadyProcessedFilter)
            .Limit(1)
            .FirstOrDefaultAsync(cancellationToken);

        if (alreadyProcessedDoc != null)
        {
            // Event already processed for this product, skip to maintain idempotency
            return false; // Not a repair, truly duplicate
        }

        // AF-24 RACE FIX: Atomic push with "not exists" condition to prevent duplicates
        var pushFilter = Builders<AffiliateAnalyticsDailyDocument>.Filter.And(
            Builders<AffiliateAnalyticsDailyDocument>.Filter.Eq(d => d.Id, docId),
            !Builders<AffiliateAnalyticsDailyDocument>.Filter.ElemMatch(
                d => d.ProductEarnings,
                pe => pe.ProductId == item.ProductId));

        var pushProduct = Builders<AffiliateAnalyticsDailyDocument>.Update.Push(
            d => d.ProductEarnings,
            new DailyProductEarning
            {
                ProductId = item.ProductId,
                ProductName = item.ProductName,
                Earnings = item.CommissionAmount,
                Orders = 1,
                ProcessedEventIds = new List<string> { eventIdStr }
            });

        var pushResult = await _mongoDbContext.AffiliateAnalyticsDaily.UpdateOneAsync(
            pushFilter,
            pushProduct,
            cancellationToken: cancellationToken);

        // If push succeeded or failed due to race (product now exists), we're done
        // The atomic filter prevents duplicate ProductEarnings entries
        if (pushResult.ModifiedCount > 0)
        {
            return true; // Repaired: created missing ProductEarning
        }

        // Race detected: another thread inserted the product between our checks
        // Retry the increment operation to add our earnings to the now-existing entry
        var retryResult = await _mongoDbContext.AffiliateAnalyticsDaily.UpdateOneAsync(
            productFilter,
            incrementProduct,
            cancellationToken: cancellationToken);

        return retryResult.ModifiedCount > 0; // Repaired if retry succeeded
    }
}

internal sealed class AffiliateAnalyticsProjectionReceipt
{
    private static readonly TimeSpan ClaimDuration = TimeSpan.FromMinutes(5);

    private readonly IMongoCollection<AffiliateProjectionEventReceiptDocument> _receipts;
    private readonly string _receiptId;
    private readonly string _consumerName;
    private readonly string _documentId;
    private readonly string _eventId;

    public AffiliateAnalyticsProjectionReceipt(
        IMongoCollection<AffiliateProjectionEventReceiptDocument> receipts,
        string consumerName,
        string documentId,
        string eventId)
    {
        _receipts = receipts;
        _consumerName = consumerName;
        _documentId = documentId;
        _eventId = eventId;
        _receiptId = $"{consumerName}:{documentId}:{eventId}";
    }

    public async Task<bool> TryClaimAsync(CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        var claimedUntilUtc = nowUtc.Add(ClaimDuration);

        try
        {
            await _receipts.InsertOneAsync(
                new AffiliateProjectionEventReceiptDocument
                {
                    Id = _receiptId,
                    ConsumerName = _consumerName,
                    DocumentId = _documentId,
                    EventId = _eventId,
                    ClaimedUntilUtc = claimedUntilUtc,
                    AttemptCount = 1,
                    CreatedAtUtc = nowUtc,
                    UpdatedAtUtc = nowUtc
                },
                cancellationToken: cancellationToken);

            return true;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            var filter = Builders<AffiliateProjectionEventReceiptDocument>.Filter.And(
                Builders<AffiliateProjectionEventReceiptDocument>.Filter.Eq(d => d.Id, _receiptId),
                Builders<AffiliateProjectionEventReceiptDocument>.Filter.Eq(d => d.ProcessedAtUtc, null),
                Builders<AffiliateProjectionEventReceiptDocument>.Filter.Or(
                    Builders<AffiliateProjectionEventReceiptDocument>.Filter.Eq(d => d.ClaimedUntilUtc, null),
                    Builders<AffiliateProjectionEventReceiptDocument>.Filter.Lte(d => d.ClaimedUntilUtc, nowUtc)));

            var update = Builders<AffiliateProjectionEventReceiptDocument>.Update
                .Set(d => d.ClaimedUntilUtc, claimedUntilUtc)
                .Set(d => d.LastError, null)
                .Set(d => d.UpdatedAtUtc, nowUtc)
                .Inc(d => d.AttemptCount, 1);

            var result = await _receipts.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
            return result.ModifiedCount > 0;
        }
    }

    public async Task MarkProcessedAsync(CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        var update = Builders<AffiliateProjectionEventReceiptDocument>.Update
            .Set(d => d.ProcessedAtUtc, nowUtc)
            .Set(d => d.ClaimedUntilUtc, null)
            .Set(d => d.LastError, null)
            .Set(d => d.UpdatedAtUtc, nowUtc);

        await _receipts.UpdateOneAsync(
            Builders<AffiliateProjectionEventReceiptDocument>.Filter.Eq(d => d.Id, _receiptId),
            update,
            cancellationToken: cancellationToken);
    }

    public async Task MarkFailedAsync(Exception exception, CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        var error = exception.Message.Length > 2000
            ? exception.Message[..2000]
            : exception.Message;

        var update = Builders<AffiliateProjectionEventReceiptDocument>.Update
            .Set(d => d.ClaimedUntilUtc, null)
            .Set(d => d.LastError, error)
            .Set(d => d.UpdatedAtUtc, nowUtc);

        await _receipts.UpdateOneAsync(
            Builders<AffiliateProjectionEventReceiptDocument>.Filter.Eq(d => d.Id, _receiptId),
            update,
            cancellationToken: cancellationToken);
    }
}

internal static class AffiliateAnalyticsDailyAtomicUpdate
{
    public static async Task<bool> ApplyOnceAsync(
        IMongoCollection<AffiliateAnalyticsDailyDocument> collection,
        string docId,
        string eventId,
        UpdateDefinition<AffiliateAnalyticsDailyDocument> update,
        CancellationToken cancellationToken)
    {
        var filter = BuildNotProcessedFilter(docId, eventId);

        try
        {
            var result = await collection.UpdateOneAsync(
                filter,
                update,
                new UpdateOptions { IsUpsert = true },
                cancellationToken);

            return result.ModifiedCount > 0 || result.UpsertedId != null;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            var retryResult = await collection.UpdateOneAsync(
                filter,
                update,
                new UpdateOptions { IsUpsert = false },
                cancellationToken);

            return retryResult.ModifiedCount > 0;
        }
    }

    private static FilterDefinition<AffiliateAnalyticsDailyDocument> BuildNotProcessedFilter(
        string docId,
        string eventId)
    {
        return Builders<AffiliateAnalyticsDailyDocument>.Filter.And(
            Builders<AffiliateAnalyticsDailyDocument>.Filter.Eq(d => d.Id, docId),
            !Builders<AffiliateAnalyticsDailyDocument>.Filter.AnyEq(d => d.ProcessedEventIds, eventId));
    }
}
