using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Supplier transaction history
/// </summary>
public class SupplierTransaction : BaseEntity, ITenantOwned
{
    /// <summary>
    /// The tenant that owns this transaction.
    /// CRITICAL: NOT NULL - enforced at database level.
    /// </summary>
    public Guid TenantId { get; set; }
    
    public Guid SupplierId { get; set; }
    
    public DateTime TransactionDate { get; set; }
    public TransactionType TransactionType { get; set; }
    
    /// <summary>
    /// Debit amount (decreases supplier balance - payments made)
    /// </summary>
    public decimal DebitAmount { get; set; }
    
    /// <summary>
    /// Credit amount (increases supplier balance - what we owe)
    /// </summary>
    public decimal CreditAmount { get; set; }
    
    /// <summary>
    /// Balance after this transaction
    /// </summary>
    public decimal BalanceAfter { get; set; }
    
    /// <summary>
    /// Reference to source document (invoice, payment, etc.)
    /// </summary>
    public Guid? ReferenceId { get; set; }
    public string? ReferenceNumber { get; set; }
    
    /// <summary>
    /// Indicates if this is an opening balance transaction
    /// </summary>
    public bool IsOpeningBalance { get; set; }
    
    public string? Notes { get; set; }
    
    // Navigation properties
    public Supplier Supplier { get; set; } = null!;
}
