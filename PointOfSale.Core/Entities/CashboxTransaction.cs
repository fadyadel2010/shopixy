using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Tracks individual cash movements in a cashbox
/// </summary>
public class CashboxTransaction : BaseEntity, ITenantOwned
{
    /// <summary>
    /// The tenant that owns this transaction.
    /// CRITICAL: NOT NULL - enforced at database level.
    /// </summary>
    public Guid TenantId { get; set; }
    
    public Guid CashboxId { get; set; }
    public DateTime TransactionDate { get; set; }
    public CashboxTransactionType TransactionType { get; set; }
    
    /// <summary>
    /// Transaction amount (positive for cash in, negative for cash out)
    /// </summary>
    public decimal Amount { get; set; }
    
    /// <summary>
    /// Running balance after this transaction
    /// </summary>
    public decimal BalanceAfter { get; set; }
    
    /// <summary>
    /// Payment method used (for filtering and reporting)
    /// </summary>
    public PaymentMethod? PaymentMethod { get; set; }
    
    /// <summary>
    /// Reference to related invoice/payment (if applicable)
    /// </summary>
    public Guid? ReferenceId { get; set; }
    
    /// <summary>
    /// Invoice/receipt number for reference
    /// </summary>
    public string? ReferenceNumber { get; set; }
    
    /// <summary>
    /// Transaction description
    /// </summary>
    public string Description { get; set; } = string.Empty;
    
    /// <summary>
    /// User who performed the transaction
    /// </summary>
    public Guid? UserId { get; set; }
    
    /// <summary>
    /// Additional notes
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Indicates if this transaction represents the initial opening balance
    /// </summary>
    public bool IsOpeningBalance { get; set; }
    
    // Navigation properties
    public Cashbox Cashbox { get; set; } = null!;
    public User? User { get; set; }
}
