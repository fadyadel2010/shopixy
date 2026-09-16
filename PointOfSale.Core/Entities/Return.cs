using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Return entity for both purchase and sales returns
/// </summary>
public class Return : BaseEntity, ITenantOwned
{
    /// <summary>
    /// The tenant that owns this return.
    /// CRITICAL: NOT NULL - enforced at database level.
    /// </summary>
    public Guid TenantId { get; set; }
    
    public string ReturnNumber { get; set; } = string.Empty;
    public DateTime ReturnDate { get; set; }
    
    public ReturnType ReturnType { get; set; }
    
    /// <summary>
    /// Reference to original invoice
    /// </summary>
    public Guid? OriginalInvoiceId { get; set; }
    public string? OriginalInvoiceNumber { get; set; }
    public SalesInvoice? OriginalInvoice { get; set; }

    /// <summary>
    /// SQL-authoritative purchase invoice lineage for purchase returns.
    /// Kept separate from OriginalInvoiceId, which is a SalesInvoice foreign key.
    /// </summary>
    public Guid? OriginalPurchaseInvoiceId { get; set; }
    public PurchaseInvoice? OriginalPurchaseInvoice { get; set; }
    
    public Guid? CustomerId { get; set; }
    public Guid? SupplierId { get; set; }
    public Guid WarehouseId { get; set; }
    
    public decimal Subtotal { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }

    /// <summary>
    /// Immutable settlement snapshot. Legacy returns may be null. Purchase returns support
    /// supplier account credit or a full cash refund; card settlement is not authoritative.
    /// </summary>
    public RefundMethod? RefundMethod { get; set; }
    public decimal CashRefundAmount { get; set; }
    public decimal CardRefundAmount { get; set; }
    public decimal CustomerCreditAmount { get; set; }
    
    public string? Reason { get; set; }
    public string? Notes { get; set; }
    
    // Navigation properties
    public Customer? Customer { get; set; }
    public Supplier? Supplier { get; set; }
    public Warehouse Warehouse { get; set; } = null!;
    public ICollection<ReturnItem> Items { get; set; } = new List<ReturnItem>();
}
