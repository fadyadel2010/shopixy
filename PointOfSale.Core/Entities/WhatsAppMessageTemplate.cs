using PointOfSale.Core.Entities;

namespace PointOfSale.Core.Entities;

public class WhatsAppMessageTemplate : TenantEntity
{
    public Guid Id { get; set; }
    
    /// <summary>
    /// Template type: "OrderConfirmation", "OTP", etc.
    /// </summary>
    public string Type { get; set; } = string.Empty;
    
    /// <summary>
    /// Language code (e.g., "en", "ar")
    /// </summary>
    public string Language { get; set; } = "en";
    
    /// <summary>
    /// Message content with {{variables}}
    /// </summary>
    public string Content { get; set; } = string.Empty;
    
    public bool IsActive { get; set; } = true;
    
    /// <summary>
    /// If true, this is a system-level default template (TenantId might be null or system tenant)
    /// </summary>
    public bool IsDefault { get; set; } = false;
    
    /// <summary>
    /// Creation timestamp for audit trail (PHASE: WhatsApp Final Hardening)
    /// </summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Last modification timestamp
    /// </summary>
    public DateTime UpdatedAtUtc { get; set; }
}
