using System.Diagnostics;
using MassTransit;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces.Affiliate;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.MongoDB.Documents.Affiliate;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.Services.Affiliate;

namespace PointOfSale.Infrastructure.Consumers.Affiliate;

/// <summary>
/// AF-2 + AF-2.5: Listens to ALL payment-capture events and triggers affiliate commission recording.
///
/// TRIGGER EVENTS:
/// - OnlinePaymentCapturedEvent  (Stripe, Paymob, etc.)
/// - OfflinePaymentCollectedEvent (offline collection delivery)
/// - ReviewedPaymentApprovedEvent (reviewed payment approved by admin)
///
/// AF-2.5 OBSERVABILITY:
/// - Structured logging with CorrelationId, TenantId, EventType
/// - Metrics: attempt, success, failed counters
/// - Event lag detection: warns + records metric if lag > 30s
/// - Processing duration histogram
/// - CorrelationId propagation to AffiliateService via scope
///
/// CRITICAL RULES:
/// 1. Commission is recorded ONLY when OrderItem.PaidAmount > 0 (enforced by AffiliateService)
/// 2. If no AffiliateAttribution exists for the order → exit silently (no exception)
/// 3. Idempotent: AffiliateService catches DB unique-violation as success
/// 4. No commission calculation inside this consumer — all logic is in AffiliateService
/// 5. Never called on OrderPlaced — only on payment capture
/// </summary>
public class OnlinePaymentCapturedAffiliateConsumer : IConsumer<OnlinePaymentCapturedEvent>
{
    private const string EventType = "OnlinePayment";

    private readonly IAffiliateService _affiliateService;
    private readonly AffiliateTelemetry _telemetry;
    private readonly ILogger<OnlinePaymentCapturedAffiliateConsumer> _logger;

    public OnlinePaymentCapturedAffiliateConsumer(
        IAffiliateService affiliateService,
        AffiliateTelemetry telemetry,
        ILogger<OnlinePaymentCapturedAffiliateConsumer> logger)
    {
        _affiliateService = affiliateService;
        _telemetry = telemetry;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<OnlinePaymentCapturedEvent> context)
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

        // Lag detection
        AffiliateConsumerHelper.DetectAndRecordLag(
            _logger, _telemetry, tenantId, EventType, evt.OccurredAtUtc);

        _logger.LogInformation(
            "[Affiliate:PaymentCaptured] Processing | OrderId: {OrderId} | Gateway: {Gateway} | Amount: {Amount}",
            evt.OrderId, evt.Gateway, evt.AmountCaptured);

        _telemetry.RecordAttempt(tenantId, EventType);

        try
        {
            await _affiliateService.RecordCommissionForOrderAsync(evt.OrderId, ct);

            sw.Stop();
            _telemetry.RecordProcessingDuration(tenantId, EventType, sw.Elapsed.TotalMilliseconds);

            _logger.LogInformation(
                "[Affiliate:PaymentCaptured] Completed | OrderId: {OrderId} | Duration: {DurationMs}ms",
                evt.OrderId, sw.Elapsed.TotalMilliseconds);
        }
        catch (AffiliateCommissionDeferredException ex)
        {
            AffiliateConsumerHelper.HandleDeferredCommission(
                _logger,
                _telemetry,
                tenantId,
                EventType,
                evt.OrderId,
                sw,
                ex,
                "Affiliate:PaymentCaptured");
            return;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _telemetry.RecordFailed(tenantId, EventType);
            _telemetry.RecordProcessingDuration(tenantId, EventType, sw.Elapsed.TotalMilliseconds);

            _logger.LogError(ex,
                "[Affiliate:PaymentCaptured] Failed | OrderId: {OrderId} | Duration: {DurationMs}ms",
                evt.OrderId, sw.Elapsed.TotalMilliseconds);
            throw; // Let MassTransit retry
        }
    }
}

/// <summary>
/// AF-2 + AF-2.5: Triggers affiliate commission recording when COD is collected at delivery.
/// Same delegation pattern with full observability instrumentation.
/// </summary>
public class CodDeliveredAffiliateConsumer : IConsumer<OfflinePaymentCollectedEvent>
{
    private const string EventType = "Cod";

