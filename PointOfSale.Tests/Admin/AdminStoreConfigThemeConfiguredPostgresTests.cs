using Microsoft.EntityFrameworkCore;
using Moq;
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
    // Existing Development PostgreSQL, no provisioning/schema/reset/committed fixture.
    // The publisher/audit/lock boundaries are controlled: no real publication reaches
    // RabbitMQ, MongoDB, Redis, customers or external services from this transaction.
    [ConfiguredRemittanceRollbackTheory]
    [InlineData("draft")]
    [InlineData("publish")]
    [InlineData("stale")]
    [InlineData("event-failure")]
    public async Task ConfiguredPostgres_ThemeContract_RollsBackAllSyntheticState(string scenario)
    {
        await using var configured = ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(configured.Database.GetConnectionString()).Options;
        var tenant = Guid.NewGuid();
        await using (var db = new ApplicationDbContext(options, new TestTenantContext(tenant)))
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '3s'; SET LOCAL statement_timeout = '30s'");
            db.Tenants.Add(new Tenant { Id = tenant, Name = "SYNTHETIC rollback-only theme QE", Slug = $"qe-{tenant:N}" });
            var stored = CreateStoreConfig(tenant);
            db.StoreConfigs.Add(stored);
            await db.SaveChangesAsync();
            var events = new Mock<IEventPublisher>();
            events.SetReturnsDefault(Task.CompletedTask);
            var service = CreateService(db, events);
            var originalLive = stored.ThemeLiveOverridesJson;
            var accepted = await service.GetStoreConfigAsync(tenant, "qe-read");
            var saved = await service.UpdateThemeOverridesAsync(tenant,
                """{"presetKey":"default","colors":{"primary":"#123456"},"typography":{"fontFamily":"Cairo","baseSize":"18px"},"borderRadius":12}""",
                "qe-draft", expectedRowVersion: uint.Parse(accepted.RowVersion));
            db.ChangeTracker.Clear();
            stored = await db.StoreConfigs.SingleAsync(c => c.TenantId == tenant);
            Assert.Equal(originalLive, stored.ThemeLiveOverridesJson);
            Assert.Equal("#123456", saved.DraftTheme.Colors.Primary);
            Assert.Equal("Cairo", saved.DraftTheme.Font);
            events.VerifyNoOtherCalls();

            if (scenario == "publish")
            {
                var live = await service.PublishThemeAsync(tenant, "qe-publish", expectedRowVersion: uint.Parse(saved.RowVersion));
                Assert.Equal("#123456", live.LiveTheme.Colors.Primary);
                Assert.Equal(stored.ThemeDraftOverridesJson, stored.ThemeLiveOverridesJson);
                events.Verify(e => e.PublishAsync(It.IsAny<StoreConfigPublishedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
            }
            else if (scenario == "stale")
            {
                // A deliberately non-current xmin, not a same-transaction write (which
                // PostgreSQL correctly gives the same transaction id).
                await Assert.ThrowsAsync<ConcurrencyConflictException>(() => service.PublishThemeAsync(
                    tenant, "qe-stale", expectedRowVersion: uint.Parse(saved.RowVersion) - 1));
                db.ChangeTracker.Clear();
                Assert.Equal(originalLive, (await db.StoreConfigs.SingleAsync(c => c.TenantId == tenant)).ThemeLiveOverridesJson);
            }
            else if (scenario == "event-failure")
            {
                events.Setup(e => e.PublishAsync(It.IsAny<StoreConfigPublishedEvent>(), It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new InvalidOperationException("Synthetic staging failure"));
                await Assert.ThrowsAsync<InvalidOperationException>(() => service.PublishThemeAsync(
                    tenant, "qe-event-failure", expectedRowVersion: uint.Parse(saved.RowVersion)));
                db.ChangeTracker.Clear();
                Assert.Equal(originalLive, (await db.StoreConfigs.SingleAsync(c => c.TenantId == tenant)).ThemeLiveOverridesJson);
            }
            await db.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE");
            await tx.RollbackAsync();
        }
        await using var observer = new ApplicationDbContext(options, new TestTenantContext(tenant));
        Assert.False(await observer.Tenants.AnyAsync(t => t.Id == tenant));
        Assert.False(await observer.StoreConfigs.AnyAsync(c => c.TenantId == tenant));
    }
}
