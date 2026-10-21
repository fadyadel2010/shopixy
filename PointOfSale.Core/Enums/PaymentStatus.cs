namespace PointOfSale.Core.Enums;

/// <summary>
/// Payment lifecycle status for e-commerce orders.
/// 
/// PHASE: COD Payment Lifecycle Architecture
/// 
/// CRITICAL RULES:
/// 1. Status changes MUST be driven by domain events
/// 2. Controllers CANNOT directly change PaymentStatus
/// 3. Every status change that recognizes money MUST create a journal entry
/// 
/// STATE MACHINE TRANSITIONS:
/// Pending → Authorized (Online pre-auth)
/// Pending → CashOnDelivery (Marketplace COD approved for fulfillment)
/// Pending → Collected (COD delivered, Online captured, Wallet approved)
/// Authorized → Collected (Online capture)
/// CashOnDelivery → Collected (Cash collected on delivery)
/// Collected → Settled (Bank settlement received)
/// Collected → Refunded (Full refund)
/// Collected → PartiallyRefunded (Partial refund)
/// Any → Failed (Payment failure)
/// </summary>
public enum PaymentStatus
{
    /// <summary>Payment not yet received. Initial state for all orders.</summary>
    Pending = 0,
    
    /// <summary>
    /// Payment authorized but not captured (online gateways only).
    /// No journal entry yet - money not recognized.
    /// </summary>
    Authorized = 1,
    
    /// <summary>
    /// Payment collected from customer.
    /// JOURNAL CREATED: DEBIT Clearing, CREDIT Revenue
    /// This is the ONLY state that recognizes revenue.
    /// </summary>
    Collected = 2,
    
    /// <summary>
    /// DEPRECATED: Use Collected instead.
    /// Kept for backward compatibility. Maps to Collected.
    /// </summary>
    [Obsolete("Use PaymentStatus.Collected instead. This status is retained for backward compatibility.")]
    Paid = 2,
    
    /// <summary>
    /// Partial payment received (legacy, rarely used).
    /// </summary>
    [Obsolete("Partial payments should be tracked at OrderItem level, not Order level.")]
    PartiallyPaid = 3,
    
    /// <summary>Payment failed permanently.</summary>
    Failed = 4,
    
    /// <summary>
    /// Payment fully refunded.
    /// JOURNAL CREATED: DEBIT Revenue, CREDIT Clearing
    /// </summary>
    Refunded = 5,
    
    /// <summary>
    /// Payment partially refunded.
    /// JOURNAL CREATED: DEBIT Revenue (partial), CREDIT Clearing (partial)
    /// </summary>
    PartiallyRefunded = 6,

    /// <summary>
    /// Payment awaiting manual verification (wallet payments).
    /// No journal entry yet - money not recognized.
    /// </summary>
    AwaitingVerification = 7,

    /// <summary>
    /// Payment rejected by admin (wallet payments).
    /// No journal entry - money never recognized.
    /// </summary>
    Rejected = 8,
    
    /// <summary>
    /// Collected money has reached the receiving Treasury account recorded in posted settlement history.
    /// JOURNAL CREATED: DEBIT receiving Cashbox/Bank, CREDIT Clearing (plus any authorized fee expense)
    /// This is the final financial state for successful orders.
    /// </summary>
    Settled = 9,

    /// <summary>
    /// Cash will be collected from the customer on delivery.
    /// Fulfillment may proceed, but revenue is NOT recognized yet.
    /// </summary>
    CashOnDelivery = 10,

    /// <summary>
    /// Payment window expired before money could be safely recognized.
    /// No journal entry is created. Any provider-side money received after this
    /// state must be refunded or manually reviewed before further mutation.
    /// </summary>
    Expired = 11,

    /// <summary>
    /// Provider-reported amount or currency did not match the stored payment intent.
    /// No journal entry is created and fulfillment must remain blocked pending manual review.
    /// </summary>
    AmountMismatch = 12,

    /// <summary>Development QA simulation only. NOT customer payment or cash receipt.</summary>
    SyntheticQaCollected = 13
}
