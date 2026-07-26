using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

public class FinancialAccount : BaseEntity, ITenantOwned
{
    /// <summary>
    /// The tenant that owns this financial account.
    /// CRITICAL: NOT NULL - enforced at database level.
    /// </summary>
    public Guid TenantId { get; set; }
    
    /// <summary>
    /// Account code for identification (e.g., 1000, 1100, 2000)
    /// </summary>
    public string AccountCode { get; set; } = string.Empty;
    
    public string Name { get; set; } = string.Empty;
    public string? AccountNumber { get; set; }
    
    /// <summary>
    /// Type of account for accounting classification
    /// </summary>
    public AccountType AccountType { get; set; } = AccountType.Asset;
    
    /// <summary>
    /// If true, this account cannot be deleted or renamed (e.g. Visa Account)
    /// </summary>
    public bool IsSystem { get; set; }
    
    public decimal Balance { get; set; }
    
    /// <summary>
    /// Level in the hierarchy (1 = top level, 2 = sub-account, etc.)
    /// </summary>
    public int Level { get; set; } = 1;
    
    public Guid? ParentAccountId { get; set; }
    public FinancialAccount? ParentAccount { get; set; }
    public ICollection<FinancialAccount> SubAccounts { get; set; } = new List<FinancialAccount>();
}
