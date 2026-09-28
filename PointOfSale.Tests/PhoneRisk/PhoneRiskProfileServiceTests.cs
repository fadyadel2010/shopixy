using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Services.PhoneRisk;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.PhoneRisk;

public sealed class PhoneRiskProfileServiceTests : IDisposable
{
    private const string NormalizedPhone = "+201000000001";

    private readonly ApplicationDbContext _dbContext;
    private readonly PhoneRiskProfileService _service;
    private readonly MutableTenantContext _tenantContext = new();

    public PhoneRiskProfileServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(options, _tenantContext);

        var normalizer = new Mock<IPhoneNumberNormalizer>();
        normalizer
            .Setup(item => item.NormalizeAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(NormalizedPhone);
        var velocity = new Mock<IOrderVelocityService>();
        velocity
            .Setup(item => item.GetCurrentAssessmentAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrderVelocityAssessment());

        _service = new PhoneRiskProfileService(
            _dbContext,
            normalizer.Object,
            velocity.Object,
            NullLogger<PhoneRiskProfileService>.Instance);
    }

    [Fact]
    public async Task IsBlockedAsync_WhenSamePhoneBlockedInOtherTenant_ShouldNotCrossTenantBlock()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await AddPhoneProfileAsync(tenantA, isBlocked: true);
        _tenantContext.TenantId = tenantB;

        var isBlocked = await _service.IsBlockedAsync(tenantB, "0100 000 0001");
        var status = await _service.GetStatusAsync(tenantB, "0100 000 0001");
        var list = await _service.ListBlockedAsync(
            tenantB,
            new AdminBlockedPhoneQuery { Page = 1, PageSize = 20 });

