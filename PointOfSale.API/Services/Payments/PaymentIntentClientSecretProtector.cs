using Microsoft.AspNetCore.DataProtection;

namespace PointOfSale.API.Services.Payments;

/// <summary>
/// Service for encrypting and decrypting payment intent client secrets.
/// Uses ASP.NET Core Data Protection API for secure encryption.
/// </summary>
public interface IPaymentIntentClientSecretProtector
{
    /// <summary>
    /// Encrypts a client secret for storage in the database.
    /// </summary>
    string Protect(string clientSecret);
    
    /// <summary>
    /// Decrypts a client secret from the database.
    /// </summary>
    string Unprotect(string encryptedClientSecret);
}

/// <summary>
/// Implementation of IPaymentIntentClientSecretProtector using ASP.NET Core Data Protection.
/// </summary>
public class PaymentIntentClientSecretProtector : IPaymentIntentClientSecretProtector
{
    private readonly IDataProtector _protector;

    public PaymentIntentClientSecretProtector(IDataProtectionProvider dataProtectionProvider)
    {
        // Create a purpose-specific protector for payment intent client secrets
        _protector = dataProtectionProvider.CreateProtector("PaymentIntent.ClientSecret");
    }

    public string Protect(string clientSecret)
    {
        if (string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new ArgumentException("Client secret cannot be null or empty", nameof(clientSecret));
        }

        return _protector.Protect(clientSecret);
    }

    public string Unprotect(string encryptedClientSecret)
    {
        if (string.IsNullOrWhiteSpace(encryptedClientSecret))
        {
            throw new ArgumentException("Encrypted client secret cannot be null or empty", nameof(encryptedClientSecret));
        }

        return _protector.Unprotect(encryptedClientSecret);
    }
}
