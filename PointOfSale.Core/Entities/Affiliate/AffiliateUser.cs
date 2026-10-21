using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities.Affiliate;

/// <summary>
/// AF-10: Independent affiliate identity — decoupled from POS User.
///
/// INVARIANTS:
/// - Exactly ONE AffiliateUser per AffiliateAccount (1:1, enforced DB + domain)
/// - Enrollment checks email globally; new identities use Guid.Empty legacy scope
/// - AffiliateUser ≠ POS User: separate JWT, separate auth boundary
/// - No PII leaks into POS user system
/// - PasswordHash uses ASP.NET Core Identity PasswordHasher (Argon2/PBKDF2)
///
/// KILL LIST:
/// - ❌ Never shares JWT with POS app
/// - ❌ Never inherits from User entity
/// - ❌ No commission logic here
/// </summary>
public class AffiliateUser : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }

    /// <summary>
    /// Email address — used as login credential.
    /// Global login credential. Legacy database index is (TenantId, Email).
    /// </summary>
    public string Email { get; private set; } = string.Empty;

    /// <summary>
    /// Hashed password via IPasswordHasher&lt;AffiliateUser&gt;.
    /// Never stored in plaintext.
    /// </summary>
    public string PasswordHash { get; private set; } = string.Empty;

    /// <summary>
    /// FK → AffiliateAccounts.Id.
    /// UNIQUE — one AffiliateUser per AffiliateAccount.
    /// </summary>
    public Guid AffiliateAccountId { get; private set; }

    /// <summary>
    /// Navigation property to the associated AffiliateAccount.
    /// </summary>
    public AffiliateAccount AffiliateAccount { get; private set; } = null!;

    /// <summary>
    /// Whether the email has been verified.
    /// </summary>
    public bool EmailVerified { get; private set; }

    /// <summary>
    /// Refresh token for session continuity.
    /// </summary>
    public string? RefreshToken { get; set; }

    /// <summary>
    /// Previous refresh token hash retained only to detect replay after rotation.
    /// </summary>
    public string? PreviousRefreshToken { get; private set; }

    /// <summary>
    /// Refresh token expiry.
    /// </summary>
    public DateTime? RefreshTokenExpiryTime { get; set; }

    /// <summary>
    /// Durable all-session cutoff. Tokens issued at or before this instant are stale.
    /// </summary>
    public DateTime? TokensValidAfterUtc { get; private set; }

    // Private ctor for EF Core
    private AffiliateUser() { }

    // ─── Factory ────────────────────────────────────────────────────

    /// <summary>
    /// AF-19: Creates a global affiliate user (tenantId can be Guid.Empty for global affiliates).
    /// </summary>
    public static AffiliateUser Create(
        Guid tenantId, string email, string passwordHash, Guid affiliateAccountId)
    {
        // AF-19: tenantId can be Guid.Empty for global affiliates (no tenant binding)
        if (string.IsNullOrWhiteSpace(email)) throw new ArgumentException("Email is required", nameof(email));
        if (string.IsNullOrWhiteSpace(passwordHash)) throw new ArgumentException("PasswordHash is required", nameof(passwordHash));
        if (affiliateAccountId == Guid.Empty) throw new ArgumentException("AffiliateAccountId is required", nameof(affiliateAccountId));

        return new AffiliateUser
        {
            TenantId = tenantId, // AF-19: Can be Guid.Empty for global affiliates
            Email = email.Trim().ToLowerInvariant(),
            PasswordHash = passwordHash,
            AffiliateAccountId = affiliateAccountId,
            EmailVerified = false,
            CreatedAt = DateTime.UtcNow
        };
    }

    // ─── Mutations ──────────────────────────────────────────────────

    public void VerifyEmail()
    {
        EmailVerified = true;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetPasswordHash(string hash)
    {
        if (string.IsNullOrWhiteSpace(hash)) throw new ArgumentException("Hash is required", nameof(hash));
        PasswordHash = hash;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetRefreshToken(string refreshTokenHash, DateTime expiresAtUtc)
    {
        if (string.IsNullOrWhiteSpace(refreshTokenHash))
            throw new ArgumentException("Refresh token hash is required", nameof(refreshTokenHash));

        PreviousRefreshToken = RefreshToken;
        RefreshToken = refreshTokenHash;
        RefreshTokenExpiryTime = expiresAtUtc;
        UpdatedAt = DateTime.UtcNow;
    }

    public void ClearRefreshToken()
    {
        PreviousRefreshToken = null;
        RefreshToken = null;
        RefreshTokenExpiryTime = null;
        UpdatedAt = DateTime.UtcNow;
    }

    public bool IsPreviousRefreshTokenReplay(string refreshTokenHash) =>
        !string.IsNullOrWhiteSpace(refreshTokenHash) &&
        string.Equals(PreviousRefreshToken, refreshTokenHash, StringComparison.Ordinal);

    public void RevokeSessions(DateTime nowUtc)
    {
        TokensValidAfterUtc = nowUtc;
        ClearRefreshToken();
    }

    // ─── EF Core setters ────────────────────────────────────────────

    public void SetEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) throw new ArgumentException("Email is required", nameof(email));

        Email = email.Trim().ToLowerInvariant();
        EmailVerified = false;
        RevokeSessions(DateTime.UtcNow);
    }
}
