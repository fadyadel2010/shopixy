# Phase 1 – Core Ledger Engine

> **Version**: 1.0  
> **Date**: 2026-02-06  
> **Scope**: Accounting Kernel Only (No POS, No E-commerce)  
> **Principle**: Double-Entry, Immutable, Idempotent, Atomic

---

## 1. Accounting Domain Model

### 1.1 AccountType Enum

```csharp
namespace PointOfSale.Core.Enums;

/// <summary>
/// Chart of Accounts classification following GAAP/IFRS standards.
/// Determines normal balance behavior and financial statement placement.
/// </summary>
public enum AccountType
{
    /// <summary>
    /// Resources owned. Normal balance: DEBIT.
    /// Examples: Cash, Bank, Inventory, Receivables, Clearing Accounts.
    /// </summary>
    Asset = 1,
    
    /// <summary>
    /// Obligations owed. Normal balance: CREDIT.
    /// Examples: Payables, Loans, Customer Deposits.
    /// </summary>
    Liability = 2,
    
    /// <summary>
    /// Owner's stake. Normal balance: CREDIT.
    /// Examples: Capital, Retained Earnings.
    /// </summary>
    Equity = 3,
    
    /// <summary>
    /// Income earned. Normal balance: CREDIT.
    /// Examples: Sales Revenue, Service Income.
    /// </summary>
    Revenue = 4,
    
    /// <summary>
    /// Costs incurred. Normal balance: DEBIT.
    /// Examples: Rent, Salaries, Utilities.
    /// </summary>
    Expense = 5,
    
    /// <summary>
    /// Direct product costs. Normal balance: DEBIT.
    /// Examples: Product Cost, Shipping Cost.
    /// </summary>
    CostOfGoodsSold = 6
}
```

### 1.2 NormalBalance Concept

```csharp
namespace PointOfSale.Core.Enums;

/// <summary>
/// The side of the T-account where increases are recorded.
/// CRITICAL: This determines how balance is calculated.
/// </summary>
public enum NormalBalance
{
    /// <summary>
    /// Balance increases with debits, decreases with credits.
    /// Used by: Asset, Expense, COGS, Contra-Liability, Contra-Equity.
    /// </summary>
    Debit = 1,
    
    /// <summary>
    /// Balance increases with credits, decreases with debits.
    /// Used by: Liability, Equity, Revenue, Contra-Asset.
    /// </summary>
    Credit = 2
}

/// <summary>
/// Extension to derive normal balance from account type.
/// </summary>
public static class AccountTypeExtensions
{
    public static NormalBalance GetNormalBalance(this AccountType type)
    {
        return type switch
        {
            AccountType.Asset => NormalBalance.Debit,
            AccountType.Expense => NormalBalance.Debit,
            AccountType.CostOfGoodsSold => NormalBalance.Debit,
            AccountType.Liability => NormalBalance.Credit,
            AccountType.Equity => NormalBalance.Credit,
            AccountType.Revenue => NormalBalance.Credit,
            _ => throw new InvalidOperationException($"Unknown AccountType: {type}")
        };
    }
}
```

### 1.3 Account Entity

```csharp
namespace PointOfSale.Core.Entities.Accounting;

/// <summary>
/// A node in the Chart of Accounts.
/// 
/// INVARIANTS:
/// - AccountCode is unique per tenant
/// - CurrentBalance always equals Sum(Debits) - Sum(Credits) for debit-normal accounts
/// - CurrentBalance always equals Sum(Credits) - Sum(Debits) for credit-normal accounts
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
    /// Immutable after creation.
    /// </summary>
    public string AccountCode { get; private set; } = string.Empty;
    
    /// <summary>
    /// Human-readable name (English).
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Human-readable name (Arabic).
    /// </summary>
    public string? NameAr { get; set; }
    
    /// <summary>
    /// Optional description of account purpose.
    /// </summary>
    public string? Description { get; set; }
    
    #endregion
    
    #region Classification
    
    /// <summary>
    /// Account classification (Asset, Liability, etc.).
    /// Immutable after creation.
    /// </summary>
    public AccountType AccountType { get; private set; }
    
    /// <summary>
    /// Derived from AccountType. Determines balance calculation.
    /// </summary>
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
    /// </summary>
    public decimal CurrentBalance { get; private set; }
    
    /// <summary>
    /// Updates balance based on debit/credit and normal balance.
    /// INTERNAL USE ONLY - called by posting engine.
    /// </summary>
    internal void ApplyEntry(decimal debitAmount, decimal creditAmount)
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
    internal void ReverseEntry(decimal debitAmount, decimal creditAmount)
    {
        ApplyEntry(creditAmount, debitAmount); // Swap debit/credit
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
    /// If false, account is disabled and cannot receive new entries.
    /// </summary>
    public bool IsActive { get; set; } = true;
    
    #endregion
    
    #region Clearing Metadata
    
    /// <summary>
    /// For clearing accounts: the external source type.
    /// Examples: "Gateway", "Carrier", "Wallet".
    /// </summary>
    public string? ClearingSourceType { get; set; }
    
    /// <summary>
    /// For clearing accounts: the specific provider.
    /// Examples: "stripe", "bosta", "vodafone".
    /// </summary>
    public string? ClearingSourceCode { get; set; }
    
    /// <summary>
    /// Expected days for settlement. Used for alerts.
    /// </summary>
    public int? ExpectedSettlementDays { get; set; }
    
    #endregion
    
    #region Audit
    
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
    
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
            throw new ArgumentException("AccountCode required", nameof(accountCode));
        
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name required", nameof(name));
        
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
            CurrentBalance = 0
        };
    }
    
    #endregion
}
```

