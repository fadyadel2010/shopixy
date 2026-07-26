using System.ComponentModel.DataAnnotations;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Supplier entity with account balance tracking
/// </summary>
public class Supplier : BaseEntity, ITenantOwned
{
    /// <summary>
    /// The tenant that owns this supplier.
    /// CRITICAL: NOT NULL - enforced at database level.
    /// </summary>
    public Guid TenantId { get; set; }
    
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;
    
    [MaxLength(50)]
    public string? Phone { get; set; }
    
    [MaxLength(200)]
    public string? Email { get; set; }
    
    public string? Address { get; set; }
    
    [MaxLength(50)]
    public string? TaxNumber { get; set; }
    
    /// <summary>
    /// Current balance in ledger sign convention (debit minus credit).
    /// Positive = debit balance, negative = credit balance.
    /// </summary>
    public decimal Balance { get; set; }
    
    /// <summary>
    /// Opening balance amount (initial balance when starting to use the system)
    /// </summary>
    public decimal? OpeningBalanceAmount { get; set; }
    
    /// <summary>
    /// Balance type label derived from the ledger sign convention.
    /// </summary>
    public string? BalanceType { get; set; } // "Debit" or "Credit"
    
    /// <summary>
    /// Date of opening balance
    /// </summary>
    public DateTime? OpeningBalanceDate { get; set; }
    
    /// <summary>
    /// Payment terms in days (e.g., Net 30)
    /// </summary>
    public int PaymentTermsDays { get; set; }
    
    public string? Notes { get; set; }
    
    // Navigation properties
    public ICollection<PurchaseInvoice> PurchaseInvoices { get; set; } = new List<PurchaseInvoice>();
    public ICollection<SupplierTransaction> Transactions { get; set; } = new List<SupplierTransaction>();
}
