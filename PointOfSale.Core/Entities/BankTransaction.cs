using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

public class BankTransaction : BaseEntity, ITenantOwned
{
    /// <summary>
    /// The tenant that owns this transaction.
    /// CRITICAL: NOT NULL - enforced at database level.
    /// </summary>
    public Guid TenantId { get; set; }
    
    [Required]
    public Guid BankId { get; set; }
    
    [ForeignKey("BankId")]
    public Bank? Bank { get; set; }
    
    [Required]
    public DateTime TransactionDate { get; set; }
    
    [Required]
    public int TransactionType { get; set; } // 1=Deposit, 2=Withdrawal, 3=TransferIn, 4=TransferOut, 5=OpeningBalance
    
    [Required]
    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }
    
    public string? Description { get; set; }
    
    public string? ReferenceNumber { get; set; }

    public string? Notes { get; set; }
    
    public bool IsOpeningBalance { get; set; } = false;
}
