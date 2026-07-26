namespace PointOfSale.Core.Interfaces.Accounting;

/// <summary>
/// Handles treasury operations: transfers between accounts.
/// 
/// All transfers create proper journal entries via FinancialPostingService.
/// Idempotency is enforced via transfer reference.
/// </summary>
public interface ITreasuryTransferService
{
    /// <summary>
    /// Transfers funds between two accounts.
    /// Creates a balanced journal entry: DEBIT to, CREDIT from.
    /// 
    /// Idempotency: Uses transferId to prevent duplicate transfers.
    /// </summary>
    /// <param name="tenantId">Tenant isolation</param>
    /// <param name="fromAccountId">Source account (credited)</param>
    /// <param name="toAccountId">Destination account (debited)</param>
    /// <param name="amount">Transfer amount (must be positive)</param>
    /// <param name="reference">Human-readable reference</param>
    /// <param name="transferId">Optional unique transfer ID for idempotency</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The journal entry ID</returns>
    Task<Guid> TransferAsync(
        Guid tenantId,
        Guid fromAccountId,
        Guid toAccountId,
        decimal amount,
        string reference,
        Guid? transferId = null,
        CancellationToken ct = default);
    
    /// <summary>
    /// Settles a clearing account to a target account.
    /// Used when payment processor remits funds.
    /// 
    /// Creates journal: DEBIT target (bank), CREDIT clearing
    /// </summary>
    Task<Guid> SettleClearingAsync(
        Guid tenantId,
        Guid clearingAccountId,
        Guid targetAccountId,
        decimal amount,
        string reference,
        Guid? settlementId = null,
        CancellationToken ct = default);
    
    /// <summary>
    /// Records a bank deposit from cashbox.
    /// 
    /// Creates journal: DEBIT bank, CREDIT cashbox
    /// </summary>
    Task<Guid> DepositToBankAsync(
        Guid tenantId,
        Guid cashboxAccountId,
        Guid bankAccountId,
        decimal amount,
        string reference,
        Guid? depositId = null,
        CancellationToken ct = default);
}
