using Microsoft.EntityFrameworkCore;
using Moq;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Exceptions;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;
using PointOfSale.Tests.Shipping;

namespace PointOfSale.Tests.Admin;

public sealed partial class AdminStoreConfigThemeContractTests
{
    [StorefrontPhase1ConfiguredFact]
    public async Task Phase1Configured_StatusOnly_PreservesUnrelatedConfigRejectsStaleAndRollsBack()
    {
        await using var configured = ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(configured.Database.GetConnectionString()).Options;
        var tenant = Guid.NewGuid();
        await using (var db = new ApplicationDbContext(options, new TestTenantContext(tenant)))
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '3s'; SET LOCAL statement_timeout = '30s'");
            db.Tenants.Add(new Tenant { Id = tenant, Name = "SYNTHETIC phase1 status", Slug = $"p1-{tenant:N}" });
            var config = CreateStoreConfig(tenant);
            db.StoreConfigs.Add(config);
            await db.SaveChangesAsync();
            var originalCustomization = config.CustomizationJson;
            var originalTheme = config.ThemeLiveOverridesJson;
            var events = new Mock<IEventPublisher>();
            events.SetReturnsDefault(Task.CompletedTask);
            var service = CreateService(db, events);
            foreach (var status in new[] { "maintenance", "disabled", "live" })
            {
                var read = await service.GetStoreConfigAsync(tenant, "phase1-read");
                var accepted = await service.UpdateStorefrontStatusAsync(tenant, new AdminStorefrontStatusRequest {
                    StorefrontStatus = status, MaintenanceMessage = "SYNTHETIC مرحباً"
                }, "phase1-status", expectedRowVersion: uint.Parse(read.RowVersion));
                db.ChangeTracker.Clear();
                var persisted = await db.StoreConfigs.SingleAsync(c => c.TenantId == tenant);
                Assert.Equal(status, accepted.StorefrontStatus);
                Assert.Equal(status, persisted.StorefrontStatus);
                Assert.Equal("SYNTHETIC مرحباً", persisted.MaintenanceMessage);
                Assert.Equal(originalCustomization, persisted.CustomizationJson);
                Assert.Equal(originalTheme, persisted.ThemeLiveOverridesJson);
            }
            var latest = await service.GetStoreConfigAsync(tenant, "phase1-stale-read");
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => service.UpdateStorefrontStatusAsync(tenant,
                new() { StorefrontStatus = "disabled" }, "phase1-stale", expectedRowVersion: uint.Parse(latest.RowVersion) - 1));
            db.ChangeTracker.Clear();
            events.Setup(e => e.PublishAsync(It.IsAny<StoreConfigUpdatedEvent>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("Synthetic staging failure"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateStorefrontStatusAsync(tenant,
                new() { StorefrontStatus = "disabled" }, "phase1-event-failure", expectedRowVersion: uint.Parse(latest.RowVersion)));
            db.ChangeTracker.Clear();
            Assert.Equal("live", (await db.StoreConfigs.SingleAsync(c => c.TenantId == tenant)).StorefrontStatus);
            await db.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE");
            await tx.RollbackAsync();
        }
        await using var observer = new ApplicationDbContext(options, new TestTenantContext(tenant));
        Assert.False(await observer.Tenants.AnyAsync(t => t.Id == tenant));
        Assert.False(await observer.StoreConfigs.AnyAsync(c => c.TenantId == tenant));
    }
}
