using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;

namespace PointOfSale.API.Services.CommerceHub;

public interface ICommerceHubOrderAccessService
{
    string GenerateAccessToken(Guid marketplaceOrderId, string orderNumber);
    bool ValidateAccessToken(Guid marketplaceOrderId, string orderNumber, string? token);
}

public class CommerceHubOrderAccessService : ICommerceHubOrderAccessService
{
    private const string Purpose = "CommerceHubOrderAccess:v1";
    private const string TokenVersion = "v2";
    private readonly byte[] _signingKey;
    private readonly CheckoutSessionSecurityOptions _securityOptions;

    /// <summary>
    /// SECURITY HARDENING (Audit C-1):
    /// CommerceHub:OrderAccessSigningKey MUST be configured.
    /// No fallback to Jwt:SecretKey or any hardcoded default.
    /// Missing key = startup failure (fail-fast, not fail-open).
    /// </summary>
    public CommerceHubOrderAccessService(
        IConfiguration configuration,
        IOptions<CheckoutSessionSecurityOptions> securityOptions)
    {
        var key = configuration["CommerceHub:OrderAccessSigningKey"];

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException(
                "[SECURITY] CommerceHub:OrderAccessSigningKey is not configured. " +
                "This key is REQUIRED for order access token generation. " +
                "Set it in appsettings.json or environment variables before starting the application.");
        }

        _signingKey = Encoding.UTF8.GetBytes(key);
        _securityOptions = securityOptions.Value;
    }

    public string GenerateAccessToken(Guid marketplaceOrderId, string orderNumber)
    {
        var expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(GetPositiveMinutes(_securityOptions.RecoveryTokenTtlMinutes));
        var expiresAtUnixSeconds = expiresAtUtc.ToUnixTimeSeconds();
        var payload = BuildPayload(marketplaceOrderId, orderNumber, expiresAtUnixSeconds);

        using var hmac = new HMACSHA256(_signingKey);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return $"{TokenVersion}.{expiresAtUnixSeconds}.{Base64UrlEncode(hash)}";
    }

    /// <summary>
    /// SECURITY HARDENING (Audit C-1):
    /// Uses constant-time comparison (CryptographicOperations.FixedTimeEquals)
    /// to prevent timing side-channel attacks on token validation.
    /// </summary>
    public bool ValidateAccessToken(Guid marketplaceOrderId, string orderNumber, string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var parts = token.Trim().Split('.', StringSplitOptions.None);
        if (parts.Length != 3 || !string.Equals(parts[0], TokenVersion, StringComparison.Ordinal))
        {
            return false;
        }

        if (!long.TryParse(parts[1], out var expiresAtUnixSeconds))
        {
            return false;
        }

        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expiresAtUnixSeconds)
        {
            return false;
        }

        var payload = BuildPayload(marketplaceOrderId, orderNumber, expiresAtUnixSeconds);
        using var hmac = new HMACSHA256(_signingKey);
        var expected = Base64UrlEncode(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(parts[2]);

        if (expectedBytes.Length != actualBytes.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    private static string BuildPayload(Guid marketplaceOrderId, string orderNumber, long expiresAtUnixSeconds) =>
        $"{Purpose}:{TokenVersion}:{marketplaceOrderId:N}:{orderNumber}:{expiresAtUnixSeconds}";

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    private static int GetPositiveMinutes(int configuredMinutes) => configuredMinutes > 0 ? configuredMinutes : 20;
}
