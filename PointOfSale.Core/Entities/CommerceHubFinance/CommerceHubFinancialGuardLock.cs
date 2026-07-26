namespace PointOfSale.Core.Entities.CommerceHubFinance;

public sealed class CommerceHubFinancialGuardLock
{
    public Guid Id { get; set; }
    public string Scope { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
