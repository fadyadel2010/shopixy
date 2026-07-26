using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace PointOfSale.Infrastructure.Shipping;

/// <summary>
/// Service for encrypting and decrypting carrier API credentials.
/// 
/// SECURITY HARDENING (P2):
/// - Uses ASP.NET Core Data Protection API (same as Payments/Analytics)
/// - Credentials encrypted at rest in PostgreSQL
/// - Decryption only occurs in-memory at runtime
/// - Backward-compatible with existing plaintext values
/// 
/// ARCHITECTURE RULES:
/// - Centralized encryption (NO adapter-level crypto)
/// - Transparent to adapters (they receive plaintext)
/// - Encryption happens BEFORE save, decryption AFTER load
/// - Failed decryption returns empty string (graceful degradation)
/// </summary>
public interface ICarrierCredentialEncryptionService
{
    /// <summary>
    /// Encrypts a carrier credential value.
    /// </summary>
    string Encrypt(string plainText);
    
    /// <summary>
    /// Decrypts a carrier credential value.
    /// Returns plaintext if value is not encrypted (backward compatibility).
    /// </summary>
    string Decrypt(string? encryptedOrPlainText);
    
    /// <summary>
    /// Checks if a value appears to be encrypted.
    /// </summary>
    bool IsEncrypted(string? value);
}

/// <summary>
/// Implementation of carrier credential encryption using Data Protection API.
/// </summary>
public sealed class CarrierCredentialEncryptionService : ICarrierCredentialEncryptionService
{
    private readonly IDataProtector _protector;
    private readonly ILogger<CarrierCredentialEncryptionService> _logger;
    
    /// <summary>
    /// Data Protection purpose for carrier credentials.
    /// Isolated from other encryption purposes for security.
    /// </summary>
    private const string ProtectorPurpose = "Shipping.CarrierCredentials.Secrets";
    
    /// <summary>
    /// Prefix for encrypted values to distinguish from plaintext.
    /// </summary>
    private const string EncryptedPrefix = "ENC:";
    
    public CarrierCredentialEncryptionService(
        IDataProtectionProvider dataProtectionProvider,
        ILogger<CarrierCredentialEncryptionService> logger)
    {
        _protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);
        _logger = logger;
    }
    
    /// <inheritdoc/>
    public string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return string.Empty;
        
        // Already encrypted? Return as-is
        if (IsEncrypted(plainText))
            return plainText;
        
        try
        {
            var encrypted = _protector.Protect(plainText);
            return $"{EncryptedPrefix}{encrypted}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to encrypt carrier credential");
            throw new InvalidOperationException("Credential encryption failed", ex);
        }
    }
    
    /// <inheritdoc/>
    public string Decrypt(string? encryptedOrPlainText)
    {
        if (string.IsNullOrEmpty(encryptedOrPlainText))
            return string.Empty;
        
        // Not encrypted? Return as-is (backward compatibility)
        if (!IsEncrypted(encryptedOrPlainText))
        {
            _logger.LogDebug("Carrier credential is not encrypted (legacy value)");
            return encryptedOrPlainText;
        }
        
        try
        {
            // Remove prefix and decrypt
            var encrypted = encryptedOrPlainText[EncryptedPrefix.Length..];
            return _protector.Unprotect(encrypted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt carrier credential - returning empty");
            return string.Empty;
        }
    }
    
    /// <inheritdoc/>
    public bool IsEncrypted(string? value)
    {
        return !string.IsNullOrEmpty(value) && value.StartsWith(EncryptedPrefix);
    }
}