### 1.4 Account Invariants

| Invariant | Enforcement |
|-----------|-------------|
| `AccountCode` unique per tenant | Database unique constraint |
| `AccountCode` immutable | Private setter, no update method |
| `AccountType` immutable | Private setter |
| Balance equals ledger sum | Transactional update with each entry |
| No cycles in hierarchy | Validation on parent assignment |
| System accounts protected | Check in delete/rename operations |
| Negative balance prevention | Pre-commit validation |

---

## 2. Journal Model

### 2.1 JournalEntry Entity

```csharp
namespace PointOfSale.Core.Entities.Accounting;

/// <summary>
/// An immutable record of a financial transaction.
/// 
/// INVARIANTS:
/// - Once posted, cannot be modified (immutable)
/// - Sum(LedgerEntries.DebitAmount) == Sum(LedgerEntries.CreditAmount)
/// - Must have at least 2 ledger entries
/// - IdempotencyKey is unique per tenant
/// - Status transitions: POSTED -> REVERSED only
/// </summary>
public class JournalEntry : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    
    #region Identity
    
    /// <summary>
    /// Sequential journal number. Format: JE-YYYY-NNNNNN.
    /// Auto-generated, unique per tenant.
    /// </summary>
    public string JournalNumber { get; private set; } = string.Empty;
    
    /// <summary>
    /// Business date of the transaction.
    /// May differ from PostedAtUtc (e.g., backdated entries).
    /// </summary>
    public DateTime JournalDate { get; private set; }
    
    /// <summary>
    /// Exact timestamp when journal was committed to database.
    /// </summary>
    public DateTime PostedAtUtc { get; private set; }
    
    #endregion
    
    #region Source Tracking
    
    /// <summary>
    /// Type of originating document.
    /// Examples: "SalesInvoice", "Order", "Settlement", "Transfer", "Adjustment".
    /// </summary>
    public string SourceType { get; private set; } = string.Empty;
    
    /// <summary>
    /// ID of the originating document.
    /// </summary>
    public Guid SourceId { get; private set; }
    
    /// <summary>
    /// Human-readable reference from source.
    /// Examples: "INV-2026-001234", "ORD-2026-005678".
    /// </summary>
    public string? SourceReference { get; set; }
    
    #endregion
    
    #region Description
    
    /// <summary>
    /// Narrative description of the transaction.
    /// </summary>
    public string Description { get; private set; } = string.Empty;
    
    #endregion
    
    #region Idempotency
    
    /// <summary>
    /// Unique key to prevent duplicate postings.
    /// Format depends on source type.
    /// CRITICAL: Checked before every posting attempt.
    /// </summary>
    public string IdempotencyKey { get; private set; } = string.Empty;
    
    #endregion
    
    #region Status
    
    /// <summary>
    /// Current status. Immutable except for reversal.
    /// </summary>
    public JournalStatus Status { get; private set; } = JournalStatus.Posted;
    
    /// <summary>
    /// If this journal was reversed, the reversing journal's ID.
    /// </summary>
    public Guid? ReversedByJournalId { get; private set; }
    
    /// <summary>
    /// If this journal is a reversal, the original journal's ID.
    /// </summary>
    public Guid? ReversesJournalId { get; private set; }
    
    /// <summary>
    /// Reason for reversal (required when reversing).
    /// </summary>
    public string? ReversalReason { get; private set; }
    
    /// <summary>
    /// Marks this journal as reversed.
    /// </summary>
    internal void MarkReversed(Guid reversedByJournalId, string reason)
    {
        if (Status == JournalStatus.Reversed)
            throw new InvalidOperationException("Journal already reversed");
        
        Status = JournalStatus.Reversed;
        ReversedByJournalId = reversedByJournalId;
        ReversalReason = reason;
    }
    
    #endregion
    
    #region Audit
    
    /// <summary>
    /// User who created/posted this journal.
    /// </summary>
    public Guid? CreatedByUserId { get; set; }
    
    #endregion
    
    #region Line Items
    
    /// <summary>
    /// Individual debit/credit entries. At least 2 required.
    /// </summary>
    public ICollection<LedgerEntry> LedgerEntries { get; private set; } = new List<LedgerEntry>();
    
    /// <summary>
    /// Total debits in this journal.
    /// </summary>
    public decimal TotalDebits => LedgerEntries.Sum(e => e.DebitAmount);
    
    /// <summary>
    /// Total credits in this journal.
    /// </summary>
    public decimal TotalCredits => LedgerEntries.Sum(e => e.CreditAmount);
    
    /// <summary>
    /// True if journal is balanced (debits == credits).
    /// </summary>
    public bool IsBalanced => TotalDebits == TotalCredits;
    
    #endregion
    
    #region Factory
    
    /// <summary>
    /// Creates a new journal entry. Use JournalBuilder for construction.
    /// </summary>
    internal static JournalEntry Create(
        Guid tenantId,
        string journalNumber,
        DateTime journalDate,
        string sourceType,
        Guid sourceId,
        string description,
        string idempotencyKey,
        Guid? userId)
    {
        return new JournalEntry
        {
            TenantId = tenantId,
            JournalNumber = journalNumber,
            JournalDate = journalDate,
            PostedAtUtc = DateTime.UtcNow,
            SourceType = sourceType,
            SourceId = sourceId,
            Description = description,
            IdempotencyKey = idempotencyKey,
            CreatedByUserId = userId,
            Status = JournalStatus.Posted
        };
    }
    
    #endregion
}

public enum JournalStatus
{
    Posted = 1,
    Reversed = 2
}
```

