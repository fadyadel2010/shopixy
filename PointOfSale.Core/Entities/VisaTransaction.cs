using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Represents all financial movements related to the Visa account
/// </summary>
public class VisaTransaction : BaseEntity, ITenantOwned
{
    /// <summary>
    /// The tenant that owns this transaction.
    /// CRITICAL: NOT NULL - enforced at database level.
    /// </summary>
    public Guid TenantId { get; set; }
    
    /// <summary>
    /// Transaction date
    /// </summary>
    [Required]
    public DateTime TransactionDate { get; set; }

    /// <summary>
    /// Type of transaction:
    /// 1 = Sale by Visa
    /// 2 = Cash Transfer to Visa
    /// 3 = Cash Transfer from Visa
    /// 4 = Bank Transfer to Visa
    /// 5 = Bank Transfer from Visa
    /// 6 = Return (Refund)
    /// 7 = Expense
    /// 8 = Opening Balance
    /// 9 = Adjustment
    /// </summary>
    [Required]
    public int TransactionType { get; set; }

    /// <summary>
    /// Transaction amount (always positive)
    /// </summary>
    [Required]
    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    /// <summary>
    /// Whether this transaction increases (true) or decreases (false) the Visa balance
    /// </summary>
    [Required]
    public bool IsDebit { get; set; }

    /// <summary>
    /// Visa balance after this transaction
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal BalanceAfter { get; set; }

    /// <summary>
    /// Reference number (Invoice #, Return #, etc.)
    /// </summary>
    public string? ReferenceNumber { get; set; }

    /// <summary>
    /// Description of the transaction
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Additional notes
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Related source entity ID (SalesInvoice, Return, Expense, etc.)
    /// </summary>
    public Guid? SourceEntityId { get; set; }

    /// <summary>
    /// Type of source entity (SalesInvoice, Return, CashboxTransaction, etc.)
    /// </summary>
    public string? SourceEntityType { get; set; }

    /// <summary>
    /// User who created this transaction
    /// </summary>
    public Guid? CreatedByUserId { get; set; }

    // Navigation properties
    public User? CreatedByUser { get; set; }

    // Helper methods
    public static string GetTransactionTypeName(int type)
    {
        return type switch
        {
            1 => "Sale by Visa",
            2 => "Cash Transfer to Visa",
            3 => "Cash Transfer from Visa",
            4 => "Bank Transfer to Visa",
            5 => "Bank Transfer from Visa",
            6 => "Return (Refund)",
            7 => "Expense",
            8 => "Opening Balance",
            9 => "Adjustment",
            10 => "Purchase",
            _ => "Unknown"
        };
    }
}
