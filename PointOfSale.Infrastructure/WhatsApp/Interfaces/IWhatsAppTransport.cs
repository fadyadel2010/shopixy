namespace PointOfSale.Infrastructure.WhatsApp.Interfaces;

/// <summary>
/// Transport abstraction for WhatsApp message delivery.
/// Enables multiple transport implementations (Playwright, Baileys) to coexist during migration.
/// </summary>
public interface IWhatsAppTransport
{
    /// <summary>
    /// Sends a WhatsApp message to the specified phone number.
    /// </summary>
    /// <param name="tenantId">Unique identifier for the tenant</param>
    /// <param name="phoneE164">Phone number in E.164 format (e.g., +5511999999999)</param>
    /// <param name="message">Message content</param>
    /// <param name="messageType">Type of message (text, media, template)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result containing message ID or error</returns>
    Task<WhatsAppSendResult> SendMessageAsync(
        string tenantId,
        string phoneE164,
        string message,
        WhatsAppMessageType messageType,
        CancellationToken cancellationToken = default,
        string? accountId = null);

    /// <summary>
    /// Retrieves QR code for WhatsApp authentication.
    /// </summary>
    /// <param name="tenantId">Unique identifier for the tenant</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>QR code as base64-encoded PNG or error</returns>
    Task<WhatsAppQrResult> GetQrCodeAsync(
        string tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current connection status for a tenant's session.
    /// </summary>
    /// <param name="tenantId">Unique identifier for the tenant</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Connection status (connected, disconnected, qr_pending)</returns>
    Task<WhatsAppStatusResult> GetStatusAsync(
        string tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects and cleans up a tenant's WhatsApp session.
    /// </summary>
    /// <param name="tenantId">Unique identifier for the tenant</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result indicating success or failure</returns>
    Task<WhatsAppDisconnectResult> DisconnectAsync(
        string tenantId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Message type classification for WhatsApp messages.
/// </summary>
public enum WhatsAppMessageType
{
    Text,
    Media,
    Template
}

/// <summary>
/// Result of a WhatsApp message send operation.
/// </summary>
public class WhatsAppSendResult
{
    public bool Success { get; set; }
    public string? MessageId { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Result of a QR code generation request.
/// </summary>
public class WhatsAppQrResult
{
    public bool Success { get; set; }
    public string? QrCodeBase64 { get; set; }
    public string? ErrorMessage { get; set; }
    
    /// <summary>
    /// Indicates that the session is already connected and no QR code is needed.
    /// </summary>
    public bool AlreadyConnected { get; set; }
}

/// <summary>
/// Result of a connection status check.
/// </summary>
public class WhatsAppStatusResult
{
    public bool Success { get; set; }
    public WhatsAppConnectionStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ConnectedPhone { get; set; }
}

/// <summary>
/// WhatsApp connection status enumeration.
/// </summary>
public enum WhatsAppConnectionStatus
{
    Connected,
    Disconnected,
    QrPending,
    Unknown
}

/// <summary>
/// Result of a disconnect operation.
/// </summary>
public class WhatsAppDisconnectResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}
