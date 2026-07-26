using PointOfSale.Core.Events;

namespace PointOfSale.API.Events;

/// <summary>
/// Event emitted when a manual payment is submitted for verification.
/// </summary>
public class ManualPaymentSubmittedEvent : PaymentEventBase
{
    public string OrderNumber { get; set; } = string.Empty;
    public string WalletType { get; set; } = string.Empty;
    
    /// <summary>
    /// Indicates whether a proof file was uploaded with this payment.
    /// </summary>
    public bool HasProofFile { get; set; }
    
    /// <summary>
    /// DEPRECATED: Use HasProofFile instead. Kept for backward compatibility with existing subscribers.
    /// This field is nullable and will be null going forward to avoid exposing sensitive storage paths.
    /// </summary>
    [Obsolete("Use HasProofFile instead. This field is maintained for backward compatibility only.")]
    public string? ProofImageUrl { get; set; }
}

/// <summary>
/// Event emitted when a manual payment is confirmed by admin.
/// </summary>
public class ManualPaymentConfirmedEvent : PaymentEventBase
{
    public string OrderNumber { get; set; } = string.Empty;
    public string VerifiedByAdmin { get; set; } = string.Empty;
    public DateTime VerifiedAtUtc { get; set; }
}

/// <summary>
/// Event emitted when a manual payment is rejected by admin.
/// </summary>
public class ManualPaymentRejectedEvent : PaymentEventBase
{
    public string OrderNumber { get; set; } = string.Empty;
    public string VerifiedByAdmin { get; set; } = string.Empty;
    public DateTime VerifiedAtUtc { get; set; }
    public string RejectionReason { get; set; } = string.Empty;
}