### 2.2 LedgerEntry Entity

```csharp
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
    
    #region Parent
    
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
    public decimal DebitAmount { get; private set; }
    
    /// <summary>
    /// Credit amount. Zero if this is a debit entry.
    /// </summary>
    public decimal CreditAmount { get; private set; }
    
    /// <summary>
    /// Account balance immediately after this entry was applied.
    /// </summary>
    public decimal BalanceAfter { get; private set; }
    
    #endregion
    
    #region Metadata
    
    /// <summary>
    /// Optional line-level description.
    /// </summary>
    public string? LineDescription { get; set; }
    
    /// <summary>
    /// Sequence number within the journal (1, 2, 3...).
    /// </summary>
    public int LineNumber { get; private set; }
    
    #endregion
    
    #region Timestamp
    
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
    
    #endregion
    
    #region Factory
    
    /// <summary>
    /// Creates a debit entry.
    /// </summary>
    internal static LedgerEntry CreateDebit(
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
        
        return new LedgerEntry
        {
            TenantId = tenantId,
            JournalEntryId = journalEntryId,
            AccountId = accountId,
            DebitAmount = amount,
            CreditAmount = 0,
            BalanceAfter = balanceAfter,
            LineNumber = lineNumber,
            LineDescription = description
        };
    }
    
    /// <summary>
    /// Creates a credit entry.
    /// </summary>
    internal static LedgerEntry CreateCredit(
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
        
        return new LedgerEntry
        {
            TenantId = tenantId,
            JournalEntryId = journalEntryId,
            AccountId = accountId,
            DebitAmount = 0,
            CreditAmount = amount,
            BalanceAfter = balanceAfter,
            LineNumber = lineNumber,
            LineDescription = description
        };
    }
    
    #endregion
}
```

### 2.3 SQL Schema

