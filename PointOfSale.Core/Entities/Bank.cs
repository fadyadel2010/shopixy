using System.ComponentModel.DataAnnotations;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

public class Bank : BaseEntity, ITenantOwned
{
    /// <summary>
    /// The tenant that owns this bank.
    /// CRITICAL: NOT NULL - enforced at database level.
    /// </summary>
    public Guid TenantId { get; set; }
    
    [Required]
    public string BankName { get; set; } = string.Empty;
    public string? BranchName { get; set; }
    public string? AccountNumber { get; set; }
    public string? IBAN { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    
    // Opening Balance fields
    public decimal? OpeningBalanceAmount { get; set; }
    public DateTime? OpeningBalanceDate { get; set; }

    public ICollection<BankTransaction> Transactions { get; set; } = new List<BankTransaction>();
}
