using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MultiTenancy;

namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubGovernanceRefreshRevocationTests
{
    [Fact]
    public async Task VendorSuspension_ClearsAllTenantPosRefreshBindingsAndWritesEvidence()
    {
        var tenantId = Guid.NewGuid();
        await using var context = Context(tenantId);
        context.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "governance-refresh-tenant",
            Slug = "governance-refresh-tenant",
            IsActive = true,
            Status = TenantStatus.Active
        });
        context.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Username = "governance.admin",
            Email = "governance.admin@example.test",
            PasswordHash = "hash",
            FirstName = "Governance",
            LastName = "Admin",
            Role = UserRole.Admin,
            IsActive = true,
            RefreshToken = "current-refresh-binding",
            RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow
        });
        context.UserGovernanceProfiles.Add(new UserGovernanceProfile
        {
            Id = Guid.NewGuid(),
            SubjectId = tenantId,
            Role = UserGovernanceRole.Vendor,
            Status = UserGovernanceStatus.Active,
            CreatedAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var profile = await context.UserGovernanceProfiles.SingleAsync();
        profile.Status = UserGovernanceStatus.Suspended;
        profile.StatusChangedAtUtc = DateTime.UtcNow;
        await context.SaveChangesAsync();

        var user = await context.Users.IgnoreQueryFilters().SingleAsync();
        Assert.Null(user.RefreshToken);
        Assert.Null(user.RefreshTokenExpiryTime);
        Assert.Contains(
            await context.AuditLogs.Select(log => log.AfterDataJson).ToListAsync(),
            json => json?.Contains("VENDOR_GOVERNANCE_RESTRICTED", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void GovernanceFreeze_RequiresSuspendedVendorRefreshAuthorityToFailClosed()
    {
        var governanceService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Governance/UserGovernanceService.cs");
        var sessionAuthority = CertificationSource.ReadRepoFile("PointOfSale.API/Security/PosSessionAuthority.cs");

        Assert.Contains("evaluation.Status == UserGovernanceStatus.Suspended", governanceService, StringComparison.Ordinal);
        Assert.Contains("governanceStatus == UserGovernanceStatus.Banned || governanceStatus == UserGovernanceStatus.Suspended", sessionAuthority, StringComparison.Ordinal);
    }

    private static ApplicationDbContext Context(Guid tenantId)
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId);

        return new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .AddInterceptors(new PrivilegedRefreshTokenRevocationInterceptor(
                    NullLogger<PrivilegedRefreshTokenRevocationInterceptor>.Instance))
                .Options,
            tenantContext);
    }
}
