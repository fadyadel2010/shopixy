namespace PointOfSale.Core.Enums;

public enum WhatsAppAccountStatus
{
    /// <summary>
    /// Waiting for QR code scan
    /// </summary>
    PendingQR = 0,
    
    /// <summary>
    /// Successfully connected and operational
    /// </summary>
    Connected = 1,
    
    /// <summary>
    /// Temporarily disconnected (will auto-reconnect)
    /// </summary>
    Disconnected = 2,
    
    /// <summary>
    /// Banned by WhatsApp (requires new number)
    /// </summary>
    Banned = 3,
    
    /// <summary>
    /// Manually disabled by admin
    /// </summary>
    Disabled = 4
}
