namespace PointOfSale.API.DTOs.Admin;

/// <summary>
/// Response DTO for Commerce AI Insights.
/// Phase 9: Admin Insights
/// </summary>
public class CommerceAiInsightsDto
{
    public long ChatOpened { get; set; }
    public long DraftCreated { get; set; }
    public string Period { get; set; } = "Last 30 Days";
}
