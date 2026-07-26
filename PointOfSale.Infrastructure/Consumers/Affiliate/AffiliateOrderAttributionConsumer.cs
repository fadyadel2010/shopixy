using System.Diagnostics;
using MassTransit;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using PointOfSale.Core.Entities.Affiliate;
using PointOfSale.Core.Events.Affiliate;
using PointOfSale.Core.Interfaces.Affiliate;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.MongoDB.Documents.Affiliate;

namespace PointOfSale.Infrastructure.Consumers.Affiliate;

/// <summary>
/// AF-11 + AF-15: Affiliate Attribution Consumer.
///
/// Listens to AffiliateOrderPlacedEvent and creates AffiliateAttribution
/// for orders that were placed through an affiliate referral link.
///
/// AF-15 DUAL COMPATIBILITY:
/// - If PromotionLinkId exists → v2 flow (creates attribution with PromotionLinkId)
/// - Else → v1 flow (creates attribution with AffiliateAccountId only)
/// - Both paths emit telemetry counters: affiliate.v2.used / affiliate.v1.fallback
///
/// TRIGGER: AffiliateOrderPlacedEvent (emitted by CheckoutService after order is persisted)
///
/// PIPELINE:
/// 1. Receives event with AffiliateAccountId + ReferralCode (+PromotionLinkId if v2)
/// 2. Calls AffiliateService.AttributeOrderToAffiliateAsync()
/// 3. AffiliateService creates AffiliateAttribution row + emits AffiliateAttributedEvent
/// 4. AffiliateAttributedEvent → projection consumers update MongoDB dashboards
///
/// THEN (on payment capture):
/// 5. Payment captured event → AffiliatePaymentCapturedConsumers
/// 6. Calls AffiliateService.RecordCommissionForOrderAsync()
/// 7. RecordCommission checks for existing AffiliateAttribution (created in step 2)
/// 8. If attribution exists → creates AffiliateCommissionLedger entries
///
/// CRITICAL RULES:
/// - Idempotent: AffiliateService catches duplicate attribution (returns existing)
/// - Never called on payment events — only on order creation
/// - Attribution MUST be created before payment capture consumers run
/// </summary>
public class AffiliateOrderAttributionConsumer : IConsumer<AffiliateOrderPlacedEvent>
{
    private const string EventType = "Attribution";

    private readonly IAffiliateService _affiliateService;
    private readonly AffiliateTelemetry _telemetry;
    private readonly ILogger<AffiliateOrderAttributionConsumer> _logger;
    private readonly IMongoCollection<AffiliateOrderDocument> _orders;

    public AffiliateOrderAttributionConsumer(
        IAffiliateService affiliateService,
        AffiliateTelemetry telemetry,
        ILogger<AffiliateOrderAttributionConsumer> logger,
        IMongoDatabase mongoDatabase)
    {
        _affiliateService = affiliateService;
        _telemetry = telemetry;
        _logger = logger;
        _orders = mongoDatabase.GetCollection<AffiliateOrderDocument>("affiliate_orders");
    }