```sql
-- ═══════════════════════════════════════════════════════════════
-- ACCOUNTS TABLE
-- ═══════════════════════════════════════════════════════════════
CREATE TABLE Accounts (
    Id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    TenantId                UUID NOT NULL,
    
    -- Identity (Immutable)
    AccountCode             VARCHAR(20) NOT NULL,
    Name                    VARCHAR(200) NOT NULL,
    NameAr                  VARCHAR(200),
    Description             VARCHAR(500),
    
    -- Classification (Immutable)
    AccountType             SMALLINT NOT NULL,  -- 1-6 per enum
    
    -- Hierarchy
    ParentAccountId         UUID REFERENCES Accounts(Id),
    Level                   SMALLINT NOT NULL DEFAULT 1,
    
    -- Balance
    CurrentBalance          DECIMAL(18,4) NOT NULL DEFAULT 0,
    
    -- Flags
    IsSystem                BOOLEAN NOT NULL DEFAULT FALSE,
    IsClearing              BOOLEAN NOT NULL DEFAULT FALSE,
    PreventNegative         BOOLEAN NOT NULL DEFAULT FALSE,
    IsActive                BOOLEAN NOT NULL DEFAULT TRUE,
    
    -- Clearing Metadata
    ClearingSourceType      VARCHAR(50),
    ClearingSourceCode      VARCHAR(50),
    ExpectedSettlementDays  SMALLINT,
    
    -- Audit
    CreatedAtUtc            TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UpdatedAtUtc            TIMESTAMPTZ,
    
    -- Constraints
    CONSTRAINT uq_account_code UNIQUE (TenantId, AccountCode),
    CONSTRAINT chk_account_type CHECK (AccountType BETWEEN 1 AND 6),
    CONSTRAINT chk_level CHECK (Level >= 1)
);

CREATE INDEX idx_accounts_tenant ON Accounts(TenantId);
CREATE INDEX idx_accounts_parent ON Accounts(ParentAccountId);
CREATE INDEX idx_accounts_clearing ON Accounts(TenantId, IsClearing) WHERE IsClearing = TRUE;

-- ═══════════════════════════════════════════════════════════════
-- JOURNAL ENTRIES TABLE
-- ═══════════════════════════════════════════════════════════════
CREATE TABLE JournalEntries (
    Id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    TenantId                UUID NOT NULL,
    
    -- Identity
    JournalNumber           VARCHAR(50) NOT NULL,
    JournalDate             DATE NOT NULL,
    PostedAtUtc             TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    
    -- Source
    SourceType              VARCHAR(50) NOT NULL,
    SourceId                UUID NOT NULL,
    SourceReference         VARCHAR(100),
    
    -- Description
    Description             VARCHAR(500) NOT NULL,
    
    -- Idempotency (CRITICAL)
    IdempotencyKey          VARCHAR(255) NOT NULL,
    
    -- Status
    Status                  SMALLINT NOT NULL DEFAULT 1,  -- 1=Posted, 2=Reversed
    ReversedByJournalId     UUID REFERENCES JournalEntries(Id),
    ReversesJournalId       UUID REFERENCES JournalEntries(Id),
    ReversalReason          VARCHAR(500),
    
    -- Audit
    CreatedByUserId         UUID,
    
    -- Constraints
    CONSTRAINT uq_idempotency UNIQUE (TenantId, IdempotencyKey),
    CONSTRAINT uq_journal_number UNIQUE (TenantId, JournalNumber),
    CONSTRAINT chk_status CHECK (Status IN (1, 2))
);

CREATE INDEX idx_journals_tenant_date ON JournalEntries(TenantId, JournalDate DESC);
CREATE INDEX idx_journals_source ON JournalEntries(TenantId, SourceType, SourceId);
CREATE INDEX idx_journals_status ON JournalEntries(TenantId, Status);

-- ═══════════════════════════════════════════════════════════════
-- LEDGER ENTRIES TABLE
-- ═══════════════════════════════════════════════════════════════
CREATE TABLE LedgerEntries (
    Id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    TenantId                UUID NOT NULL,
    JournalEntryId          UUID NOT NULL REFERENCES JournalEntries(Id),
    
    -- Account
    AccountId               UUID NOT NULL REFERENCES Accounts(Id),
    
    -- Amounts
    DebitAmount             DECIMAL(18,4) NOT NULL DEFAULT 0,
    CreditAmount            DECIMAL(18,4) NOT NULL DEFAULT 0,
    BalanceAfter            DECIMAL(18,4) NOT NULL,
    
    -- Metadata
    LineDescription         VARCHAR(200),
    LineNumber              SMALLINT NOT NULL,
    
    -- Audit
    CreatedAtUtc            TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    
    -- Constraints
    CONSTRAINT chk_single_side CHECK (
        (DebitAmount > 0 AND CreditAmount = 0) OR
        (DebitAmount = 0 AND CreditAmount > 0)
    ),
    CONSTRAINT chk_positive CHECK (DebitAmount >= 0 AND CreditAmount >= 0)
);

CREATE INDEX idx_ledger_journal ON LedgerEntries(JournalEntryId);
CREATE INDEX idx_ledger_account ON LedgerEntries(AccountId, CreatedAtUtc DESC);
CREATE INDEX idx_ledger_tenant ON LedgerEntries(TenantId);

-- ═══════════════════════════════════════════════════════════════
-- JOURNAL NUMBER SEQUENCE
-- ═══════════════════════════════════════════════════════════════
CREATE TABLE JournalNumberSequence (
    TenantId                UUID PRIMARY KEY,
    CurrentYear             SMALLINT NOT NULL,
    NextNumber              INT NOT NULL DEFAULT 1
);
```

---

## 3. Idempotency Model

### 3.1 Strategy

```
IDEMPOTENCY KEY STRUCTURE
════════════════════════════════════════════════════════════════

Format: {OPERATION}:{TENANT_ID}:{UNIQUE_IDENTIFIERS}

The key MUST be:
• Deterministic: Same input always produces same key
• Unique: No two different operations produce same key
• Immutable: Key components cannot change after creation
```

