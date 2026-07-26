using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace PointOfSale.API.Security;

public interface IAffiliateAttributionEvidenceTokenService
{
    string Create(
        Guid tenantId,
        Guid affiliateAccountId,
        string referralCode,
        Guid? promotionLinkId);

    bool TryValidate(
        string token,
        Guid expectedTenantId,
        out AffiliateAttributionEvidence evidence);
}

public sealed record AffiliateAttributionEvidence(
    Guid TenantId,
    Guid AffiliateAccountId,
    string ReferralCode,
    Guid? PromotionLinkId,
    DateTime ExpiresAtUtc);

public sealed class AffiliateAttributionEvidenceTokenService : IAffiliateAttributionEvidenceTokenService
{
    private static readonly TimeSpan AttributionWindow = TimeSpan.FromDays(30);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDataProtector _protector;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AffiliateAttributionEvidenceTokenService> _logger;

    public AffiliateAttributionEvidenceTokenService(
        IDataProtectionProvider dataProtectionProvider,
        TimeProvider timeProvider,
        ILogger<AffiliateAttributionEvidenceTokenService> logger)
    {
        _protector = dataProtectionProvider.CreateProtector(
            "AffiliateAttributionEvidenceTokenService.v1");
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public string Create(
        Guid tenantId,
        Guid affiliateAccountId,
        string referralCode,
        Guid? promotionLinkId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant ID is required.", nameof(tenantId));
        }

        if (affiliateAccountId == Guid.Empty)
        {
            throw new ArgumentException("Affiliate account ID is required.", nameof(affiliateAccountId));
        }

        var normalizedCode = NormalizeReferralCode(referralCode);
        if (normalizedCode.Length == 0)
        {
            throw new ArgumentException("Referral code is required.", nameof(referralCode));
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var payload = new AffiliateAttributionEvidence(
            tenantId,
            affiliateAccountId,
            normalizedCode,
            promotionLinkId,
            nowUtc.Add(AttributionWindow));

        return _protector.Protect(JsonSerializer.Serialize(payload, JsonOptions));
    }

    public bool TryValidate(
        string token,
        Guid expectedTenantId,
        out AffiliateAttributionEvidence evidence)
    {
        evidence = default!;
        if (string.IsNullOrWhiteSpace(token) || expectedTenantId == Guid.Empty)
        {
            return false;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<AffiliateAttributionEvidence>(
                _protector.Unprotect(token),
                JsonOptions);
            if (payload == null ||
                payload.TenantId != expectedTenantId ||
                payload.AffiliateAccountId == Guid.Empty ||
                payload.ExpiresAtUtc <= _timeProvider.GetUtcNow().UtcDateTime ||
                !string.Equals(
                    payload.ReferralCode,
                    NormalizeReferralCode(payload.ReferralCode),
                    StringComparison.Ordinal))
            {
                return false;
            }

            evidence = payload;
            return true;
        }
        catch (Exception ex) when (
            ex is CryptographicException or JsonException or FormatException)
        {
            _logger.LogWarning(
                "Rejected invalid affiliate attribution evidence | TenantId: {TenantId}",
                expectedTenantId);
            return false;
        }
    }

    private static string NormalizeReferralCode(string? referralCode) =>
        referralCode?.Trim().ToUpperInvariant() ?? string.Empty;
}
