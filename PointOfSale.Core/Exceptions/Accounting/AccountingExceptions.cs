using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Exceptions.Accounting;

/// <summary>
/// Base exception for all accounting-related errors.
/// </summary>
public abstract class AccountingException : Exception
{
    protected AccountingException(string message) : base(message) { }
    protected AccountingException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Thrown when journal debits do not equal credits.
/// </summary>
public class UnbalancedJournalException : AccountingException
{
    public decimal TotalDebits { get; }
    public decimal TotalCredits { get; }
    public decimal Difference => TotalDebits - TotalCredits;
    
    public UnbalancedJournalException(decimal totalDebits, decimal totalCredits)
        : base($"Journal is unbalanced. Debits={totalDebits:N4}, Credits={totalCredits:N4}, Difference={totalDebits - totalCredits:N4}")
    {
        TotalDebits = totalDebits;
        TotalCredits = totalCredits;
    }
}

/// <summary>
/// Thrown when journal has insufficient lines or invalid line structure.
/// </summary>
public class InvalidJournalException : AccountingException
{
    public InvalidJournalException(string message) : base(message) { }
}

/// <summary>
/// Thrown when an account cannot be found.
/// </summary>
public class AccountNotFoundException : AccountingException
{
    public Guid? AccountId { get; }
    public string? AccountCode { get; }
    
    public AccountNotFoundException(Guid accountId)
        : base($"Account not found: {accountId}")
    {
        AccountId = accountId;
    }
    
    public AccountNotFoundException(string accountCode)
        : base($"Account not found: {accountCode}")
    {
        AccountCode = accountCode;
    }
    
    public AccountNotFoundException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Thrown when an operation is attempted on an inactive account.
/// </summary>
public class InactiveAccountException : AccountingException
{
    public IEnumerable<string> AccountCodes { get; }
    
    public InactiveAccountException(IEnumerable<string> accountCodes)
        : base($"Cannot post to inactive accounts: {string.Join(", ", accountCodes)}")
    {
        AccountCodes = accountCodes;
    }
    
    public InactiveAccountException(string accountCode)
        : base($"Cannot post to inactive account: {accountCode}")
    {
        AccountCodes = new[] { accountCode };
    }
}

/// <summary>
/// Thrown when a posting would cause negative balance on a PreventNegative account.
/// </summary>
public class InsufficientFundsException : AccountingException
{
    public string AccountCode { get; }
    public decimal CurrentBalance { get; }
    public decimal RequestedAmount { get; }
    public decimal ShortfallAmount => RequestedAmount - CurrentBalance;
    
    public InsufficientFundsException(string accountCode, decimal currentBalance, decimal requestedAmount)
        : base($"Insufficient funds in account {accountCode}. Current={currentBalance:N4}, Requested={requestedAmount:N4}, Shortfall={requestedAmount - currentBalance:N4}")
    {
        AccountCode = accountCode;
        CurrentBalance = currentBalance;
        RequestedAmount = requestedAmount;
    }
}

/// <summary>
/// Thrown when a journal cannot be found.
/// </summary>
public class JournalNotFoundException : AccountingException
{
    public Guid JournalId { get; }
    
    public JournalNotFoundException(Guid journalId)
        : base($"Journal not found: {journalId}")
    {
        JournalId = journalId;
    }
}

/// <summary>
/// Thrown when attempting to reverse an already-reversed journal.
/// </summary>
public class JournalAlreadyReversedException : AccountingException
{
    public Guid JournalId { get; }
    
    public JournalAlreadyReversedException(Guid journalId)
        : base($"Journal {journalId} has already been reversed")
    {
        JournalId = journalId;
    }
}

/// <summary>
/// Thrown when attempting to modify an immutable journal or ledger entry.
/// </summary>
public class JournalImmutableException : AccountingException
{
    public JournalImmutableException(string operation)
        : base($"Journals are immutable. Operation not allowed: {operation}")
    {
    }
}

/// <summary>
/// Thrown when a concurrent operation caused a conflict.
/// </summary>
public class ConcurrencyConflictException : AccountingException
{
    public ConcurrencyConflictException(string message) : base(message) { }
    public ConcurrencyConflictException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Thrown when treasury operation validation fails (e.g., invalid amount, invalid account combination).
/// </summary>
public class TreasuryValidationException : AccountingException
{
    public TreasuryValidationException(string message) : base(message) { }
    public TreasuryValidationException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Thrown when no active cashbox ledger account could be resolved for a tenant.
/// </summary>
public class NoCashboxAccountException : AccountingException
{
    public Guid TenantId { get; }
    
    public NoCashboxAccountException(Guid tenantId)
        : base($"No active cashbox ledger account could be resolved for tenant {tenantId}.")
    {
        TenantId = tenantId;
    }
}

/// <summary>
/// Thrown when an opening balance is attempted more than once for the same party.
/// </summary>
public class DuplicateOpeningBalanceException : AccountingException
{
    public PartyType PartyType { get; }
    public Guid PartyId { get; }

    public DuplicateOpeningBalanceException(PartyType partyType, Guid partyId)
        : base($"Opening balance already exists for {partyType} {partyId}.")
    {
        PartyType = partyType;
        PartyId = partyId;
    }
}
