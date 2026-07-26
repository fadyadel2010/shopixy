using System.Security.Cryptography;
using System.Text;

namespace PointOfSale.Core.Entities.Affiliate;

public class AffiliatePasswordResetToken : BaseEntity
{
    public Guid AffiliateUserId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? ConsumedAtUtc { get; private set; }

    public AffiliateUser AffiliateUser { get; private set; } = null!;

    private AffiliatePasswordResetToken() { }

    public static AffiliatePasswordResetToken Create(Guid affiliateUserId, string rawToken, DateTime expiresAtUtc)
    {
        if (affiliateUserId == Guid.Empty) throw new ArgumentException("Affiliate user id is required.", nameof(affiliateUserId));

        return new AffiliatePasswordResetToken
        {
            AffiliateUserId = affiliateUserId,
            TokenHash = HashToken(rawToken),
            ExpiresAtUtc = expiresAtUtc,
            CreatedAt = DateTime.UtcNow
        };
    }

    public bool CanConsume(DateTime nowUtc) => ConsumedAtUtc == null && ExpiresAtUtc > nowUtc;

    public void Consume(DateTime nowUtc)
    {
        if (!CanConsume(nowUtc)) throw new InvalidOperationException("Affiliate password reset token is not consumable.");

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
