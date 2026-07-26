namespace PointOfSale.Core.Entities;

public class CommerceHubSearchClick : BaseEntity
{
    public string QueryText { get; set; } = string.Empty;
    public Guid ProductId { get; set; }
    public Guid? VariantId { get; set; }
    public int Position { get; set; }
    public DateTime ClickedAt { get; set; }
    public string? SessionId { get; set; }
    public Guid? TenantId { get; set; }
}
