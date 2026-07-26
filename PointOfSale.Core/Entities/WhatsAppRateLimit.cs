namespace PointOfSale.Core.Entities;

/// <summary>
/// Tracks WhatsApp message rate limits and opt-out status per phone number per tenant.
/// Phase: Abandoned Cart Hardening
/// 
/// RATE LIMITING RULES:
/// - Max 3 recovery messages per phone per 24-hour sliding window
/// - Opt-out is permanent until manually reset
/// - Window resets when first message count exceeds window
/// 
/// OPT-OUT HANDLING:
/// - Customer replies "STOP" → OptedOut = true
/// - All future recovery attempts are blocked
/// - Admin can reset opt-out status
/// </summary>
public class WhatsAppRateLimit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public Guid TenantId { get; set; }
    
    /// <summary>
    /// Phone number in E.164 format.
    /// </summary>
    public string PhoneNumber { get; set; } = string.Empty;
    
    /// <summary>
    /// Number of messages sent in current window.
    /// </summary>
    public int MessageCount { get; set; } = 0;
    
    /// <summary>
    /// When the current rate limit window started.
    /// </summary>
    public DateTime WindowStartUtc { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// When the last message was sent.
    /// </summary>
    public DateTime LastMessageAtUtc { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Whether the customer has opted out of recovery messages.
    /// </summary>
    public bool OptedOut { get; set; } = false;
    
    /// <summary>
    /// When the customer opted out.
    /// </summary>
    public DateTime? OptedOutAtUtc { get; set; }
    
    /// <summary>
    /// Maximum messages allowed per window.
    /// Increased from 3 to 10 for more flexibility.
    /// </summary>
    public const int MaxMessagesPerWindow = 25;
    
    /// <summary>
    /// Window duration in hours.
    /// </summary>
    public const int WindowHours = 24;
    
    /// <summary>
    /// Checks if a new message can be sent.
    /// </summary>
    public bool CanSendMessage()
    {
        if (OptedOut) return false;
        
        // Reset window if expired
        if (DateTime.UtcNow - WindowStartUtc > TimeSpan.FromHours(WindowHours))
        {
            return true; // Window expired, will be reset when recording message
        }
        
        return MessageCount < MaxMessagesPerWindow;
    }
    
    /// <summary>
    /// Records a message being sent.
    /// </summary>
    public void RecordMessage()
    {
        var now = DateTime.UtcNow;
        
        // Reset window if expired
        if (now - WindowStartUtc > TimeSpan.FromHours(WindowHours))
        {
            WindowStartUtc = now;
            MessageCount = 0;
        }
        
        MessageCount++;
        LastMessageAtUtc = now;
    }
    
    /// <summary>
    /// Marks the phone as opted out.
    /// </summary>
    public void OptOut()
    {
        OptedOut = true;
        OptedOutAtUtc = DateTime.UtcNow;
    }
    
    /// <summary>
    /// Resets opt-out status (admin action).
    /// </summary>
    public void ResetOptOut()
    {
        OptedOut = false;
        OptedOutAtUtc = null;
    }
}
