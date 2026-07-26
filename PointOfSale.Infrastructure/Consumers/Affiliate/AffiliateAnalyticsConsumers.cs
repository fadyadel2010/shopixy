using MassTransit;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using PointOfSale.Core.Events.Affiliate;
using PointOfSale.Infrastructure.MongoDB.Documents.Affiliate;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.Services.Affiliate;

namespace PointOfSale.Infrastructure.Consumers.Affiliate;

// ═══════════════════════════════════════════════════════════════════
// AF-8: Click Tracking & Analytics Consumers
//
// RULES:
// - Append-only: clicks are never deleted or overwritten
// - Duplicate clicks suppressed via unique index (TenantId, SessionId, ProductId)
// - No commission logic — analytics only
// - No PII stored
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// AF-8: Consumes AffiliateLinkClickedEvent and writes AffiliateClickDocument to MongoDB.
///
/// Idempotency: Unique index on (TenantId, SessionId, ProductId) prevents duplicates.
/// Duplicate clicks in the same session for the same product are silently ignored.
/// </summary>
public class AffiliateLinkClickedConsumer : IConsumer<AffiliateLinkClickedEvent>
{
    private readonly MongoDbContext _mongoDbContext;
    private readonly ILogger<AffiliateLinkClickedConsumer> _logger;
    private readonly AffiliateRankingService _rankingService;

    public AffiliateLinkClickedConsumer(
        MongoDbContext mongoDbContext,
        ILogger<AffiliateLinkClickedConsumer> logger,
        AffiliateRankingService rankingService)
    {
        _mongoDbContext = mongoDbContext;
        _logger = logger;
        _rankingService = rankingService;
    }

    public async Task Consume(ConsumeContext<AffiliateLinkClickedEvent> context)
    {
        var evt = context.Message;
        var ct = context.CancellationToken;

        _logger.LogInformation(
            "[Affiliate:Click] Processing | AffiliateId: {AffiliateId} | Ref: {Ref} | SessionId: {SessionId} | TenantId: {TenantId}",
            evt.AffiliateAccountId, evt.ReferralCode, evt.SessionId, evt.TenantId);

        if (!evt.TenantId.HasValue)
        {
            _logger.LogError("[Affiliate:Click] REJECTED: Missing TenantId");
            return;
        }

        var tenantId = evt.TenantId.Value;

        // ═══════════════════════════════════════════════════════════════════
        // STEP 1: Insert click document (append-only)
        // ═══════════════════════════════════════════════════════════════════

        var clickDoc = new AffiliateClickDocument
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AffiliateAccountId = evt.AffiliateAccountId,
            ReferralCode = evt.ReferralCode,
            ProductId = evt.ProductId,
            SessionId = evt.SessionId,
            UserAgent = evt.UserAgent,
            IpHash = evt.IpHash,
            ClickedAtUtc = evt.OccurredAtUtc
        };

        try
        {
            await _mongoDbContext.AffiliateClicks.InsertOneAsync(clickDoc, cancellationToken: ct);

            _logger.LogInformation(
                "[Affiliate:Click] Stored | ClickId: {ClickId} | AffiliateId: {AffiliateId} | SessionId: {SessionId}",
                clickDoc.Id, evt.AffiliateAccountId, evt.SessionId);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // Idempotency: same session + same product = skip
            _logger.LogDebug(
                "[Affiliate:Click] Duplicate suppressed | SessionId: {SessionId} | ProductId: {ProductId}",
                evt.SessionId, evt.ProductId);
            return;
        }

        // ═══════════════════════════════════════════════════════════════════
        // STEP 2: Update analytics summary (upsert)
        // ═══════════════════════════════════════════════════════════════════

