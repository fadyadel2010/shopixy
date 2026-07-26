using Microsoft.AspNetCore.DataProtection;
using PointOfSale.API.Services.Storefront;

namespace PointOfSale.Tests.Storefront;

public sealed class StorefrontThemePreviewTokenServiceTests
{
    [Fact]
    public void Token_IsBoundToTenantAndSupportedTheme()
    {
        var tenantId = Guid.NewGuid();
        var service = new StorefrontThemePreviewTokenService(
            new EphemeralDataProtectionProvider(),
            new FixedTimeProvider(new DateTimeOffset(2026, 6, 9, 12, 0, 0, TimeSpan.Zero)));

        var preview = service.Create(tenantId, "fashion");

        Assert.True(service.TryValidate(preview.Token, tenantId, out var themeId));
        Assert.Equal("fashion", themeId);
        Assert.False(service.TryValidate(preview.Token, Guid.NewGuid(), out _));
        Assert.Throws<ArgumentException>(() => service.Create(tenantId, "spoofed-theme"));
    }

    [Fact]
    public void Token_ExpiresAndCannotBecomePersistentPublicAuthority()
    {
        var tenantId = Guid.NewGuid();
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 6, 9, 12, 0, 0, TimeSpan.Zero));
        var service = new StorefrontThemePreviewTokenService(
            new EphemeralDataProtectionProvider(),
            clock);

        var preview = service.Create(tenantId, "minimal");
        clock.UtcNow = clock.UtcNow.AddMinutes(11);

        Assert.False(service.TryValidate(preview.Token, tenantId, out _));
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
