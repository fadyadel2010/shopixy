using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Services.PhoneRisk;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Services;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.PhoneRisk;

public sealed class CustomerTrustEvidenceTests
{
    private static async Task<(ApplicationDbContext Db, PhoneRiskProfileService Service, Guid Tenant)> CreateAsync()
    {
        var tenant = Guid.NewGuid();
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new TestTenantContext(tenant));
        db.Tenants.Add(new Tenant { Id = tenant, Name = "Trust QA", Slug = "trust-" + tenant.ToString("N"), Country = "EG" });
        await db.SaveChangesAsync();
        var normalizer = new PhoneNumberNormalizer(db, null, NullLogger<PhoneNumberNormalizer>.Instance);
        var velocity = new Mock<IOrderVelocityService>();
        velocity.SetReturnsDefault(Task.FromResult(new OrderVelocityAssessment()));
        return (db, new PhoneRiskProfileService(db, normalizer, velocity.Object, NullLogger<PhoneRiskProfileService>.Instance), tenant);
    }
    private static Order Order(Guid tenant, string phone, OrderStatus status = OrderStatus.Pending) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenant, OrderNumber = "TRUST-" + Guid.NewGuid().ToString("N"), Source = OrderSource.Ecommerce,
        GuestPhone = phone, Status = status, Currency = "EGP", ShippingAddress = "QA", CreatedAt = DateTime.UtcNow,
    };

    [Theory]
    [InlineData("+201000000088")]
    [InlineData("")]
    public async Task TrustAndSpam_UseOrderCheckoutSnapshot_NotMutableOrEmptyAccountPhone(string accountPhone)
    {
        var (db, service, tenant) = await CreateAsync(); await using var dispose = db;
        var customer = new OnlineCustomer { Id = Guid.NewGuid(), TenantId = tenant, Email = "trust@example.test", NormalizedEmail = "trust@example.test", Phone = accountPhone };
        db.OnlineCustomers.Add(customer);
        var order = Order(tenant, "01000000099"); order.OnlineCustomerId = customer.Id;
        db.Orders.Add(order); await db.SaveChangesAsync();
        var trust = await service.GetOrderCustomerTrustAsync(tenant, order.Id);
        Assert.Equal("+201000000099", trust!.PhoneNumber);
        var report = await service.ReportSpamAsync(tenant, order.Id, null);
        Assert.Equal(trust.PhoneNumber, report.PhoneNumber);
    }

    [Theory]
    [InlineData("+20 (100) 000-0099")]
    [InlineData("+٢٠ (١٠٠) ٠٠٠-٠٠٩٩")]
    [InlineData("+20\t(100) 000-0099")]
    public async Task TrustHistory_NormalizesEquivalentPhones_AndExcludesForeignDeletedAndMarketplaceOrders(string formattedPhone)
    {
        var (db, service, tenant) = await CreateAsync(); await using var dispose = db;
        var current = Order(tenant, "+201000000099"); db.Orders.Add(current);
        db.Orders.AddRange(Order(tenant, "01000000099", OrderStatus.Cancelled), Order(tenant, "00201000000099", OrderStatus.Cancelled), Order(tenant, formattedPhone, OrderStatus.Delivered));
        db.Orders.Add(Order(Guid.NewGuid(), "+201000000099", OrderStatus.Cancelled));
        var archived = Order(tenant, "+201000000099"); archived.IsDeleted = true; db.Orders.Add(archived);
        var marketplace = Order(tenant, "+201000000099"); marketplace.Source = OrderSource.Marketplace; db.Orders.Add(marketplace);
        db.Orders.Add(Order(tenant, "+201111110099")); // Same suffix is not canonical identity.
        await db.SaveChangesAsync();
        var trust = await service.GetOrderCustomerTrustAsync(tenant, current.Id);
        Assert.Equal(3, trust!.PreviousOrdersCount);
        Assert.Equal(2, trust.CancelledOrdersCount);
        Assert.Contains("HIGH_CANCELLATION_RATE", trust.RiskSignals);
        Assert.Equal("SUSPICIOUS", trust.RiskLevel);
    }

    [Fact]
    public async Task InvalidLegacyPhone_IsUnavailable_NotSafeOrAnUnhandledReadFailure()
    {
        var (db, service, tenant) = await CreateAsync(); await using var dispose = db;
        var order = Order(tenant, "123"); db.Orders.Add(order); await db.SaveChangesAsync();
        var trust = await service.GetOrderCustomerTrustAsync(tenant, order.Id);
        Assert.False(trust!.HasPhone);
        Assert.Equal("UNAVAILABLE", trust.RiskLevel);
        Assert.Null(trust.PhoneRisk);
    }

    [Fact]
    public async Task RepeatedSpamReport_IsOneReportAndIsNotAnAutomaticBlock()
    {
        var (db, service, tenant) = await CreateAsync(); await using var dispose = db;
        var order = Order(tenant, "+201000000099"); db.Orders.Add(order); await db.SaveChangesAsync();
        await service.ReportSpamAsync(tenant, order.Id, null); await service.ReportSpamAsync(tenant, order.Id, null);
        db.ChangeTracker.Clear();
        var trust = await service.GetOrderCustomerTrustAsync(tenant, order.Id);
        Assert.True(trust!.HasReportedSpam);
        Assert.Equal(1, trust.PhoneRisk!.SpamReports);
        Assert.Equal(1, trust.PhoneRisk.GlobalSpamReports);
        Assert.False(trust.PhoneRisk.IsBlocked);
        Assert.Equal("SUSPICIOUS", trust.RiskLevel);
        Assert.Single(await db.PhoneSpamReports.ToListAsync());
    }

    [Theory]
    [InlineData(0, 0, "SAFE")]
    [InlineData(1, 0, "SUSPICIOUS")]
    [InlineData(0, 3, "SUSPICIOUS")]
    [InlineData(1, 3, "HIGH_RISK")]
    [InlineData(0, 5, "CRITICAL_RISK")]
    public async Task TrustRisk_UsesExistingEvidenceThresholds_NotAnAutomaticBlock(int local, int global, string expected)
    {
        var (db, service, tenant) = await CreateAsync(); await using var dispose = db;
        const string phone = "+201000000099";
        var order = Order(tenant, phone); db.Orders.Add(order);
        db.PhoneRiskProfiles.Add(new PhoneRiskProfile { Id = Guid.NewGuid(), TenantId = tenant, PhoneNumber = phone, SpamReports = local });
        db.GlobalPhoneReputations.Add(new GlobalPhoneReputation { Id = Guid.NewGuid(), PhoneNumber = phone, SpamReports = global });
        await db.SaveChangesAsync();
        var trust = await service.GetOrderCustomerTrustAsync(tenant, order.Id);
        Assert.Equal(expected, trust!.RiskLevel);
        Assert.False(trust.PhoneRisk!.IsBlocked);
    }
}
