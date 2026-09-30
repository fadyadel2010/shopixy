using MassTransit;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Events.WhatsApp;
using PointOfSale.Core.Interfaces.WhatsApp;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.WhatsApp.Interfaces;
using MongoDB.Bson;

namespace PointOfSale.Infrastructure.Consumers.WhatsApp;

public class WhatsAppOrderConfirmationConsumer : IConsumer<WhatsAppOrderConfirmationRequestedEvent>
{
    private readonly IWhatsAppFeatureGateService _featureGate;
    private readonly IWhatsAppMessageDispatcher _dispatcher;
    private readonly IWhatsAppMessageLogService _logService;
    private readonly ILogger<WhatsAppOrderConfirmationConsumer> _logger;

    public WhatsAppOrderConfirmationConsumer(
        IWhatsAppFeatureGateService featureGate,
        IWhatsAppMessageDispatcher dispatcher,
        IWhatsAppMessageLogService logService,
        ILogger<WhatsAppOrderConfirmationConsumer> logger)
    {
        _featureGate = featureGate;
        _dispatcher = dispatcher;
        _logService = logService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<WhatsAppOrderConfirmationRequestedEvent> context)
    {
        var msg = context.Message;
        
        // 1. Feature Gate Check (Double check)
        if (!msg.TenantId.HasValue)
        {
             _logger.LogWarning("Event received without TenantId, skipping.");
             return;
        }
        
        if (!await _featureGate.IsOrderConfirmationEnabledAsync(msg.TenantId.Value))
        {
            _logger.LogInformation("WhatsApp Order Confirmation disabled for Tenant {TenantId}, skipping.", msg.TenantId);
            return;
        }

        // 2. Resolve Account
        var account = await _featureGate.GetActiveAccountAsync(msg.TenantId.Value, Core.Enums.WhatsAppUseCase.OrderConfirmation);
        if (account == null)
        {
             _logger.LogWarning("No active WhatsApp account found for Tenant {TenantId} despite gate check.", msg.TenantId);
             return;
        }

        // 3. Create a work log. The dispatcher rebuilds message content from current SQL authority.
        var log = new WhatsAppMessageLogDocument
        {
            TenantId = msg.TenantId.ToString(),
            AccountId = account.Id.ToString(),
            TargetPhone = msg.CustomerPhone,
            MessageType = "ORDER_CONFIRMATION",
            RelatedEntityId = msg.OrderId.ToString(),
            RelatedEntityType = "Order",
            Status = "PENDING",
            PayloadSnapshot = new BsonDocument 
            { 
                { "orderNumber", msg.OrderNumber },
                { "amount", (double)msg.OrderTotal }
            },
            IdempotencyKey = $"whatsapp:order-confirmation:{msg.TenantId.Value:N}:{msg.OrderId:N}"
        };

        log = await _logService.CreateLogAsync(log);

        // 4. Dispatch
        await _dispatcher.DispatchAsync(log.Id);
    }
}
