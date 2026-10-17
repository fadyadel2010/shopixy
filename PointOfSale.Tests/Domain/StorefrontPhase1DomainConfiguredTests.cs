using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Admin;
using PointOfSale.Tests.Infrastructure;
using PointOfSale.Tests.Shipping;

namespace PointOfSale.Tests.CustomDomains;

public partial class DomainWriteServiceTests
{
    [StorefrontPhase1ConfiguredFact]
    public async Task Phase1Configured_DraftDomainLifecycleAndAudit_RollBackWithoutDnsTlsOrRouting()
    {
        await using var configured = ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(configured.Database.GetConnectionString()).Options;
        var tenant = Guid.NewGuid();
        Guid domainId;
        await using (var db = new ApplicationDbContext(options, new TestTenantContext(tenant)))
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '3s'; SET LOCAL statement_timeout = '30s'");
            db.Tenants.Add(new Tenant { Id = tenant, Name = "SYNTHETIC phase1 domain", Slug = $"p1-{tenant:N}" });
            await db.SaveChangesAsync();
            var publisher = new TestEventPublisher();
            var service = CreateWriteService(db, tenant, publisher);
            var host = $"PHASE1-{tenant:N}.example.test.";
            var created = await service.CreateDomainAsync(new() { HostName = host, DomainType = DomainType.Alias }, tenant, null, "phase1-create");
            domainId = created.DomainId;
            db.ChangeTracker.Clear();
            var saved = await db.Domains.AsNoTracking().SingleAsync(d => d.Id == domainId);
            Assert.Equal(host.TrimEnd('.').ToLowerInvariant(), saved.NormalizedHostName);
            Assert.Equal(DomainLifecycleState.Draft, saved.LifecycleState);
            Assert.Null(saved.SslCertificateId);
            var read = new DomainReadService(db, NullLogger<DomainReadService>.Instance);
            Assert.Equal(1, (await read.GetDashboardSummaryAsync(tenant)).TotalDomains);
            Assert.Null(await read.GetDomainDetailAsync(Guid.NewGuid(), domainId));
            await Assert.ThrowsAsync<DomainConflictException>(() => service.CreateDomainAsync(new() { HostName = saved.NormalizedHostName, DomainType = DomainType.Alias }, tenant, null, "phase1-duplicate"));
            var updated = await service.UpdateDomainAsync(domainId, new() { ExpectedVersion = created.Version, HostName = $"changed-{tenant:N}.example.test" }, tenant, null, "phase1-update");
            db.ChangeTracker.Clear();
            await Assert.ThrowsAsync<DomainConcurrencyException>(() => service.UpdateDomainAsync(domainId, new() { ExpectedVersion = created.Version, HostName = $"stale-{tenant:N}.example.test" }, tenant, null, "phase1-stale"));
            await Assert.ThrowsAsync<DomainNotFoundException>(() => service.UpdateDomainAsync(domainId, new() { ExpectedVersion = updated.Version }, Guid.NewGuid(), null, "phase1-foreign"));
            var archived = await service.ArchiveDomainAsync(domainId, new() { ExpectedVersion = updated.Version, Reason = "Synthetic rollback" }, tenant, null, "phase1-archive");
            var restored = await service.RestoreDomainAsync(domainId, new() { ExpectedVersion = archived.Version }, tenant, null, "phase1-restore");
            await service.DeleteDomainAsync(domainId, new() { ExpectedVersion = restored.Version, Reason = "Synthetic rollback" }, tenant, null, "phase1-delete");
            db.ChangeTracker.Clear();
            Assert.True((await db.Domains.IgnoreQueryFilters().SingleAsync(d => d.Id == domainId)).IsDeleted);
            Assert.Equal(5, await db.DomainAuditEntries.IgnoreQueryFilters().CountAsync(a => a.TenantId == tenant && a.DomainId == domainId));
            Assert.Equal(5, publisher.PublishedEvents.Count);
            Assert.False(await db.SslCertificates.IgnoreQueryFilters().AnyAsync(c => c.TenantId == tenant));
            await db.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE");
            await tx.RollbackAsync();
        }
        await using var observer = new ApplicationDbContext(options, new TestTenantContext(tenant));
        Assert.False(await observer.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Id == tenant));
        Assert.False(await observer.Domains.IgnoreQueryFilters().AnyAsync(d => d.Id == domainId));
        Assert.False(await observer.DomainAuditEntries.IgnoreQueryFilters().AnyAsync(a => a.TenantId == tenant));
        await File.WriteAllTextAsync(Path.Combine(Environment.GetEnvironmentVariable("STOREFRONT_PHASE1_EVIDENCE")!, "domain-postgres-rollback.json"), JsonSerializer.Serialize(new { tenant, domainId, rolledBack = true, freshObserverEmpty = true, fakeEventPublisher = true, dnsTlsOrRoutingExecuted = false }));
    }
}
