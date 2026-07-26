using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace PointOfSale.API.Services.Cms;

public interface ICmsPagePreviewTokenService
{
    CmsPagePreviewTokenResult CreatePreviewToken(
        Guid tenantId,
        Guid pageGroupId,
        string slug,
        int pageVersion);

    bool TryValidatePreviewToken(
        string token,
        Guid tenantId,
        string slug,
        out Guid pageGroupId,
        out int pageVersion);
}

public sealed record CmsPagePreviewTokenResult(
    string Token,
    DateTime ExpiresAtUtc);

internal sealed class CmsPagePreviewTokenPayload
{
    public Guid TenantId { get; set; }
    public Guid PageGroupId { get; set; }
    public string Slug { get; set; } = string.Empty;
    public int PageVersion { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}

public sealed class CmsPagePreviewTokenService : ICmsPagePreviewTokenService
{
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(15);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IDataProtector _protector;
    private readonly ILogger<CmsPagePreviewTokenService> _logger;

    public CmsPagePreviewTokenService(
        IDataProtectionProvider dataProtectionProvider,
        ILogger<CmsPagePreviewTokenService> logger)
    {
        _protector = dataProtectionProvider.CreateProtector("CmsPagePreviewTokenService.v1");
        _logger = logger;
    }

    public CmsPagePreviewTokenResult CreatePreviewToken(
        Guid tenantId,
        Guid pageGroupId,
        string slug,
        int pageVersion)
    {
        if (pageVersion < 1)
        {
            throw new InvalidOperationException("CMS preview version must be greater than zero.");
        }

        var normalizedSlug = CmsPageReadService.NormalizeSlug(slug);
        var expiresAtUtc = DateTime.UtcNow.Add(DefaultLifetime);
        var payload = new CmsPagePreviewTokenPayload
        {
            TenantId = tenantId,
            PageGroupId = pageGroupId,
            Slug = normalizedSlug,
            PageVersion = pageVersion,
            ExpiresAtUtc = expiresAtUtc
        };

        var protectedPayload = _protector.Protect(JsonSerializer.Serialize(payload, JsonOptions));
        return new CmsPagePreviewTokenResult(protectedPayload, expiresAtUtc);
    }

    public bool TryValidatePreviewToken(
        string token,
        Guid tenantId,
        string slug,
        out Guid pageGroupId,
        out int pageVersion)
    {
        pageGroupId = Guid.Empty;
        pageVersion = 0;

        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        try
        {
            var unprotected = _protector.Unprotect(token);
            var payload = JsonSerializer.Deserialize<CmsPagePreviewTokenPayload>(unprotected, JsonOptions);
            if (payload == null)
            {
                return false;
            }

            if (payload.ExpiresAtUtc <= DateTime.UtcNow)
            {
                return false;
            }

            var normalizedSlug = CmsPageReadService.NormalizeSlug(slug);
            if (payload.TenantId != tenantId ||
                payload.PageVersion < 1 ||
                !string.Equals(payload.Slug, normalizedSlug, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            pageGroupId = payload.PageGroupId;
            pageVersion = payload.PageVersion;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invalid CMS preview token.");
            return false;
        }
    }
}
