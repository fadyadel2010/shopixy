namespace PointOfSale.Core.Interfaces.Accounting;

/// <summary>
/// Metrics service for accounting operations.
/// 
/// PHASE 3.5: Observability for financial integrity.
/// </summary>
public interface IAccountingMetrics
{
    /// <summary>
    /// Increments the counter for posted POS sales.
    /// </summary>
    void IncrementPosSalesPosted(Guid tenantId);
    
    /// <summary>
    /// Increments the counter for posted POS purchases.
    /// </summary>
    void IncrementPosPurchasesPosted(Guid tenantId);
    
    /// <summary>
    /// Increments the counter for posted e-commerce payments.
    /// </summary>
    void IncrementEcommercePaymentsPosted(Guid tenantId, string gateway);
    
    /// <summary>
    /// Increments the counter for posted refunds.
    /// </summary>
    void IncrementRefundsPosted(Guid tenantId);
    
    /// <summary>
    /// Increments the counter for posted settlements.
    /// </summary>
    void IncrementSettlementsPosted(Guid tenantId, string type);
    
    /// <summary>
    /// Increments the counter for accounting failures.
    /// </summary>
    void IncrementAccountingFailures(Guid tenantId, string operationType, string? errorType = null);
    
    /// <summary>
    /// Records the current pending outbox count.
    /// </summary>
    void SetOutboxPendingCount(int count);
    
    /// <summary>
    /// Records the current failed outbox count.
    /// </summary>
    void SetOutboxFailedCount(int count);
    
    /// <summary>
    /// Records posting latency.
    /// </summary>
    void RecordPostingLatency(double milliseconds, string operationType);
    
    /// <summary>
    /// Records idempotent (duplicate) call.
    /// </summary>
    void IncrementIdempotentCalls(Guid tenantId, string operationType);

    /// <summary>
    /// Records when an outbox message becomes terminally failed and enters DLQ-equivalent state.
    /// </summary>
    void IncrementOutboxDeadLettered(Guid tenantId, string operationType);

    /// <summary>
    /// Records outbox cleanup activity.
    /// </summary>
    void RecordOutboxCleanup(int processedDeleted, int failedDeleted);
}
