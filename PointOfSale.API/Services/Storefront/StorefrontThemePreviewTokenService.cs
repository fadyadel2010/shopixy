using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using PointOfSale.Core.Constants;

namespace PointOfSale.API.Services.Storefront;

public interface IStorefrontThemePreviewTokenService
{
    StorefrontThemePreviewTokenResult Create(Guid tenantId, string themeId);

    bool TryValidate(
        string token,
        Guid expectedTenantId,
        out string themeId);
}

public sealed record StorefrontThemePreviewTokenResult(
    string Token,
    DateTime ExpiresAtUtc);

internal sealed class StorefrontThemePreviewTokenPayload
{
    public Guid TenantId { get; set; }
    public string ThemeId { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
}

public sealed class StorefrontThemePreviewTokenService : IStorefrontThemePreviewTokenService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private readonly IDataProtector _protector;
    private readonly TimeProvider _timeProvider;

    public StorefrontThemePreviewTokenService(
        IDataProtectionProvider dataProtectionProvider,
        TimeProvider timeProvider)
    {
        _protector = dataProtectionProvider.CreateProtector("StorefrontThemePreviewTokenService.v1");
        _timeProvider = timeProvider;
    }

    public StorefrontThemePreviewTokenResult Create(Guid tenantId, string themeId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant is required.", nameof(tenantId));
        }

        if (!StorefrontThemes.IsSupported(themeId))
        {
            throw new ArgumentException("Unsupported storefront theme.", nameof(themeId));
        }

        var expiresAtUtc = _timeProvider.GetUtcNow().UtcDateTime.Add(Lifetime);
        var payload = new StorefrontThemePreviewTokenPayload
        {
            TenantId = tenantId,
            ThemeId = StorefrontThemes.NormalizeOrDefault(themeId),
            ExpiresAtUtc = expiresAtUtc
        };

        var token = _protector.Protect(JsonSerializer.Serialize(payload, JsonOptions));
        return new StorefrontThemePreviewTokenResult(token, expiresAtUtc);
    }

    public bool TryValidate(
        string token,
        Guid expectedTenantId,
        out string themeId)
    {
        themeId = string.Empty;
        if (string.IsNullOrWhiteSpace(token) || expectedTenantId == Guid.Empty)
        {
            return false;
        }

        try
        {
            var json = _protector.Unprotect(token);
            var payload = JsonSerializer.Deserialize<StorefrontThemePreviewTokenPayload>(json, JsonOptions);
            if (payload == null ||
                payload.TenantId != expectedTenantId ||
                payload.ExpiresAtUtc <= _timeProvider.GetUtcNow().UtcDateTime ||
                !StorefrontThemes.IsSupported(payload.ThemeId))
            {
                return false;
            }

            themeId = StorefrontThemes.NormalizeOrDefault(payload.ThemeId);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
