using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace PointOfSale.API.Services.MarketplaceCms;

public interface IMarketplaceCmsPagePreviewTokenService
{
    MarketplaceCmsPagePreviewTokenResult CreatePreviewToken(Guid pageGroupId, string slug, int pageVersion);

    bool TryValidatePreviewToken(
        string token,
        string slug,
        out Guid pageGroupId,
        out int pageVersion);
}

public sealed record MarketplaceCmsPagePreviewTokenResult(
    string Token,
    DateTime ExpiresAtUtc);

internal sealed class MarketplaceCmsPagePreviewTokenPayload
{
    public Guid PageGroupId { get; set; }
    public string Slug { get; set; } = string.Empty;
    public int PageVersion { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}

public sealed class MarketplaceCmsPagePreviewTokenService : IMarketplaceCmsPagePreviewTokenService
{
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(15);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IDataProtector _protector;
    private readonly ILogger<MarketplaceCmsPagePreviewTokenService> _logger;

    public MarketplaceCmsPagePreviewTokenService(
        IDataProtectionProvider dataProtectionProvider,
        ILogger<MarketplaceCmsPagePreviewTokenService> logger)
    {
        _protector = dataProtectionProvider.CreateProtector("MarketplaceCmsPagePreviewTokenService.v1");
        _logger = logger;
    }

    public MarketplaceCmsPagePreviewTokenResult CreatePreviewToken(Guid pageGroupId, string slug, int pageVersion)
    {
        if (pageVersion < 1)
        {
            throw new InvalidOperationException("Marketplace CMS preview version must be greater than zero.");
        }

        var normalizedSlug = MarketplaceCmsPageReadService.NormalizeSlug(slug);
        var expiresAtUtc = DateTime.UtcNow.Add(DefaultLifetime);
        var payload = new MarketplaceCmsPagePreviewTokenPayload
        {
            PageGroupId = pageGroupId,
            Slug = normalizedSlug,
            PageVersion = pageVersion,
            ExpiresAtUtc = expiresAtUtc
        };

        var protectedPayload = _protector.Protect(JsonSerializer.Serialize(payload, JsonOptions));
        return new MarketplaceCmsPagePreviewTokenResult(protectedPayload, expiresAtUtc);
    }

    public bool TryValidatePreviewToken(
        string token,
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
            var payload = JsonSerializer.Deserialize<MarketplaceCmsPagePreviewTokenPayload>(unprotected, JsonOptions);
            if (payload == null)
            {
                return false;
            }

            if (payload.ExpiresAtUtc <= DateTime.UtcNow)
            {
                return false;
            }

            var normalizedSlug = MarketplaceCmsPageReadService.NormalizeSlug(slug);
            if (payload.PageVersion < 1 ||
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
            _logger.LogWarning(ex, "Invalid marketplace CMS preview token.");
            return false;
        }
    }
}
