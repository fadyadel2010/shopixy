namespace PointOfSale.Core.SuperAdmin.Models;

public sealed class SuperAdminUser
{
    private SuperAdminUser()
    {
    }

    public Guid Id { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public bool IsSystem { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? LastLoginAt { get; private set; }
    public string? RefreshTokenHash { get; private set; }
    public DateTime? RefreshTokenExpiresAtUtc { get; private set; }
    public int TokenVersion { get; private set; }

    public static SuperAdminUser Create(
        string email,
        string passwordHash,
        bool isSystem,
        DateTime utcNow)
    {
        return new SuperAdminUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = passwordHash,
            IsSystem = isSystem,
            CreatedAt = utcNow
        };
    }

    public void RecordLogin(DateTime utcNow)
    {
        LastLoginAt = utcNow;
    }

    public void UpdatePasswordHash(string passwordHash)
    {
        PasswordHash = passwordHash;
    }

    public void SetRefreshToken(string refreshTokenHash, DateTime expiresAtUtc)
    {
        RefreshTokenHash = refreshTokenHash;
        RefreshTokenExpiresAtUtc = expiresAtUtc;
        TokenVersion++;
    }

    public void ClearRefreshToken()
    {
        RefreshTokenHash = null;
        RefreshTokenExpiresAtUtc = null;
        // Increment version on revocation so any outstanding tokens are invalidated
        TokenVersion++;
    }
}