    private readonly IAffiliateService _affiliateService;
    private readonly AffiliateTelemetry _telemetry;
    private readonly ILogger<CodDeliveredAffiliateConsumer> _logger;

    public CodDeliveredAffiliateConsumer(
        IAffiliateService affiliateService,
        AffiliateTelemetry telemetry,
        ILogger<CodDeliveredAffiliateConsumer> logger)
    {
        _affiliateService = affiliateService;
        _telemetry = telemetry;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<OfflinePaymentCollectedEvent> context)
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
            "[Affiliate:CodDelivered] Processing | OrderId: {OrderId} | Amount: {Amount}",
            evt.OrderId, evt.AmountCollected);

        _telemetry.RecordAttempt(tenantId, EventType);

        try
        {
            await _affiliateService.RecordCommissionForOrderAsync(evt.OrderId, ct);

            sw.Stop();
            _telemetry.RecordProcessingDuration(tenantId, EventType, sw.Elapsed.TotalMilliseconds);

            _logger.LogInformation(
                "[Affiliate:CodDelivered] Completed | OrderId: {OrderId} | Duration: {DurationMs}ms",
                evt.OrderId, sw.Elapsed.TotalMilliseconds);
        }
        catch (AffiliateCommissionDeferredException ex)
        {
            AffiliateConsumerHelper.HandleDeferredCommission(
                _logger,
                _telemetry,
                tenantId,
                EventType,
                evt.OrderId,
                sw,
                ex,
                "Affiliate:CodDelivered");
            return;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _telemetry.RecordFailed(tenantId, EventType);
            _telemetry.RecordProcessingDuration(tenantId, EventType, sw.Elapsed.TotalMilliseconds);

            _logger.LogError(ex,
                "[Affiliate:CodDelivered] Failed | OrderId: {OrderId} | Duration: {DurationMs}ms",
                evt.OrderId, sw.Elapsed.TotalMilliseconds);
            throw;
        }
    }
}

/// <summary>
/// AF-2 + AF-2.5 + AF-36: Triggers affiliate commission recording when wallet payment is approved.
/// Also fires the deferred conversion + ranking signals that were gated at attribution time.
/// </summary>
public class WalletPaymentApprovedAffiliateConsumer : IConsumer<ReviewedPaymentApprovedEvent>
{
    private const string EventType = "Wallet";

    private readonly IAffiliateService _affiliateService;
    private readonly AffiliateTelemetry _telemetry;
    private readonly ILogger<WalletPaymentApprovedAffiliateConsumer> _logger;
    private readonly MongoDbContext _mongoDbContext;
    private readonly AffiliateRankingService _rankingService;

    public WalletPaymentApprovedAffiliateConsumer(
        IAffiliateService affiliateService,
        AffiliateTelemetry telemetry,
        ILogger<WalletPaymentApprovedAffiliateConsumer> logger,
        MongoDbContext mongoDbContext,
        AffiliateRankingService rankingService)
    {
        _affiliateService = affiliateService;
        _telemetry = telemetry;
        _logger = logger;
        _mongoDbContext = mongoDbContext;
        _rankingService = rankingService;
    }

    public async Task Consume(ConsumeContext<ReviewedPaymentApprovedEvent> context)
    {
        var evt = context.Message;
        var ct = context.CancellationToken;
        var tenantId = evt.TenantId ?? Guid.Empty;
        var correlationId = evt.CorrelationId ?? evt.OrderId.ToString();
        var sw = System.Diagnostics.Stopwatch.StartNew();

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
            "[Affiliate:WalletApproved] Processing | OrderId: {OrderId} | Amount: {Amount}",
            evt.OrderId, evt.AmountApproved);

        _telemetry.RecordAttempt(tenantId, EventType);

