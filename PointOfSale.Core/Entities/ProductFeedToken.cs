using System.Security.Cryptography;
using System.Text;

namespace PointOfSale.Core.Entities;

/// <summary>
/// FEED-1A: Persistent opaque token granting anonymous read access to a
/// tenant's public product feed for a single channel.
///
/// SECURITY INVARIANTS:
/// - The raw token is generated once, returned once at creation, and NEVER persisted.
/// - Only the SHA-256 <see cref="TokenHash"/> is stored (secure-token repository convention).
/// - The token encodes nothing (no tenant id, product id, slug, email, or timestamp);
///   the hash lookup is the only path from token to tenant.
/// - Revocation (<see cref="RevokedAtUtc"/>) stops the feed URL immediately.
/// </summary>
public class ProductFeedToken : BaseEntity
{
    /// <summary>
    /// Supported public feed channels. The column lets the token table serve
    /// every channel without schema change; public feed read paths are
    /// strictly channel-scoped (a token never authorizes another channel).
    /// </summary>
    public const string MetaChannel = "meta";
    public const string GoogleChannel = "google";
    public const string TikTokChannel = "tiktok";
    public const string SnapchatChannel = "snapchat";

    public static IReadOnlyList<string> SupportedChannels { get; } = new[] { MetaChannel, GoogleChannel, TikTokChannel, SnapchatChannel };

    public static bool IsSupportedChannel(string? channel) =>
        channel != null && SupportedChannels.Contains(channel);

    public Guid TenantId { get; private set; }
    public string Channel { get; private set; } = string.Empty;
    public string TokenHash { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }

    public bool IsActive => RevokedAtUtc == null && !IsDeleted;

    private ProductFeedToken() { }

    public static ProductFeedToken Create(Guid tenantId, string channel, string rawToken)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(channel)) throw new ArgumentException("Channel is required.", nameof(channel));

        return new ProductFeedToken
        {
            TenantId = tenantId,
            Channel = channel,
            TokenHash = HashToken(rawToken),
            CreatedAtUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void Revoke(DateTime nowUtc)
    {
        if (RevokedAtUtc != null)
        {
            return;
        }

        RevokedAtUtc = nowUtc;
        UpdatedAt = nowUtc;
    }

    /// <summary>
    /// SHA-256 hex hash of the raw token — the repository's existing
    /// secure-token storage convention. Raw tokens are never stored.
    /// </summary>
    public static string HashToken(string rawToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken)) throw new ArgumentException("Token is required.", nameof(rawToken));

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes);
    }
}
