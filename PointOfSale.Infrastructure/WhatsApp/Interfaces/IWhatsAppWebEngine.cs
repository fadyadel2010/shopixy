using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;

namespace PointOfSale.Infrastructure.WhatsApp.Interfaces;

public interface IWhatsAppWebEngine
{
    /// <summary>
    /// Generates a QR code for account authentication.
    /// Returns base64-encoded PNG image.
    /// </summary>
    Task<QrCodeResult> GenerateQrCodeAsync(Guid accountId, CancellationToken ct = default);
    
    /// <summary>
    /// Checks if the account is authenticated and ready.
    /// </summary>
    Task<ConnectionStatus> CheckConnectionAsync(Guid accountId, CancellationToken ct = default);
    
    /// <summary>
    /// Sends a text message to the specified phone number.
    /// </summary>
    Task<SendResult> SendMessageAsync(
        Guid accountId, 
        string phoneNumber, 
        string message, 
        CancellationToken ct = default);
    
    /// <summary>
    /// Disconnects and clears session for the account.
    /// </summary>
    Task DisconnectAsync(Guid accountId, CancellationToken ct = default);
}

public record QrCodeResult(bool Success, string? QrImageBase64, string? Error, int? ExpiresInSeconds, bool AlreadyConnected = false);
public record ConnectionStatus(bool IsConnected, WhatsAppAccountStatus Status, DateTime? LastActivity, string? ConnectedPhone = null);
public record SendResult(bool Success, string? MessageId, string? Error);