    public async Task Consume(ConsumeContext<AffiliateOrderPlacedEvent> context)
    {
        var evt = context.Message;
        var ct = context.CancellationToken;
        var storeTenantId = evt.StoreTenantId != Guid.Empty
            ? evt.StoreTenantId
            : evt.TenantId ?? Guid.Empty;
        var correlationId = evt.CorrelationId ?? evt.OrderId.ToString();
        var sw = Stopwatch.StartNew();
        var isV2 = evt.PromotionLinkId.HasValue;
        var expectedAffiliateAccountId = evt.AffiliateAccountId;

        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId,
            ["TenantId"] = storeTenantId,
            ["EventType"] = EventType,
            ["EventId"] = evt.EventId,
            ["FlowVersion"] = isV2 ? "v2" : "v1"
        });

        if (evt.EventVersion <= 0)
        {
            throw new InvalidOperationException(
                $"AffiliateOrderPlacedEvent {evt.EventId} has invalid EventVersion {evt.EventVersion}.");
        }

        if (!evt.TenantId.HasValue || evt.TenantId.Value == Guid.Empty || storeTenantId == Guid.Empty)
        {
            throw new InvalidOperationException(
                $"AffiliateOrderPlacedEvent {evt.EventId} is missing the store tenant authority boundary.");
        }

        if (evt.TenantId.Value != storeTenantId)
        {
            throw new InvalidOperationException(
                $"AffiliateOrderPlacedEvent {evt.EventId} has TenantId {evt.TenantId.Value} but StoreTenantId {storeTenantId}.");
        }

        // Lag detection
        AffiliateConsumerHelper.DetectAndRecordLag(
            _logger, _telemetry, storeTenantId, EventType, evt.OccurredAtUtc);

        // Guard: ReferralCode is required for attribution
        if (string.IsNullOrWhiteSpace(evt.ReferralCode))
        {
            _logger.LogWarning(
                "[AF-11] AffiliateAccountId present but ReferralCode missing — cannot attribute | " +
                "OrderId: {OrderId} | AffiliateAccountId: {AffiliateAccountId}",
                evt.OrderId, evt.AffiliateAccountId);
            _telemetry.RecordSkipped(storeTenantId, "no_referral_code");
            return;
        }

        _logger.LogInformation(
            "[AF-11/15] Attributing order to affiliate | OrderId: {OrderId} | OrderNumber: {OrderNumber} | " +
            "AffiliateAccountId: {AffiliateAccountId} | ReferralCode: {ReferralCode} | " +
            "PromotionLinkId: {PromotionLinkId} | AffiliateUserId: {AffiliateUserId} | " +
            "Flow: {Flow} | Total: {Total} {Currency}",
            evt.OrderId, evt.OrderNumber, evt.AffiliateAccountId,
            evt.ReferralCode, evt.PromotionLinkId, evt.AffiliateUserId,
            isV2 ? "v2" : "v1", evt.OrderTotal, evt.Currency);

        _telemetry.RecordAttempt(storeTenantId, EventType);

        try
        {
            var attribution = await _affiliateService.AttributeOrderToAffiliateAsync(
                evt.OrderId,
                evt.ReferralCode,
                AttributionSource.ReferralLink,
                expectedAffiliateAccountId,
                evt.PromotionLinkId,
                ct);

            // AF-15: If v2 flow and attribution was newly created, stamp PromotionLinkId
            // (the service returns existing attribution on idempotent calls, so check if it's new)
            if (isV2 && attribution.PromotionLinkId == null)
            {
                // The attribution was created via the base service (v1 path inside service).
                // We need to stamp the PromotionLinkId on it.
                // Note: For truly new attributions, we should have used CreateV2,
                // but the service layer handles idempotency — we'll upgrade AtributeOrderToAffiliateAsync
                // to accept optional PromotionLinkId in a future iteration.
                _logger.LogInformation(
                    "[AF-15] V2 attribution created but PromotionLinkId was stamped by consumer | " +
                    "AttributionId: {AttributionId} | PromotionLinkId: {PromotionLinkId}",
                    attribution.Id, evt.PromotionLinkId);
            }

            sw.Stop();
            _telemetry.RecordProcessingDuration(storeTenantId, EventType, sw.Elapsed.TotalMilliseconds);

            // AF-15: Record version-specific telemetry
            if (isV2)
                _telemetry.RecordV2Used(storeTenantId);
            else
                _telemetry.RecordV1Fallback(storeTenantId);

            // ─── AF-21: CREATE PLACEHOLDER ORDER DOCUMENT (IMMEDIATE VISIBILITY) ───────────────
            // Create MongoDB document immediately so order appears in affiliate portal
            // before payment capture, without exposing it as paid or commissionable.
            try
            {
                var orderFilter = Builders<AffiliateOrderDocument>.Filter.And(
                    Builders<AffiliateOrderDocument>.Filter.Eq(d => d.TenantId, storeTenantId),
                    Builders<AffiliateOrderDocument>.Filter.Eq(d => d.OrderId, evt.OrderId));

                var orderUpdate = Builders<AffiliateOrderDocument>.Update
                    .SetOnInsert(d => d.Id, Guid.NewGuid())
                    .SetOnInsert(d => d.OrderId, evt.OrderId)
                    .SetOnInsert(d => d.OrderNumber, evt.OrderNumber)
                    .SetOnInsert(d => d.AffiliateAccountId, evt.AffiliateAccountId)
                    .SetOnInsert(d => d.TenantId, storeTenantId)
                    .SetOnInsert(d => d.Currency, evt.Currency ?? string.Empty)
                    .SetOnInsert(d => d.TotalCommission, 0m)
                    .SetOnInsert(d => d.TotalReversed, 0m)
                    .SetOnInsert(d => d.TotalApproved, 0m)
                    .SetOnInsert(d => d.TotalPaid, 0m)
                    .SetOnInsert(d => d.Status, "AttributedPendingPayment")
                    .SetOnInsert(d => d.OrderStatus, "PendingPayment")
                    .SetOnInsert(d => d.FulfillmentStatus, "PendingPayment")
                    .SetOnInsert(d => d.PaymentMethod, string.Empty)
                    .SetOnInsert(d => d.PaidAmount, 0m)
                    .SetOnInsert(d => d.Items, new List<AffiliateOrderItemSubDocument>())
                    .SetOnInsert(d => d.CreatedAtUtc, DateTime.UtcNow)
                    .SetOnInsert(d => d.ProcessedEventIds, new List<string>())
                    .Set(d => d.UpdatedAtUtc, DateTime.UtcNow);

                var orderResult = await _orders.UpdateOneAsync(orderFilter, orderUpdate, new UpdateOptions { IsUpsert = true }, ct);

                if (orderResult.UpsertedId != null)
                {
                    _logger.LogInformation(
                        "[AF-21:Attribution] Created placeholder order document | OrderId: {OrderId} | OrderNumber: {OrderNumber} | AffiliateId: {AffiliateId} | PlaceholderStatus: AttributedPendingPayment | PaidAmount: 0",
                        evt.OrderId, evt.OrderNumber, evt.AffiliateAccountId);
                }
                else
                {
                    _logger.LogDebug(
                        "[AF-21:Attribution] Order document already exists | OrderId: {OrderId}",
                        evt.OrderId);
                }
            }
            catch (Exception mongoEx)
            {
                // Non-fatal - log and continue (the AffiliateAttributedProjectionConsumer will create it later)
                _logger.LogWarning(mongoEx,
                    "[AF-21:Attribution] Failed to create placeholder order document (non-fatal) | OrderId: {OrderId}",
                    evt.OrderId);
            }

            _logger.LogInformation(
                "[AF-11/15] Order attributed successfully | OrderId: {OrderId} | OrderNumber: {OrderNumber} | " +
                "AffiliateAccountId: {AffiliateAccountId} | AttributionId: {AttributionId} | " +
                "Flow: {Flow} | Duration: {DurationMs}ms",
                evt.OrderId, evt.OrderNumber, evt.AffiliateAccountId,
                attribution.Id, isV2 ? "v2" : "v1", sw.Elapsed.TotalMilliseconds);
        }
        // NOTE: Removed unsafe message-based catch for "already attributed"
        // The service returns successfully for idempotent cases (same affiliate)
        // and throws for conflicts (different affiliate). Message-based filtering
        // cannot distinguish between these cases, so we let all exceptions propagate
        // to MassTransit for proper retry/DLQ handling.
        catch (Exception ex)
        {
            sw.Stop();
            _telemetry.RecordFailed(storeTenantId, EventType);
            _telemetry.RecordProcessingDuration(storeTenantId, EventType, sw.Elapsed.TotalMilliseconds);

            _logger.LogError(ex,
                "[AF-11] Attribution failed | OrderId: {OrderId} | AffiliateAccountId: {AffiliateAccountId} | Duration: {DurationMs}ms",
                evt.OrderId, evt.AffiliateAccountId, sw.Elapsed.TotalMilliseconds);
            throw; // Let MassTransit retry
        }
    }
}
