using MassTransit;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Infrastructure.Consumers;

/// <summary>
/// Listens for OrderCreatedEvent and marks the corresponding checkout session
/// as ConvertedToOrder.
/// 
/// Phase: Abandoned Checkout / Lost Orders
/// 
/// This consumer bridges Order creation → CheckoutSession conversion.
/// It is idempotent: calling MarkAsConvertedAsync multiple times is safe.
/// 
/// ARCHITECTURAL RULES:
/// - Non-blocking: if no checkout session exists for the cart, silently skips
/// - Fires AFTER order is committed to PostgreSQL
/// - Uses CartId to find the matching active/abandoned checkout session
/// </summary>
public class CheckoutSessionOrderConversionConsumer : IConsumer<OrderCreatedEvent>
{
    private readonly ICheckoutSessionService _sessionService;
    private readonly ILogger<CheckoutSessionOrderConversionConsumer> _logger;

    public CheckoutSessionOrderConversionConsumer(
        ICheckoutSessionService sessionService,
        ILogger<CheckoutSessionOrderConversionConsumer> logger)
    {
        _sessionService = sessionService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<OrderCreatedEvent> context)
    {
        var @event = context.Message;
        var ct = context.CancellationToken;

        if (!@event.TenantId.HasValue || @event.TenantId.Value == Guid.Empty)
            return;

        if (!@event.CartId.HasValue || @event.CartId.Value == Guid.Empty)
            return;

        try
        {
            await _sessionService.MarkAsConvertedAsync(
                @event.CartId.Value,
                @event.TenantId.Value,
                @event.OrderId,
                @event.OrderNumber,
                ct);

            _logger.LogInformation(
                "[AbandonedCheckout] Order conversion bridge | OrderId: {OrderId} | CartId: {CartId} | OrderNumber: {OrderNumber}",
                @event.OrderId, @event.CartId, @event.OrderNumber);
        }
        catch (Exception ex)
        {
            // Non-fatal: don't block order creation
            _logger.LogWarning(ex,
                "[AbandonedCheckout] Failed to mark checkout session as converted (non-fatal) | OrderId: {OrderId} | CartId: {CartId}",
                @event.OrderId, @event.CartId);
        }
    }
}
