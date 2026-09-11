using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Immutable customer account payment and its exact treasury allocation.
/// </summary>
public class CustomerPayment : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid CustomerId { get; set; }
    public DateTime PaymentDate { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public decimal CashAmount { get; set; }
    public decimal CardAmount { get; set; }
    public decimal BankTransferAmount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }

    public Customer Customer { get; set; } = null!;
}
