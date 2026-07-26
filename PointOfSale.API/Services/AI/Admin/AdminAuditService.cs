using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PointOfSale.API.Services.AI.Models;
using PointOfSale.API.Services.AI.Router;

namespace PointOfSale.API.Services.AI.Admin;

/// <summary>
/// Admin audit service implementation.
/// PHASE 4: Audit & Explanation Intelligence.
/// 
/// Uses the AI to generate human-readable explanations of system events.
/// </summary>
public class AdminAuditService : IAdminAuditService
{
    private readonly IModelRouter? _modelRouter;
    private readonly IAIPromptManager _promptManager;
    private readonly IAIResponseValidator _responseValidator;
    private readonly CommerceAIOptions _options;
    private readonly ILogger<AdminAuditService> _logger;

    public AdminAuditService(
        IAIPromptManager promptManager,
        IAIResponseValidator responseValidator,
        IOptions<CommerceAIOptions> options,
        ILogger<AdminAuditService> logger,
        IModelRouter? modelRouter = null)
    {
        _promptManager = promptManager;
        _responseValidator = responseValidator;
        _options = options.Value;
        _logger = logger;
        _modelRouter = modelRouter;
    }

    /// <inheritdoc />
    public async Task<AdminExplanationResponse> ExplainEventAsync(
        AdminAuditContext context,
        CancellationToken cancellationToken = default)
    {
        var correlationId = Guid.NewGuid().ToString();

        try
        {
            _logger.LogDebug(
                "Explaining audit event | EventType: {EventType} | Actor: {Actor} | Source: {Source} | CorrelationId: {CorrelationId}",
                context.Event.Type, context.Actor, context.Source, correlationId);

            // Build the admin prompt
            var systemPrompt = BuildAdminSystemPrompt();
            var userPrompt = BuildAdminUserPrompt(context);

            // For now, generate a rule-based explanation
            // In production, this would use the AI model
            var explanation = GenerateExplanation(context);

            _logger.LogInformation(
                "Audit event explained | EventType: {EventType} | WasManual: {WasManual} | CorrelationId: {CorrelationId}",
                context.Event.Type, context.Actor == AuditActors.Admin, correlationId);

            return new AdminExplanationResponse
            {
                Success = true,
                Explanation = explanation,
                ActorSummary = GetActorSummary(context.Actor, context.Source),
                WasManual = context.Actor == AuditActors.Admin,
                EventTimestamp = context.Event.At
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "Failed to explain audit event | CorrelationId: {CorrelationId}",
                correlationId);

            return new AdminExplanationResponse
            {
                Success = false,
                Explanation = "Unable to generate explanation at this time. Please try again.",
                EventTimestamp = context.Event.At
            };
        }
    }

    /// <inheritdoc />
    public Task<AdminAuditContext> BuildAuditContextAsync(
        Guid tenantId,
        string eventType,
        Guid entityId,
        string question)
    {
        // This would fetch the event details from the audit log
        // For now, return a basic context
        var context = new AdminAuditContext
        {
            Actor = AuditActors.System,
            Source = AuditSources.System,
            Question = question,
            Event = new AuditEventContext
            {
                Type = eventType,
                At = DateTime.UtcNow,
                EntityId = entityId
            }
        };

        return Task.FromResult(context);
    }

    /// <summary>
    /// Build the admin mode system prompt.
    /// </summary>
    private string BuildAdminSystemPrompt()
    {
        return @"
You are CommerceAI in Admin Mode.

Your role is to:
- Explain WHY something happened
- Translate system events into human reasoning
- Help admins understand orders & shipments
- NEVER change anything

RESPONSE RULES:
❌ DO NOT:
- Blame users, carriers, or the system
- Use technical jargon
- Expose internal class names or enums
- Suggest rollback, edit, retry, or override

✅ DO:
- Explain causality (who/what triggered the event)
- Mention the source (carrier, webhook, admin)
- Be neutral & factual
- Be calm & clear

OUTPUT FORMAT:
{
  ""type"": ""message"",
  ""message"": ""Human explanation of why the event happened"",
  ""orderDraft"": null,
  ""missingInfo"": []
}
".Trim();
    }

