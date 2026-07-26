using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MultiTenancy;

namespace PointOfSale.Tests.Security;

public sealed class TenantSaveChangesInterceptorTests
{
    [Fact]
    public async Task SaveChanges_MustRejectCrossTenantHardDelete()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantA);
        var serviceProvider = new ServiceCollection()
            .AddEntityFrameworkInMemoryDatabase()
            .AddSingleton<PointOfSale.Core.Interfaces.ITenantContext>(tenantContext)
            .BuildServiceProvider();

        await using (var seedContext = new ApplicationDbContext(
                         new DbContextOptionsBuilder<ApplicationDbContext>()
                             .UseInMemoryDatabase(databaseName)
                             .UseInternalServiceProvider(serviceProvider)
                             .Options))
        {
            seedContext.Units.Add(new PointOfSale.Core.Entities.Unit
            {
                Id = unitId,
                TenantId = tenantB,
                Name = "Foreign unit",
                Symbol = "foreign",
                IsActive = true
            });
            await seedContext.SaveChangesAsync();
        }

        var interceptor = new TenantSaveChangesInterceptor(
            NullLogger<TenantSaveChangesInterceptor>.Instance);

        await using var guardedContext = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName)
                .UseInternalServiceProvider(serviceProvider)
                .AddInterceptors(interceptor)
                .Options,
            tenantContext);

        var foreignUnit = await guardedContext.Units
            .IgnoreQueryFilters()
            .SingleAsync(unit => unit.Id == unitId);
        guardedContext.Units.Remove(foreignUnit);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => guardedContext.SaveChangesAsync());

        Assert.Contains("different tenant", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
