using System.ComponentModel.DataAnnotations;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities.Accounting;

/// <summary>
/// Treasury account tracking for cashboxes, banks, and clearing accounts.
/// 
/// This entity provides a lightweight view layer on top of the underlying
/// ledger accounts, allowing multiple cashboxes/banks per tenant.
/// 
/// The actual balance is stored in the linked Account.
/// </summary>
public class TreasuryAccount : BaseEntity, ITenantOwned
{
    /// <summary>
    /// Tenant isolation.
    /// </summary>
    public Guid TenantId { get; set; }
    
    /// <summary>
    /// The underlying ledger account that holds the balance.
    /// </summary>
    public Guid AccountId { get; set; }
    public Account? Account { get; set; }
    
    /// <summary>
    /// Type of treasury account.
    /// </summary>
    public TreasuryAccountType Type { get; set; }
    
    /// <summary>
    /// Display name for this treasury account.
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Display name (Arabic).
    /// </summary>
    [MaxLength(100)]
    public string? NameAr { get; set; }
    
    /// <summary>
    /// Optional reference code (e.g., branch code, bank account number).
    /// </summary>
    [MaxLength(50)]
    public string? ReferenceCode { get; set; }
    
    /// <summary>
    /// Optional location/branch identifier.
    /// </summary>
    [MaxLength(100)]
    public string? Location { get; set; }
    
    /// <summary>
    /// If true, this is the default account of its type.
    /// Only one per type per tenant.
    /// </summary>
    public bool IsDefault { get; set; }
    
    /// <summary>
    /// If false, treasury account is archived.
    /// </summary>
    public bool IsActive { get; set; } = true;
    
    /// <summary>
    /// Display order in UI.
    /// </summary>
    public int DisplayOrder { get; set; }
    
    /// <summary>
    /// Gets the current balance from the underlying account.
    /// </summary>
    public decimal CurrentBalance => Account?.CurrentBalance ?? 0;
    
    #region Factory Methods
    
    /// <summary>
    /// Creates a cashbox treasury account.
    /// </summary>
    public static TreasuryAccount CreateCashbox(
        Guid tenantId,
        Guid accountId,
        string name,
        string? location = null,
        bool isDefault = false)
    {
        return new TreasuryAccount
        {
            TenantId = tenantId,
            AccountId = accountId,
            Type = TreasuryAccountType.Cashbox,
            Name = name,
            Location = location,
            IsDefault = isDefault,
            IsActive = true
        };
    }
    
    /// <summary>
    /// Creates a bank treasury account.
    /// </summary>
    public static TreasuryAccount CreateBank(
        Guid tenantId,
        Guid accountId,
        string name,
        string? referenceCode = null,
        bool isDefault = false)
    {
        return new TreasuryAccount
        {
            TenantId = tenantId,
            AccountId = accountId,
            Type = TreasuryAccountType.Bank,
            Name = name,
            ReferenceCode = referenceCode,
            IsDefault = isDefault,
            IsActive = true
        };
    }
    
    /// <summary>
    /// Creates a clearing treasury account.
    /// </summary>
    public static TreasuryAccount CreateClearing(
        Guid tenantId,
        Guid accountId,
        string name)
    {
        return new TreasuryAccount
        {
            TenantId = tenantId,
            AccountId = accountId,
            Type = TreasuryAccountType.Clearing,
            Name = name,
            IsDefault = false,
            IsActive = true
        };
    }
    
    #endregion
}

/// <summary>
/// Type of treasury account.
/// </summary>
public enum TreasuryAccountType
{
    /// <summary>
    /// Physical cash drawer.
    /// </summary>
    Cashbox = 1,
    
    /// <summary>
    /// Bank account.
    /// </summary>
    Bank = 2,
    
    /// <summary>
    /// Clearing/transit account for pending settlements.
    /// </summary>
    Clearing = 3
}
