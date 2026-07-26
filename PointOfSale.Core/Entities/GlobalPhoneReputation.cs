namespace PointOfSale.Core.Entities;

/// <summary>
/// Global phone reputation shared across all tenants.
/// Warning-only intelligence; never used for automatic blocking.
/// </summary>
public class GlobalPhoneReputation
{
    public Guid Id { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public int SpamReports { get; set; }
    public DateTime FirstReportedAtUtc { get; set; }
    public DateTime LastReportedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
