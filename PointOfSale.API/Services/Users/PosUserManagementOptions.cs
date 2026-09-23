namespace PointOfSale.API.Services.Users;

/// <summary>
/// Tunables for the tenant POS user lifecycle. Invitation expiry defaults to
/// 24 hours and password-reset expiry to 30 minutes per blueprint §24
/// recommendations. FrontendBaseUrl is the absolute POS web origin used to
/// build acceptance and recovery links.
/// </summary>
public sealed class PosUserManagementOptions
{
    public const string SectionName = "PosUserManagement";

    public int InvitationExpiryHours { get; set; } = 24;

    public int PasswordResetExpiryMinutes { get; set; } = 30;

    public string FrontendBaseUrl { get; set; } = string.Empty;
}
