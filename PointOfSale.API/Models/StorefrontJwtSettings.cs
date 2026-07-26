namespace PointOfSale.API.Models;

/// <summary>
/// JWT settings SPECIFICALLY for Storefront customer authentication.
/// 
/// ARCHITECTURAL RULES:
/// - This is COMPLETELY SEPARATE from POS JwtSettings
/// - Different secret, issuer, and audience
/// - Storefront JWTs CANNOT be used for POS APIs
/// - POS JWTs CANNOT be used for Storefront APIs
/// 
/// Phase: 10.3.2 - Storefront Login / Register
/// </summary>
public class StorefrontJwtSettings
{
    public const string SectionName = "StorefrontJwt";

    /// <summary>
    /// Secret key for signing Storefront JWTs.
    /// MUST be different from POS JWT secret.
    /// Minimum 32 characters.
    /// </summary>
    public string Secret { get; set; } = string.Empty;

    /// <summary>
    /// Issuer claim for Storefront JWTs.
    /// Should be different from POS issuer (e.g., "StorefrontAPI").
    /// </summary>
    public string Issuer { get; set; } = "StorefrontAPI";

    /// <summary>
    /// Audience claim for Storefront JWTs.
    /// Should be different from POS audience (e.g., "StorefrontClient").
    /// </summary>
    public string Audience { get; set; } = "StorefrontClient";

    /// <summary>
    /// Access token expiration in minutes.
    /// Shorter than POS for security (customers on public networks).
    /// </summary>
    public int AccessTokenExpirationMinutes { get; set; } = 30;

    /// <summary>
    /// Refresh token expiration in days.
    /// Shorter than POS for security.
    /// </summary>
    public int RefreshTokenExpirationDays { get; set; } = 7;
}
