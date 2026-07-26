using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.API.Services.Cms;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Cms;

public sealed class CmsPagePreviewTokenServiceTests
{
    [Fact]
    public void CreatePreviewToken_ValidatesForMatchingTenantAndSlug()
    {
        var service = CreateService();
        var tenantId = Guid.NewGuid();
        var pageGroupId = Guid.NewGuid();

        var token = service.CreatePreviewToken(tenantId, pageGroupId, "about-us", 7);
        var isValid = service.TryValidatePreviewToken(
            token.Token,
            tenantId,
            "about-us",
            out var resolvedPageGroupId,
            out var resolvedVersion);

        Assert.True(isValid);
        Assert.Equal(pageGroupId, resolvedPageGroupId);
        Assert.Equal(7, resolvedVersion);
        Assert.True(token.ExpiresAtUtc > DateTime.UtcNow);
    }

    [Fact]
    public void TryValidatePreviewToken_RejectsMismatchedSlugOrTenant()
    {
        var service = CreateService();
        var tenantId = Guid.NewGuid();
        var token = service.CreatePreviewToken(tenantId, Guid.NewGuid(), "privacy-policy", 3);

        var wrongSlug = service.TryValidatePreviewToken(
            token.Token,
            tenantId,
            "terms",
            out _,
            out _);

        var wrongTenant = service.TryValidatePreviewToken(
            token.Token,
            Guid.NewGuid(),
            "privacy-policy",
            out _,
            out _);

        Assert.False(wrongSlug);
        Assert.False(wrongTenant);
    }

    private static CmsPagePreviewTokenService CreateService()
    {
        var provider = TestDataProtectionProviderFactory.CreateProvider();
        return new CmsPagePreviewTokenService(provider, NullLogger<CmsPagePreviewTokenService>.Instance);
    }
}
