using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.WhatsApp.Services;

namespace PointOfSale.Tests.Infrastructure;

public sealed class WhatsAppTemplateResolverTests
{
    [Fact]
    public async Task ResolveOtpAsync_BrandsSystemTemplateWithStorefrontName()
    {
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantId);
        dbContext.StoreConfigs.Add(new StoreConfig
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            StoreName = "AhramStore"
        });
        await dbContext.SaveChangesAsync();

        var resolver = new WhatsAppTemplateResolver(
            dbContext,
            NullLogger<WhatsAppTemplateResolver>.Instance);

        var message = await resolver.ResolveOtpAsync(tenantId, "123456");

        Assert.Contains("AhramStore verification code", message, StringComparison.Ordinal);
        Assert.Contains("*123456*", message, StringComparison.Ordinal);
        Assert.Contains("expires in 5 minutes", message, StringComparison.Ordinal);
        Assert.DoesNotContain("{{", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveOtpAsync_UsesTenantNameWhenStorefrontNameIsUnavailable()
    {
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantId);
        dbContext.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Fallback Store"
        });
        await dbContext.SaveChangesAsync();

        var resolver = new WhatsAppTemplateResolver(
            dbContext,
            NullLogger<WhatsAppTemplateResolver>.Instance);

        var message = await resolver.ResolveOtpAsync(tenantId, "654321", "ar");

        Assert.Contains("رمز التحقق من Fallback Store", message, StringComparison.Ordinal);
        Assert.Contains("*654321*", message, StringComparison.Ordinal);
        Assert.DoesNotContain("{{", message, StringComparison.Ordinal);
    }

    private static ApplicationDbContext CreateDbContext(Guid tenantId) =>
        new(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new TestTenantContext(tenantId));
}
