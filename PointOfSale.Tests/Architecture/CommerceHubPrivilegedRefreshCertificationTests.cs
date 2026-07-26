using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MultiTenancy;

namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubPrivilegedRefreshCertificationTests
{
    [Fact]
    public async Task PosRoleChange_ClearsRefreshBindingAndWritesAuditEvidence()
    {
        var tenantId = Guid.NewGuid();
        await using var context = Context(tenantId);
        context.Tenants.Add(Tenant(tenantId));
        context.Users.Add(User(tenantId));
        await context.SaveChangesAsync();

        var user = await context.Users.IgnoreQueryFilters().SingleAsync();
        user.Role = UserRole.Cashier;
        await context.SaveChangesAsync();

        var persisted = await context.Users.IgnoreQueryFilters().SingleAsync();
        Assert.Null(persisted.RefreshToken);
        Assert.Null(persisted.RefreshTokenExpiryTime);

        var audit = await context.AuditLogs.SingleAsync(log =>
            log.ActionType == "PRIVILEGED_REFRESH_REVOKED" &&
            log.EntityType == "User");
        Assert.Equal(tenantId, audit.TenantId);
        Assert.Contains("POS_USER_AUTHORITY_CHANGED", audit.AfterDataJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TenantSuspension_ClearsTenantUserRefreshBindings()
    {
        var tenantId = Guid.NewGuid();
        await using var context = Context(tenantId);
        context.Tenants.Add(Tenant(tenantId));
        context.Users.Add(User(tenantId));
        await context.SaveChangesAsync();

        var tenant = await context.Tenants.SingleAsync();
        tenant.Status = TenantStatus.Suspended;
        tenant.IsActive = false;
        await context.SaveChangesAsync();

        var user = await context.Users.IgnoreQueryFilters().SingleAsync();
        Assert.Null(user.RefreshToken);
        Assert.Null(user.RefreshTokenExpiryTime);
        Assert.Contains(
            await context.AuditLogs.Select(log => log.AfterDataJson).ToListAsync(),
            json => json?.Contains("TENANT_AUTHORITY_REDUCED", StringComparison.Ordinal) == true);
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

    private static Tenant Tenant(Guid tenantId) =>
        new()
        {
            Id = tenantId,
            Name = "privileged-refresh-tenant",
            Slug = "privileged-refresh-tenant",
            IsActive = true,
            Status = TenantStatus.Active
        };

    private static User User(Guid tenantId) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Username = $"admin-{Guid.NewGuid():N}",
            Email = $"admin-{Guid.NewGuid():N}@example.test",
            PasswordHash = "hash",
            FirstName = "Privileged",
            LastName = "Refresh",
            Role = UserRole.Admin,
            IsActive = true,
            RefreshToken = "current-refresh-binding",
            RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow
        };
}
