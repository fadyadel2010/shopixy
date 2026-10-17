using Microsoft.EntityFrameworkCore;
using Moq;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Exceptions;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;
using PointOfSale.Tests.Security;
using PointOfSale.Tests.Shipping;

namespace PointOfSale.Tests.Admin;

public sealed partial class AdminStoreConfigThemeContractTests
{
    [Phase7ConfiguredFact]
    public async Task Phase7Configured_AiOnlyPreservesUnrelatedSettingsRejectsStaleAndRollsBack()
    {
        await using var configured = ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(configured.Database.GetConnectionString()).Options;
        var tenant = Guid.NewGuid();
        await using (var db = new ApplicationDbContext(options, new TestTenantContext(tenant)))
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='3s'; SET LOCAL statement_timeout='30s'");
            db.Tenants.Add(new() { Id = tenant, Name = "P7QA rollback AI", Slug = $"p7qa-{tenant:N}" });
            var config = CreateStoreConfig(tenant); db.StoreConfigs.Add(config); await db.SaveChangesAsync();
            var originalName = config.StoreName; var originalCustomization = config.CustomizationJson; var originalTheme = config.ThemeLiveOverridesJson;
            var events = new Mock<IEventPublisher>(); events.SetReturnsDefault(Task.CompletedTask);
            var service = CreateService(db, events); var initial = await service.GetStoreConfigAsync(tenant, "p7");
            var candidate = new AdminCommerceAiConfigDto { Enabled = true, WidgetStyle = "compact", WidgetPosition = "middle-right", Personality = "professional", AutoGreetingEnabled = true, AutoGreetingMessage = "P7QA مرحباً" };
            var accepted = await service.UpdateCommerceAiAsync(tenant, candidate, "p7-ai", expectedRowVersion: uint.Parse(initial.RowVersion));
            db.ChangeTracker.Clear(); var persisted = await db.StoreConfigs.SingleAsync(x => x.TenantId == tenant);
            Assert.True(persisted.CommerceAiEnabled); Assert.Equal("P7QA مرحباً", persisted.CommerceAiAutoGreetingMessage);
            Assert.Equal(originalName, persisted.StoreName); Assert.Equal(originalCustomization, persisted.CustomizationJson); Assert.Equal(originalTheme, persisted.ThemeLiveOverridesJson);
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => service.UpdateCommerceAiAsync(tenant, new(), "p7-stale", expectedRowVersion: uint.Parse(initial.RowVersion)));
            db.ChangeTracker.Clear();
            events.Setup(x => x.PublishAsync(It.IsAny<StoreConfigUpdatedEvent>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("synthetic staging failure"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateCommerceAiAsync(tenant, new(), "p7-fail", expectedRowVersion: uint.Parse(accepted.RowVersion)));
            db.ChangeTracker.Clear(); Assert.True((await db.StoreConfigs.SingleAsync(x => x.TenantId == tenant)).CommerceAiEnabled);
            await db.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE"); await tx.RollbackAsync();
        }
        await using var observer = new ApplicationDbContext(options, new TestTenantContext(tenant));
        Assert.False(await observer.Tenants.AnyAsync(x => x.Id == tenant)); Assert.False(await observer.StoreConfigs.AnyAsync(x => x.TenantId == tenant));
        await Phase7ConfiguredTests.Write("configured-ai-rollback.json", new { tenant, scope = "P7QA", aiOnly = true, staleRejected = true, eventFailureAtomic = true, freshObserverAbsent = true });
    }
}
