namespace PointOfSale.Core.Entities;

public class CommerceHubPayoutItem : BaseEntity
{
    public Guid PayoutId { get; set; }
    public CommerceHubPayout Payout { get; set; } = null!;
    public Guid EarningId { get; set; }
    public CommerceHubVendorEarning Earning { get; set; } = null!;
    public decimal Amount { get; set; }
}
