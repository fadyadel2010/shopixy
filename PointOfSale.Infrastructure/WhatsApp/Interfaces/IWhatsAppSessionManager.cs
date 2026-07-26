namespace PointOfSale.Infrastructure.WhatsApp.Interfaces;

public interface IWhatsAppSessionManager
{
    /// <summary>
    /// Encrypts and saves the session data for the account.
    /// </summary>
    Task SaveSessionAsync(Guid accountId, string sessionData, CancellationToken ct = default);
    
    /// <summary>
    /// Retrieves and decrypts the session data for the account.
    /// Returns null if no session exists.
    /// </summary>
    Task<string?> GetSessionAsync(Guid accountId, CancellationToken ct = default);
    
    /// <summary>
    /// Deletes the session for the account.
    /// </summary>
    Task DeleteSessionAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>
    /// Updates the heartbeat for the active session.
    /// </summary>
    Task TouchSessionAsync(Guid accountId, CancellationToken ct = default);
}
