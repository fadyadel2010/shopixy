namespace PointOfSale.Core.Interfaces;

/// <summary>
/// Service for managing WhatsApp rate limiting and opt-out.
/// Phase: Abandoned Cart Hardening
/// 
/// RATE LIMITING:
/// - Max 3 messages per phone per 24-hour window
/// - Window resets after 24 hours of first message
/// 
/// OPT-OUT:
/// - Permanent until admin reset
/// - Blocks all recovery messages
/// </summary>
public interface IWhatsAppRateLimitService
{
    /// <summary>
    /// Checks if a message can be sent to the phone number.
    /// Returns false if opted out or rate limited.
    /// </summary>
    Task<bool> CanSendMessageAsync(Guid tenantId, string phoneNumber, CancellationToken ct = default);
    
    /// <summary>
    /// Records a message being sent (updates rate limit).
    /// </summary>
    Task RecordMessageSentAsync(Guid tenantId, string phoneNumber, CancellationToken ct = default);
    
    /// <summary>
    /// Marks a phone as opted out.
    /// </summary>
    Task OptOutAsync(Guid tenantId, string phoneNumber, CancellationToken ct = default);
    
    /// <summary>
    /// Resets opt-out status (admin action).
    /// </summary>
    Task ResetOptOutAsync(Guid tenantId, string phoneNumber, CancellationToken ct = default);
    
    /// <summary>
    /// Gets current rate limit status for a phone.
    /// </summary>
    Task<RateLimitStatus> GetStatusAsync(Guid tenantId, string phoneNumber, CancellationToken ct = default);
}

/// <summary>
/// Rate limit status for a phone number.
/// </summary>
public record RateLimitStatus(
    bool CanSend,
    int MessageCount,
    int RemainingMessages,
    bool OptedOut,
    DateTime? WindowResetAtUtc
);
