using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Payment record for sales invoice
/// </summary>
public class SalesInvoicePayment : BaseEntity
{
    public Guid SalesInvoiceId { get; set; }
    
    public DateTime PaymentDate { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }
    
    // Navigation properties
    public SalesInvoice SalesInvoice { get; set; } = null!;
}
