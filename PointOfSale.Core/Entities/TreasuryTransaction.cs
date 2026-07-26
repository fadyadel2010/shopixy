using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Represents cash movements between Treasury and other accounts
/// </summary>
public class TreasuryTransaction : BaseEntity, ITenantOwned
{
    /// <summary>
    /// The tenant that owns this transaction.
    /// CRITICAL: NOT NULL - enforced at database level.
    /// </summary>
    public Guid TenantId { get; set; }
    
    /// <summary>
    /// Sequential transaction number for tracking
    /// </summary>
    [Required]
    public string TransactionNumber { get; set; } = string.Empty;
    
    /// <summary>
    /// Transaction date
    /// </summary>
    [Required]
    public DateTime TransactionDate { get; set; }
    
    /// <summary>
    /// Type of transaction: 1=Deposit (Cash In), 2=Withdrawal (Cash Out)
    /// </summary>
    [Required]
    public int TransactionType { get; set; }
    
    /// <summary>
    /// Payment method / Target account type: 1=Bank, 2=Visa, 3=Supplier, 4=Customer
    /// </summary>
    [Required]
    public int PaymentMethod { get; set; }
    
    /// <summary>
    /// Target account ID (Bank/Supplier/Customer ID, null for Visa)
    /// </summary>
    public Guid? TargetAccountId { get; set; }
    
    /// <summary>
    /// Target account name (for display purposes)
    /// </summary>
    public string? TargetAccountName { get; set; }
    
    /// <summary>
    /// Transaction amount
    /// </summary>
    [Required]
    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }
    
    /// <summary>
    /// Treasury balance after this transaction
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal BalanceAfter { get; set; }
    
    /// <summary>
    /// Reference number for tracking
    /// </summary>
    public string? ReferenceNumber { get; set; }
    
    /// <summary>
    /// Transaction notes
    /// </summary>
    public string? Notes { get; set; }
    
    /// <summary>
    /// User who created this transaction
    /// </summary>
    public Guid? CreatedByUserId { get; set; }
    
    // Navigation properties
    public User? CreatedByUser { get; set; }
}
