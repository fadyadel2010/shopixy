using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

public class Expense : BaseEntity, ITenantOwned
{
    /// <summary>
    /// The tenant that owns this expense.
    /// CRITICAL: NOT NULL - enforced at database level.
    /// </summary>
    public Guid TenantId { get; set; }
    
    [Required]
    public Guid ExpenseCategoryId { get; set; }
    
    [ForeignKey("ExpenseCategoryId")]
    public ExpenseCategory? Category { get; set; }
    
    [Required]
    public DateTime ExpenseDate { get; set; }
    
    [Required]
    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }
    
    [Required]
    [MaxLength(200)]
    public string Description { get; set; } = string.Empty;
    
    public string? Notes { get; set; }
    
    [Required]
    [MaxLength(50)]
    public string PaymentMethod { get; set; } = "Cash"; // Cash, Card, BankTransfer, Cheque
    
    public Guid? SupplierId { get; set; }
    
    [ForeignKey("SupplierId")]
    public Supplier? Supplier { get; set; }
    
    public Guid? CashboxId { get; set; }
    
    [ForeignKey("CashboxId")]
    public Cashbox? Cashbox { get; set; }
    
    public Guid? BankId { get; set; }
    
    [ForeignKey("BankId")]
    public Bank? Bank { get; set; }
    
    [MaxLength(500)]
    public string? ReceiptPath { get; set; } // Path to uploaded receipt/document
    
    [MaxLength(100)]
    public string? ReferenceNumber { get; set; }
    
    public bool IsRecurring { get; set; } = false;
    
    [MaxLength(50)]
    public string? RecurringFrequency { get; set; } // Monthly, Quarterly, Yearly
    
    public bool RequiresApproval { get; set; } = false;
    
    public bool IsApproved { get; set; } = false;
    
    public Guid? ApprovedBy { get; set; }
    
    public DateTime? ApprovedAt { get; set; }

    /// <summary>
    /// Monotonic accounting mutation version for replay-safe expense posting.
    /// Incremented only when an approved expense mutation must affect the ledger.
    /// </summary>
    public int AccountingMutationVersion { get; set; }
}
