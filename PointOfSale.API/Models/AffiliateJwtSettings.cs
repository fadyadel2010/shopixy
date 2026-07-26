namespace PointOfSale.API.Models;

/// <summary>
/// JWT settings SPECIFICALLY for Affiliate Portal authentication.
///
/// ARCHITECTURAL RULES (Y1 Audit Fix):
/// - This is COMPLETELY SEPARATE from POS JwtSettings and StorefrontJwtSettings
/// - Different secret, issuer, and audience
/// - Affiliate JWTs CANNOT be used for POS or Storefront APIs
/// - POS/Storefront JWTs CANNOT be used for Affiliate APIs
///
/// SECURITY: Cryptographic boundary isolation — even if a POS token is leaked,
/// it cannot be used against Affiliate endpoints because the signing key differs.
/// </summary>
public class AffiliateJwtSettings
{
    public const string SectionName = "AffiliateJwt";

    /// <summary>
    /// Secret key for signing Affiliate JWTs.
    /// MUST be different from POS and Storefront JWT secrets.
    /// Minimum 32 characters.
    /// </summary>
    public string Secret { get; set; } = string.Empty;

    /// <summary>
    /// Issuer claim for Affiliate JWTs.
    /// Should be different from POS/Storefront issuers (e.g., "AffiliateAPI").
    /// </summary>
    public string Issuer { get; set; } = "AffiliateAPI";

    /// <summary>
    /// Audience claim for Affiliate JWTs.
    /// Should be different from POS/Storefront audiences (e.g., "AffiliatePortal").
    /// </summary>
    public string Audience { get; set; } = "AffiliatePortal";

    /// <summary>
    /// Access token expiration in minutes.
    /// </summary>
    public int AccessTokenExpirationMinutes { get; set; } = 60;

    /// <summary>
    /// Refresh token expiration in days.
    /// </summary>
    public int RefreshTokenExpirationDays { get; set; } = 7;
}
