namespace PointOfSale.Core.Entities.Affiliate;

public class AffiliateRefreshTokenReplayEntry : BaseEntity
{
    public Guid AffiliateUserId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTime RotatedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? ReplayedAtUtc { get; private set; }

    public AffiliateUser AffiliateUser { get; private set; } = null!;

    private AffiliateRefreshTokenReplayEntry() { }

    public static AffiliateRefreshTokenReplayEntry RecordRotation(
        Guid affiliateUserId,
        string tokenHash,
        DateTime rotatedAtUtc,
        DateTime expiresAtUtc)
    {
        if (affiliateUserId == Guid.Empty)
            throw new ArgumentException("Affiliate user id is required.", nameof(affiliateUserId));
        if (string.IsNullOrWhiteSpace(tokenHash))
            throw new ArgumentException("Refresh token hash is required.", nameof(tokenHash));
        if (expiresAtUtc <= rotatedAtUtc)
            throw new ArgumentException("Refresh token expiry must be after rotation time.", nameof(expiresAtUtc));

        return new AffiliateRefreshTokenReplayEntry
        {
            Id = Guid.NewGuid(),
            AffiliateUserId = affiliateUserId,
            TokenHash = tokenHash,
            RotatedAtUtc = rotatedAtUtc,
            ExpiresAtUtc = expiresAtUtc,
            CreatedAt = rotatedAtUtc
        };
    }

    public bool IsReplayCandidate(DateTime nowUtc) => ExpiresAtUtc > nowUtc;

    public void MarkReplayed(DateTime nowUtc)
    {
        ReplayedAtUtc ??= nowUtc;
        UpdatedAt = nowUtc;
    }
}