### 3.2 Key Patterns

| Operation | Pattern | Example |
|-----------|---------|---------|
| POS Sale | `SALE:{TenantId}:{InvoiceId}` | `SALE:abc123:inv-456` |
| Order Payment | `ORDERPAY:{TenantId}:{OrderId}:{PaymentIntentId}` | `ORDERPAY:abc123:ord-789:pi-012` |
| Settlement | `SETTLEMENT:{TenantId}:{Provider}:{ExternalId}` | `SETTLEMENT:abc123:stripe:po_xyz` |
| Transfer | `TRANSFER:{TenantId}:{TransferId}` | `TRANSFER:abc123:trf-345` |
| Adjustment | `ADJUST:{TenantId}:{AdjustmentId}` | `ADJUST:abc123:adj-678` |
| Reversal | `REVERSE:{TenantId}:{OriginalJournalId}` | `REVERSE:abc123:je-901` |

### 3.3 Duplicate Handling

```csharp
public class IdempotencyService
{
    /// <summary>
    /// Checks if operation was already performed.
    /// Returns existing journal if duplicate, null if new.
    /// </summary>
    public async Task<JournalEntry?> CheckDuplicateAsync(
        Guid tenantId,
        string idempotencyKey,
        CancellationToken ct = default)
    {
        return await _db.JournalEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(j =>
                j.TenantId == tenantId &&
                j.IdempotencyKey == idempotencyKey,
                ct);
    }
}

// USAGE IN POSTING ENGINE:
public async Task<JournalEntry> PostJournalAsync(PostingRequest request)
{
    // Step 1: Check idempotency BEFORE starting transaction
    var existing = await _idempotency.CheckDuplicateAsync(
        request.TenantId, 
        request.IdempotencyKey);
    
    if (existing != null)
    {
        _logger.LogWarning(
            "Duplicate posting attempt. Key={Key}, ExistingJournal={JournalId}",
            request.IdempotencyKey,
            existing.Id);
        return existing; // Return existing, don't throw
    }
    
    // Step 2: Proceed with posting
    // ...
}
```

---

## 4. Posting Engine

### 4.1 Interface

```csharp
namespace PointOfSale.Core.Interfaces.Accounting;

public interface IFinancialPostingService
{
    /// <summary>
    /// Posts a balanced journal entry atomically.
    /// Returns existing journal if idempotency key matches.
    /// </summary>
    Task<JournalEntry> PostJournalAsync(
        PostingRequest request,
        CancellationToken ct = default);
    
    /// <summary>
    /// Creates a reversal journal for an existing journal.
    /// Original journal is marked as Reversed.
    /// </summary>
    Task<JournalEntry> ReverseJournalAsync(
        Guid tenantId,
        Guid journalId,
        string reason,
        Guid? userId = null,
        CancellationToken ct = default);
    
    /// <summary>
    /// Validates a posting request without persisting.
    /// Throws if validation fails.
    /// </summary>
    Task ValidateJournalAsync(
        PostingRequest request,
        CancellationToken ct = default);
}
```

### 4.2 Request Model

```csharp
public class PostingRequest
{
    public Guid TenantId { get; set; }
    public DateTime JournalDate { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public Guid SourceId { get; set; }
    public string? SourceReference { get; set; }
    public string Description { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public Guid? UserId { get; set; }
    public List<PostingLine> Lines { get; set; } = new();
}

public class PostingLine
{
    public Guid AccountId { get; set; }
    public decimal DebitAmount { get; set; }
    public decimal CreditAmount { get; set; }
    public string? Description { get; set; }
}
```

### 4.3 Implementation

