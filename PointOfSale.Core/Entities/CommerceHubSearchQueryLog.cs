namespace PointOfSale.Core.Entities;

public class CommerceHubSearchQueryLog : BaseEntity
{
    public string QueryText { get; set; } = string.Empty;
    public long ResultCount { get; set; }
    public string Intent { get; set; } = "unknown";
    public string? AppliedExpansionsJson { get; set; }
    public DateTime SearchedAt { get; set; }
}
