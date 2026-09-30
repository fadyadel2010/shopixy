using MassTransit;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Events;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.Infrastructure.Consumers.AbandonedCart;

/// <summary>
/// Handles order creation to mark abandoned carts as recovered.
/// Phase: Smart Abandoned Cart Recovery Engine - Hardening
/// 
/// CRITICAL FIX:
/// This consumer ensures that when a customer completes an order,
/// the corresponding abandoned cart is marked as Recovered.
/// 
/// TRIGGER:
/// - OrderCreatedEvent published when order is placed
/// 
/// ACTION:
/// - Calls MarkAsRecoveredAsync for the cart associated with the order
/// - Idempotent: safe to call multiple times
/// 
/// NOTE: This consumer runs independently of OrderProjectionConsumer.
/// Each serves a different purpose (projection vs. abandoned cart tracking).
/// </summary>
public class OrderRecoveryConsumer : IConsumer<OrderCreatedEvent>
{
    private readonly IAbandonedCartService _cartService;
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<OrderRecoveryConsumer> _logger;

    public OrderRecoveryConsumer(
        IAbandonedCartService cartService,
        ApplicationDbContext dbContext,
        ILogger<OrderRecoveryConsumer> logger)
    {
        _cartService = cartService;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<OrderCreatedEvent> context)
    {
        var msg = context.Message;
        var ct = context.CancellationToken;

        // Skip if no cart ID (shouldn't happen but defensive)
        if (!msg.CartId.HasValue || msg.CartId == Guid.Empty)
        {
            _logger.LogDebug(
                "[Order Recovery] No CartId in event, skipping | OrderId: {OrderId}",
                msg.OrderId);
            return;
        }

        if (!msg.TenantId.HasValue || msg.TenantId.Value == Guid.Empty)
        {
            _logger.LogWarning(
                "[Order Recovery] Missing TenantId in event, skipping | OrderId: {OrderId} | CartId: {CartId}",
                msg.OrderId, msg.CartId);
            return;
        }

        if (msg.Source != OrderSource.Ecommerce)
        {
            _logger.LogDebug(
                "[Order Recovery] Non-storefront order source rejected | OrderId: {OrderId} | Source: {Source}",
                msg.OrderId,
                msg.Source);
            return;
        }

        var tenantId = msg.TenantId.Value;

        try
        {
            if (!await TenantLineageAuthority.IsActiveTenantAsync(_dbContext, tenantId, ct))
            {
                _logger.LogWarning(
                    "[Order Recovery] Inactive tenant authority rejected recovery closure | TenantId: {TenantId} | OrderId: {OrderId} | CartId: {CartId}",
                    tenantId, msg.OrderId, msg.CartId);
                return;
            }

            _logger.LogDebug(
                "[Order Recovery] Processing order | TenantId: {TenantId} | OrderId: {OrderId} | CartId: {CartId}",
                tenantId, msg.OrderId, msg.CartId);

            await _cartService.MarkAsRecoveredAsync(tenantId, msg.CartId.Value, msg.OrderId, ct);

            _logger.LogInformation(
                "[Order Recovery] Cart marked as recovered | TenantId: {TenantId} | CartId: {CartId} | OrderId: {OrderId}",
                tenantId, msg.CartId, msg.OrderId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Order Recovery] Error marking cart as recovered | TenantId: {TenantId} | CartId: {CartId} | OrderId: {OrderId}",
                msg.TenantId, msg.CartId, msg.OrderId);

            // This is an asynchronous consumer, so retrying cannot roll back order creation.
            throw;
        }
    }
}

/// <summary>
/// Consumer definition for OrderRecoveryConsumer.
/// Uses separate queue from OrderProjectionConsumer.
/// </summary>
public class OrderRecoveryConsumerDefinition : ConsumerDefinition<OrderRecoveryConsumer>
{
    public OrderRecoveryConsumerDefinition()
    {
        // Separate queue for abandoned cart recovery
        EndpointName = "abandoned-cart-recovery-v1";
        
        // Limit concurrency to avoid overwhelming database
        ConcurrentMessageLimit = 10;
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<OrderRecoveryConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        // Retry on transient failures
        endpointConfigurator.UseMessageRetry(r => r.Intervals(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(15)
        ));
    }
}
