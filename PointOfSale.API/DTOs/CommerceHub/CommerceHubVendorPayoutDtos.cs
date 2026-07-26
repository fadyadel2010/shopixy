using PointOfSale.Core.Enums;

namespace PointOfSale.API.DTOs.CommerceHub;

public sealed class CommerceHubVendorPayoutMethodDto
{
    public Guid Id { get; set; }
    public Guid VendorId { get; set; }
    public string MethodType { get; set; } = string.Empty;
    public string DestinationValue { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsVerified { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class CommerceHubCreateVendorPayoutMethodRequestDto
{
    public CommerceHubPayoutMethod MethodType { get; set; } = CommerceHubPayoutMethod.BankTransfer;
    public string DestinationValue { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
}