    /// <summary>
    /// Build the admin user prompt from audit context.
    /// </summary>
    private string BuildAdminUserPrompt(AdminAuditContext context)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        return JsonSerializer.Serialize(context, options);
    }

    /// <summary>
    /// Generate a rule-based explanation for the event.
    /// In production, this would use the AI model for more nuanced explanations.
    /// </summary>
    private string GenerateExplanation(AdminAuditContext context)
    {
        var eventType = context.Event.Type;
        var actor = context.Actor;
        var source = context.Source;
        var from = context.Event.From;
        var to = context.Event.To;
        var timestamp = context.Event.At.ToString("MMMM d, yyyy 'at' h:mm tt");

        return eventType switch
        {
            AuditEventTypes.ShipmentStatusChanged => GenerateShipmentStatusExplanation(context, timestamp),
            AuditEventTypes.ShipmentDelivered => GenerateDeliveredExplanation(context, timestamp),
            AuditEventTypes.ShipmentFailed => GenerateFailedShipmentExplanation(context, timestamp),
            AuditEventTypes.OrderStatusChanged => GenerateOrderStatusExplanation(context, timestamp),
            AuditEventTypes.OrderCancelled => GenerateOrderCancelledExplanation(context, timestamp),
            AuditEventTypes.PaymentReceived => GeneratePaymentReceivedExplanation(context, timestamp),
            AuditEventTypes.ManualOverride => GenerateManualOverrideExplanation(context, timestamp),
            _ => GenerateGenericExplanation(context, timestamp)
        };
    }

    private string GenerateShipmentStatusExplanation(AdminAuditContext context, string timestamp)
    {
        var carrier = context.Shipment?.Carrier ?? "the carrier";
        var from = context.Event.From ?? "previous status";
        var to = context.Event.To ?? "new status";
        var source = context.Source;

        if (source == AuditSources.Webhook)
        {
            return $"The shipment status changed from \"{from}\" to \"{to}\" on {timestamp}. This update was received automatically through a tracking update from {carrier}.";
        }
        else if (context.Actor == AuditActors.Admin)
        {
            var adminName = context.Event.AdminName ?? "an administrator";
            return $"The shipment status was manually changed from \"{from}\" to \"{to}\" by {adminName} on {timestamp}.";
        }
        else
        {
            return $"The shipment status changed from \"{from}\" to \"{to}\" on {timestamp}. This was an automatic update from the system.";
        }
    }

    private string GenerateDeliveredExplanation(AdminAuditContext context, string timestamp)
    {
        var carrier = context.Shipment?.Carrier ?? "the carrier";
        var orderNumber = context.Order?.Number ?? "the order";

        if (context.Source == AuditSources.Webhook)
        {
            return $"The order was marked as delivered on {timestamp} because {carrier} confirmed successful delivery through their tracking update.";
        }
        else if (context.Actor == AuditActors.Admin)
        {
            var adminName = context.Event.AdminName ?? "An administrator";
            return $"{adminName} manually marked the order as delivered on {timestamp}.";
        }
        else
        {
            return $"The order was automatically marked as delivered on {timestamp} based on carrier confirmation.";
        }
    }

    private string GenerateFailedShipmentExplanation(AdminAuditContext context, string timestamp)
    {
        var carrier = context.Shipment?.Carrier ?? "the carrier";
        var reason = context.Event.Reason ?? "The specific reason was not provided";

        return $"The delivery attempt failed on {timestamp}. {carrier} reported that the delivery could not be completed. {reason}. Note: This does not automatically cancel the order - an admin review may be needed to determine next steps.";
    }

    private string GenerateOrderStatusExplanation(AdminAuditContext context, string timestamp)
    {
        var from = context.Event.From ?? "previous status";
        var to = context.Event.To ?? "new status";

        if (context.Actor == AuditActors.Admin)
        {
            var adminName = context.Event.AdminName ?? "An administrator";
            return $"{adminName} changed the order status from \"{from}\" to \"{to}\" on {timestamp}.";
        }
        else if (context.Actor == AuditActors.Payment)
        {
            return $"The order status changed from \"{from}\" to \"{to}\" on {timestamp} due to a payment status update.";
        }
        else if (context.Actor == AuditActors.Carrier)
        {
            return $"The order status changed from \"{from}\" to \"{to}\" on {timestamp} based on shipment tracking updates.";
        }
        else
        {
            return $"The order status changed from \"{from}\" to \"{to}\" on {timestamp}. This was an automatic system update.";
        }
    }

    private string GenerateOrderCancelledExplanation(AdminAuditContext context, string timestamp)
    {
        var reason = context.Event.Reason ?? "No specific reason was provided";

        if (context.Actor == AuditActors.Admin)
        {
            var adminName = context.Event.AdminName ?? "An administrator";
            return $"{adminName} cancelled the order on {timestamp}. {reason}.";
        }
        else if (context.Actor == AuditActors.Customer)
        {
            return $"The customer requested cancellation, and the order was cancelled on {timestamp}.";
        }
        else if (context.Actor == AuditActors.Payment)
        {
            return $"The order was cancelled on {timestamp} due to a payment issue. {reason}.";
        }
        else
        {
            return $"The order was cancelled on {timestamp}. {reason}.";
        }
    }

    private string GeneratePaymentReceivedExplanation(AdminAuditContext context, string timestamp)
    {
        var method = context.Order?.Payment.Method ?? "the selected payment method";
        var amount = context.Order?.Total ?? 0;
        var currency = context.Order?.Currency ?? "EGP";

        if (context.Source == AuditSources.Webhook)
        {
            return $"Payment of {amount:N2} {currency} was received on {timestamp} through {method}. This was confirmed via the payment gateway webhook.";
        }
        else if (context.Actor == AuditActors.Admin)
        {
            var adminName = context.Event.AdminName ?? "An administrator";
            return $"{adminName} manually recorded a payment of {amount:N2} {currency} on {timestamp}.";
        }
        else
        {
            return $"Payment of {amount:N2} {currency} was received on {timestamp} via {method}.";
        }
    }

    private string GenerateManualOverrideExplanation(AdminAuditContext context, string timestamp)
    {
        var adminName = context.Event.AdminName ?? "An administrator";
        var reason = context.Event.Reason ?? "No specific reason was recorded";
        var from = context.Event.From ?? "the previous value";
        var to = context.Event.To ?? "the new value";

        return $"This was a manual override performed by {adminName} on {timestamp}. The value was changed from \"{from}\" to \"{to}\". Reason: {reason}.";
    }

    private string GenerateGenericExplanation(AdminAuditContext context, string timestamp)
    {
        var eventType = context.Event.Type;
        var description = context.Event.Description ?? $"A {eventType} event occurred";
        var actorSummary = GetActorSummary(context.Actor, context.Source);

        return $"{description} on {timestamp}. This was triggered by {actorSummary}.";
    }

    /// <summary>
    /// Get a human-readable summary of the actor.
    /// </summary>
    private string GetActorSummary(string actor, string source)
    {
        return (actor, source) switch
        {
            (AuditActors.Admin, _) => "an administrator",
            (AuditActors.Carrier, AuditSources.Webhook) => "a carrier webhook update",
            (AuditActors.Carrier, _) => "the carrier",
            (AuditActors.Payment, AuditSources.Webhook) => "a payment gateway notification",
            (AuditActors.Payment, _) => "the payment system",
            (AuditActors.Customer, _) => "the customer",
            (AuditActors.System, AuditSources.BackgroundJob) => "an automated background process",
            (AuditActors.System, _) => "the system automatically",
            (AuditActors.Integration, _) => "an external integration",
            _ => "the system"
        };
    }
}
