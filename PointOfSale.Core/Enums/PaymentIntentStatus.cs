namespace PointOfSale.Core.Enums;

/// <summary>
/// Payment intent status for e-commerce payments.
/// Phase 4.1: Payments API
/// </summary>
public enum PaymentIntentStatus
{
    /// <summary>Payment intent created, awaiting payment</summary>
    Pending = 0,
    
    /// <summary>Payment authorized but not captured (2-step)</summary>
    Authorized = 1,
    
    /// <summary>Payment captured/completed successfully</summary>
    Paid = 2,
    
    /// <summary>Payment failed</summary>
    Failed = 3,
    
    /// <summary>Payment cancelled by user or system</summary>
    Cancelled = 4,
    
    /// <summary>Payment expired (timeout)</summary>
    Expired = 5,
    
    /// <summary>Payment skipped (COD orders)</summary>
    Skipped = 6,

    /// <summary>Payment awaiting manual verification</summary>
    AwaitingVerification = 7,

    /// <summary>Payment rejected by admin</summary>
    Rejected = 8,

    /// <summary>Provider reported an amount/currency that does not match the stored intent</summary>
    AmountMismatch = 9,

    /// <summary>Payment is quarantined for manual investigation</summary>
    Suspicious = 10
}
