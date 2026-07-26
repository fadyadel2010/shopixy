using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services.CommerceHub.Finance;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.CommerceHub;

public sealed class CommerceHubEarningsServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly CommerceHubEarningsService _service;

    public CommerceHubEarningsServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(options);
        var payoutReadService = new CommerceHubVendorPayoutReadService(
            _dbContext,
            Options.Create(new RefundSettings()),
            Options.Create(new CommerceHubPayoutSettings()),
            NullLogger<CommerceHubVendorPayoutReadService>.Instance);

        _service = new CommerceHubEarningsService(_dbContext, payoutReadService);
    }

    [Fact]
    public async Task GetVendorEarningsAsync_WhenVendorIsSuspended_ShouldReturnGovernanceBanner()
    {
        var vendorId = Guid.NewGuid();
        _dbContext.Tenants.Add(new Tenant
        {
            Id = vendorId,
            Name = "Suspended Vendor",
            Currency = "EGP",
            IsActive = true,
            Status = TenantStatus.Active
        });
        _dbContext.UserGovernanceProfiles.Add(new UserGovernanceProfile
        {
            Id = Guid.NewGuid(),
            SubjectId = vendorId,
            Role = UserGovernanceRole.Vendor,
            Status = UserGovernanceStatus.Suspended,
            StatusReason = "Compliance hold.",
            CreatedAtUtc = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetVendorEarningsAsync(vendorId);

        Assert.Equal("Suspended", result.Governance.Status);
        Assert.Equal("FulfillmentOnly", result.Governance.OperationalMode);
        Assert.True(result.Governance.ProductsHidden);
        Assert.True(result.Governance.BlocksPayouts);
        Assert.Equal("Compliance hold.", result.Governance.Reason);
        Assert.Equal("Your account is suspended. Your products are hidden from customers.", result.Governance.BannerMessage);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
