namespace PointOfSale.Core.Entities;

public class CommerceHubSearchImpression : BaseEntity
{
    public string QueryText { get; set; } = string.Empty;
    public Guid ProductId { get; set; }
    public int Position { get; set; }
    public DateTime ImpressedAt { get; set; }
    public string? SessionId { get; set; }
    public Guid? TenantId { get; set; }
}
