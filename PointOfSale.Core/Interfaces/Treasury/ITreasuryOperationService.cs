using PointOfSale.Core.Entities.Accounting;

namespace PointOfSale.Core.Interfaces.Treasury;

/// <summary>
/// PHASE TOJ: Treasury Operations Journaling Service
/// 
/// This service provides ledger-based treasury operations.
/// ALL money mutations MUST go through this service.
/// 
/// NO DIRECT DATABASE WRITES ALLOWED.
/// Every operation creates a balanced journal entry.
/// 
/// Posting Rules:
/// - Deposit:  DEBIT TargetAccount, CREDIT SourceAccount
/// - Withdraw: DEBIT TargetAccount, CREDIT SourceAccount
/// - Transfer: DEBIT TargetAccount, CREDIT SourceAccount
/// - Expense:  DEBIT ExpenseAccount, CREDIT PaidFromAccount
/// </summary>
public interface ITreasuryOperationService
{
    /// <summary>
    /// Deposit money into an account.
    /// Example: Cash deposit to bank.
    /// 
    /// Journal:
    ///   DEBIT  Target (Bank 1030)
    ///   CREDIT Source (Cash 1010)
    /// </summary>
    /// <param name="tenantId">Tenant ID</param>
    /// <param name="sourceAccountCode">Account to take money from (e.g., "1010" Cashbox)</param>
    /// <param name="targetAccountCode">Account to deposit into (e.g., "1030" Bank)</param>
    /// <param name="amount">Amount to deposit</param>
    /// <param name="description">Description for the journal</param>
    /// <param name="reference">Optional reference number</param>
    /// <param name="operationId">Unique operation ID for idempotency</param>
    /// <param name="userId">User performing the action</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The created journal entry</returns>
    Task<JournalEntry> DepositAsync(
        Guid tenantId,
        string sourceAccountCode,
        string targetAccountCode,
        decimal amount,
        string description,
        string? reference = null,
        Guid? operationId = null,
        Guid? userId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Withdraw money from an account.
    /// Example: Cash withdrawal from bank.
    /// 
    /// Journal:
    ///   DEBIT  Target (Cash 1010)
    ///   CREDIT Source (Bank 1030)
    /// </summary>
    Task<JournalEntry> WithdrawAsync(
        Guid tenantId,
        string sourceAccountCode,
        string targetAccountCode,
        decimal amount,
        string description,
        string? reference = null,
        Guid? operationId = null,
        Guid? userId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Transfer money between any two accounts.
    /// 
    /// Journal:
    ///   DEBIT  ToAccount
    ///   CREDIT FromAccount
    /// </summary>
    Task<JournalEntry> TransferAsync(
        Guid tenantId,
        string fromAccountCode,
        string toAccountCode,
        decimal amount,
        string description,
        string? reference = null,
        Guid? operationId = null,
        Guid? userId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Record an expense paid from a treasury account.
    /// 
    /// Journal:
    ///   DEBIT  Expense account (6xxx)
    ///   CREDIT Paid-from account (e.g., 1010 Cash)
    /// </summary>
    /// <param name="tenantId">Tenant ID</param>
    /// <param name="paidFromAccountCode">Account that paid (e.g., "1010" Cashbox)</param>
    /// <param name="expenseAccountCode">Expense category account (e.g., "6100" General Expenses)</param>
    /// <param name="amount">Expense amount</param>
    /// <param name="description">Expense description</param>
    /// <param name="reference">Optional reference (receipt number)</param>
    /// <param name="operationId">Unique operation ID for idempotency</param>
    /// <param name="userId">User performing the action</param>
    /// <param name="ct">Cancellation token</param>
    Task<JournalEntry> RecordExpenseAsync(
        Guid tenantId,
        string paidFromAccountCode,
        string expenseAccountCode,
        decimal amount,
        string description,
        string? reference = null,
        Guid? operationId = null,
        Guid? userId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Cash in/receive money into a treasury account.
    /// Example: Receive cash from external source.
    /// 
    /// Journal:
    ///   DEBIT  Treasury account (e.g., 1010 Cash)
    ///   CREDIT Income/Other account
    /// </summary>
    Task<JournalEntry> CashInAsync(
        Guid tenantId,
        string targetAccountCode,
        string creditAccountCode,
        decimal amount,
        string description,
        string? reference = null,
        Guid? operationId = null,
        Guid? userId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Cash out/pay money from a treasury account.
    /// Example: Pay cash for non-expense items.
    /// 
    /// Journal:
    ///   DEBIT  Target account
    ///   CREDIT Treasury account (e.g., 1010 Cash)
    /// </summary>
    Task<JournalEntry> CashOutAsync(
        Guid tenantId,
        string sourceAccountCode,
        string debitAccountCode,
        decimal amount,
        string description,
        string? reference = null,
        Guid? operationId = null,
        Guid? userId = null,
        CancellationToken ct = default);
}
