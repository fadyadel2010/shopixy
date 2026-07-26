using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

namespace PointOfSale.API.Security;

public interface ISmartOfferRedeemTokenService
{
    string CreateToken(Guid tenantId, Guid smartOfferId, Guid couponId, Guid customerId);

    SmartOfferRedeemTokenValidationResult ValidateToken(string token, Guid expectedTenantId, Guid expectedCustomerId);
}

public sealed record SmartOfferRedeemTokenValidationResult(
    bool IsValid,
    bool IsExpired,
    Guid? SmartOfferId,
    Guid? CouponId,
    Guid? CustomerId,
    DateTimeOffset? ExpiresAtUtc)
{
    public static SmartOfferRedeemTokenValidationResult Invalid() =>
        new(false, false, null, null, null, null);

    public static SmartOfferRedeemTokenValidationResult Expired() =>
        new(false, true, null, null, null, null);
}

public sealed class SmartOfferRedeemTokenService : ISmartOfferRedeemTokenService
{
    private const string ProtectorPurpose = "SmartOfferRedeemTokens.v1";
    private const int DefaultTokenLifetimeMinutes = 5;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDataProtector _protector;
    private readonly ILogger<SmartOfferRedeemTokenService> _logger;

    public SmartOfferRedeemTokenService(
        IDataProtectionProvider dataProtectionProvider,
        ILogger<SmartOfferRedeemTokenService> logger)
    {
        _protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);
        _logger = logger;
    }

    public string CreateToken(Guid tenantId, Guid smartOfferId, Guid couponId, Guid customerId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant ID is required.", nameof(tenantId));
        }

        if (smartOfferId == Guid.Empty)
        {
            throw new ArgumentException("Smart offer ID is required.", nameof(smartOfferId));
        }

        if (couponId == Guid.Empty)
        {
            throw new ArgumentException("Coupon ID is required.", nameof(couponId));
        }

        if (customerId == Guid.Empty)
        {
            throw new ArgumentException("Customer ID is required.", nameof(customerId));
        }

        return Protect(new SmartOfferRedeemTokenPayload
        {
            TenantId = tenantId,
            SmartOfferId = smartOfferId,
            CouponId = couponId,
            CustomerId = customerId,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(DefaultTokenLifetimeMinutes)
        });
    }

    public SmartOfferRedeemTokenValidationResult ValidateToken(string token, Guid expectedTenantId, Guid expectedCustomerId)
    {
        if (string.IsNullOrWhiteSpace(token) || expectedTenantId == Guid.Empty || expectedCustomerId == Guid.Empty)
        {
            return SmartOfferRedeemTokenValidationResult.Invalid();
        }

        try
        {
            var protectedBytes = WebEncoders.Base64UrlDecode(token);
            var protectedPayload = Encoding.UTF8.GetString(protectedBytes);
            var json = _protector.Unprotect(protectedPayload);
            var payload = JsonSerializer.Deserialize<SmartOfferRedeemTokenPayload>(json, JsonOptions);

            if (payload == null ||
                payload.TenantId != expectedTenantId ||
                payload.CustomerId != expectedCustomerId ||
                payload.SmartOfferId == Guid.Empty ||
                payload.CouponId == Guid.Empty)
            {
                return SmartOfferRedeemTokenValidationResult.Invalid();
            }

            if (payload.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            {
                return SmartOfferRedeemTokenValidationResult.Expired();
            }

            return new SmartOfferRedeemTokenValidationResult(
                true,
                false,
                payload.SmartOfferId,
                payload.CouponId,
                payload.CustomerId,
                payload.ExpiresAtUtc);
        }
        catch (CryptographicException)
        {
            return SmartOfferRedeemTokenValidationResult.Invalid();
        }
        catch (FormatException)
        {
            return SmartOfferRedeemTokenValidationResult.Invalid();
        }
        catch (JsonException)
        {
            return SmartOfferRedeemTokenValidationResult.Invalid();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            _logger.LogWarning(
                ex,
                "Unexpected exception during smart-offer token validation | TenantId: {TenantId} | CustomerId: {CustomerId}",
                expectedTenantId,
                expectedCustomerId);
            return SmartOfferRedeemTokenValidationResult.Invalid();
        }
    }

    private string Protect(SmartOfferRedeemTokenPayload payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var protectedPayload = _protector.Protect(json);
        return WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(protectedPayload));
    }

    private sealed class SmartOfferRedeemTokenPayload
    {
        public Guid TenantId { get; set; }

        public Guid SmartOfferId { get; set; }

        public Guid CouponId { get; set; }

        public Guid CustomerId { get; set; }

        public DateTimeOffset ExpiresAtUtc { get; set; }
    }
}
