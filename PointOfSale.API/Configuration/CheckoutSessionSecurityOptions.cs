using System.ComponentModel.DataAnnotations;

namespace PointOfSale.API.Configuration;

public sealed class CheckoutSessionSecurityOptions
{
    public const string SectionName = "CheckoutSession:Security";

    [Range(1, 1440, ErrorMessage = "RecoveryTokenTtlMinutes must be between 1 and 1440 minutes")]
    public int RecoveryTokenTtlMinutes { get; set; } = 20;

    [Range(1, 1440, ErrorMessage = "PaymentTokenTtlMinutes must be between 1 and 1440 minutes")]
    public int PaymentTokenTtlMinutes { get; set; } = 20;

    [Range(1, 1440, ErrorMessage = "ManualWalletUploadTokenTtlMinutes must be between 1 and 1440 minutes")]
    public int ManualWalletUploadTokenTtlMinutes { get; set; } = 20;

    [Range(1, 120, ErrorMessage = "GuestTrackingTokenTtlMinutes must be between 1 and 120 minutes")]
    public int GuestTrackingTokenTtlMinutes { get; set; } = 20;
}
