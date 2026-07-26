namespace PointOfSale.API.Models;

/// <summary>
/// JWT settings for global CommerceHub customer authentication.
/// Separate from POS, Super Admin, Affiliate, and tenant-scoped Storefront JWTs.
/// </summary>
public class CustomerJwtSettings
{
    public const string SectionName = "CustomerJwt";

    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = "CommerceHubCustomerAPI";
    public string Audience { get; set; } = "CommerceHubCustomerClient";
    public int AccessTokenExpirationMinutes { get; set; } = 60;
}
