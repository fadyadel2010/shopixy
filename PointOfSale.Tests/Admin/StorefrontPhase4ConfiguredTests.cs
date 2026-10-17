using System.Data;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Services.PhoneRisk;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.Services;
using PointOfSale.Tests.Infrastructure;
using PointOfSale.Tests.Shipping;

namespace PointOfSale.Tests.Admin;

public sealed class StorefrontPhase4ConfiguredTests
{
    private static readonly Guid Store = Guid.Parse("c9c5c1ae-949e-498e-bc63-07242daeb49d");
    private static string Evidence => Environment.GetEnvironmentVariable("STOREFRONT_PHASE4_EVIDENCE")!;
    private static Task Write(string name, object data) => File.WriteAllTextAsync(Path.Combine(Evidence, name), JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));

    [Phase4ConfiguredFact]
    public async Task ConfiguredRegisteredCustomerSessions_ExpiryRevocationAndMultipleSignins_Rollback()
    {
        await using var configured = ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(configured.Database.GetConnectionString()).Options;
        var tenant = Guid.NewGuid(); var customer = Guid.NewGuid();
        await using (var db = new ApplicationDbContext(options, new TestTenantContext(tenant)))
        await using (var tx = await db.Database.BeginTransactionAsync()) {
            db.Tenants.Add(new Tenant { Id = tenant, Name = "P4QA sessions", Slug = "p4-" + tenant.ToString("N") });
            db.OnlineCustomers.Add(new OnlineCustomer { Id = customer, TenantId = tenant, Email = "p4qa@example.test", NormalizedEmail = "p4qa@example.test", IsActive = true, CreatedAtUtc = DateTime.UtcNow });
            for (var i = 0; i < 4; i++) db.OnlineCustomerRefreshTokens.Add(new OnlineCustomerRefreshToken { Id = Guid.NewGuid(), TenantId = tenant, OnlineCustomerId = customer, TokenHash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"), ExpiresAtUtc = DateTime.UtcNow.AddMinutes(i == 2 ? -1 : 20), IsRevoked = i == 3, CreatedByIp = "127.0.0.1", CreatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
            var controller = new OnlineCustomersAdminController(db, new TestTenantContext(tenant), Mock.Of<IEventPublisher>(), NullLogger<OnlineCustomersAdminController>.Instance, null!, null!);
            controller.ControllerContext = new ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() };
            async Task<int> Count() {
                var result = Assert.IsType<OkObjectResult>(await controller.GetOnlineCustomers());
                return Assert.Single(Assert.IsType<OnlineCustomerListResponse>(result.Value).Items).ActiveSessions;
            }
            Assert.Equal(2, await Count());
            await db.OnlineCustomerRefreshTokens.Where(x => x.OnlineCustomerId == customer).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsRevoked, true));
            Assert.Equal(0, await Count());
            await db.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE");
            await tx.RollbackAsync();
        }
        await using var observer = new ApplicationDbContext(options, new TestTenantContext(tenant));
        Assert.False(await observer.OnlineCustomers.IgnoreQueryFilters().AnyAsync(x => x.Id == customer));
        Assert.False(await observer.OnlineCustomerRefreshTokens.IgnoreQueryFilters().AnyAsync(x => x.TenantId == tenant));
        await Write("registered-sessions-postgres.json", new { tenant, customer, activeSignins = 2, expiredExcluded = true, revokedExcluded = true, afterRevoke = 0, heartbeatPresence = false, freshObserverAbsent = true });
    }

    [Phase4ConfiguredFact]
    public async Task IndependentReadOnly_SessionsOrdersAndPriorCouponState()
    {
        await using var db = ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        await using var connection = new NpgsqlConnection(db.Database.GetConnectionString());
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        await using (var ro = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, tx)) await ro.ExecuteNonQueryAsync();
        async Task<string[][]> Query(string sql) {
            await using var command = new NpgsqlCommand(sql, connection, tx);
            command.Parameters.AddWithValue("tenant", Store);
            await using var reader = await command.ExecuteReaderAsync();
            var rows = new List<string[]>();
            while (await reader.ReadAsync()) rows.Add(Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? "" : reader.GetValue(i).ToString()!).ToArray());
            return rows.ToArray();
        }
        var sessions = await Query("SELECT \"Status\"::text,count(*)::text,coalesce(sum(\"CartTotal\"),0)::text FROM \"CheckoutSessions\" WHERE \"TenantId\"=@tenant AND \"InvalidatedAtUtc\" IS NULL GROUP BY \"Status\" ORDER BY \"Status\"");
        var orders = await Query("SELECT \"Status\"::text,\"PaymentStatus\"::text,count(*)::text,sum(\"Total\")::text FROM \"Orders\" WHERE \"TenantId\"=@tenant AND NOT \"IsDeleted\" AND \"Source\"=0 GROUP BY \"Status\",\"PaymentStatus\" ORDER BY 1,2");
        var qaOrders = await Query("SELECT \"Id\"::text,\"OrderNumber\",\"Status\"::text,\"PaymentStatus\"::text,\"FinancialPostingStatus\"::text,\"Total\"::text,\"OnlineCustomerId\"::text FROM \"Orders\" WHERE \"TenantId\"=@tenant AND \"CustomerNotes\" LIKE 'P4QA%' ORDER BY \"CreatedAt\"");
        var priorCoupons = await Query("SELECT count(*)::text,count(*) FILTER(WHERE NOT \"IsDeleted\")::text,coalesce(sum(\"CurrentUsage\"),0)::text FROM \"Coupons\" WHERE \"TenantId\"=@tenant AND \"Code\" LIKE 'P3QA%'");
        await Write("database-readonly-" + (Environment.GetEnvironmentVariable("STOREFRONT_PHASE4_STAGE") ?? "current") + ".json", new { observedUtc = DateTime.UtcNow, readOnly = true, sessions, orders, qaOrders, priorCoupons });
        await tx.RollbackAsync();
    }

    [Phase4ConfiguredFact]
    public async Task ConfiguredPhoneIdentity_EquivalentFormatsTenantIsolationAndUnblock_Rollback()
    {
        await using var configured = ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(configured.Database.GetConnectionString()).Options;
        var tenant = Guid.NewGuid();
        await using (var db = new ApplicationDbContext(options, new TestTenantContext(tenant)))
        await using (var tx = await db.Database.BeginTransactionAsync()) {
            db.Tenants.Add(new Tenant { Id = tenant, Name = "P4QA phone rollback", Slug = "p4-" + tenant.ToString("N"), Country = "EG" });
            await db.SaveChangesAsync();
            var service = new PhoneRiskProfileService(db, new PhoneNumberNormalizer(db, null, NullLogger<PhoneNumberNormalizer>.Instance), Mock.Of<IOrderVelocityService>(), NullLogger<PhoneRiskProfileService>.Instance);
            foreach (var number in new[] { "01000000099", "+20 (100) 000-0099", "00201000000099" }) {
                Assert.True((await service.BlockAsync(tenant, number, "P4QA", null)).IsBlocked);
                Assert.True(await service.IsBlockedAsync(tenant, "+201000000099"));
            }
            Assert.Single(await db.PhoneRiskProfiles.IgnoreQueryFilters().Where(x => x.TenantId == tenant).ToListAsync());
            Assert.False(await service.IsBlockedAsync(Store, "+201000000099"));
            Assert.False((await service.UnblockAsync(tenant, "01000000099")).IsBlocked);
            Assert.False(await service.IsBlockedAsync(tenant, "+201000000099"));
            await db.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE");
            await tx.RollbackAsync();
        }
        await using var observer = new ApplicationDbContext(options, new TestTenantContext(tenant));
        Assert.False(await observer.Tenants.IgnoreQueryFilters().AnyAsync(x => x.Id == tenant));
        Assert.False(await observer.PhoneRiskProfiles.IgnoreQueryFilters().AnyAsync(x => x.TenantId == tenant));
        await Write("phone-postgres-rollback.json", new { tenant, canonicalEquivalence = true, foreignTenantUnblocked = true, freshObserverAbsent = true });
    }

    [Phase4ConfiguredFact]
    public async Task ConfiguredAbandonment_ThresholdStaleTrackedCandidateDuplicateAndEventFailure()
    {
        await using var configured = ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(configured.Database.GetConnectionString()).Options;
        var tenant = Guid.NewGuid();
        var ids = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToArray();
        try {
            await using (var db = new ApplicationDbContext(options, new TestTenantContext(tenant))) {
                db.Tenants.Add(new Tenant { Id = tenant, Name = "P4QA abandonment", Slug = "p4-" + tenant.ToString("N"), Country = "EG" });
                for (var i = 0; i < ids.Length; i++) db.CheckoutSessions.Add(new CheckoutSession { Id = ids[i], TenantId = tenant, CartId = Guid.NewGuid(), SessionId = "P4QA-" + i, RecoveryToken = Guid.NewGuid().ToString("N"), LastActivityAtUtc = DateTime.UtcNow.AddMinutes(i == 1 ? -29 : -31), Currency = "EGP", Status = CheckoutSessionStatus.Active });
                await db.SaveChangesAsync();
            }
            var publisher = new Mock<IEventPublisher>();
            publisher.SetReturnsDefault(Task.CompletedTask);
            await using (var stale = new ApplicationDbContext(options, new TestTenantContext(tenant))) {
                await stale.CheckoutSessions.IgnoreQueryFilters().SingleAsync(x => x.Id == ids[0]);
                await using (var fresh = new ApplicationDbContext(options, new TestTenantContext(tenant)))
                    await fresh.CheckoutSessions.IgnoreQueryFilters().Where(x => x.Id == ids[0]).ExecuteUpdateAsync(s => s.SetProperty(x => x.LastActivityAtUtc, DateTime.UtcNow));
                await new CheckoutSessionService(stale, publisher.Object, new CommerceReliabilityTelemetry(), NullLogger<CheckoutSessionService>.Instance).MarkAsAbandonedAsync(ids[0]);
            }
            var count = 0;
            publisher.Setup(x => x.PublishAsync(It.IsAny<CheckoutSessionAbandonedEvent>(), It.IsAny<CancellationToken>())).Callback(() => Interlocked.Increment(ref count)).Returns(Task.CompletedTask);
            await Task.WhenAll(Enumerable.Range(0, 3).Select(async _ => {
                await using var db = new ApplicationDbContext(options, new TestTenantContext(tenant));
                await new CheckoutSessionService(db, publisher.Object, new CommerceReliabilityTelemetry(), NullLogger<CheckoutSessionService>.Instance).MarkAsAbandonedAsync(ids[2]);
            }));
            Assert.Equal(1, count);
            await using (var observer = new ApplicationDbContext(options, new TestTenantContext(tenant))) {
                var rows = await observer.CheckoutSessions.IgnoreQueryFilters().Where(x => x.TenantId == tenant).ToListAsync();
                Assert.Equal(CheckoutSessionStatus.Active, rows.Single(x => x.Id == ids[0]).Status);
                Assert.Equal(CheckoutSessionStatus.Active, rows.Single(x => x.Id == ids[1]).Status);
                Assert.Equal(CheckoutSessionStatus.Abandoned, rows.Single(x => x.Id == ids[2]).Status);
                await observer.CheckoutSessions.IgnoreQueryFilters().Where(x => x.Id == ids[1]).ExecuteUpdateAsync(s => s.SetProperty(x => x.LastActivityAtUtc, DateTime.UtcNow.AddHours(-1)));
            }
            publisher.Setup(x => x.PublishAsync(It.IsAny<CheckoutSessionAbandonedEvent>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("P4QA controlled staging failure"));
            await using (var db = new ApplicationDbContext(options, new TestTenantContext(tenant)))
                await Assert.ThrowsAsync<InvalidOperationException>(() => new CheckoutSessionService(db, publisher.Object, new CommerceReliabilityTelemetry(), NullLogger<CheckoutSessionService>.Instance).MarkAsAbandonedAsync(ids[1]));
            await using (var observer = new ApplicationDbContext(options, new TestTenantContext(tenant)))
                Assert.Equal(CheckoutSessionStatus.Active, (await observer.CheckoutSessions.IgnoreQueryFilters().SingleAsync(x => x.Id == ids[1])).Status);
        } finally {
            await using var cleanup = new ApplicationDbContext(options, new TestTenantContext(tenant));
            await cleanup.CheckoutSessions.IgnoreQueryFilters().Where(x => x.TenantId == tenant && ids.Contains(x.Id)).ExecuteDeleteAsync();
            await cleanup.Tenants.IgnoreQueryFilters().Where(x => x.Id == tenant).ExecuteDeleteAsync();
        }
        await using var finalObserver = new ApplicationDbContext(options, new TestTenantContext(tenant));
        Assert.False(await finalObserver.CheckoutSessions.IgnoreQueryFilters().AnyAsync(x => x.TenantId == tenant));
        Assert.False(await finalObserver.Tenants.IgnoreQueryFilters().AnyAsync(x => x.Id == tenant));
        await Write("abandonment-postgres.json", new { tenant, ids, thresholdMinutes = 30, concurrentWriters = 3, acceptedEvents = 1, refreshedCandidateRemainsActive = true, stagingFailureRolledBack = true, freshCleanupObserverAbsent = true, publisher = "controlled" });
    }
}

public sealed class Phase4ConfiguredFactAttribute : FactAttribute
{
    public Phase4ConfiguredFactAttribute() { if (Environment.GetEnvironmentVariable("STOREFRONT_PHASE4_CONFIGURED") != "1") Skip = "Explicit Phase4 existing-database opt-in required. No provisioning."; }
}
