using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.API.Services.MarketplaceCms;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Cms;

public sealed class MarketplaceCmsPagePreviewTokenServiceTests
{
    [Fact]
    public void CreatePreviewToken_AllowsValidationForSameSlug()
    {
        var service = CreateService();
        var pageGroupId = Guid.NewGuid();

        var result = service.CreatePreviewToken(pageGroupId, "about", 4);
        var isValid = service.TryValidatePreviewToken(result.Token, "about", out var validatedPageGroupId, out var validatedVersion);

        Assert.True(isValid);
        Assert.Equal(pageGroupId, validatedPageGroupId);
        Assert.Equal(4, validatedVersion);
        Assert.True(result.ExpiresAtUtc > DateTime.UtcNow);
    }

    [Fact]
    public void TryValidatePreviewToken_RejectsSlugMismatch()
    {
        var service = CreateService();
        var result = service.CreatePreviewToken(Guid.NewGuid(), "privacy", 2);

        var isValid = service.TryValidatePreviewToken(result.Token, "terms", out _, out _);

        Assert.False(isValid);
    }

    [Fact]
    public void TryValidatePreviewToken_RejectsTamperedToken()
    {
        var service = CreateService();
        var result = service.CreatePreviewToken(Guid.NewGuid(), "contact", 3);

        var isValid = service.TryValidatePreviewToken($"{result.Token}tampered", "contact", out _, out _);

        Assert.False(isValid);
    }

    private static MarketplaceCmsPagePreviewTokenService CreateService()
    {
        var provider = TestDataProtectionProviderFactory.CreateProvider();
        return new MarketplaceCmsPagePreviewTokenService(provider, NullLogger<MarketplaceCmsPagePreviewTokenService>.Instance);
    }
}
