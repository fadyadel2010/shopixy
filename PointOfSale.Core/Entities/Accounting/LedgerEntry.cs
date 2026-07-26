using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities.Accounting;

/// <summary>
/// A single debit or credit line within a journal.
/// 
/// INVARIANTS:
/// - Exactly one of DebitAmount/CreditAmount is > 0, the other is 0
/// - BalanceAfter is the account balance immediately after this entry
/// - LineNumber is sequential within the journal
/// - Immutable after creation
/// </summary>
public class LedgerEntry : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    
    #region Parent Journal
    
    /// <summary>
    /// Parent journal entry.
    /// </summary>
    public Guid JournalEntryId { get; private set; }
    public JournalEntry JournalEntry { get; private set; } = null!;
    
    #endregion
    
    #region Account
    
    /// <summary>
    /// The account being debited or credited.
    /// </summary>
    public Guid AccountId { get; private set; }
    public Account Account { get; private set; } = null!;
    
    #endregion
    
    #region Amounts
    
    /// <summary>
    /// Debit amount. Zero if this is a credit entry.
    /// </summary>
    [Column(TypeName = "decimal(18,4)")]
    public decimal DebitAmount { get; private set; }
    
    /// <summary>
    /// Credit amount. Zero if this is a debit entry.
    /// </summary>
    [Column(TypeName = "decimal(18,4)")]
    public decimal CreditAmount { get; private set; }
    
    /// <summary>
    /// Account balance immediately after this entry was applied.
    /// Provides audit trail and allows point-in-time balance queries.
    /// </summary>
    [Column(TypeName = "decimal(18,4)")]
    public decimal BalanceAfter { get; private set; }
    
    #endregion
    
    #region Metadata
    
    /// <summary>
    /// Optional line-level description.
    /// </summary>
    [MaxLength(200)]
    public string? LineDescription { get; set; }
    
    /// <summary>
    /// Sequence number within the journal (1, 2, 3...).
    /// </summary>
    public int LineNumber { get; private set; }
    
    #endregion
    
    #region Timestamp
    
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
    
    #endregion
    
    #region Computed Properties
    
    /// <summary>
    /// True if this is a debit entry.
    /// </summary>
    [NotMapped]
    public bool IsDebit => DebitAmount > 0;
    
    /// <summary>
    /// True if this is a credit entry.
    /// </summary>
    [NotMapped]
    public bool IsCredit => CreditAmount > 0;
    
    /// <summary>
    /// The amount (always positive).
    /// </summary>
    [NotMapped]
    public decimal Amount => DebitAmount > 0 ? DebitAmount : CreditAmount;
    
    #endregion
    
    #region Factory Methods
    
    /// <summary>
    /// Creates a debit entry.
    /// </summary>
    public static LedgerEntry CreateDebit(
        Guid tenantId,
        Guid journalEntryId,
        Guid accountId,
        decimal amount,
        decimal balanceAfter,
        int lineNumber,
        string? description = null)
    {
        if (amount <= 0)
            throw new ArgumentException("Amount must be positive", nameof(amount));
        
        if (lineNumber <= 0)
            throw new ArgumentException("LineNumber must be positive", nameof(lineNumber));
        
        return new LedgerEntry
        {
            TenantId = tenantId,
            JournalEntryId = journalEntryId,
            AccountId = accountId,
            DebitAmount = amount,
            CreditAmount = 0,
            BalanceAfter = balanceAfter,
            LineNumber = lineNumber,
            LineDescription = description,
            CreatedAtUtc = DateTime.UtcNow
        };
    }
    
    /// <summary>
    /// Creates a credit entry.
    /// </summary>
    public static LedgerEntry CreateCredit(
        Guid tenantId,
        Guid journalEntryId,
        Guid accountId,
        decimal amount,
        decimal balanceAfter,
        int lineNumber,
        string? description = null)
    {
        if (amount <= 0)
            throw new ArgumentException("Amount must be positive", nameof(amount));
        
        if (lineNumber <= 0)
            throw new ArgumentException("LineNumber must be positive", nameof(lineNumber));
        
        return new LedgerEntry
        {
            TenantId = tenantId,
            JournalEntryId = journalEntryId,
            AccountId = accountId,
            DebitAmount = 0,
            CreditAmount = amount,
            BalanceAfter = balanceAfter,
            LineNumber = lineNumber,
            LineDescription = description,
            CreatedAtUtc = DateTime.UtcNow
        };
    }
    
    #endregion
    
    #region EF Core Setters
    
    public void SetJournalEntryId(Guid id) => JournalEntryId = id;
    public void SetAccountId(Guid id) => AccountId = id;
    public void SetDebitAmount(decimal amount) => DebitAmount = amount;
    public void SetCreditAmount(decimal amount) => CreditAmount = amount;
    public void SetBalanceAfter(decimal balance) => BalanceAfter = balance;
    public void SetLineNumber(int number) => LineNumber = number;
    public void SetCreatedAtUtc(DateTime timestamp) => CreatedAtUtc = timestamp;
    
    #endregion
}