```csharp
public class FinancialPostingService : IFinancialPostingService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<FinancialPostingService> _logger;

    public async Task<JournalEntry> PostJournalAsync(
        PostingRequest request,
        CancellationToken ct = default)
    {
        // ═══════════════════════════════════════════════════════
        // STEP 1: IDEMPOTENCY CHECK (before transaction)
        // ═══════════════════════════════════════════════════════
        var existing = await _db.JournalEntries
            .FirstOrDefaultAsync(j =>
                j.TenantId == request.TenantId &&
                j.IdempotencyKey == request.IdempotencyKey, ct);
        
        if (existing != null)
        {
            _logger.LogInformation("Idempotent: returning existing journal {Id}", existing.Id);
            return existing;
        }
        
        // ═══════════════════════════════════════════════════════
        // STEP 2: VALIDATION
        // ═══════════════════════════════════════════════════════
        await ValidateJournalAsync(request, ct);
        
        // ═══════════════════════════════════════════════════════
        // STEP 3: BEGIN SERIALIZABLE TRANSACTION
        // ═══════════════════════════════════════════════════════
        await using var transaction = await _db.Database
            .BeginTransactionAsync(IsolationLevel.Serializable, ct);
        
        try
        {
            // ═══════════════════════════════════════════════════
            // STEP 4: LOCK ACCOUNTS & VALIDATE BALANCES
            // ═══════════════════════════════════════════════════
            var accountIds = request.Lines.Select(l => l.AccountId).Distinct().ToList();
            
            // SELECT FOR UPDATE - locks rows until commit
            var accounts = await _db.Accounts
                .Where(a => accountIds.Contains(a.Id))
                .OrderBy(a => a.Id) // Consistent ordering prevents deadlocks
                .ToListAsync(ct);
            
            var accountMap = accounts.ToDictionary(a => a.Id);
            
            // Validate sufficient funds for credits on PreventNegative accounts
            foreach (var line in request.Lines.Where(l => l.CreditAmount > 0))
            {
                var account = accountMap[line.AccountId];
                if (account.PreventNegative && 
                    account.NormalBalance == NormalBalance.Debit)
                {
                    var projectedBalance = account.CurrentBalance - line.CreditAmount;
                    if (projectedBalance < 0)
                    {
                        throw new InsufficientFundsException(
                            account.AccountCode,
                            account.CurrentBalance,
                            line.CreditAmount);
                    }
                }
            }
            
            // ═══════════════════════════════════════════════════
            // STEP 5: GENERATE JOURNAL NUMBER
            // ═══════════════════════════════════════════════════
            var journalNumber = await GenerateJournalNumberAsync(request.TenantId, ct);
            
            // ═══════════════════════════════════════════════════
            // STEP 6: CREATE JOURNAL ENTRY
            // ═══════════════════════════════════════════════════
            var journal = JournalEntry.Create(
                request.TenantId,
                journalNumber,
                request.JournalDate,
                request.SourceType,
                request.SourceId,
                request.Description,
                request.IdempotencyKey,
                request.UserId);
            
            _db.JournalEntries.Add(journal);
            
            // ═══════════════════════════════════════════════════
            // STEP 7: CREATE LEDGER ENTRIES & UPDATE BALANCES
            // ═══════════════════════════════════════════════════
            int lineNumber = 1;
            foreach (var line in request.Lines)
            {
                var account = accountMap[line.AccountId];
                
                // Apply to account balance
                account.ApplyEntry(line.DebitAmount, line.CreditAmount);
                
                // Create ledger entry
                LedgerEntry entry;
                if (line.DebitAmount > 0)
                {
                    entry = LedgerEntry.CreateDebit(
                        request.TenantId,
                        journal.Id,
                        account.Id,
                        line.DebitAmount,
                        account.CurrentBalance,
                        lineNumber++,
                        line.Description);
                }
                else
                {
                    entry = LedgerEntry.CreateCredit(
                        request.TenantId,
                        journal.Id,
                        account.Id,
                        line.CreditAmount,
                        account.CurrentBalance,
                        lineNumber++,
                        line.Description);
                }
                
                _db.LedgerEntries.Add(entry);
            }
            
            // ═══════════════════════════════════════════════════
            // STEP 8: SAVE & COMMIT
            // ═══════════════════════════════════════════════════
            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            
            _logger.LogInformation(
                "Posted journal {JournalNumber} with {LineCount} entries",
                journalNumber,
                request.Lines.Count);
            
            return journal;
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }
    
    public async Task<JournalEntry> ReverseJournalAsync(
        Guid tenantId,
        Guid journalId,
        string reason,
        Guid? userId = null,
        CancellationToken ct = default)
    {
        // Build reversal request with swapped debits/credits
        var original = await _db.JournalEntries
            .Include(j => j.LedgerEntries)
            .FirstOrDefaultAsync(j => j.Id == journalId && j.TenantId == tenantId, ct)
            ?? throw new JournalNotFoundException(journalId);
        
        if (original.Status == JournalStatus.Reversed)
            throw new JournalAlreadyReversedException(journalId);
        
        var reversalRequest = new PostingRequest
        {
            TenantId = tenantId,
            JournalDate = DateTime.UtcNow.Date,
            SourceType = "Reversal",
            SourceId = journalId,
            SourceReference = original.JournalNumber,
            Description = $"Reversal of {original.JournalNumber}: {reason}",
            IdempotencyKey = $"REVERSE:{tenantId}:{journalId}",
            UserId = userId,
            Lines = original.LedgerEntries.Select(e => new PostingLine
            {
                AccountId = e.AccountId,
                DebitAmount = e.CreditAmount, // Swap!
                CreditAmount = e.DebitAmount, // Swap!
                Description = $"Reversal: {e.LineDescription}"
            }).ToList()
        };
        
        var reversalJournal = await PostJournalAsync(reversalRequest, ct);
        
        // Mark original as reversed
        original.MarkReversed(reversalJournal.Id, reason);
        await _db.SaveChangesAsync(ct);
        
        return reversalJournal;
    }
    
    public async Task ValidateJournalAsync(PostingRequest request, CancellationToken ct)
    {
        // 1. Must have lines
        if (request.Lines.Count < 2)
            throw new InvalidJournalException("Minimum 2 lines required");
        
        // 2. Each line must have exactly one side
        foreach (var line in request.Lines)
        {
            bool hasDebit = line.DebitAmount > 0;
            bool hasCredit = line.CreditAmount > 0;
            
            if (hasDebit == hasCredit)
                throw new InvalidJournalException(
                    "Each line must have exactly one debit or credit");
            
            if (line.DebitAmount < 0 || line.CreditAmount < 0)
                throw new InvalidJournalException("Amounts cannot be negative");
        }
        
        // 3. Must balance
        var totalDebits = request.Lines.Sum(l => l.DebitAmount);
        var totalCredits = request.Lines.Sum(l => l.CreditAmount);
        
        if (totalDebits != totalCredits)
            throw new UnbalancedJournalException(totalDebits, totalCredits);
        
        // 4. Accounts must exist and be active
        var accountIds = request.Lines.Select(l => l.AccountId).Distinct().ToList();
        var accounts = await _db.Accounts
            .Where(a => accountIds.Contains(a.Id) && a.TenantId == request.TenantId)
            .ToListAsync(ct);
        
        if (accounts.Count != accountIds.Count)
            throw new AccountNotFoundException("One or more accounts not found");
        
        var inactiveAccounts = accounts.Where(a => !a.IsActive).ToList();
        if (inactiveAccounts.Any())
            throw new InactiveAccountException(inactiveAccounts.Select(a => a.AccountCode));
    }
}
```

