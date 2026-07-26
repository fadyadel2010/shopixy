using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MultiTenancy;

namespace PointOfSale.Tests.Architecture;

public sealed class PosRoleAuthorityCertificationTests
{
    [Fact]
    public async Task RuntimeRoleMutation_RevokesCurrentRefreshAuthority()
    {
        var tenantId = Guid.NewGuid();
        await using var context = Context(tenantId);
        context.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "role-authority-tenant",
            Slug = $"role-authority-{tenantId:N}",
            IsActive = true,
            Status = TenantStatus.Active
        });
        context.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Username = "role.authority.admin",
            Email = "role.authority.admin@example.test",
            PasswordHash = "hash",
            FirstName = "Role",
            LastName = "Authority",
            Role = UserRole.Admin,
            IsActive = true,
            RefreshToken = "current-refresh-binding",
            RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var user = await context.Users.IgnoreQueryFilters().SingleAsync();
        user.Role = UserRole.Cashier;
        await context.SaveChangesAsync();

        Assert.Null(user.RefreshToken);
        Assert.Null(user.RefreshTokenExpiryTime);
        Assert.Contains(
            await context.AuditLogs.Select(log => log.AfterDataJson).ToListAsync(),
            json => json?.Contains("POS_USER_AUTHORITY_CHANGED", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void JwtRoleClaims_MustBeRevalidatedAgainstCurrentSqlRole()
    {
        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");
        var authority = CertificationSource.ReadRepoFile("PointOfSale.API/Security/PosSessionAuthority.cs");
        var revocation = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/Data/PrivilegedRefreshTokenRevocationInterceptor.cs");

        Assert.Contains("OnTokenValidated = async context =>", program, StringComparison.Ordinal);
        Assert.Contains("ValidateAccessTokenAsync", program, StringComparison.Ordinal);
        Assert.Contains("actor.Role.ToString()", authority, StringComparison.Ordinal);
        Assert.Contains("stale_actor_role_claim", authority, StringComparison.Ordinal);
        Assert.Contains("entry.Property(nameof(User.Role)).IsModified", revocation, StringComparison.Ordinal);
        Assert.Contains("user.RefreshToken = null", revocation, StringComparison.Ordinal);
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
