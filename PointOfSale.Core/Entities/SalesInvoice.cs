using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Sales invoice to customer
/// </summary>
public class SalesInvoice : BaseEntity, ITenantOwned
{
    /// <summary>
    /// The tenant that owns this invoice.
    /// CRITICAL: NOT NULL - enforced at database level.
    /// </summary>
    public Guid TenantId { get; set; }
    
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; }
    
    public Guid? CustomerId { get; set; }
    public Guid WarehouseId { get; set; }
    
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Completed;
    
    /// <summary>
    /// Subtotal before discount and tax
    /// </summary>
    public decimal Subtotal { get; set; }
    
    /// <summary>
    /// Discount amount
    /// </summary>
    public decimal DiscountAmount { get; set; }
    
    /// <summary>
    /// Discount percentage
    /// </summary>
    public decimal DiscountPercentage { get; set; }
    
    /// <summary>
    /// Tax amount
    /// </summary>
    public decimal TaxAmount { get; set; }
    
    /// <summary>
    /// Total amount = Subtotal - Discount + Tax
    /// </summary>
    public decimal TotalAmount { get; set; }
    
    /// <summary>
    /// Amount paid
    /// </summary>
    public decimal PaidAmount { get; set; }
    
    /// <summary>
    /// Remaining balance
    /// </summary>
    public decimal RemainingAmount => TotalAmount - PaidAmount;
    
    public PaymentMethod? PaymentMethod { get; set; }
    
    /// <summary>
    /// For mixed payments (cash + card)
    /// </summary>
    public decimal? CashAmount { get; set; }
    public decimal? CardAmount { get; set; }
    
    public string? Notes { get; set; }
    
    /// <summary>
    /// Sales representative who created this invoice (optional)
    /// </summary>
    public Guid? SalesRepresentativeId { get; set; }

    /// <summary>
    /// Monotonic version used by asynchronous accounting outbox messages.
    /// Stale sale posting/reversal messages must not post after a newer sale
    /// mutation has committed.
    /// </summary>
    public int AccountingMutationVersion { get; set; }
    
    // Navigation properties
    public Customer? Customer { get; set; }
    public Warehouse Warehouse { get; set; } = null!;
    public SalesRepresentative? SalesRepresentative { get; set; }
    public ICollection<SalesInvoiceItem> Items { get; set; } = new List<SalesInvoiceItem>();
    public ICollection<SalesInvoicePayment> Payments { get; set; } = new List<SalesInvoicePayment>();
}
