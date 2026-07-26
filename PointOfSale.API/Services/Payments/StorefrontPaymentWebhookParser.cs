using System.Text.Json;
using PointOfSale.API.DTOs.Payments;

namespace PointOfSale.API.Services.Payments;

internal static class StorefrontPaymentWebhookParser
{
    public static PaymentWebhookEventDto? Parse(string provider, string rawPayload, ILogger? logger = null)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawPayload);
            var root = doc.RootElement;

            var webhookEvent = new PaymentWebhookEventDto
            {
                Provider = provider,
                EventTimestamp = DateTime.UtcNow,
                EventType = TryGetString(root, "type", "event_type", "eventType") ?? "unknown",
                ProviderEventId = TryGetString(root, "id", "event_id", "eventId") ?? Guid.NewGuid().ToString()
            };

            if (root.TryGetProperty("data", out var dataElement) &&
                dataElement.TryGetProperty("object", out var objectElement))
            {
                webhookEvent.ProviderIntentId = TryGetString(objectElement, "id", "payment_intent");
                webhookEvent.PaymentReference = TryGetString(objectElement, "metadata", "payment_reference");
                webhookEvent.Status = TryGetString(objectElement, "status");

                // Extract currency first as it's needed for amount conversion
                webhookEvent.Currency = TryGetString(objectElement, "currency")?.ToUpperInvariant();

                if (objectElement.TryGetProperty("amount", out var amountElement))
                {
                    // Use currency-aware conversion instead of hard-coded division by 100
                    var decimalPlaces = PointOfSale.Infrastructure.Payments.CurrencyHelper.GetDecimalPlaces(webhookEvent.Currency);
                    webhookEvent.Amount = amountElement.GetDecimal() / (decimal)Math.Pow(10, decimalPlaces);
                }

                if (objectElement.TryGetProperty("metadata", out var metadataElement))
                {
                    webhookEvent.PaymentReference = TryGetString(metadataElement, "payment_reference", "paymentReference");
                    var orderIdValue = TryGetString(metadataElement, "order_id", "orderId");
                    if (Guid.TryParse(orderIdValue, out var orderId))
                    {
                        webhookEvent.OrderId = orderId;
                    }
                }
            }

            if (root.TryGetProperty("error", out var errorElement))
            {
                webhookEvent.FailureReason = TryGetString(errorElement, "message");
                webhookEvent.FailureCode = TryGetString(errorElement, "code");
            }

            return webhookEvent;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to parse storefront webhook payload for provider {Provider}", provider);
            return null;
        }
    }

    public static bool IsSuccessEvent(string eventType) =>
        eventType.Contains("succeeded", StringComparison.OrdinalIgnoreCase) ||
        eventType.Contains("completed", StringComparison.OrdinalIgnoreCase) ||
        eventType.Contains("paid", StringComparison.OrdinalIgnoreCase) ||
        eventType.Contains("captured", StringComparison.OrdinalIgnoreCase) ||
        eventType.Contains("charge.success", StringComparison.OrdinalIgnoreCase);

    public static bool IsFailureEvent(string eventType) =>
        eventType.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
        eventType.Contains("declined", StringComparison.OrdinalIgnoreCase) ||
        eventType.Contains("expired", StringComparison.OrdinalIgnoreCase) ||
        eventType.Contains("canceled", StringComparison.OrdinalIgnoreCase) ||
        eventType.Contains("charge.failed", StringComparison.OrdinalIgnoreCase);

    public static bool IsAuthorizationEvent(string eventType) =>
        eventType.Contains("authorized", StringComparison.OrdinalIgnoreCase) ||
        eventType.Contains("requires_capture", StringComparison.OrdinalIgnoreCase);

    private static string? TryGetString(JsonElement element, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String)
            {
                return property.GetString();
            }
        }

        return null;
    }
}
