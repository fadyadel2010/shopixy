namespace PointOfSale.Infrastructure.Notifications;

/// <summary>
/// Abstraction for sending notifications across multiple channels.
/// Phase 5.3: Tracking & Notifications
/// 
/// Channel Support (Ready for future implementation):
/// - Email
/// - SMS
/// - WhatsApp
/// - Webhooks
/// 
/// IMPORTANT:
/// - All implementations must be best-effort (never throw)
/// - Notifications should NOT block shipment lifecycle
/// - Log all attempts with structured data for observability
/// 
/// Error Handling:
/// - Return false on failure, never throw
/// - Log failures internally
/// - Caller will acknowledge event regardless of send result
/// </summary>
public interface INotificationSender
{
    /// <summary>
    /// Sends a notification asynchronously.
    /// </summary>
    /// <param name="message">The notification message to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if sent successfully, false otherwise.</returns>
    Task<bool> SendAsync(
        NotificationMessage message,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the sender's channel type (for logging/metrics).
    /// </summary>
    string Channel { get; }

    /// <summary>
    /// Gets whether this sender is currently available.
    /// </summary>
    bool IsAvailable { get; }
}
