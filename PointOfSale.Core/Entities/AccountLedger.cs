using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Unified ledger for tracking all financial transactions for customers and suppliers
/// </summary>
public class AccountLedger : BaseEntity, ITenantOwned
{
    /// <summary>
    /// The tenant that owns this ledger entry.
    /// CRITICAL: NOT NULL - enforced at database level.
    /// </summary>
    public Guid TenantId { get; set; }
    
    /// <summary>
    /// Type of party (Customer or Supplier)
    /// </summary>
    public PartyType PartyType { get; set; }
    
    /// <summary>
    /// ID of the customer or supplier
    /// </summary>
    public Guid PartyId { get; set; }
    
    /// <summary>
    /// Transaction date
    /// </summary>
    public DateTime TransactionDate { get; set; }
    
    /// <summary>
    /// Type of transaction
    /// </summary>
    public TransactionType TransactionType { get; set; }
    
    /// <summary>
    /// Document/Reference number (Invoice number, payment receipt, etc.)
    /// </summary>
    public string DocumentNumber { get; set; } = string.Empty;
    
    /// <summary>
    /// Debit amount (increases receivable for customers, decreases payable for suppliers)
    /// </summary>
    public decimal DebitAmount { get; set; }
    
    /// <summary>
    /// Credit amount (decreases receivable for customers, increases payable for suppliers)
    /// </summary>
    public decimal CreditAmount { get; set; }
    
    /// <summary>
    /// Running balance after this transaction
    /// </summary>
    public decimal RunningBalance { get; set; }
    
    /// <summary>
    /// Reference to source document ID
    /// </summary>
    public Guid? ReferenceId { get; set; }
    
    /// <summary>
    /// Additional notes or description
    /// </summary>
    public string? Notes { get; set; }
    
    /// <summary>
    /// User who created this transaction
    /// </summary>
    public Guid? UserId { get; set; }
    
    // Navigation properties
    public User? User { get; set; }
}
