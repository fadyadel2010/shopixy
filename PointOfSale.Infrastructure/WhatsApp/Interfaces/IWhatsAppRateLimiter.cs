namespace PointOfSale.Infrastructure.WhatsApp.Interfaces;

public interface IWhatsAppRateLimiter
{
    /// <summary>
    /// Checks if the message can be sent based on all rate limit rules.
    /// Returns true if allowed, false if limited.
    /// </summary>
    Task<bool> CanSendMessageAsync(Guid tenantId, Guid accountId, string recipientPhone, CancellationToken ct = default);
    
    /// <summary>
    /// Checks if the message can be sent based on all rate limit rules.
    /// Transactional messages (OrderConfirmation, ShipmentUpdate) bypass recipient cooldown.
    /// </summary>
    Task<bool> CanSendMessageAsync(Guid tenantId, Guid accountId, string recipientPhone, string messageType, CancellationToken ct = default);
    
    /// <summary>
    /// Records a sent message to update counters.
    /// </summary>
    Task RecordMessageSentAsync(Guid tenantId, Guid accountId, string recipientPhone, CancellationToken ct = default);
    
    /// <summary>
    /// Gets the delay (jitter) to apply before sending.
    /// </summary>
    TimeSpan GetRandomDelay();
}

