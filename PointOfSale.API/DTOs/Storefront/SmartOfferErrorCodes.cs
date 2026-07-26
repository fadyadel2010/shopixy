namespace PointOfSale.API.DTOs.Storefront;

/// <summary>
/// Error codes specific to Smart Offers functionality.
/// These codes are returned to the frontend for localized message display.
/// </summary>
public static class SmartOfferErrorCodes
{
    /// <summary>Smart-offer generated coupons must be redeemed via server-issued token</summary>
    public const string SmartOfferTokenRequired = "SMART_OFFER_TOKEN_REQUIRED";

    /// <summary>Smart-offer redemption token is invalid or was tampered with</summary>
    public const string SmartOfferTokenInvalid = "SMART_OFFER_TOKEN_INVALID";

    /// <summary>Smart-offer redemption token is expired</summary>
    public const string SmartOfferTokenExpired = "SMART_OFFER_TOKEN_EXPIRED";
}
