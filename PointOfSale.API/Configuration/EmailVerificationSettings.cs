namespace PointOfSale.API.Configuration;

/// <summary>
/// Email verification configuration.
/// Phase 8.4: Email Verification & Account Trust
/// </summary>
public class EmailVerificationSettings
{
    public const string SectionName = "EmailVerification";
    
    /// <summary>
    /// Enable/disable email verification system.
    /// When disabled, new users are created as Verified.
    /// </summary>
    public bool Enabled { get; set; } = true;
    
    /// <summary>
    /// Token expiration time in hours.
    /// </summary>
    public int TokenExpirationHours { get; set; } = 24;
    
    /// <summary>
    /// Maximum number of resend requests per hour per user.
    /// </summary>
    public int MaxResendsPerHour { get; set; } = 3;
    
    /// <summary>
    /// Base URL for verification links.
    /// Example: https://pos.myplatform.com
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:5173";
    
    /// <summary>
    /// Email sender name.
    /// </summary>
    public string SenderName { get; set; } = "MyPlatform";
    
    /// <summary>
    /// Email sender address.
    /// </summary>
    public string SenderEmail { get; set; } = "noreply@myplatform.com";
}
