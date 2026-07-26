namespace PointOfSale.Core.Events.WhatsApp;

/// <summary>
/// Triggered when phone verification is requested AND tenant has WhatsApp OTP enabled.
/// Falls back to SMS/Email if WhatsApp is not available.
/// </summary>
public class WhatsAppOtpRequestedEvent : IntegrationEvent, INonAuthoritativeIntegrationEvent, ISensitiveIntegrationEvent
{
    public Guid CustomerId { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string OtpCode { get; set; } = string.Empty;
    public int ExpiryMinutes { get; set; } = 5;
    public string Purpose { get; set; } = string.Empty; // PHONE_VERIFICATION, LOGIN_2FA
}
