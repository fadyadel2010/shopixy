using PointOfSale.Core.Enums;

namespace PointOfSale.API.DTOs.Customers;

public sealed class CustomerUpsertPayoutDetailsRequestDto
{
    public RefundPayoutMethod? Method { get; set; }
    public string? AccountIdentifier { get; set; }
    public string? AccountHolderName { get; set; }
}

public sealed class CustomerPayoutDetailsDto
{
    public Guid Id { get; set; }
    public RefundPayoutMethod Method { get; set; }
    public string MaskedAccountIdentifier { get; set; } = string.Empty;
    public string? AccountHolderName { get; set; }
    public bool IsVerified { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public sealed class CustomerRefundPayoutInputDto
{
    public RefundPayoutMethod? Method { get; set; }
    public string? AccountIdentifier { get; set; }
    public string? AccountHolderName { get; set; }
}

public sealed class CustomerRefundPayoutSnapshotDto
{
    public RefundPayoutMethod Method { get; set; }
    public string MaskedAccountIdentifier { get; set; } = string.Empty;
    public string? AccountHolderName { get; set; }
    public string? TransactionReference { get; set; }
    public DateTime? PaidAt { get; set; }
    public bool IsDevelopmentSimulation { get; set; }
    public bool ExternalMoneyMovementConfirmed { get; set; }
}
