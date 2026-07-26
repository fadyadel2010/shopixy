using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Security;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Architecture;

public sealed class PosUserAuthorityCertificationTests
{
    private const string SessionBinding = "current-pos-session-binding";

    [Fact]
    public async Task DeletedOrMissingSqlUser_CannotRetainPosAuthority()
    {
        var tenantId = Guid.NewGuid();
        var deletedUserId = Guid.NewGuid();
        await using var context = Context(tenantId);
        context.Tenants.Add(ActiveTenant(tenantId));
        context.Users.Add(User(deletedUserId, tenantId, isDeleted: true));
        await context.SaveChangesAsync();

        var authority = new PosSessionAuthority(context);
        var deleted = await authority.ValidateAccessTokenAsync(
            Principal(deletedUserId, tenantId, "Admin"),
            CancellationToken.None);
        var missing = await authority.ValidateAccessTokenAsync(
            Principal(Guid.NewGuid(), tenantId, "Admin"),
            CancellationToken.None);

        Assert.False(deleted.Authorized);
        Assert.Equal("inactive_or_deleted_actor", deleted.Reason);
        Assert.False(missing.Authorized);
        Assert.Equal("missing_current_actor_sql_authority", missing.Reason);
    }

    [Fact]
    public void PosUserAuthority_MustRemainCurrentSqlState()
    {
        var authority = CertificationSource.ReadRepoFile("PointOfSale.API/Security/PosSessionAuthority.cs");

        Assert.Contains("_dbContext.Users", authority, StringComparison.Ordinal);
        Assert.Contains("user.IsActive", authority, StringComparison.Ordinal);
        Assert.Contains("user.IsDeleted", authority, StringComparison.Ordinal);
        Assert.Contains("missing_current_actor_sql_authority", authority, StringComparison.Ordinal);
        Assert.Contains("inactive_or_deleted_actor", authority, StringComparison.Ordinal);
        Assert.Contains("TenantLineageAuthority.IsActiveTenantAsync", authority, StringComparison.Ordinal);
        Assert.Contains("UserGovernanceRole.Vendor", authority, StringComparison.Ordinal);
    }

    private static ApplicationDbContext Context(Guid tenantId) =>
        new(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options,
            new TestTenantContext(tenantId));

    private static Tenant ActiveTenant(Guid tenantId) =>
        new()
        {
            Id = tenantId,
            Name = "user-authority-tenant",
            Slug = $"user-authority-{tenantId:N}",
            IsActive = true,
            Status = TenantStatus.Active
        };

    private static User User(Guid userId, Guid tenantId, bool isDeleted) =>
        new()
        {
            Id = userId,
            TenantId = tenantId,
            Username = $"user-{userId:N}",
            Email = $"user-{userId:N}@example.test",
            PasswordHash = "hash",
            FirstName = "POS",
            LastName = "User",
            Role = UserRole.Admin,
            IsActive = true,
            IsDeleted = isDeleted,
            RefreshToken = SessionBinding,
            RefreshTokenExpiryTime = DateTime.UtcNow.AddMinutes(20),
            CreatedAt = DateTime.UtcNow
        };

    private static ClaimsPrincipal Principal(Guid userId, Guid tenantId, string role) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString("D")),
            new Claim(ClaimTypes.Role, role),
            new Claim("tenant_id", tenantId.ToString("D")),
            new Claim(PosSecurityConstants.SessionBindingClaim, SessionBinding)
        ], "Bearer"));
}