        try
        {
            var filter = Builders<AffiliateAnalyticsSummaryDocument>.Filter.And(
                Builders<AffiliateAnalyticsSummaryDocument>.Filter.Eq(d => d.TenantId, tenantId),
                Builders<AffiliateAnalyticsSummaryDocument>.Filter.Eq(d => d.AffiliateAccountId, evt.AffiliateAccountId));

            var update = Builders<AffiliateAnalyticsSummaryDocument>.Update
                .Inc(d => d.TotalClicks, 1)
                .Set(d => d.LastClickAtUtc, evt.OccurredAtUtc)
                .Set(d => d.UpdatedAtUtc, DateTime.UtcNow)
                .SetOnInsert(d => d.Id, Guid.NewGuid())
                .SetOnInsert(d => d.TenantId, tenantId)
                .SetOnInsert(d => d.AffiliateAccountId, evt.AffiliateAccountId)
                .SetOnInsert(d => d.TotalConversions, 0)
                .SetOnInsert(d => d.ConversionRate, 0);

            await _mongoDbContext.AffiliateAnalyticsSummaries.UpdateOneAsync(
                filter, update,
                new UpdateOptions { IsUpsert = true },
                cancellationToken: ct);

            // Update top products if productId is present
            if (evt.ProductId.HasValue)
            {
                await UpdateTopProductClicks(tenantId, evt.AffiliateAccountId, evt.ProductId.Value, ct);

                // AF-34: Increment click signal on product intelligence for ranking
                await _rankingService.IncrementClickSignalAsync(evt.ProductId.Value, tenantId, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Affiliate:Click] Analytics summary update failed (non-critical) | AffiliateId: {AffiliateId}",
                evt.AffiliateAccountId);
            // Non-critical — click is already stored
        }
    }

    private async Task UpdateTopProductClicks(
        Guid tenantId, Guid affiliateAccountId, Guid productId, CancellationToken ct)
    {
        // Check if product already exists in top products
        var existsFilter = Builders<AffiliateAnalyticsSummaryDocument>.Filter.And(
            Builders<AffiliateAnalyticsSummaryDocument>.Filter.Eq(d => d.TenantId, tenantId),
            Builders<AffiliateAnalyticsSummaryDocument>.Filter.Eq(d => d.AffiliateAccountId, affiliateAccountId),
            Builders<AffiliateAnalyticsSummaryDocument>.Filter.ElemMatch(
                d => d.TopProducts, tp => tp.ProductId == productId));

        var existsResult = await _mongoDbContext.AffiliateAnalyticsSummaries
            .Find(existsFilter).FirstOrDefaultAsync(ct);

        if (existsResult != null)
        {
            // Increment existing product clicks
            var incUpdate = Builders<AffiliateAnalyticsSummaryDocument>.Update
                .Inc("topProducts.$.clicks", 1L);
            await _mongoDbContext.AffiliateAnalyticsSummaries
                .UpdateOneAsync(existsFilter, incUpdate, cancellationToken: ct);
        }
        else
        {
            // Add new product to top products
            var baseFilter = Builders<AffiliateAnalyticsSummaryDocument>.Filter.And(
                Builders<AffiliateAnalyticsSummaryDocument>.Filter.Eq(d => d.TenantId, tenantId),
                Builders<AffiliateAnalyticsSummaryDocument>.Filter.Eq(d => d.AffiliateAccountId, affiliateAccountId));

            var pushUpdate = Builders<AffiliateAnalyticsSummaryDocument>.Update
                .Push(d => d.TopProducts, new TopProductSubDocument
                {
                    ProductId = productId,
                    Clicks = 1,
                    Conversions = 0
                });
            await _mongoDbContext.AffiliateAnalyticsSummaries
                .UpdateOneAsync(baseFilter, pushUpdate, cancellationToken: ct);
        }
    }
}

/// <summary>
/// AF-8: Conversion Bridge Consumer.
///
/// Listens to AffiliateAttributedEvent (existing event from AF-2) and links
/// the attributed order to the most recent unconverted click for that affiliate.
///
/// Flow:
/// 1. Find AffiliateClickDocument where TenantId + AffiliateAccountId + OrderId is null
/// 2. Order by ClickedAtUtc desc (most recent click)
/// 3. Set OrderId + ConvertedAtUtc
/// 4. Increment TotalConversions in analytics summary
/// </summary>
public class AffiliateClickConversionConsumer : IConsumer<AffiliateAttributedEvent>
{
    private readonly MongoDbContext _mongoDbContext;
    private readonly ILogger<AffiliateClickConversionConsumer> _logger;
    private readonly AffiliateRankingService _rankingService;

    public AffiliateClickConversionConsumer(
        MongoDbContext mongoDbContext,
        ILogger<AffiliateClickConversionConsumer> logger,
        AffiliateRankingService rankingService)
    {
        _mongoDbContext = mongoDbContext;
        _logger = logger;
        _rankingService = rankingService;
    }