---

## 5. Balance Update Strategy

### 5.1 Approach: Serializable Transaction + Row Locking

```
STRATEGY: PESSIMISTIC LOCKING WITH SERIALIZABLE ISOLATION
══════════════════════════════════════════════════════════

1. Start SERIALIZABLE transaction
2. SELECT accounts WITH "FOR UPDATE" lock (EF Core + raw SQL)
3. Validate projected balances
4. Update Account.CurrentBalance in memory
5. Insert ledger entries with BalanceAfter
6. SaveChanges (writes to DB)
7. Commit (releases locks)

WHY SERIALIZABLE?
• Prevents phantom reads
• Prevents lost updates
• Guarantees consistency

WHY ROW LOCKING?
• Only locks affected accounts
• Other tenants unaffected
• Minimal contention
```

### 5.2 Deadlock Prevention

```csharp
// ALWAYS acquire locks in consistent order (by Account.Id)
var accounts = await _db.Accounts
    .Where(a => accountIds.Contains(a.Id))
    .OrderBy(a => a.Id)  // <-- CRITICAL: Consistent ordering
    .ToListAsync(ct);
```

### 5.3 Race Condition Analysis

```
SCENARIO: Two concurrent sales to same cashbox
═══════════════════════════════════════════════

Thread A: Sale $100            Thread B: Sale $50
────────────────────────────   ────────────────────────────
1. BEGIN SERIALIZABLE          1. BEGIN SERIALIZABLE
2. SELECT Cashbox FOR UPDATE   2. SELECT Cashbox FOR UPDATE
   (acquires lock)                (BLOCKS - waiting for A)
3. CurrentBalance = 1000
4. NewBalance = 1100
5. INSERT LedgerEntry
6. UPDATE Cashbox SET Balance
7. COMMIT (releases lock)
                               3. (unblocks)
                               4. CurrentBalance = 1100 (sees A's commit)
                               5. NewBalance = 1150
                               6. INSERT LedgerEntry
                               7. UPDATE Cashbox SET Balance
                               8. COMMIT

RESULT: Final balance = 1150 ✓ Correct
```

---

## 6. Double-Entry Validation

### 6.1 Algorithm

```csharp
public static class DoubleEntryValidator
{
    /// <summary>
    /// Validates a set of posting lines follows double-entry rules.
    /// </summary>
    public static ValidationResult Validate(IEnumerable<PostingLine> lines)
    {
        var errors = new List<string>();
        var lineList = lines.ToList();
        
        // Rule 1: Minimum 2 lines
        if (lineList.Count < 2)
        {
            errors.Add("Journal must have at least 2 lines");
        }
        
        decimal totalDebits = 0;
        decimal totalCredits = 0;
        
        for (int i = 0; i < lineList.Count; i++)
        {
            var line = lineList[i];
            
            // Rule 2: Exactly one side non-zero
            bool hasDebit = line.DebitAmount > 0;
            bool hasCredit = line.CreditAmount > 0;
            
            if (!hasDebit && !hasCredit)
            {
                errors.Add($"Line {i + 1}: Must have debit or credit > 0");
            }
            else if (hasDebit && hasCredit)
            {
                errors.Add($"Line {i + 1}: Cannot have both debit and credit");
            }
            
            // Rule 3: No negative amounts
            if (line.DebitAmount < 0)
            {
                errors.Add($"Line {i + 1}: Debit cannot be negative");
            }
            if (line.CreditAmount < 0)
            {
                errors.Add($"Line {i + 1}: Credit cannot be negative");
            }
            
            totalDebits += line.DebitAmount;
            totalCredits += line.CreditAmount;
        }
        
        // Rule 4: Debits must equal Credits
        if (totalDebits != totalCredits)
        {
            errors.Add($"Journal unbalanced: Debits={totalDebits}, Credits={totalCredits}");
        }
        
        return new ValidationResult(errors.Count == 0, errors);
    }
}
```

