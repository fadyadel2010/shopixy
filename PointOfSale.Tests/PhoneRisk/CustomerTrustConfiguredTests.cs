using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services.PhoneRisk;
using PointOfSale.API.Services;
using PointOfSale.API.Services.Admin;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Payments;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Data.Repositories;
using PointOfSale.Infrastructure.Services;
using PointOfSale.Tests.Infrastructure;
using PointOfSale.Tests.Shipping;

namespace PointOfSale.Tests.PhoneRisk;

public sealed class CustomerTrustConfiguredTests
{
    [TrustConfiguredFact]
    public async Task HostedQaPhoneAndOrders_IndependentCleanupObserver_ReadOnly()
    {
        var tenant = Guid.Parse("c9c5c1ae-949e-498e-bc63-07242daeb49d");
        const string phone = "+201000000099";
        var ids = new[] { Guid.Parse("ee42f429-fa3d-46b6-988a-b900cc3e8648"), Guid.Parse("258be5dc-92d9-4489-a1c4-0872d7f8b2b0") };
        await using var db = ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY");
        var profile = await db.PhoneRiskProfiles.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenant && x.PhoneNumber == phone)
            .Select(x => new { x.Id, x.PhoneNumber, x.IsBlocked, x.SpamReports, x.BlockedReason }).SingleAsync();
        Assert.False(profile.IsBlocked); Assert.Equal(0, profile.SpamReports);
        var reports = await db.PhoneSpamReports.IgnoreQueryFilters().CountAsync(x => x.TenantId == tenant && x.PhoneNumber == phone);
        Assert.Equal(0, reports);
        var global = await db.GlobalPhoneReputations.AsNoTracking().Where(x => x.PhoneNumber == phone).Select(x => x.SpamReports).ToListAsync();
        Assert.Equal(0, global.Sum());
        var orders = await db.Orders.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenant && ids.Contains(x.Id))
            .Select(x => new { x.Id, x.Status, x.PaymentStatus, x.Total }).ToListAsync();
        Assert.Equal(2, orders.Count);
        Assert.Single(orders, x => x.Status == OrderStatus.Cancelled && x.PaymentStatus == PaymentStatus.Failed && x.Total == 35m);
        Assert.Single(orders, x => x.Status == OrderStatus.Completed && x.PaymentStatus == PaymentStatus.Collected && x.Total == 40m);
        var journals = await db.JournalEntries.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenant && ids.Contains(x.SourceId))
            .Select(x => new { x.SourceId, x.SourceType, TotalDebits = x.LedgerEntries.Sum(l => l.DebitAmount), TotalCredits = x.LedgerEntries.Sum(l => l.CreditAmount) }).ToListAsync();
        Assert.Equal(2, journals.Count); Assert.All(journals, x => Assert.Equal(x.TotalDebits, x.TotalCredits));
        Assert.Equal(40m, Assert.Single(journals, x => x.SourceType == "Order_COD").TotalDebits);
        Assert.Equal(10m, Assert.Single(journals, x => x.SourceType == "Order_COGS").TotalDebits);
        await File.WriteAllTextAsync(Path.Combine(Environment.GetEnvironmentVariable("STOREFRONT_TRUST_EVIDENCE")!, "hosted-cleanup-readonly.json"), JsonSerializer.Serialize(new {
            observedUtc = DateTime.UtcNow, readOnly = true, profile, committedSpamReports = reports, globalReports = global.Sum(), orders, journals,
            retainedMetadata = "Supported block/unblock retains the latest reason/timestamp/audit data; no profile/history erasure."
        }, new JsonSerializerOptions { WriteIndented = true }));
        await tx.RollbackAsync();
    }

    [TrustConfiguredFact]
    public async Task ExistingPostgres_TrustSnapshotAliasesBlockUnblockAndSpam_OnlyOwnedRollback()
    {
        await using var configured = ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(configured.Database.GetConnectionString()).Options;
        var tenant = Guid.NewGuid(); const string phone = "+201000007399";
        var orderIds = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
        var evidence = Environment.GetEnvironmentVariable("STOREFRONT_TRUST_EVIDENCE")!;
        await File.WriteAllTextAsync(Path.Combine(evidence, "configured-fixture.json"), JsonSerializer.Serialize(new { tenant, phone, orderIds, rollbackOnly = true, noProvisioning = true }));
        await using (var db = new ApplicationDbContext(options, new TestTenantContext(tenant)))
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='3s'; SET LOCAL statement_timeout='30s'");
            Assert.False(await db.GlobalPhoneReputations.AnyAsync(x => x.PhoneNumber == phone));
            db.Tenants.Add(new Tenant { Id = tenant, Name = "Trust rollback QA", Slug = "trust-" + tenant.ToString("N"), Country = "EG" });
            var customer = new OnlineCustomer { Id = Guid.NewGuid(), TenantId = tenant, Email = "trust@example.test", NormalizedEmail = "trust@example.test", Phone = "+201000007488" };
            db.OnlineCustomers.Add(customer);
            var forms = new[] { "01000007399", "00201000007399", "+٢٠ (١٠٠) ٠٠٠-٧٣٩٩", phone };
            for (var i = 0; i < orderIds.Length; i++) db.Orders.Add(new Order {
                Id = orderIds[i], TenantId = tenant, OrderNumber = "TRUST-" + orderIds[i].ToString("N"), Source = OrderSource.Ecommerce,
                OnlineCustomerId = i == 0 ? customer.Id : null, GuestPhone = forms[i], ShippingAddress = "Synthetic QA",
                Status = i is 1 or 2 ? OrderStatus.Cancelled : OrderStatus.Pending, Currency = "EGP", CreatedAt = DateTime.UtcNow,
            });
            using var lifecycle = CommerceHubBoundaryMutationScope.BeginOrderLifecycle();
            using var shipping = CommerceHubBoundaryMutationScope.BeginShippingAssignment();
            await db.SaveChangesAsync();
            var velocity = new Mock<IOrderVelocityService>(); velocity.SetReturnsDefault(Task.FromResult(new OrderVelocityAssessment()));
            var service = new PhoneRiskProfileService(db, new PhoneNumberNormalizer(db, null, NullLogger<PhoneNumberNormalizer>.Instance), velocity.Object, NullLogger<PhoneRiskProfileService>.Instance);
            var trust = await service.GetOrderCustomerTrustAsync(tenant, orderIds[0]);
            Assert.Equal(phone, trust!.PhoneNumber); Assert.Equal(3, trust.PreviousOrdersCount); Assert.Equal(2, trust.CancelledOrdersCount);
            var orders = new AdminOrderService(db, Mock.Of<IEventPublisher>(), Mock.Of<IRefundEvidenceService>(), Mock.Of<ICouponValidationService>(),
                Mock.Of<IPaymentLifecycleService>(), Mock.Of<IProductVariantStockRepository>(), Mock.Of<IR2StorageService>(), NullLogger<AdminOrderService>.Instance);
            Assert.Equal(forms[0], (await orders.GetOrderDetailsAsync(tenant, orderIds[0], "trust-rollback-details"))!.Customer.Phone);
            Assert.Equal("SUSPICIOUS", trust.RiskLevel);
            Assert.Null(await service.GetOrderCustomerTrustAsync(Guid.NewGuid(), orderIds[0]));
            await service.BlockAsync(tenant, forms[2], "Trust rollback QA", null);
            Assert.True((await service.GetOrderCustomerTrustAsync(tenant, orderIds[0]))!.PhoneRisk!.IsBlocked);
            Assert.Equal(phone, Assert.Single((await service.ListBlockedAsync(tenant, new AdminBlockedPhoneQuery())).Items).PhoneNumber);
            await service.UnblockAsync(tenant, forms[1]);
            Assert.Empty((await service.ListBlockedAsync(tenant, new AdminBlockedPhoneQuery())).Items);
            await service.ReportSpamAsync(tenant, orderIds[0], null); await service.ReportSpamAsync(tenant, orderIds[0], null);
            db.ChangeTracker.Clear();
            trust = await service.GetOrderCustomerTrustAsync(tenant, orderIds[0]);
            Assert.True(trust!.HasReportedSpam); Assert.Equal(1, trust.PhoneRisk!.SpamReports); Assert.Equal(1, trust.PhoneRisk.GlobalSpamReports); Assert.False(trust.PhoneRisk.IsBlocked);
            Assert.Single(await db.PhoneSpamReports.Where(x => x.TenantId == tenant).ToListAsync());
            await db.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE");
            await tx.RollbackAsync();
        }
        await using var observer = new ApplicationDbContext(options, new TestTenantContext(tenant));
        Assert.False(await observer.Tenants.IgnoreQueryFilters().AnyAsync(x => x.Id == tenant));
        Assert.False(await observer.Orders.IgnoreQueryFilters().AnyAsync(x => x.TenantId == tenant));
        Assert.False(await observer.PhoneRiskProfiles.IgnoreQueryFilters().AnyAsync(x => x.TenantId == tenant));
        Assert.False(await observer.PhoneSpamReports.IgnoreQueryFilters().AnyAsync(x => x.TenantId == tenant));
        Assert.False(await observer.GlobalPhoneReputations.AnyAsync(x => x.PhoneNumber == phone));
        await File.WriteAllTextAsync(Path.Combine(evidence, "configured-trust-result.json"), JsonSerializer.Serialize(new { tenant, orderIds, snapshotPhone = phone, equivalentHistory = 3, cancelledHistory = 2, repeatedSpamReports = 1, blockAndUnblock = true, newContextCleanupAbsent = true, publisherOrProvider = "none" }));
    }
}

public sealed class TrustConfiguredFactAttribute : FactAttribute
{
    public TrustConfiguredFactAttribute() { if (Environment.GetEnvironmentVariable("STOREFRONT_TRUST_CONFIGURED") != "1") Skip = "Explicit existing-database trust rollback opt-in required; no provisioning or schema operations."; }
}
