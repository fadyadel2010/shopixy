using System.Text.Json.Serialization;

namespace PointOfSale.API.Services.Analytics;

public interface IAnalyticsSessionJourneyService
{
    Task<AnalyticsSessionJourneyResponse?> GetOrderJourneyAsync(
        Guid tenantId,
        Guid orderId,
        CancellationToken cancellationToken = default);

    Task<AnalyticsSessionJourneyResponse?> GetJourneyAsync(
        Guid tenantId,
        Guid orderId,
        string sessionId,
        CancellationToken cancellationToken = default);
}

public class AnalyticsSessionJourneyResponse
{
    [JsonIgnore]
    public string SessionId { get; set; } = string.Empty;
    public Guid OrderId { get; set; }
    public string Source { get; set; } = "direct";
    public string Device { get; set; } = "unknown";
    public List<string> VisitedPages { get; set; } = new();
    public int DurationSeconds { get; set; }
    public DateTime FirstVisitAtUtc { get; set; }
    public DateTime? CheckoutVisitAtUtc { get; set; }
    public DateTime PurchaseCompletedAtUtc { get; set; }
    public int SessionsCount { get; set; }
    public int EventCount { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Medium { get; set; }
}