---

## 7. Safety Guarantees

| Threat | Prevention Mechanism |
|--------|---------------------|
| **Partial Writes** | Serializable transaction - all or nothing |
| **Duplicate Postings** | Idempotency key unique constraint |
| **Negative Balances** | PreventNegative flag + pre-commit validation |
| **Phantom Money** | Double-entry validation (debits = credits) |
| **Lost Updates** | Pessimistic locking (SELECT FOR UPDATE) |
| **Invalid State** | Immutable journal entries (no UPDATE/DELETE) |
| **Audit Gap** | BalanceAfter on every ledger entry |

---

## 8. Failure Modes

| Failure | Outcome | Recovery |
|---------|---------|----------|
| **DB crash mid-transaction** | Transaction rolled back automatically | None needed - atomic |
| **Duplicate API call** | Second call returns existing journal | Idempotent - safe |
| **Network timeout (client)** | Transaction may have committed | Client retries, idempotency returns existing |
| **Deadlock** | One transaction aborted | Retry with backoff |
| **Validation failure** | Transaction not started | Fix input, retry |
| **Insufficient funds** | Exception thrown, no commit | Handle in caller |

---

## 9. Migration Strategy

### 9.1 Parallel Running

```
PHASE A: Build & Deploy (No Data)
─────────────────────────────────
1. Create new tables (Accounts, JournalEntries, LedgerEntries)
2. Deploy posting service (inactive)
3. Create chart of accounts (system accounts)

PHASE B: Dual-Write
────────────────────
1. Existing operations continue unchanged
2. New engine posts journals IN PARALLEL
3. Compare balances daily
4. Fix discrepancies

PHASE C: Cutover
────────────────
1. Stop old posting logic
2. New engine becomes primary
3. Old tables become read-only (historical)

PHASE D: Cleanup
────────────────
1. Migrate historical data (optional)
2. Drop old tables
```

### 9.2 Account Mapping

| Existing Entity | New Account |
|-----------------|-------------|
| `Cashbox` | Account 1010-1019 |
| `FinancialAccount` (Visa) | Account 1020 |
| `Bank` | Account 1030-1039 |
| `VisaTransaction` | LedgerEntries for 1020/1110 |
| `CashboxTransaction` | LedgerEntries for 1010 |
| `SupplierTransaction` | LedgerEntries for 2010 |
| `CustomerTransaction` | LedgerEntries for 1210 |

---

## 10. Definition of Done

### Core Ledger Engine Checklist

- [ ] **Entities Created**
  - [ ] `Account` entity with all fields
  - [ ] `JournalEntry` entity with immutability
  - [ ] `LedgerEntry` entity with constraints
  - [ ] Enums: `AccountType`, `NormalBalance`, `JournalStatus`

- [ ] **Database Schema**
  - [ ] Tables created with constraints
  - [ ] Indexes optimized for queries
  - [ ] Unique constraint on idempotency key
  - [ ] Check constraint on single-side amounts

- [ ] **Posting Service**
  - [ ] `IFinancialPostingService` interface
  - [ ] `PostJournalAsync` implementation
  - [ ] `ReverseJournalAsync` implementation
  - [ ] `ValidateJournalAsync` implementation

- [ ] **Safety Mechanisms**
  - [ ] Serializable transaction isolation
  - [ ] Idempotency check before posting
  - [ ] PreventNegative validation
  - [ ] Double-entry balance validation

- [ ] **Unit Tests**
  - [ ] Balanced journal posts successfully
  - [ ] Unbalanced journal throws
  - [ ] Duplicate idempotency key returns existing
  - [ ] PreventNegative blocks overdraw
  - [ ] Reversal creates mirror journal

- [ ] **Integration Tests**
  - [ ] Concurrent postings don't corrupt balances
  - [ ] Deadlock is handled gracefully
  - [ ] Transaction rollback on failure

---

*Phase 1 Complete. Core Ledger Engine Ready for Integration.*
