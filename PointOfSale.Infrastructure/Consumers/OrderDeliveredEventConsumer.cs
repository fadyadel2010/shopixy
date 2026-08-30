using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Consumers;

/// <summary>
/// PHASE OFL: Order Delivered Event Handler
/// 
/// This consumer observes delivery without treating delivery as payment evidence.
/// 
/// INTEGRATION RULES:
/// - Offline collection orders: Remain Pending until explicit collection evidence enters the payment lifecycle
///   and is recorded only by explicit admin/carrier collection authority
/// - Wallet orders (AwaitingVerification): Fail closed until payment approval completes
/// - Online orders: No action (already Collected via webhook)
/// 
/// Physical delivery and financial recognition are deliberately separate authorities.
/// </summary>
public class FulfillmentDeliveredEventConsumer : IConsumer<FulfillmentDeliveredEvent>
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<FulfillmentDeliveredEventConsumer> _logger;

    public FulfillmentDeliveredEventConsumer(
        ApplicationDbContext dbContext,
        ILogger<FulfillmentDeliveredEventConsumer> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<FulfillmentDeliveredEvent> context)
    {
        var evt = context.Message;
        var ct = context.CancellationToken;

        _logger.LogInformation(
            "[FulfillmentDeliveredHandler] Processing | OrderId: {OrderId} | OrderNumber: {OrderNumber} | " +
            "PaymentMethod: {PaymentMethod} | PaymentStatus: {PaymentStatus}",
            evt.OrderId, evt.OrderNumber, evt.PaymentMethod, evt.PaymentStatus);

        if (!evt.TenantId.HasValue || evt.TenantId.Value == Guid.Empty)
        {
            _logger.LogError(
                "[FulfillmentDeliveredHandler] Rejected delivery event without tenant authority | OrderId: {OrderId} | EventId: {EventId}",
                evt.OrderId,
                evt.EventId);
            return;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // RULE 1: COD Orders - Delivery is not cash collection evidence
        // ═══════════════════════════════════════════════════════════════════════
        if (IsCodPaymentMethod(evt.PaymentMethod) && evt.PaymentStatus == PaymentStatus.Pending)
        {
            _logger.LogInformation(
                "[OrderDeliveredHandler] COD order delivered with payment pending; awaiting explicit collection evidence | " +
                "OrderId: {OrderId}",
                evt.OrderId);
        }
        // ═══════════════════════════════════════════════════════════════════════
        // RULE 2: Wallet Orders - Delivery before payment recognition must fail closed
        // ═══════════════════════════════════════════════════════════════════════
        else if (IsWalletPaymentMethod(evt.PaymentMethod) && 
                 evt.PaymentStatus == PaymentStatus.AwaitingVerification)
        {
            // Reload authoritative payment state to avoid blocking retries if payment was approved
            // after the event was published but before this consumer processed it
            var order = await _dbContext.Orders
                .AsNoTracking()
                .Where(o => o.Id == evt.OrderId && !o.IsDeleted)
                .Select(o => new { o.PaymentStatus })
                .FirstOrDefaultAsync(ct);

            if (order == null)
            {
                _logger.LogWarning(
                    "[OrderDeliveredHandler] Order not found when reloading payment status | OrderId: {OrderId}",
                    evt.OrderId);
                throw new InvalidOperationException($"Order {evt.OrderId} not found.");
            }

            // Check the fresh payment status
            if (order.PaymentStatus == PaymentStatus.AwaitingVerification)
            {
                _logger.LogCritical(
                    "[OrderDeliveredHandler] Wallet delivery arrived before payment authority was recognized | " +
                    "OrderId: {OrderId} | Status: AwaitingVerification (confirmed from DB)",
                    evt.OrderId);

                throw new InvalidOperationException(
                    $"Wallet order {evt.OrderId} cannot be accepted as delivered while payment is still AwaitingVerification. Manual payment approval must complete first.");
            }
            else
            {
                _logger.LogInformation(
                    "[OrderDeliveredHandler] Wallet payment status changed since event was published | " +
                    "OrderId: {OrderId} | EventStatus: AwaitingVerification | CurrentStatus: {CurrentStatus} | " +
                    "Proceeding with delivery processing",
                    evt.OrderId, order.PaymentStatus);
            }
        }
        // ═══════════════════════════════════════════════════════════════════════
        // RULE 3: Online Orders - Already collected, no action needed
        // ═══════════════════════════════════════════════════════════════════════
        else if (evt.PaymentStatus == PaymentStatus.Collected ||
                 evt.PaymentStatus == PaymentStatus.Settled)
        {
            _logger.LogInformation(
                "[OrderDeliveredHandler] Online payment already collected - no action needed | " +
                "OrderId: {OrderId} | PaymentStatus: {PaymentStatus}",
                evt.OrderId, evt.PaymentStatus);
        }
        else
        {
            _logger.LogInformation(
                "[OrderDeliveredHandler] No payment action required | OrderId: {OrderId} | " +
                "PaymentMethod: {PaymentMethod} | PaymentStatus: {PaymentStatus}",
                evt.OrderId, evt.PaymentMethod, evt.PaymentStatus);
        }
    }

    private static bool IsCodPaymentMethod(string paymentMethod)
    {
        return string.Equals(paymentMethod, "cod", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(paymentMethod, "cash_on_delivery", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(paymentMethod, "cash-on-delivery", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWalletPaymentMethod(string paymentMethod)
    {
        return string.Equals(paymentMethod, "wallet", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(paymentMethod, "vodafone_cash", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(paymentMethod, "instapay", StringComparison.OrdinalIgnoreCase);
    }
}
