using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Entities;

public class CustomerPayoutDetails : BaseEntity
{
    public Guid CustomerId { get; set; }
    public CommerceHubCustomer Customer { get; set; } = null!;
    public RefundPayoutMethod Method { get; set; }
    public string AccountIdentifier { get; set; } = string.Empty;
    public string? AccountHolderName { get; set; }
    public bool IsVerified { get; set; }
}
