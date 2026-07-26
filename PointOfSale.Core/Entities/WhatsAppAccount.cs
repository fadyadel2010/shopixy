using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Represents a WhatsApp account configured for a tenant.
/// Each tenant can have multiple accounts but only one active per use case.
/// </summary>
public class WhatsAppAccount : TenantEntity
{
    /// <summary>
    /// WhatsApp phone number in E.164 format (e.g., +201234567890)
    /// </summary>
    public string PhoneNumber { get; set; } = string.Empty;
    
    /// <summary>
    /// Display name for admin identification
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;
    
    /// <summary>
    /// Current account status
    /// </summary>
    public WhatsAppAccountStatus Status { get; set; } = WhatsAppAccountStatus.PendingQR;
    
    /// <summary>
    /// Whether this account is enabled for messaging
    /// </summary>
    public bool Enabled { get; set; } = false;
    
    /// <summary>
    /// Use for order confirmation messages
    /// </summary>
    public bool UseForOrderConfirmation { get; set; } = false;
    
    /// <summary>
    /// Use for OTP verification messages
    /// </summary>
    public bool UseForOtpVerification { get; set; } = false;
    
    /// <summary>
    /// Use for manual payment approval notifications
    /// </summary>
    public bool UseForPaymentApproval { get; set; } = false;
    
    /// <summary>
    /// Use for abandoned cart recovery messages
    /// Phase: Smart Abandoned Cart Recovery Engine
    /// </summary>
    public bool UseForAbandonedCartRecovery { get; set; } = false;
    
    /// <summary>
    /// Last successful connection timestamp
    /// </summary>
    public DateTime? LastConnectedAtUtc { get; set; }
    
    /// <summary>
    /// Last disconnection timestamp
    /// </summary>
    public DateTime? LastDisconnectedAtUtc { get; set; }
    
    /// <summary>
    /// Reason for current status (e.g., ban reason, disconnect reason)
    /// </summary>
    public string? StatusReason { get; set; }
    
    public DateTime? UpdatedAtUtc { get; set; }
    
    /// <summary>
    /// Creation timestamp in UTC
    /// </summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public virtual Tenant Tenant { get; set; } = null!;
}
