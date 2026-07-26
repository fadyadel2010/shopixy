namespace PointOfSale.Infrastructure.Consumers.WhatsApp;

/// <summary>
/// Exception thrown when a WhatsApp OTP request fails due to a permanent validation failure
/// that should not be retried (e.g., missing TenantId, feature gate disabled, no active account).
/// This exception is explicitly excluded from MassTransit retry policies.
/// </summary>
public class WhatsAppOtpPermanentFailureException : Exception
{
    public WhatsAppOtpPermanentFailureException(string message) : base(message)
    {
    }

    public WhatsAppOtpPermanentFailureException(string message, Exception innerException) 
        : base(message, innerException)
    {
    }
}