    public async Task Consume(ConsumeContext<AffiliateAttributedEvent> context)
    {
        var evt = context.Message;
        var ct = context.CancellationToken;

        _logger.LogInformation(
            "[Affiliate:Conversion] Processing | OrderId: {OrderId} | AffiliateId: {AffiliateId} | TenantId: {TenantId} | RequiresVerification: {RequiresVerification}",
            evt.OrderId, evt.AffiliateAccountId, evt.TenantId, evt.RequiresPaymentVerification);

        // AF-36: Manual payment orders (Vodafone Cash, InstaPay, etc.) must NOT count
        // as conversions until admin approves the payment.
        // Conversion + ranking signals will fire via WalletPaymentApprovedAffiliateConsumer.
        if (evt.RequiresPaymentVerification)
        {
            _logger.LogInformation(
                "[Affiliate:Conversion] Skipped — requires payment verification first | " +
                "OrderId: {OrderId} | PaymentMethod: {PaymentMethod}",
                evt.OrderId, evt.PaymentMethodCode);
            return;
        }

        if (!evt.TenantId.HasValue)
        {
            _logger.LogError("[Affiliate:Conversion] REJECTED: Missing TenantId");
            return;
        }

        var tenantId = evt.TenantId.Value;

        // ═══════════════════════════════════════════════════════════════════
        // STEP 1: Find most recent unconverted click for this affiliate
        // ═══════════════════════════════════════════════════════════════════

        try
        {
            var clickFilter = Builders<AffiliateClickDocument>.Filter.And(
                Builders<AffiliateClickDocument>.Filter.Eq(d => d.TenantId, tenantId),
                Builders<AffiliateClickDocument>.Filter.Eq(d => d.AffiliateAccountId, evt.AffiliateAccountId),
                Builders<AffiliateClickDocument>.Filter.Eq(d => d.OrderId, null));

            var sort = Builders<AffiliateClickDocument>.Sort.Descending(d => d.ClickedAtUtc);

            var conversionUpdate = Builders<AffiliateClickDocument>.Update
                .Set(d => d.OrderId, evt.OrderId)
                .Set(d => d.ConvertedAtUtc, DateTime.UtcNow);

            var result = await _mongoDbContext.AffiliateClicks
                .FindOneAndUpdateAsync(clickFilter, conversionUpdate,
                    new FindOneAndUpdateOptions<AffiliateClickDocument>
                    {
                        Sort = sort,
                        ReturnDocument = ReturnDocument.After
                    },
                    cancellationToken: ct);

            if (result != null)
            {
                _logger.LogInformation(
                    "[Affiliate:Conversion] Click linked to order | ClickId: {ClickId} | OrderId: {OrderId} | AffiliateId: {AffiliateId}",
                    result.Id, evt.OrderId, evt.AffiliateAccountId);

                // AF-34/AF-36: Increment conversion signal ONLY for non-manual-payment orders.
                // For reviewed payments this is gated; signal fires on ReviewedPaymentApprovedEvent.
                if (result.ProductId.HasValue && !evt.RequiresPaymentVerification)
                {
                    await _rankingService.IncrementConversionSignalAsync(
                        result.ProductId.Value, tenantId, 0, ct); // Commission amount added by commission consumer
                }
            }
            else
            {
                _logger.LogDebug(
                    "[Affiliate:Conversion] No unconverted click found for affiliate | AffiliateId: {AffiliateId} | OrderId: {OrderId}",
                    evt.AffiliateAccountId, evt.OrderId);
                // This is normal — order may have come via direct attribution, not a tracked click
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Affiliate:Conversion] Click-to-order linking failed (non-critical) | OrderId: {OrderId}",
                evt.OrderId);
        }

        // ═══════════════════════════════════════════════════════════════════
        // STEP 2: Increment conversion count in analytics summary
        // ═══════════════════════════════════════════════════════════════════

        try
        {
            var summaryFilter = Builders<AffiliateAnalyticsSummaryDocument>.Filter.And(
                Builders<AffiliateAnalyticsSummaryDocument>.Filter.Eq(d => d.TenantId, tenantId),
                Builders<AffiliateAnalyticsSummaryDocument>.Filter.Eq(d => d.AffiliateAccountId, evt.AffiliateAccountId));

            // First get the current document to compute new conversion rate
            var summary = await _mongoDbContext.AffiliateAnalyticsSummaries
                .Find(summaryFilter).FirstOrDefaultAsync(ct);

            if (summary != null)
            {
                var newConversions = summary.TotalConversions + 1;
                var newRate = summary.TotalClicks > 0
                    ? (double)newConversions / summary.TotalClicks
                    : 0;

                var summaryUpdate = Builders<AffiliateAnalyticsSummaryDocument>.Update
                    .Inc(d => d.TotalConversions, 1)
                    .Set(d => d.ConversionRate, newRate)
                    .Set(d => d.UpdatedAtUtc, DateTime.UtcNow);

                await _mongoDbContext.AffiliateAnalyticsSummaries
                    .UpdateOneAsync(summaryFilter, summaryUpdate, cancellationToken: ct);

                _logger.LogInformation(
                    "[Affiliate:Conversion] Analytics updated | AffiliateId: {AffiliateId} | Conversions: {Conversions} | Rate: {Rate:P1}",
                    evt.AffiliateAccountId, newConversions, newRate);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Affiliate:Conversion] Analytics summary update failed (non-critical) | AffiliateId: {AffiliateId}",
                evt.AffiliateAccountId);
        }
    }
}
