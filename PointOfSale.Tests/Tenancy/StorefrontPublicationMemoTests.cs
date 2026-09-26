using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Tenancy;

namespace PointOfSale.Tests.Tenancy;

public sealed class StorefrontPublicationMemoTests
{
    [Fact]
    public async Task RepeatedPublicationChecks_ShareOneQueryWithinScope()
    {
        using var fixture = new TenantMemoTestFixture();
        Guid tenantId;
        using (var seed = fixture.CreateContext())
        {
            tenantId = TenantMemoTestFixture.SeedTenant(seed, "published-store").Id;
            SeedConfig(seed, tenantId, "live");
        }

        using var request = fixture.CreateContext();
        _ = await TenantAuthorityMemo.GetTenantByIdAsync(request, tenantId);
        var service = new StorefrontPublicationStatusService(
            request,
            NullLogger<StorefrontPublicationStatusService>.Instance);
        fixture.Counter.Reset();

        Assert.True(await service.IsPublishedAsync(tenantId));
        Assert.True(await service.IsPublishedAsync(tenantId));
        Assert.Equal(1, fixture.Counter.Count);
    }

    [Fact]
    public async Task PublicationMutation_IsVisibleToNextScope()
    {
        using var fixture = new TenantMemoTestFixture();
        Guid tenantId;
        using (var seed = fixture.CreateContext())
        {
            tenantId = TenantMemoTestFixture.SeedTenant(seed, "publication-change").Id;
            SeedConfig(seed, tenantId, "live");
        }

        using (var firstRequest = fixture.CreateContext())
        {
            var first = new StorefrontPublicationStatusService(
                firstRequest,
                NullLogger<StorefrontPublicationStatusService>.Instance);
            Assert.True(await first.IsPublishedAsync(tenantId));
        }

        using (var mutation = fixture.CreateContext())
        {
            var config = mutation.StoreConfigs.IgnoreQueryFilters().Single(item => item.TenantId == tenantId);
            config.StorefrontStatus = "disabled";
            mutation.SaveChanges();
        }

        using var nextRequest = fixture.CreateContext();
        var next = new StorefrontPublicationStatusService(
            nextRequest,
            NullLogger<StorefrontPublicationStatusService>.Instance);
        Assert.False(await next.IsPublishedAsync(tenantId));
    }

    private static void SeedConfig(
        PointOfSale.Infrastructure.Data.ApplicationDbContext dbContext,
        Guid tenantId,
        string status)
    {
        dbContext.StoreConfigs.Add(new StoreConfig
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            StoreName = $"Store {tenantId:N}",
            StorefrontStatus = status
        });
        dbContext.SaveChanges();
        dbContext.ChangeTracker.Clear();
    }
}
