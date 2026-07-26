using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.Services.CommerceHub.Finance;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.CommerceHub;

public sealed class CommerceHubVendorPayoutMethodsServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly CommerceHubVendorPayoutMethodsService _service;

    public CommerceHubVendorPayoutMethodsServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(options);
        _service = new CommerceHubVendorPayoutMethodsService(_dbContext);
    }

    [Fact]
    public async Task CreateAsync_ShouldAutoDefaultFirstMethod_AndPromoteExplicitDefaultWithoutDuplicates()
    {
        var vendorId = await SeedVendorAsync("Payout Method Vendor");

        var first = await _service.CreateAsync(
            vendorId,
            new CommerceHubCreateVendorPayoutMethodRequestDto
            {
                MethodType = CommerceHubPayoutMethod.BankTransfer,
                DestinationValue = "EG-ACC-001",
                IsDefault = false
            },
            Guid.NewGuid());

        var duplicatePromotion = await _service.CreateAsync(
            vendorId,
            new CommerceHubCreateVendorPayoutMethodRequestDto
            {
                MethodType = CommerceHubPayoutMethod.BankTransfer,
                DestinationValue = "EG-ACC-001",
                IsDefault = true
            },
            Guid.NewGuid());

        var second = await _service.CreateAsync(
            vendorId,
            new CommerceHubCreateVendorPayoutMethodRequestDto
            {
                MethodType = CommerceHubPayoutMethod.VodafoneCash,
                DestinationValue = "01012345678",
                IsDefault = true
            },
            Guid.NewGuid());

        var methods = await _service.GetAsync(vendorId);

        Assert.True(first.IsDefault);
        Assert.Equal(first.Id, duplicatePromotion.Id);
        Assert.True(duplicatePromotion.IsDefault);
        Assert.Equal(2, methods.Count);
        Assert.Contains(methods, method => method.Id == first.Id && !method.IsDefault);
        Assert.Contains(methods, method => method.Id == second.Id && method.IsDefault);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private async Task<Guid> SeedVendorAsync(string name)
    {
        var vendorId = Guid.NewGuid();
        _dbContext.Tenants.Add(new Tenant
        {
            Id = vendorId,
            Name = name,
            Currency = "EGP",
            IsActive = true,
            Status = TenantStatus.Active
        });

        await _dbContext.SaveChangesAsync();
        return vendorId;
    }
}
