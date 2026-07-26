using Microsoft.Extensions.Logging;

namespace PointOfSale.Infrastructure.Notifications;

/// <summary>
/// Log-based notification sender for development and testing.
/// Phase 5.3: Tracking & Notifications
/// 
/// This is a stub implementation that logs all notification attempts
/// instead of actually sending to external channels.
/// 
/// Purpose:
/// - Development and local testing
/// - Integration testing without external dependencies
/// - Audit trail of notification attempts
/// 
/// IMPORTANT:
/// - Always returns true (simulating successful send)
/// - All notifications are logged with structured data
/// - Never throws exceptions (best-effort pattern)
/// </summary>
public class LogNotificationSender : INotificationSender
{
    private readonly ILogger<LogNotificationSender> _logger;

    public LogNotificationSender(ILogger<LogNotificationSender> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Channel => NotificationChannels.Log;

    /// <inheritdoc/>
    public bool IsAvailable => true;

    /// <inheritdoc/>
    public Task<bool> SendAsync(
        NotificationMessage message,
        CancellationToken cancellationToken = default)
    {
        if (message == null)
        {
            _logger.LogWarning("Notification SKIPPED: Message is null");
            return Task.FromResult(false);
        }

        // Validate required fields
        if (string.IsNullOrEmpty(message.Recipient))
        {
            _logger.LogWarning(
                "Notification SKIPPED: Missing recipient | TenantId: {TenantId} | OrderId: {OrderId} | ShipmentId: {ShipmentId} | Type: {Type} | EventId: {EventId} | CorrelationId: {CorrelationId}",
                message.TenantId,
                message.OrderId,
                message.ShipmentId,
                message.Type,
                message.EventId,
                message.CorrelationId);
            return Task.FromResult(false);
        }

        // Log the notification attempt with full structured data
        _logger.LogInformation(
            "📧 NOTIFICATION SENT (Log) | " +
            "Type: {Type} | " +
            "Channel: {Channel} | " +
            "Recipient: {Recipient} | " +
            "TenantId: {TenantId} | " +
            "OrderId: {OrderId} | " +
            "ShipmentId: {ShipmentId} | " +
            "OrderNumber: {OrderNumber} | " +
            "ShipmentNumber: {ShipmentNumber} | " +
            "EventId: {EventId} | " +
            "OccurredAt: {OccurredAtUtc} | " +
            "CorrelationId: {CorrelationId}",
            message.Type,
            message.Channel,
            message.Recipient,
            message.TenantId,
            message.OrderId,
            message.ShipmentId,
            message.OrderNumber,
            message.ShipmentNumber,
            message.EventId,
            message.OccurredAtUtc,
            message.CorrelationId);

        // Log payload details at Debug level
        if (message.Payload.Count > 0)
        {
            foreach (var (key, value) in message.Payload)
            {
                _logger.LogDebug(
                    "  📧 Payload | {Key}: {Value} | EventId: {EventId}",
                    key,
                    value,
                    message.EventId);
            }
        }

        // Simulate successful send
        return Task.FromResult(true);
    }
}
