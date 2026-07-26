using System.Security.Cryptography;
using System.Text;

namespace PointOfSale.Core.Security;

/// <summary>
/// Generates cryptographically secure refresh tokens.
/// 
/// SECURITY RULES:
/// - Uses 64 bytes of cryptographically secure random data
/// - URL-safe Base64 encoding (no + / = characters)
/// - NOT a GUID - GUIDs are not cryptographically secure
/// - NOT predictable
/// 
/// Phase: 10.3.4 - Storefront Refresh Tokens & Token Revocation
/// </summary>
public static class RefreshTokenGenerator
{
    private const int TokenSizeBytes = 64;

    /// <summary>
    /// Generates a new cryptographically secure refresh token.
    /// </summary>
    /// <returns>URL-safe Base64 encoded token.</returns>
    public static string Generate()
    {
        var randomBytes = RandomNumberGenerator.GetBytes(TokenSizeBytes);
        return ToUrlSafeBase64(randomBytes);
    }

    /// <summary>
    /// Converts bytes to URL-safe Base64 string.
    /// Replaces + with -, / with _, and removes = padding.
    /// </summary>
    private static string ToUrlSafeBase64(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}

/// <summary>
/// Hashes refresh tokens using SHA256.
/// 
/// SECURITY RULES:
/// - SHA256 ONLY
/// - Deterministic (same input = same output)
/// - Returns lowercase hex string
/// - NEVER log raw tokens
/// 
/// Phase: 10.3.4 - Storefront Refresh Tokens & Token Revocation
/// </summary>
public static class RefreshTokenHasher
{
    /// <summary>
    /// Computes SHA256 hash of a refresh token.
    /// </summary>
    /// <param name="token">The raw refresh token.</param>
    /// <returns>Lowercase hex string of the hash.</returns>
    public static string Hash(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("Token cannot be null or empty", nameof(token));

        var bytes = Encoding.UTF8.GetBytes(token);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Verifies that a raw token matches a stored hash.
    /// </summary>
    /// <param name="rawToken">The raw token to verify.</param>
    /// <param name="storedHash">The stored hash to compare against.</param>
    /// <returns>True if the token matches the hash.</returns>
    public static bool Verify(string rawToken, string storedHash)
    {
        if (string.IsNullOrWhiteSpace(rawToken) || string.IsNullOrWhiteSpace(storedHash))
            return false;

        var computedHash = Hash(rawToken);
        try
        {
            var computedBytes = Convert.FromHexString(computedHash);
            var storedBytes = Convert.FromHexString(storedHash);
            return computedBytes.Length == storedBytes.Length &&
                   CryptographicOperations.FixedTimeEquals(computedBytes, storedBytes);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
