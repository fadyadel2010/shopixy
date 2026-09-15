using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities.Accounting;

/// <summary>
/// A node in the Chart of Accounts. Represents an account in the double-entry ledger.
/// 
/// INVARIANTS:
/// - AccountCode is unique per tenant
/// - AccountCode and AccountType are immutable after creation
/// - CurrentBalance always equals the sum of ledger entries
/// - System accounts cannot be deleted or renamed
/// - Parent-child hierarchy must not form cycles
/// </summary>
public class Account : BaseEntity, ITenantOwned
{
    /// <summary>
    /// Tenant isolation. NOT NULL.
    /// </summary>
    public Guid TenantId { get; set; }
    
    #region Identity
    
    /// <summary>
    /// Unique account code within tenant's chart of accounts.
    /// Format: 4-digit grouping (1010, 1110, 4010).
    /// IMMUTABLE after creation.
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string AccountCode { get; private set; } = string.Empty;
    
    /// <summary>
    /// Human-readable name (English).
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Human-readable name (Arabic).
    /// </summary>
    [MaxLength(200)]
    public string? NameAr { get; set; }
    
    /// <summary>
    /// Optional description of account purpose.
    /// </summary>
    [MaxLength(500)]
    public string? Description { get; set; }
    
    #endregion
    
    #region Classification
    
    /// <summary>
    /// Account classification (Asset, Liability, etc.).
    /// IMMUTABLE after creation.
    /// </summary>
    public AccountType AccountType { get; private set; }
    
    /// <summary>
    /// Derived from AccountType. Determines balance calculation direction.
    /// </summary>
    [NotMapped]
    public NormalBalance NormalBalance => AccountType.GetNormalBalance();
    
    #endregion
    
    #region Hierarchy
    
    /// <summary>
    /// Parent account for sub-account hierarchy.
    /// NULL for top-level accounts.
    /// </summary>
    public Guid? ParentAccountId { get; set; }
    public Account? ParentAccount { get; set; }
    
    /// <summary>
    /// Child accounts.
    /// </summary>
    public ICollection<Account> SubAccounts { get; set; } = new List<Account>();
    
    /// <summary>
    /// Hierarchy level (1 = top-level, 2 = sub-account, etc.).
    /// </summary>
    public int Level { get; set; } = 1;
    
    #endregion
    
    #region Balance
    
    /// <summary>
    /// Current balance (denormalized for performance).
    /// CRITICAL: Updated atomically with each ledger entry.
    /// 
    /// For DEBIT normal: CurrentBalance = Sum(Debits) - Sum(Credits)
    /// For CREDIT normal: CurrentBalance = Sum(Credits) - Sum(Debits)
    /// 
    /// Precision: 18,4 to handle currencies with high precision.
    /// </summary>
    [Column(TypeName = "decimal(18,4)")]
    public decimal CurrentBalance { get; private set; }
    
    /// <summary>
    /// Updates balance based on debit/credit and normal balance.
    /// INTERNAL USE ONLY - called by posting engine within transaction.
    /// </summary>
    public void ApplyEntry(decimal debitAmount, decimal creditAmount)
    {
        if (NormalBalance == Enums.NormalBalance.Debit)
        {
            CurrentBalance += debitAmount - creditAmount;
        }
        else
        {
            CurrentBalance += creditAmount - debitAmount;
        }
    }
    
    /// <summary>
    /// Reverses an entry effect. Used in journal reversal.
    /// </summary>
    public void ReverseEntry(decimal debitAmount, decimal creditAmount)
    {
        ApplyEntry(creditAmount, debitAmount); // Swap debit/credit
    }
    
    /// <summary>
    /// Sets the current balance directly. Used only for opening balance setup.
    /// </summary>
    public void SetOpeningBalance(decimal balance)
    {
        CurrentBalance = balance;
    }
    
    #endregion
    
    #region Flags
    
    /// <summary>
    /// If true, account cannot be deleted or renamed.
    /// Used for: Cashbox, Visa Account, system clearing accounts.
    /// </summary>
    public bool IsSystem { get; set; }
    
    /// <summary>
    /// If true, this is a clearing/holding account for money in transit.
    /// Clearing accounts should trend toward zero and require settlement.
    /// </summary>
    public bool IsClearing { get; set; }
    
    /// <summary>
    /// If true, balance cannot go below zero (for debit-normal accounts).
    /// Enforced by posting engine before commit.
    /// Used for: Cashbox, Bank accounts.
    /// </summary>
    public bool PreventNegative { get; set; }

    /// <summary>
    /// Explicitly designates a dedicated funded supplier-card Asset account.
    /// This classification is immutable after account creation.
    /// </summary>
    public bool IsSupplierCardFunding { get; private set; }
    
    /// <summary>
    /// If false, account is disabled and cannot receive new entries.
    /// </summary>
    public bool IsActive { get; set; } = true;
    
    #endregion
    
    #region Clearing Metadata
    
    /// <summary>
    /// For clearing accounts: the external source type.
    /// Examples: "Gateway", "Carrier", "Wallet".
    /// </summary>
    [MaxLength(50)]
    public string? ClearingSourceType { get; set; }
    
    /// <summary>
    /// For clearing accounts: the specific provider.
    /// Examples: "stripe", "bosta", "vodafone".
    /// </summary>
    [MaxLength(50)]
    public string? ClearingSourceCode { get; set; }
    
    /// <summary>
    /// Expected days for settlement. Used for monitoring alerts.
    /// </summary>
    public int? ExpectedSettlementDays { get; set; }
    
    #endregion
    
    #region Audit
    
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
    
    // NOTE: Optimistic concurrency is handled by PostgreSQL's xmin system column.
    // EF Core is configured to use UseXminAsConcurrencyToken() in AccountConfiguration.
    
    #endregion
    
    #region Factory Methods
    
    /// <summary>
    /// Creates a new account. Enforces immutability of key fields.
    /// </summary>
    public static Account Create(
        Guid tenantId,
        string accountCode,
        string name,
        AccountType accountType,
        Guid? parentAccountId = null,
        bool isSystem = false,
        bool isClearing = false,
        bool preventNegative = false)
    {
        if (string.IsNullOrWhiteSpace(accountCode))
            throw new ArgumentException("AccountCode is required", nameof(accountCode));
        
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required", nameof(name));
        
        return new Account
        {
            TenantId = tenantId,
            AccountCode = accountCode.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            AccountType = accountType,
            ParentAccountId = parentAccountId,
            IsSystem = isSystem,
            IsClearing = isClearing,
            PreventNegative = preventNegative,
            Level = parentAccountId.HasValue ? 2 : 1,
            CurrentBalance = 0,
            CreatedAtUtc = DateTime.UtcNow
        };
    }
    
    /// <summary>
    /// Sets the account code. Only for EF Core materialization.
    /// </summary>
    public void SetAccountCode(string code)
    {
        AccountCode = code;
    }
    
    /// <summary>
    /// Sets the account type. Only for EF Core materialization.
    /// </summary>
    public void SetAccountType(AccountType type)
    {
        AccountType = type;
    }

    public void DesignateAsSupplierCardFunding()
    {
        if (AccountType != Enums.AccountType.Asset || IsClearing)
            throw new InvalidOperationException("Supplier card funding accounts must be non-clearing Asset accounts.");

        IsSupplierCardFunding = true;
        PreventNegative = true;
    }
    
    #endregion
}
