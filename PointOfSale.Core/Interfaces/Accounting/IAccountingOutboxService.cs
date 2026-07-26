using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Interfaces.Accounting;

/// <summary>
/// Result of an outbox enqueue operation.
/// Provides visibility into whether a message was created or skipped.
/// </summary>
public record OutboxEnqueueResult(
    bool WasCreated,
    bool AlreadyExists,
    Guid? MessageId,
    Guid? ExistingMessageId,
    string? Status,
    string IdempotencyKey)
{
    public static OutboxEnqueueResult Created(Guid messageId, string idempotencyKey) =>
        new(WasCreated: true, AlreadyExists: false, MessageId: messageId, 
            ExistingMessageId: null, Status: "CREATED", IdempotencyKey: idempotencyKey);
    
    public static OutboxEnqueueResult Exists(Guid existingMessageId, string status, string idempotencyKey) =>
        new(WasCreated: false, AlreadyExists: true, MessageId: null, 
            ExistingMessageId: existingMessageId, Status: status, IdempotencyKey: idempotencyKey);
}

/// <summary>
/// Service for managing the accounting outbox.
/// 
/// PHASE 3.5: Transactional outbox pattern for guaranteed delivery.
/// </summary>
public interface IAccountingOutboxService
{
    /// <summary>
    /// Enqueues a POS sale for accounting processing.
    /// Call this WITHIN the business transaction.
    /// </summary>
    Task EnqueuePosSaleAsync(
        Guid tenantId,
        Guid invoiceId,
        int? mutationVersion = null,
        CancellationToken ct = default);

    /// <summary>
    /// Enqueues a sale-update accounting mutation.
    /// The processor reverses the currently posted sale journals first,
    /// then posts the updated sale once using the supplied operation key.
    /// </summary>
    /// <remarks>
    /// For <see cref="EnqueueSaleReversalAsync(Guid, Guid, string, int, CancellationToken)"/>, callers must reuse the same
    /// <paramref name="operationKey"/> for retry attempts of the same business update because it is used for downstream
    /// journal idempotency; changing it causes the reversal/repost flow to create a new journal set instead of deduplicating.
    /// </remarks>
    Task EnqueueSaleReversalAsync(
        Guid tenantId,
        Guid invoiceId,
        string operationKey,
        int mutationVersion,
        CancellationToken ct = default);
    
    /// <summary>
    /// Enqueues a POS purchase for accounting processing.
    /// </summary>
    Task EnqueuePosPurchaseAsync(
        Guid tenantId,
        Guid invoiceId,
        int mutationVersion,
        decimal cashAmount = 0m,
        decimal cardAmount = 0m,
        CancellationToken ct = default);

    /// <summary>
    /// Enqueues a purchase mutation that reverses any currently posted purchase journals
    /// and reposts the current purchase state using a distinct operation key.
    /// </summary>
    Task EnqueuePurchaseReversalAsync(
        Guid tenantId,
        Guid invoiceId,
        string operationKey,
        int mutationVersion,
        decimal cashAmount = 0m,
        decimal cardAmount = 0m,
        CancellationToken ct = default);
    
    /// <summary>
    /// Enqueues a supplier payment for accounting processing.
    /// </summary>
    Task EnqueueSupplierPaymentAsync(Guid tenantId, Guid paymentId, CancellationToken ct = default);

    /// <summary>
    /// Enqueues a supplier opening balance ledger entry for accounting processing.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="openingBalanceId">The AccountLedger.Id of the supplier opening balance entry (not the supplier's Id).</param>
    /// <param name="ct">Cancellation token.</param>
    Task EnqueueSupplierOpeningBalanceAsync(Guid tenantId, Guid openingBalanceId, CancellationToken ct = default);

    /// <summary>
    /// Enqueues a customer opening balance ledger entry for accounting processing.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="openingBalanceId">The AccountLedger.Id of the customer opening balance entry (not the customer's Id).</param>
    /// <param name="ct">Cancellation token.</param>
    Task EnqueueCustomerOpeningBalanceAsync(Guid tenantId, Guid openingBalanceId, CancellationToken ct = default);

    /// <summary>
    /// Enqueues a manual party ledger adjustment for accounting processing.
    /// </summary>
    Task EnqueuePartyLedgerAdjustmentAsync(Guid tenantId, Guid adjustmentId, CancellationToken ct = default);
    
    /// <summary>
    /// Enqueues a POS refund for accounting processing.
    /// </summary>
    Task EnqueuePosRefundAsync(
        Guid tenantId,
        Guid returnId,
        Guid originalInvoiceId,
        RefundMethod refundMethod,
        decimal cashAmount,
        decimal cardAmount,
        int? mutationVersion = null,
        CancellationToken ct = default);

    /// <summary>
    /// Enqueues a purchase return for accounting processing.
    /// </summary>
    Task EnqueuePurchaseReturnAsync(
        Guid tenantId,
        Guid returnId,
        Guid? originalInvoiceId = null,
        int? mutationVersion = null,
        CancellationToken ct = default);
    
