namespace PointOfSale.Core.Entities;

public class SystemSchemaVersion : BaseEntity
{
    public string Component { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public DateTime AppliedAt { get; set; }
    public string? DetailsJson { get; set; }
}
