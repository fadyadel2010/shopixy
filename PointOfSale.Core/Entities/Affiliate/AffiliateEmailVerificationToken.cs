using System.Security.Cryptography;
using System.Text;

namespace PointOfSale.Core.Entities.Affiliate;

public class AffiliateEmailVerificationToken : BaseEntity
{
    /// <summary>
    /// 32 random bytes → 64-char uppercase hexadecimal canonical token form.
    /// Hexadecimal is invariant across URL query decoding, HTML attribute encoding,
    /// email line-wrapping and whitespace trimming, eliminating the ambiguous
    /// '+', '/' and '=' transport characters of padded Base64 while retaining
    /// 256 bits of entropy.
    /// </summary>
    public const int RawTokenByteLength = 32;
    public const int RawTokenCharLength = RawTokenByteLength * 2;
    private const int LegacyRawTokenCharLength = 88;

    public Guid AffiliateUserId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? ConsumedAtUtc { get; private set; }

    public AffiliateUser AffiliateUser { get; private set; } = null!;

    private AffiliateEmailVerificationToken() { }

    public static AffiliateEmailVerificationToken Create(Guid affiliateUserId, string rawToken, DateTime expiresAtUtc)
    {
        if (affiliateUserId == Guid.Empty) throw new ArgumentException("Affiliate user id is required.", nameof(affiliateUserId));
        if (!IsCanonicalRawToken(rawToken)) throw new ArgumentException("Raw token must be in canonical form.", nameof(rawToken));
        if (expiresAtUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Expiry must be UTC.", nameof(expiresAtUtc));

        return new AffiliateEmailVerificationToken
        {
            AffiliateUserId = affiliateUserId,
            TokenHash = HashToken(rawToken),
            ExpiresAtUtc = expiresAtUtc,
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Generates a cryptographically secure raw verification token (256-bit entropy)
    /// in its canonical URL-safe representation.
    /// </summary>
    public static string NewRawToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(RawTokenByteLength));

    /// <summary>
    /// Strict canonical-shape check: exactly 64 uppercase hexadecimal characters.
    /// Malformed input is rejected before hashing so lookups never depend on
    /// attacker-controlled encoding variants.
    /// </summary>
    public static bool IsCanonicalRawToken(string? rawToken) =>
        !string.IsNullOrWhiteSpace(rawToken) &&
        rawToken.Length == RawTokenCharLength &&
        rawToken.All(IsCanonicalHexDigit);

    /// <summary>
    /// Accepts the current hexadecimal form and the previously issued 64-byte
    /// padded Base64 form. The exact presented value is hashed in both cases;
    /// this only preserves links already delivered before the encoding change.
    /// </summary>
    public static bool IsSupportedPresentedToken(string? rawToken) =>
        IsCanonicalRawToken(rawToken) || IsLegacyRawToken(rawToken);

    private static bool IsCanonicalHexDigit(char c) =>
        c is >= '0' and <= '9' or >= 'A' and <= 'F';

    private static bool IsLegacyRawToken(string? rawToken)
    {
        if (rawToken is null ||
            rawToken.Length != LegacyRawTokenCharLength ||
            rawToken[^2..] != "==" ||
            rawToken[..^2].Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '+' and not '/'))
        {
            return false;
        }

        Span<byte> decoded = stackalloc byte[64];
        return Convert.TryFromBase64String(rawToken, decoded, out var bytesWritten) && bytesWritten == decoded.Length;
    }

    public bool CanConsume(DateTime nowUtc) => ConsumedAtUtc == null && ExpiresAtUtc > nowUtc;

    public void Consume(DateTime nowUtc)
    {
        if (!CanConsume(nowUtc)) throw new InvalidOperationException("Affiliate verification token is not consumable.");

        ConsumedAtUtc = nowUtc;
        UpdatedAt = nowUtc;
    }

    public static string HashToken(string rawToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken)) throw new ArgumentException("Token is required.", nameof(rawToken));

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes);
    }
}
