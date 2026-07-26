using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Payment record for purchase invoice
/// </summary>
public class PurchaseInvoicePayment : BaseEntity
{
    public Guid PurchaseInvoiceId { get; set; }
    
    public DateTime PaymentDate { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }
    
    // Navigation properties
    public PurchaseInvoice PurchaseInvoice { get; set; } = null!;
}
