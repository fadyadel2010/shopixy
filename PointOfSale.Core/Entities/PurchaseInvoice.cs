using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Purchase invoice from supplier
/// </summary>
public class PurchaseInvoice : BaseEntity, ITenantOwned
{
    /// <summary>
    /// The tenant that owns this invoice.
    /// CRITICAL: NOT NULL - enforced at database level.
    /// </summary>
    public Guid TenantId { get; set; }
    
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; }
    public DateTime? DueDate { get; set; }
    
    public Guid SupplierId { get; set; }
    public Guid WarehouseId { get; set; }
    
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;

    /// <summary>
    /// Monotonic mutation version used to reject stale purchase accounting work.
    /// </summary>
    public int AccountingMutationVersion { get; set; }
    
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
    public string? Notes { get; set; }
    
    // Navigation properties
    public Supplier Supplier { get; set; } = null!;
    public Warehouse Warehouse { get; set; } = null!;
    public ICollection<PurchaseInvoiceItem> Items { get; set; } = new List<PurchaseInvoiceItem>();
    public ICollection<PurchaseInvoicePayment> Payments { get; set; } = new List<PurchaseInvoicePayment>();
}
