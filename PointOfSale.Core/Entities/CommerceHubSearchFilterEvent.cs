namespace PointOfSale.Core.Entities;

public class CommerceHubSearchFilterEvent : BaseEntity
{
    public string FilterKey { get; set; } = string.Empty;
    public string FilterValue { get; set; } = string.Empty;
    public string QueryText { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
    public string? SessionId { get; set; }
    public Guid? TenantId { get; set; }
}