        Assert.False(isBlocked);
        Assert.Null(status);
        Assert.Empty(list.Items);
    }

    [Fact]
    public async Task BlockAsync_WhenSamePhoneExistsInOtherTenant_ShouldCreateTenantScopedProfile()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await AddPhoneProfileAsync(tenantA, isBlocked: false);
        _tenantContext.TenantId = tenantB;

        var result = await _service.BlockAsync(
            tenantB,
            "0100-000-0001",
            "Tenant B policy decision.",
            blockedByUserId: null);

        var profiles = await _dbContext.PhoneRiskProfiles
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(profile => profile.PhoneNumber == NormalizedPhone)
            .OrderBy(profile => profile.TenantId)
            .ToListAsync();

        Assert.True(result.IsBlocked);
        Assert.Equal(2, profiles.Count);
        Assert.False(profiles.Single(profile => profile.TenantId == tenantA).IsBlocked);
        Assert.True(profiles.Single(profile => profile.TenantId == tenantB).IsBlocked);
    }

    [Fact]
    public async Task ReportSpamAsync_WhenSamePhoneReportedAcrossTenants_ShouldKeepTenantCountsIsolated()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var tenantAOrderId = await AddOrderAsync(tenantA);
        var tenantBOrderId = await AddOrderAsync(tenantB);

        _tenantContext.TenantId = tenantA;
        var tenantAStatus = await _service.ReportSpamAsync(
            tenantA,
            tenantAOrderId,
            reportedByUserId: null);

        _tenantContext.TenantId = tenantB;
        var tenantBStatus = await _service.ReportSpamAsync(
            tenantB,
            tenantBOrderId,
            reportedByUserId: null);

        var profiles = await _dbContext.PhoneRiskProfiles
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(profile => profile.PhoneNumber == NormalizedPhone)
            .ToListAsync();
        var globalReputation = await _dbContext.GlobalPhoneReputations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(reputation => reputation.PhoneNumber == NormalizedPhone);

        Assert.Equal(1, tenantAStatus.SpamReports);
        Assert.Equal(2, tenantBStatus.GlobalSpamReports);
        Assert.False(tenantAStatus.IsBlocked);
        Assert.False(tenantBStatus.IsBlocked);
        Assert.Equal(2, profiles.Count);
        Assert.All(profiles, profile => Assert.Equal(1, profile.SpamReports));
        Assert.Equal(2, globalReputation.SpamReports);
    }

    [Fact]
    public async Task ReportSpamAsync_ShouldUseTenantOwnedOrderPhone()
    {
        var tenantId = Guid.NewGuid();
        var orderId = await AddOrderAsync(tenantId);
        _tenantContext.TenantId = tenantId;

        await _service.ReportSpamAsync(tenantId, orderId, reportedByUserId: null);

        var report = await _dbContext.PhoneSpamReports.AsNoTracking().SingleAsync();
        Assert.Equal(tenantId, report.TenantId);
        Assert.Equal(orderId, report.OrderId);
        Assert.Equal(NormalizedPhone, report.PhoneNumber);
    }

    [Fact]
    public async Task OrderPhoneResolution_WhenAccountPhoneMissing_ShouldUseGuestSnapshot()
    {
        var tenantId = Guid.NewGuid();
        _tenantContext.TenantId = tenantId;
        var onlineCustomer = new OnlineCustomer
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Email = "fallback@example.test",
            NormalizedEmail = "fallback@example.test",
            Phone = null,
        };
        _dbContext.OnlineCustomers.Add(onlineCustomer);
        await _dbContext.SaveChangesAsync();
        var orderId = await AddOrderAsync(tenantId, onlineCustomerId: onlineCustomer.Id);

        var trust = await _service.GetOrderCustomerTrustAsync(tenantId, orderId);
        await _service.ReportSpamAsync(tenantId, orderId, reportedByUserId: null);

        Assert.NotNull(trust);
        Assert.True(trust.HasPhone);
        Assert.Equal(NormalizedPhone, trust.PhoneNumber);
        var report = await _dbContext.PhoneSpamReports.AsNoTracking().SingleAsync();
        Assert.Equal(orderId, report.OrderId);
        Assert.Equal(NormalizedPhone, report.PhoneNumber);
    }

    [Fact]
    public async Task GetOrderCustomerTrustAsync_ShouldKeepOrderHistoryTenantScoped()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var currentOrderId = await AddOrderAsync(tenantA);
        await AddOrderAsync(tenantA, PointOfSale.Core.Enums.OrderStatus.Cancelled);
        await AddOrderAsync(tenantA, PointOfSale.Core.Enums.OrderStatus.Cancelled);
        await AddOrderAsync(tenantA, PointOfSale.Core.Enums.OrderStatus.Delivered);
        await AddOrderAsync(tenantB, PointOfSale.Core.Enums.OrderStatus.Cancelled);

        _tenantContext.TenantId = tenantA;
        var result = await _service.GetOrderCustomerTrustAsync(tenantA, currentOrderId);
        _tenantContext.TenantId = tenantB;
        var foreignResult = await _service.GetOrderCustomerTrustAsync(tenantB, currentOrderId);

        Assert.NotNull(result);
        Assert.Equal(NormalizedPhone, result.PhoneNumber);
        Assert.Equal(3, result.PreviousOrdersCount);
        Assert.Equal(2, result.CancelledOrdersCount);
        Assert.Equal("SUSPICIOUS", result.RiskLevel);
        Assert.Contains("HIGH_CANCELLATION_RATE", result.RiskSignals);
        Assert.Null(foreignResult);
    }

    [Fact]
    public async Task ReportSpamAsync_WhenOrderBelongsToAnotherTenant_ShouldRejectMutation()
    {
        var owningTenantId = Guid.NewGuid();
        var foreignTenantId = Guid.NewGuid();
        var orderId = await AddOrderAsync(owningTenantId);
        _tenantContext.TenantId = foreignTenantId;

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.ReportSpamAsync(foreignTenantId, orderId, reportedByUserId: null));

        Assert.Empty(await _dbContext.PhoneSpamReports.AsNoTracking().ToListAsync());
    }

    private async Task<Guid> AddOrderAsync(
        Guid tenantId,
        PointOfSale.Core.Enums.OrderStatus status = PointOfSale.Core.Enums.OrderStatus.Pending,
        Guid? onlineCustomerId = null)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderNumber = $"ORDER-{Guid.NewGuid():N}",
            Source = PointOfSale.Core.Enums.OrderSource.Ecommerce,
            Status = status,
            OnlineCustomerId = onlineCustomerId,
            GuestPhone = "0100 000 0001",
            ShippingAddress = "Test address",
            Currency = "EGP",
            CreatedAt = DateTime.UtcNow,
        };
        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync();
        return order.Id;
    }

    private async Task AddPhoneProfileAsync(Guid tenantId, bool isBlocked)
    {
        _dbContext.PhoneRiskProfiles.Add(new PhoneRiskProfile
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            PhoneNumber = NormalizedPhone,
            IsBlocked = isBlocked,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            BlockedAtUtc = isBlocked ? DateTime.UtcNow : null,
            BlockedReason = isBlocked ? "Tenant policy decision." : null
        });

        await _dbContext.SaveChangesAsync();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private sealed class MutableTenantContext : ITenantContext
    {
        public Guid? TenantId { get; set; }
    }
}
