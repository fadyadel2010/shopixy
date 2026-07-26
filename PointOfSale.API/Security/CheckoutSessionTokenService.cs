using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.Core.Entities;

namespace PointOfSale.API.Security;

public interface ICheckoutSessionTokenService
{
    string CreateRecoveryToken(CheckoutSession session);

    string CreatePaymentToken(Guid tenantId, Guid orderId);

    string CreateGuestTrackingToken(Guid tenantId, Guid orderId, string sessionId);

    string CreateManualWalletUploadToken(CheckoutSession session);

    CheckoutSessionTokenValidationResult ValidateRecoveryToken(string token, Guid expectedTenantId);

    CheckoutSessionTokenValidationResult ValidatePaymentToken(string token, Guid expectedTenantId);

    CheckoutSessionTokenValidationResult ValidateGuestTrackingToken(string token, Guid expectedTenantId);

    CheckoutSessionTokenValidationResult ValidateManualWalletUploadToken(string token, Guid expectedTenantId);
}

public sealed record CheckoutSessionTokenValidationResult(
    bool IsValid,
    bool IsExpired,
    string? Subject,
    string? Nonce,
    DateTimeOffset? ExpiresAtUtc,
    string Purpose)
{
    public static CheckoutSessionTokenValidationResult Invalid(string purpose) =>
        new(false, false, null, null, null, purpose);

    public static CheckoutSessionTokenValidationResult Expired(string purpose) =>
        new(false, true, null, null, null, purpose);
}

public sealed class CheckoutSessionTokenService : ICheckoutSessionTokenService
{
    private const string RecoveryPurpose = "checkout-recovery";
    private const string PaymentPurpose = "checkout-payment";
    private const string GuestTrackingPurpose = "guest-order-tracking";
    private const string ManualWalletUploadPurpose = "manual-wallet-upload";
    private const string ProtectorPurpose = "CheckoutSessionTokens.v1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDataProtector _protector;
    private readonly CheckoutSessionSecurityOptions _options;
    private readonly ILogger<CheckoutSessionTokenService> _logger;

