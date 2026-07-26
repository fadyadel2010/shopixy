using System.Security.Cryptography;
using System.Text;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Email verification token for CommerceHub customer accounts.
///
/// SECURITY CRITICAL:
/// - Raw token is NEVER stored — only SHA-256 hash
/// - Tokens are single-use (IsUsed flag)
/// - Tokens expire after 24 hours
/// - Previous unused tokens are invalidated on resend
/// - Verifying email does NOT grant session access — it only sets the verified flag
/// </summary>
public class CommerceHubCustomerEmailVerificationToken
{
    public Guid Id { get; set; }

    /// <summary>
    /// The CommerceHub customer this token was issued for.
    /// </summary>
    public Guid CustomerId { get; set; }

    /// <summary>
    /// SHA-256 hash of the verification token.
    /// RAW TOKEN IS NEVER STORED.
    /// </summary>
    public string TokenHash { get; set; } = null!;

    /// <summary>
    /// When this token expires.
    /// Default: 24 hours from creation.
    /// </summary>
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>
    /// Whether this token has been consumed.
    /// Once used, the token cannot be reused.
    /// </summary>
    public bool IsUsed { get; set; }

    /// <summary>
    /// When the token was consumed. NULL if not yet used.
    /// </summary>
    public DateTime? UsedAtUtc { get; set; }

    /// <summary>
    /// When this token was created.
    /// </summary>
    public DateTime CreatedAtUtc { get; set; }

    // ---- Static helpers ----

    public const int TokenExpirationHours = 24;

    /// <summary>
    /// Generates a cryptographically secure raw token string.
    /// </summary>
    public static string GenerateRawToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// Produces the SHA-256 hash stored in the database.
    /// </summary>
    public static string HashToken(string rawToken)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexStringLower(hash);
    }
}
