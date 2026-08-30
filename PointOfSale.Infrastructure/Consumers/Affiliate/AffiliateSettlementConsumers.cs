using System.Diagnostics;
using MassTransit;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces.Affiliate;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.Services.Affiliate;

namespace PointOfSale.Infrastructure.Consumers.Affiliate;

/// <summary>
/// AF-7: Consumes delivery truth events to approve earned commissions.
///
/// Settlement lifecycle trigger: Delivery confirms the order is fulfilled,
/// so earned commissions can be approved for payout.
///
/// IDEMPOTENCY: Delegated to AffiliateService.ApproveCommissionForOrderAsync
/// which uses unique constraints per (TenantId, OrderItemId, EntryType).
///
/// TELEMETRY: Emits attempt, success, failure counters + duration + lag.
/// </summary>
public class AffiliateOrderDeliveredConsumer :
    IConsumer<OrderDeliveredEvent>,
    IConsumer<FulfillmentDeliveredEvent>
{
    private readonly IAffiliateService _affiliateService;
    private readonly AffiliateTelemetry _telemetry;
    private readonly ILogger<AffiliateOrderDeliveredConsumer> _logger;

    public AffiliateOrderDeliveredConsumer(
        IAffiliateService affiliateService,
        AffiliateTelemetry telemetry,
        ILogger<AffiliateOrderDeliveredConsumer> logger)
    {
        _affiliateService = affiliateService;
        _telemetry = telemetry;
        _logger = logger;
    }

    public Task Consume(ConsumeContext<OrderDeliveredEvent> context)
    {
        var evt = context.Message;
        return ApproveCommissionForDeliveredOrderAsync(
            evt.TenantId,
            evt.OrderId,
            evt.DeliveredAt,
            nameof(OrderDeliveredEvent),
            evt.EventVersion,
            context.CancellationToken);
    }

    public Task Consume(ConsumeContext<FulfillmentDeliveredEvent> context)
    {
        var evt = context.Message;
        return ApproveCommissionForDeliveredOrderAsync(
            evt.TenantId,
            evt.OrderId,
            evt.TransitionedAt,
            nameof(FulfillmentDeliveredEvent),
            evt.EventVersion,
            context.CancellationToken,
            evt.DeliveryReference,
            evt.ProofOfDeliveryUrl);
    }

    private async Task ApproveCommissionForDeliveredOrderAsync(
        Guid? tenantId,
        Guid orderId,
        DateTime deliveredAtUtc,
        string sourceEvent,
        int eventVersion,
        CancellationToken ct,
        string? deliveryReference = null,
        string? proofOfDeliveryUrl = null)
    {
        var stopwatch = Stopwatch.StartNew();

        if (deliveredAtUtc != default)
        {
            var lagSeconds = (DateTime.UtcNow - deliveredAtUtc).TotalSeconds;
            _telemetry.RecordLag(tenantId ?? Guid.Empty, "settlement", lagSeconds);

            if (lagSeconds > 30)
            {
                _logger.LogWarning(
                    "[Affiliate:Settlement:Delivered] High event lag: {LagSeconds:F1}s | OrderId: {OrderId} | SourceEvent: {SourceEvent}",
                    lagSeconds, orderId, sourceEvent);
            }
        }

        _telemetry.RecordAttempt(tenantId ?? Guid.Empty, "settlement");

        _logger.LogInformation(
            "[Affiliate:Settlement:Delivered] OrderId: {OrderId} | DeliveredAt: {DeliveredAt} | SourceEvent: {SourceEvent}",
            orderId, deliveredAtUtc, sourceEvent);

        try
        {
            await _affiliateService.ApproveCommissionForDeliveredOrderAsync(
                orderId,
                tenantId,
                deliveredAtUtc,
                sourceEvent,
                eventVersion,
                ct,
                deliveryReference,
                proofOfDeliveryUrl);

            stopwatch.Stop();
            _telemetry.RecordSuccess(tenantId ?? Guid.Empty, 1);
            _telemetry.RecordProcessingDuration(tenantId ?? Guid.Empty, "settlement", stopwatch.ElapsedMilliseconds);

            _logger.LogInformation(
                "[Affiliate:Settlement:Delivered] Approval complete | OrderId: {OrderId} | Duration: {DurationMs}ms | SourceEvent: {SourceEvent}",
                orderId, stopwatch.ElapsedMilliseconds, sourceEvent);
        }
        catch (AffiliateCommissionDeferredException ex)
        {
            stopwatch.Stop();
            _telemetry.RecordSkipped(tenantId ?? Guid.Empty, "settlement_not_ready");
            _telemetry.RecordProcessingDuration(tenantId ?? Guid.Empty, "settlement", stopwatch.ElapsedMilliseconds);

            // Delivery can legitimately precede payment collection/accounting (notably COD).
            // The authoritative payment/accounting consumers replay commission recording and
            // reconcile approval once both durable SQL boundaries are satisfied.
            _logger.LogInformation(
                ex,
                "[Affiliate:Settlement:Delivered] Deferred to payment/accounting reconciliation | OrderId: {OrderId} | Reason: {Reason} | SourceEvent: {SourceEvent}",
                orderId, ex.Reason, sourceEvent);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _telemetry.RecordProcessingDuration(tenantId ?? Guid.Empty, "settlement", stopwatch.ElapsedMilliseconds);
            _telemetry.RecordFailed(tenantId ?? Guid.Empty, "settlement");

            _logger.LogError(ex,
                "[Affiliate:Settlement:Delivered] Failed | OrderId: {OrderId} | Duration: {DurationMs}ms | SourceEvent: {SourceEvent}",
                orderId, stopwatch.ElapsedMilliseconds, sourceEvent);

            throw;
        }
    }
}
