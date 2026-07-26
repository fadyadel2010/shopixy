using System.Text.Json.Serialization;

namespace PointOfSale.API.DTOs.Storefront;

/// <summary>
/// Event types for Commerce AI metrics.
/// Phase 9: Admin Insights
/// </summary>
public enum CommerceAiEventType
{
    ChatOpened,
    OrderDraftCreated
}

/// <summary>
/// Request DTO for Commerce AI events.
/// POST /api/storefront/commerce-ai/event
/// </summary>
public class CommerceAiEventDto
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CommerceAiEventType Type { get; set; }
}
