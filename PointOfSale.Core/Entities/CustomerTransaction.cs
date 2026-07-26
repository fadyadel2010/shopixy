using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Customer transaction history
/// </summary>
public class CustomerTransaction : BaseEntity, ITenantOwned
{
    /// <summary>
    /// The tenant that owns this transaction.
    /// CRITICAL: NOT NULL - enforced at database level.
    /// </summary>
    public Guid TenantId { get; set; }
    
    public Guid CustomerId { get; set; }
    
    public DateTime TransactionDate { get; set; }
    public TransactionType TransactionType { get; set; }
    
    /// <summary>
    /// Debit amount (increases customer balance - what they owe)
    /// </summary>
    public decimal DebitAmount { get; set; }
    
    /// <summary>
    /// Credit amount (decreases customer balance - payments received)
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
    public Customer Customer { get; set; } = null!;
}
