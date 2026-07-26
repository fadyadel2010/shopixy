namespace PointOfSale.API.Services.Admin;

/// <summary>
/// Exception thrown when storefront publishing is blocked due to email verification.
/// Phase 8.4: Email Verification & Account Trust
/// </summary>
public class StorefrontVerificationException : Exception
{
    public StorefrontVerificationException(string message) : base(message)
    {
    }
}
