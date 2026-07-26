using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace PointOfSale.API.Services.Payments.Paymob;

public interface IPaymobSensitiveArtifactProtector
{
    string ProtectPaymentSession(string paymentKey, string iframeUrl);

    PaymobProtectedPaymentSession UnprotectPaymentSession(string protectedSession);

    string ProtectWebhookPayload(string rawPayload);

    string UnprotectWebhookPayload(string protectedPayload);

    string DigestHmacSignature(string signature);
}

public sealed record PaymobProtectedPaymentSession(string PaymentKey, string IframeUrl);

public sealed class PaymobSensitiveArtifactProtector : IPaymobSensitiveArtifactProtector
{
    public const string PaymentSessionPrefix = "paymob-session:v1:";
    public const string WebhookPayloadPrefix = "paymob-webhook:v1:";
    public const string SignatureDigestPrefix = "sha256:";

    private const string PaymentSessionPurpose = "PointOfSale.Paymob.PaymentSession.v1";
    private const string WebhookPayloadPurpose = "PointOfSale.Paymob.WebhookPayload.v1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDataProtector _paymentSessionProtector;
    private readonly IDataProtector _webhookPayloadProtector;

    public PaymobSensitiveArtifactProtector(IDataProtectionProvider dataProtectionProvider)
    {
        ArgumentNullException.ThrowIfNull(dataProtectionProvider);
        _paymentSessionProtector = dataProtectionProvider.CreateProtector(PaymentSessionPurpose);
        _webhookPayloadProtector = dataProtectionProvider.CreateProtector(WebhookPayloadPurpose);
    }

    public string ProtectPaymentSession(string paymentKey, string iframeUrl)
    {
        if (string.IsNullOrWhiteSpace(paymentKey))
        {
            throw new ArgumentException("Paymob payment key is required.", nameof(paymentKey));
        }

        if (string.IsNullOrWhiteSpace(iframeUrl))
        {
            throw new ArgumentException("Paymob iframe URL is required.", nameof(iframeUrl));
        }

        var serialized = JsonSerializer.Serialize(
            new PaymobProtectedPaymentSession(paymentKey.Trim(), iframeUrl.Trim()),
            JsonOptions);
        return PaymentSessionPrefix + _paymentSessionProtector.Protect(serialized);
    }

    public PaymobProtectedPaymentSession UnprotectPaymentSession(string protectedSession)
    {
        var protectedValue = RequireEnvelope(
            protectedSession,
            PaymentSessionPrefix,
            "Paymob payment session");
        var serialized = _paymentSessionProtector.Unprotect(protectedValue);
        var session = JsonSerializer.Deserialize<PaymobProtectedPaymentSession>(serialized, JsonOptions)
            ?? throw new CryptographicException("The protected Paymob payment session is invalid.");

        if (string.IsNullOrWhiteSpace(session.PaymentKey) || string.IsNullOrWhiteSpace(session.IframeUrl))
        {
            throw new CryptographicException("The protected Paymob payment session is incomplete.");
        }

        return new PaymobProtectedPaymentSession(session.PaymentKey.Trim(), session.IframeUrl.Trim());
    }

    public string ProtectWebhookPayload(string rawPayload)
    {
        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            throw new ArgumentException("Paymob webhook payload is required.", nameof(rawPayload));
        }

        return WebhookPayloadPrefix + _webhookPayloadProtector.Protect(rawPayload);
    }

    public string UnprotectWebhookPayload(string protectedPayload)
    {
        var protectedValue = RequireEnvelope(
            protectedPayload,
            WebhookPayloadPrefix,
            "Paymob webhook payload");
        return _webhookPayloadProtector.Unprotect(protectedValue);
    }

    public string DigestHmacSignature(string signature)
    {
        if (string.IsNullOrWhiteSpace(signature))
        {
            throw new ArgumentException("Paymob HMAC signature is required.", nameof(signature));
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(signature));
        return SignatureDigestPrefix + Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string RequireEnvelope(string value, string prefix, string artifactName)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new CryptographicException($"{artifactName} is not stored in the required protected envelope.");
        }

        return value[prefix.Length..];
    }
}
