using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Entities.CommerceHubFinance;

public sealed class CommerceHubVendorPayoutMethod : BaseEntity
{
    public Guid VendorId { get; set; }
    public CommerceHubPayoutMethod MethodType { get; set; } = CommerceHubPayoutMethod.BankTransfer;
    public string DestinationValue { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsVerified { get; set; }
}
