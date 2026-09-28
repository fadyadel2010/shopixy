using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.Infrastructure;

public sealed class SharedInMemoryDatabaseRootTests
{
    [Fact]
    public void ManyIsolatedDatabases_ReuseCompatibleEfInternalServiceProvider()
    {
        for (var index = 0; index < 25; index++)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(
                    $"shared-root-provider-regression-{index}-{Guid.NewGuid():N}",
                    SharedInMemoryDatabaseRoot.Instance)
                .Options;

            using var context = new ApplicationDbContext(options);
            Assert.NotNull(context.Model);
        }
    }

    [Fact]
    public async Task UniqueDatabaseNames_RemainStateIsolatedWithSharedRoot()
    {
        var firstDatabaseName = $"shared-root-isolation-a-{Guid.NewGuid():N}";
        var secondDatabaseName = $"shared-root-isolation-b-{Guid.NewGuid():N}";
        var tenantId = Guid.NewGuid();

        await using (var first = CreateContext(firstDatabaseName))
        {
            first.Tenants.Add(new Tenant
            {
                Id = tenantId,
                Name = "Shared Root Isolation",
                Slug = $"shared-root-{tenantId:N}",
                ContactEmail = "shared-root@example.test",
                IsActive = true,
                Status = TenantStatus.Active,
                Currency = "EGP",
                Country = "EG",
                CreatedAtUtc = DateTime.UtcNow
            });
            await first.SaveChangesAsync();
        }

        await using (var second = CreateContext(secondDatabaseName))
        {
            Assert.Empty(await second.Tenants.IgnoreQueryFilters().ToListAsync());
        }

        await using var reopenedFirst = CreateContext(firstDatabaseName);
        Assert.Equal(
            tenantId,
            (await reopenedFirst.Tenants.IgnoreQueryFilters().SingleAsync()).Id);
    }

    private static ApplicationDbContext CreateContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName, SharedInMemoryDatabaseRoot.Instance)
            .Options;

        return new ApplicationDbContext(options);
    }
}
