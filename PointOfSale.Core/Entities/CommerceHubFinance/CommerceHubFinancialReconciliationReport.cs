namespace PointOfSale.Core.Entities.CommerceHubFinance;

public sealed class CommerceHubFinancialReconciliationReport
{
    public Guid Id { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = "Healthy";
    public int MismatchCount { get; set; }
    public string DetailsJson { get; set; } = "{}";
}