        try
        {
            // ── STEP 1: Record commission (same as before) ──────────────────────
            await _affiliateService.RecordCommissionForOrderAsync(evt.OrderId, ct);

            sw.Stop();
            _telemetry.RecordProcessingDuration(tenantId, EventType, sw.Elapsed.TotalMilliseconds);

            _logger.LogInformation(
                "[Affiliate:WalletApproved] Commission recorded | OrderId: {OrderId} | Duration: {DurationMs}ms",
                evt.OrderId, sw.Elapsed.TotalMilliseconds);
        }
        catch (AffiliateCommissionDeferredException ex)
        {
            AffiliateConsumerHelper.HandleDeferredCommission(
                _logger,
                _telemetry,
                tenantId,
                EventType,
                evt.OrderId,
                sw,
                ex,
                "Affiliate:WalletApproved");
        }
        catch (Exception ex)
        {
            sw.Stop();
            _telemetry.RecordFailed(tenantId, EventType);
            _telemetry.RecordProcessingDuration(tenantId, EventType, sw.Elapsed.TotalMilliseconds);

            _logger.LogError(ex,
                "[Affiliate:WalletApproved] Commission recording failed | OrderId: {OrderId} | Duration: {DurationMs}ms",
                evt.OrderId, sw.Elapsed.TotalMilliseconds);
            throw;
        }

        // ── STEP 2: Deferred conversion + ranking signals (AF-36) ───────────
        // These were skipped at AffiliateAttributedEvent time because the order
        // required payment verification. Now that payment is approved, fire them.
        try
        {
            if (tenantId == Guid.Empty || _mongoDbContext == null)
            {
                _logger.LogWarning(
                    "[Affiliate:WalletApproved:Conversion] Skipping deferred conversion signal (TenantId empty or no MongoDB context) | OrderId: {OrderId}",
                    evt.OrderId);
                return;
            }

            // Find the most recent unconverted click for this affiliate/order (we need AffiliateAccountId)
            // Get it from the AffiliateOrderDocument (written by AffiliateOrderAttributionConsumer)
            var orderFilter = Builders<AffiliateOrderDocument>.Filter.And(
                Builders<AffiliateOrderDocument>.Filter.Eq(d => d.TenantId, tenantId),
                Builders<AffiliateOrderDocument>.Filter.Eq(d => d.OrderId, evt.OrderId));

            var affiliateOrder = await _mongoDbContext.Database
                .GetCollection<AffiliateOrderDocument>("affiliate_orders")
                .Find(orderFilter)
                .FirstOrDefaultAsync(ct);

            if (affiliateOrder == null)
            {
                _logger.LogDebug(
                    "[Affiliate:WalletApproved:Conversion] No affiliate order document found — not an affiliate order | OrderId: {OrderId}",
                    evt.OrderId);
                return;
            }

            var affiliateAccountId = affiliateOrder.AffiliateAccountId;

            // Link the most recent unconverted click to this order
            var clickFilter = Builders<AffiliateClickDocument>.Filter.And(
                Builders<AffiliateClickDocument>.Filter.Eq(d => d.TenantId, tenantId),
                Builders<AffiliateClickDocument>.Filter.Eq(d => d.AffiliateAccountId, affiliateAccountId),
                Builders<AffiliateClickDocument>.Filter.Eq(d => d.OrderId, null));

            var sort = Builders<AffiliateClickDocument>.Sort.Descending(d => d.ClickedAtUtc);

            var conversionUpdate = Builders<AffiliateClickDocument>.Update
                .Set(d => d.OrderId, evt.OrderId)
                .Set(d => d.ConvertedAtUtc, DateTime.UtcNow);

            var clickResult = await _mongoDbContext.AffiliateClicks.FindOneAndUpdateAsync(
                clickFilter, conversionUpdate,
                new FindOneAndUpdateOptions<AffiliateClickDocument>
                {
                    Sort = sort,
                    ReturnDocument = ReturnDocument.After
                }, ct);

            if (clickResult?.ProductId != null)
            {
                await _rankingService.IncrementConversionSignalAsync(
                    clickResult.ProductId.Value, tenantId, 0, ct);

                _logger.LogInformation(
                    "[Affiliate:WalletApproved:Conversion] Deferred ranking signal fired | OrderId: {OrderId} | ProductId: {ProductId}",
                    evt.OrderId, clickResult.ProductId.Value);
            }

            // Increment TotalConversions in analytics summary
            var summaryFilter = Builders<AffiliateAnalyticsSummaryDocument>.Filter.And(
                Builders<AffiliateAnalyticsSummaryDocument>.Filter.Eq(d => d.TenantId, tenantId),
                Builders<AffiliateAnalyticsSummaryDocument>.Filter.Eq(d => d.AffiliateAccountId, affiliateAccountId));

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
                    "[Affiliate:WalletApproved:Conversion] Deferred TotalConversions incremented | AffiliateId: {AffiliateId} | Conversions: {Conversions}",
                    affiliateAccountId, newConversions);
            }
        }
        catch (Exception ex)
        {
            // Non-fatal: commission already recorded. Log and continue.
            _logger.LogError(ex,
                "[Affiliate:WalletApproved:Conversion] Deferred conversion signal failed (non-critical) | OrderId: {OrderId}",
                evt.OrderId);
        }
    }
}