    /// <summary>
    /// Enqueues an online payment capture for accounting processing.
    /// </summary>
    Task EnqueueOnlinePaymentAsync(Guid tenantId, Guid orderId, string paymentIntentId, string gateway, CancellationToken ct = default);

    /// <summary>
    /// Enqueues an unresolved captured payment into suspense when provider cash is known
    /// but the system cannot safely recognize the final business outcome yet.
    /// </summary>
    Task EnqueueUnresolvedOnlinePaymentAsync(
        Guid tenantId,
        decimal amount,
        string currency,
        string gateway,
        string providerEventId,
        Guid businessEntityId = default,
        string? paymentReference = null,
        CancellationToken ct = default);

    /// <summary>
    /// Enqueues suspense clearance for a previously unresolved online payment that
    /// has now been matched back to a real storefront order.
    /// </summary>
    Task EnqueueResolvedUnresolvedOnlinePaymentAsync(
        Guid tenantId,
        Guid orderId,
        decimal amount,
        string gateway,
        string providerEventId,
        CancellationToken ct = default);

    /// <summary>
    /// Enqueues suspense reversal for a CommerceHub provider-success webhook that was
    /// previously routed to suspense and has now been matched to an already-paid local payment row.
    /// </summary>
    Task EnqueueResolvedUnresolvedCommerceHubPaymentAsync(
        Guid tenantId,
        Guid paymentId,
        decimal amount,
        string gateway,
        string providerEventId,
        CancellationToken ct = default);
    
    /// <summary>
    /// Enqueues a COD delivery for accounting processing.
    /// Returns result indicating whether a new message was created or existing one found.
    /// </summary>
    Task<OutboxEnqueueResult> EnqueueCodDeliveredAsync(Guid tenantId, Guid orderId, CancellationToken ct = default);
    
    /// <summary>
    /// Enqueues a wallet payment approval for accounting processing.
    /// </summary>
    Task EnqueueWalletApprovedAsync(Guid tenantId, Guid orderId, CancellationToken ct = default);
    
    /// <summary>
    /// Enqueues an e-commerce refund for accounting processing.
    /// </summary>
    Task EnqueueEcommerceRefundAsync(Guid tenantId, Guid orderId, decimal refundAmount, string refundId, CancellationToken ct = default);

    /// <summary>
    /// Enqueues a post-settlement e-commerce refund with the settlement/payout snapshot
    /// that determines which financial account must absorb the refund.
    /// </summary>
    Task EnqueueSettledEcommerceRefundAsync(
        Guid tenantId,
        Guid orderId,
        decimal refundAmount,
        string refundId,
        EcommerceRefundAccountingContext settlementContext,
        CancellationToken ct = default);
    
    /// <summary>
    /// Enqueues a gateway settlement for accounting processing.
    /// </summary>
    Task EnqueueGatewaySettlementAsync(Guid tenantId, Guid settlementBatchId, string gateway, decimal grossAmount, decimal fees, string settlementId, CancellationToken ct = default);
    
    /// <summary>
    /// Enqueues a COD settlement for accounting processing.
    /// </summary>
    Task EnqueueCodSettlementAsync(Guid tenantId, Guid settlementBatchId, decimal grossAmount, decimal fees, string settlementId, CancellationToken ct = default);
    
    /// <summary>
    /// Enqueues a POS expense for accounting processing.
    /// PHASE LCR: Added for expense ledger posting.
    /// </summary>
    Task EnqueuePosExpenseAsync(
        Guid tenantId,
        Guid expenseId,
        string? operationKey = null,
        int? mutationVersion = null,
        CancellationToken ct = default);

    /// <summary>
    /// Enqueues an expense mutation that reverses any currently posted expense journals
    /// and reposts the current expense state when it is still approved and active.
    /// </summary>
    Task EnqueueExpenseReversalAsync(
        Guid tenantId,
        Guid expenseId,
        string operationKey,
        int mutationVersion,
        CancellationToken ct = default);
}

/// <summary>
/// Processor for outbox messages.
/// Called by background worker.
/// </summary>
public interface IAccountingOutboxProcessor
{
    /// <summary>
    /// Processes pending outbox messages.
    /// Returns the number of messages processed.
    /// </summary>
    Task<int> ProcessPendingMessagesAsync(int batchSize = 50, CancellationToken ct = default);
    
    /// <summary>
    /// Gets the count of pending messages.
    /// </summary>
    Task<int> GetPendingCountAsync(CancellationToken ct = default);
    
    /// <summary>
    /// Gets the count of failed messages.
    /// </summary>
    Task<int> GetFailedCountAsync(CancellationToken ct = default);
    
    /// <summary>
    /// Retries a specific failed message.
    /// </summary>
    Task<bool> RetryFailedMessageAsync(Guid messageId, CancellationToken ct = default);

    /// <summary>
    /// Cleans up processed and terminally failed outbox messages after the configured retention windows.
    /// </summary>
    Task<AccountingOutboxCleanupResult> CleanupAsync(CancellationToken ct = default);
}

public sealed record AccountingOutboxCleanupResult(int ProcessedDeleted, int FailedDeleted)
{
    public int TotalDeleted => ProcessedDeleted + FailedDeleted;
}
