namespace PointOfSale.Core.Enums;

/// <summary>
/// Recovery status for abandoned carts.
/// Phase: Smart Abandoned Cart Recovery Engine - Hardening
/// 
/// LIFECYCLE STATE MACHINE:
/// Active → Abandoned → Pending → Triggered → Recovered
///                  ↓                              ↓
///               (failed)                       Expired
///                  ↓
///              Abandoned (retry) or Failed (exhausted)
/// </summary>
public enum AbandonedCartRecoveryStatus
{
    /// <summary>
    /// Cart is active, customer is still shopping.
    /// Transitions to Abandoned after FirstMessageDelayMinutes of inactivity.
    /// </summary>
    Active = 0,
    
    /// <summary>
    /// Cart has been idle beyond threshold, marked as abandoned.
    /// Ready to trigger recovery workflow.
    /// </summary>
    Abandoned = 1,
    
    /// <summary>
    /// HARDENING: Intermediate state while n8n call is in progress.
    /// Prevents duplicate triggers during webhook execution.
    /// If n8n call fails, reverts to Abandoned for retry.
    /// If n8n call succeeds, transitions to Triggered.
    /// </summary>
    Pending = 2,
    
    /// <summary>
    /// Recovery workflow has been triggered (n8n webhook succeeded).
    /// Workflow is now executing in n8n.
    /// </summary>
    Triggered = 3,
    
    /// <summary>
    /// Customer returned and completed the order.
    /// Success metric.
    /// </summary>
    Recovered = 4,
    
    /// <summary>
    /// Recovery window expired without conversion.
    /// No further action will be taken.
    /// </summary>
    Expired = 5,
    
    /// <summary>
    /// HARDENING: Recovery attempts exhausted after max retries.
    /// No further action will be taken.
    /// </summary>
    Failed = 6
}