/// <summary>
/// Replays affiliate commission recording at the authoritative accounting-posted boundary.
/// This prevents payment-capture consumers from outrunning ledger truth.
/// </summary>
public sealed class OrderCollectionAccountingPostedAffiliateConsumer : IConsumer<OrderCollectionAccountingPostedEvent>
{
    private const string EventType = "AccountingPosted";

    private readonly IAffiliateService _affiliateService;
    private readonly AffiliateTelemetry _telemetry;
    private readonly ILogger<OrderCollectionAccountingPostedAffiliateConsumer> _logger;

    public OrderCollectionAccountingPostedAffiliateConsumer(
        IAffiliateService affiliateService,
        AffiliateTelemetry telemetry,
        ILogger<OrderCollectionAccountingPostedAffiliateConsumer> logger)
    {
        _affiliateService = affiliateService;
        _telemetry = telemetry;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<OrderCollectionAccountingPostedEvent> context)
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
            "[Affiliate:AccountingPosted] Processing | OrderId: {OrderId} | JournalId: {JournalId} | FinancialPostingStatus: {FinancialPostingStatus}",
            evt.OrderId, evt.FinancialPostingJournalId, evt.NewFinancialPostingStatus);

        _telemetry.RecordAttempt(tenantId, EventType);

        try
        {
            await _affiliateService.RecordCommissionForOrderAsync(evt.OrderId, ct);

            sw.Stop();
            _telemetry.RecordProcessingDuration(tenantId, EventType, sw.Elapsed.TotalMilliseconds);

            _logger.LogInformation(
                "[Affiliate:AccountingPosted] Completed | OrderId: {OrderId} | Duration: {DurationMs}ms",
                evt.OrderId, sw.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _telemetry.RecordFailed(tenantId, EventType);
            _telemetry.RecordProcessingDuration(tenantId, EventType, sw.Elapsed.TotalMilliseconds);

            _logger.LogError(ex,
                "[Affiliate:AccountingPosted] Failed | OrderId: {OrderId} | Duration: {DurationMs}ms",
                evt.OrderId, sw.Elapsed.TotalMilliseconds);
            throw;
        }
    }
}

/// <summary>
/// AF-2.5: Shared helper for affiliate consumer observability.
/// Extracted to avoid duplication across consumers.
/// </summary>
internal static class AffiliateConsumerHelper
{
    /// <summary>
    /// Measures event lag (UtcNow - OccurredAtUtc) and:
    /// - Records the lag in the histogram metric
    /// - Logs a warning if lag exceeds 30 seconds
    /// </summary>
    public static void DetectAndRecordLag(
        ILogger logger,
        AffiliateTelemetry telemetry,
        Guid tenantId,
        string eventType,
        DateTime occurredAtUtc)
    {
        var lagSeconds = (DateTime.UtcNow - occurredAtUtc).TotalSeconds;
        telemetry.RecordLag(tenantId, eventType, lagSeconds);

        if (lagSeconds > AffiliateTelemetry.LagWarningThresholdSeconds)
        {
            logger.LogWarning(
                "[Affiliate:LagDetected] Event lag {LagSeconds:F1}s exceeds threshold ({Threshold}s) | " +
                "EventType: {EventType} | OccurredAt: {OccurredAt:O}",
                lagSeconds, AffiliateTelemetry.LagWarningThresholdSeconds,
                eventType, occurredAtUtc);
        }
    }

