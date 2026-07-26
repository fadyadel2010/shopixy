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

    public PhoneRiskProfileServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(options);

        var normalizer = new Mock<IPhoneNumberNormalizer>();
        normalizer
            .Setup(item => item.NormalizeAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(NormalizedPhone);

        _service = new PhoneRiskProfileService(
            _dbContext,
            normalizer.Object,
            NullLogger<PhoneRiskProfileService>.Instance);
    }

    [Fact]
    public async Task IsBlockedAsync_WhenSamePhoneBlockedInOtherTenant_ShouldNotCrossTenantBlock()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await AddPhoneProfileAsync(tenantA, isBlocked: true);

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

        var tenantAStatus = await _service.ReportSpamAsync(
            tenantA,
            Guid.NewGuid(),
            "0100 000 0001",
            reportedByUserId: null);

        var tenantBStatus = await _service.ReportSpamAsync(
            tenantB,
            Guid.NewGuid(),
            "0100 000 0001",
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
}
