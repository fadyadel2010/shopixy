namespace PointOfSale.Core.Entities;

public class CommerceHubPaymentItem : BaseEntity
{
    public Guid PaymentId { get; set; }
    public CommerceHubPayment Payment { get; set; } = null!;
    public Guid SubOrderId { get; set; }
    public CommerceHubSubOrder SubOrder { get; set; } = null!;
    public decimal Amount { get; set; }
}