    public CheckoutSessionTokenService(
        IDataProtectionProvider dataProtectionProvider,
        IOptions<CheckoutSessionSecurityOptions> options,
        ILogger<CheckoutSessionTokenService> logger)
    {
        _protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);
        _options = options.Value;
        _logger = logger;
    }

    public string CreateRecoveryToken(CheckoutSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return Protect(new CheckoutSessionTokenPayload
        {
            TenantId = session.TenantId,
            Subject = session.Id.ToString("N"),
            Purpose = RecoveryPurpose,
            Nonce = session.RecoveryToken,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(GetPositiveMinutes(_options.RecoveryTokenTtlMinutes))
        });
    }

    public string CreatePaymentToken(Guid tenantId, Guid orderId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant ID is required.", nameof(tenantId));
        }

        if (orderId == Guid.Empty)
        {
            throw new ArgumentException("Order ID is required.", nameof(orderId));
        }

        // SECURITY: Bind token to order-specific identifier (order ID) instead of browser session.
        // This prevents token reuse across multiple orders from the same browser session.
        return Protect(new CheckoutSessionTokenPayload
        {
            TenantId = tenantId,
            Subject = orderId.ToString("N"),
            Purpose = PaymentPurpose,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(GetPositiveMinutes(_options.PaymentTokenTtlMinutes))
        });
    }

    public string CreateGuestTrackingToken(Guid tenantId, Guid orderId, string sessionId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant ID is required.", nameof(tenantId));
        }

        if (orderId == Guid.Empty)
        {
            throw new ArgumentException("Order ID is required.", nameof(orderId));
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("Session ID is required.", nameof(sessionId));
        }

        return Protect(new CheckoutSessionTokenPayload
        {
            TenantId = tenantId,
            Subject = orderId.ToString("N"),
            Purpose = GuestTrackingPurpose,
            Nonce = sessionId.Trim(),
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(GetPositiveMinutes(_options.GuestTrackingTokenTtlMinutes))
        });
    }

    public string CreateManualWalletUploadToken(CheckoutSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return Protect(new CheckoutSessionTokenPayload
        {
            TenantId = session.TenantId,
            Subject = session.Id.ToString("N"),
            Purpose = ManualWalletUploadPurpose,
            Nonce = session.RecoveryToken,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(GetPositiveMinutes(_options.ManualWalletUploadTokenTtlMinutes))
        });
    }

    public CheckoutSessionTokenValidationResult ValidateRecoveryToken(string token, Guid expectedTenantId)
    {
        return Validate(token, expectedTenantId, RecoveryPurpose);
    }

    public CheckoutSessionTokenValidationResult ValidatePaymentToken(string token, Guid expectedTenantId)
    {
        return Validate(token, expectedTenantId, PaymentPurpose);
    }

    public CheckoutSessionTokenValidationResult ValidateGuestTrackingToken(string token, Guid expectedTenantId)
    {
        return Validate(token, expectedTenantId, GuestTrackingPurpose);
    }

    public CheckoutSessionTokenValidationResult ValidateManualWalletUploadToken(string token, Guid expectedTenantId)
    {
        return Validate(token, expectedTenantId, ManualWalletUploadPurpose);
    }

    private CheckoutSessionTokenValidationResult Validate(string token, Guid expectedTenantId, string expectedPurpose)
    {
        if (string.IsNullOrWhiteSpace(token) || expectedTenantId == Guid.Empty)
        {
            return CheckoutSessionTokenValidationResult.Invalid(expectedPurpose);
        }

        try
        {
            var protectedBytes = WebEncoders.Base64UrlDecode(token);
            var protectedPayload = Encoding.UTF8.GetString(protectedBytes);
            var json = _protector.Unprotect(protectedPayload);
            var payload = JsonSerializer.Deserialize<CheckoutSessionTokenPayload>(json, JsonOptions);

            if (payload == null ||
                payload.TenantId != expectedTenantId ||
                !string.Equals(payload.Purpose, expectedPurpose, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(payload.Subject))
            {
                return CheckoutSessionTokenValidationResult.Invalid(expectedPurpose);
            }

            if (payload.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            {
                return CheckoutSessionTokenValidationResult.Expired(expectedPurpose);
            }

            return new CheckoutSessionTokenValidationResult(
                true,
                false,
                payload.Subject,
                payload.Nonce,
                payload.ExpiresAtUtc,
                payload.Purpose);
        }
        catch (CryptographicException)
        {
            // Expected: tampered or invalid token
            return CheckoutSessionTokenValidationResult.Invalid(expectedPurpose);
        }
        catch (FormatException)
        {
            // Expected: malformed base64 or JSON
            return CheckoutSessionTokenValidationResult.Invalid(expectedPurpose);
        }
        catch (JsonException)
        {
            // Expected: invalid JSON structure
            return CheckoutSessionTokenValidationResult.Invalid(expectedPurpose);
        }
        catch (Exception ex) when (ex is OutOfMemoryException or StackOverflowException)
        {
            // Critical runtime exceptions - rethrow
            throw;
        }
        catch (Exception ex)
        {
            // Unexpected exception - log and fail closed
            _logger.LogWarning(ex, 
                "Unexpected exception during token validation | Purpose: {Purpose} | TenantId: {TenantId}", 
                expectedPurpose, 
                expectedTenantId);
            return CheckoutSessionTokenValidationResult.Invalid(expectedPurpose);
        }
    }

    private string Protect(CheckoutSessionTokenPayload payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var protectedPayload = _protector.Protect(json);
        return WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(protectedPayload));
    }

    private static int GetPositiveMinutes(int configuredMinutes) => configuredMinutes > 0 ? configuredMinutes : 20;

    private sealed class CheckoutSessionTokenPayload
    {
        public Guid TenantId { get; set; }

        public string Subject { get; set; } = string.Empty;

        public string Purpose { get; set; } = string.Empty;

        public string? Nonce { get; set; }

        public DateTimeOffset ExpiresAtUtc { get; set; }
    }
}