    public static void HandleDeferredCommission(
        ILogger logger,
        AffiliateTelemetry telemetry,
        Guid tenantId,
        string eventType,
        Guid orderId,
        Stopwatch stopwatch,
        AffiliateCommissionDeferredException exception,
        string operationName)
    {
        stopwatch.Stop();
        telemetry.RecordSkipped(tenantId, "accounting_not_posted");
        telemetry.RecordProcessingDuration(tenantId, eventType, stopwatch.Elapsed.TotalMilliseconds);

        logger.LogInformation(
            exception,
            "[{Operation}] Deferred until collection accounting is posted | OrderId: {OrderId} | Reason: {Reason} | Duration: {DurationMs}ms",
            operationName,
            orderId,
            exception.Reason,
            stopwatch.Elapsed.TotalMilliseconds);
    }
}

/// <summary>
/// AF-18: Triggers affiliate commission recording when order is marked as Completed.
///
/// SCENARIO: Admin changes order status to Completed (either from Delivered or directly).
/// This ensures commission is recorded even if the order skips the Delivered status.
///
/// IDEMPOTENCY: RecordCommissionForOrderAsync is idempotent via unique constraint
/// on (TenantId, OrderItemId, EntryType). If commission was already recorded when
/// the order was Delivered, this consumer will silently skip duplicate entries.
///
/// CRITICAL: This fixes the bug where affiliates become invisible in /admin/affiliates
/// when orders go directly to Completed status without triggering OfflinePaymentCollectedEvent.
/// </summary>
public class OrderCompletedAffiliateConsumer : IConsumer<OrderCompletedEvent>
{
    private const string EventType = "OrderCompleted";

    private readonly IAffiliateService _affiliateService;
    private readonly AffiliateTelemetry _telemetry;
    private readonly ILogger<OrderCompletedAffiliateConsumer> _logger;

    public OrderCompletedAffiliateConsumer(
        IAffiliateService affiliateService,
        AffiliateTelemetry telemetry,
        ILogger<OrderCompletedAffiliateConsumer> logger)
    {
        _affiliateService = affiliateService;
        _telemetry = telemetry;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<OrderCompletedEvent> context)
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
            "[Affiliate:OrderCompleted] Processing | OrderId: {OrderId}",
            evt.OrderId);

        _telemetry.RecordAttempt(tenantId, EventType);

        try
        {
            await _affiliateService.RecordCommissionForOrderAsync(evt.OrderId, ct);

            sw.Stop();
            _telemetry.RecordProcessingDuration(tenantId, EventType, sw.Elapsed.TotalMilliseconds);

            _logger.LogInformation(
                "[Affiliate:OrderCompleted] Completed | OrderId: {OrderId} | Duration: {DurationMs}ms",
                evt.OrderId, sw.Elapsed.TotalMilliseconds);
        }
        catch (AffiliateCommissionDeferredException ex)
        {
            AffiliateConsumerHelper.HandleDeferredCommission(
                _logger,
                _telemetry,
                tenantId,
                EventType,
                evt.OrderId,
                sw,
                ex,
                "Affiliate:OrderCompleted");
            return;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _telemetry.RecordFailed(tenantId, EventType);
            _telemetry.RecordProcessingDuration(tenantId, EventType, sw.Elapsed.TotalMilliseconds);

            _logger.LogError(ex,
                "[Affiliate:OrderCompleted] Failed | OrderId: {OrderId} | Duration: {DurationMs}ms",
                evt.OrderId, sw.Elapsed.TotalMilliseconds);
            throw; // Let MassTransit retry
        }
    }
}
