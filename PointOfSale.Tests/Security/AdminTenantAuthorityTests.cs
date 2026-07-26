using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Security;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Security;

public sealed class AdminTenantAuthorityTests
{
    [Fact]
    public async Task ValidateTenantActorAsync_AllowsCurrentSqlAdminForTrustedTenant()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var context = CreateDbContext(tenantId);
        context.Tenants.Add(ActiveTenant(tenantId));
        context.Users.Add(User(userId, tenantId, UserRole.Admin));
        await context.SaveChangesAsync();

        var authority = new AdminTenantAuthority(context);
        var result = await authority.ValidateTenantActorAsync(
            Principal(userId, tenantId, "Admin"),
            tenantId,
            RoleSets("Owner,Admin"),
            CancellationToken.None);

        Assert.True(result.Authorized);
        Assert.Equal("current_sql_actor_authority", result.Reason);
        Assert.Equal("Admin", result.CurrentRole);
    }

    [Fact]
    public async Task ValidateTenantActorAsync_RejectsStaleAdminTokenAfterRoleDowngrade()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var context = CreateDbContext(tenantId);
        context.Tenants.Add(ActiveTenant(tenantId));
        context.Users.Add(User(userId, tenantId, UserRole.Manager));
        await context.SaveChangesAsync();

        var authority = new AdminTenantAuthority(context);
        var result = await authority.ValidateTenantActorAsync(
            Principal(userId, tenantId, "Admin"),
            tenantId,
            RoleSets("Owner,Admin"),
            CancellationToken.None);

        Assert.False(result.Authorized);
        Assert.Equal("current_actor_role_not_authorized", result.Reason);
        Assert.Equal("Manager", result.CurrentRole);
    }

    [Fact]
    public async Task ValidateTenantActorAsync_RejectsCrossTenantActor()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var context = CreateDbContext(tenantId);
        context.Tenants.AddRange(ActiveTenant(tenantId), ActiveTenant(otherTenantId));
        context.Users.Add(User(userId, otherTenantId, UserRole.Admin));
        await context.SaveChangesAsync();

        var authority = new AdminTenantAuthority(context);
        var result = await authority.ValidateTenantActorAsync(
            Principal(userId, tenantId, "Admin"),
            tenantId,
            RoleSets("Owner,Admin"),
            CancellationToken.None);

        Assert.False(result.Authorized);
        Assert.Equal("current_actor_tenant_mismatch", result.Reason);
    }

    [Fact]
    public async Task ValidateTenantActorAsync_RejectsInactiveTenantAndInactiveActor()
    {
        var tenantId = Guid.NewGuid();
        var inactiveActorId = Guid.NewGuid();
        await using var inactiveActorContext = CreateDbContext(tenantId);
        inactiveActorContext.Tenants.Add(ActiveTenant(tenantId));
        inactiveActorContext.Users.Add(User(inactiveActorId, tenantId, UserRole.Admin, isActive: false));
        await inactiveActorContext.SaveChangesAsync();

        var inactiveActorResult = await new AdminTenantAuthority(inactiveActorContext).ValidateTenantActorAsync(
            Principal(inactiveActorId, tenantId, "Admin"),
            tenantId,
            RoleSets("Owner,Admin"),
            CancellationToken.None);

        Assert.False(inactiveActorResult.Authorized);
        Assert.Equal("inactive_or_deleted_actor", inactiveActorResult.Reason);

        var inactiveTenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var inactiveTenantContext = CreateDbContext(inactiveTenantId);
        inactiveTenantContext.Tenants.Add(ActiveTenant(inactiveTenantId, isActive: false));
        inactiveTenantContext.Users.Add(User(userId, inactiveTenantId, UserRole.Admin));
        await inactiveTenantContext.SaveChangesAsync();

        var inactiveTenantResult = await new AdminTenantAuthority(inactiveTenantContext).ValidateTenantActorAsync(
            Principal(userId, inactiveTenantId, "Admin"),
            inactiveTenantId,
            RoleSets("Owner,Admin"),
            CancellationToken.None);

        Assert.False(inactiveTenantResult.Authorized);
        Assert.Equal("tenant_inactive_or_deleted", inactiveTenantResult.Reason);
    }

    [Fact]
    public void TenantActorRoles_MustMatchAssignableUserRoleModel()
    {
        var assignableRoles = Enum.GetNames<UserRole>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(assignableRoles, AdminTenantAuthority.TenantActorRoles);
        Assert.DoesNotContain("Accountant", AdminTenantAuthority.TenantActorRoles);
    }

    [Fact]
    public void AdminOperationalEndpoints_DoNotUseRawTenantHeaderAsAuthority()
    {
        var tenantScopedAdminControllers = new[]
        {
            ApiPath("Controllers", "AdminDataSyncController.cs"),
            ApiPath("Controllers", "AdminSearchSyncController.cs"),
            ApiPath("Controllers", "AdminOrdersController.cs"),
            ApiPath("Controllers", "AdminShipmentsController.cs"),
            ApiPath("Controllers", "AdminInventoryController.cs"),
            ApiPath("Controllers", "AdminStoreConfigController.cs"),
            ApiPath("Controllers", "AdminCmsPagesController.cs")
        };

        foreach (var controllerPath in tenantScopedAdminControllers)
        {
            var source = File.ReadAllText(controllerPath);
            Assert.DoesNotContain("Request.Headers.TryGetValue(TenantIdHeader", source, StringComparison.Ordinal);
            Assert.Contains("_tenantContext.RequiredTenantId", source, StringComparison.Ordinal);
        }

        var storeConfig = File.ReadAllText(ApiPath("Controllers", "AdminStoreConfigController.cs"));
        var cmsPages = File.ReadAllText(ApiPath("Controllers", "AdminCmsPagesController.cs"));
        Assert.Contains("[Authorize(Roles = \"Owner,Admin\")]", storeConfig, StringComparison.Ordinal);
        Assert.Contains("[Authorize(Roles = \"Owner,Admin\")]", cmsPages, StringComparison.Ordinal);
    }

    [Fact]
    public void TenantAdminGlobalOperationalControls_AreBlockedOrTenantScoped()
    {
        var searchReindex = File.ReadAllText(ApiPath("Controllers", "SearchReindexController.cs"));
        var affiliateReindexController = File.ReadAllText(ApiPath("Controllers", "Admin", "AF16E_ReindexController.cs"));
        var affiliateReindexScript = File.ReadAllText(InfrastructurePath("Scripts", "AF16E_ReindexAffiliateProductsWithStorefrontUrl.cs"));

        Assert.Contains("GLOBAL_SEARCH_CONTROL_FORBIDDEN", searchReindex, StringComparison.Ordinal);
        Assert.Contains("ReindexTenantAsync", affiliateReindexController, StringComparison.Ordinal);
        Assert.Contains("ReindexTenantAsync(Guid tenantId", affiliateReindexScript, StringComparison.Ordinal);
        Assert.Contains("d.TenantId, tenantScopeId.Value", affiliateReindexScript, StringComparison.Ordinal);
    }

    private static ApplicationDbContext CreateDbContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options, new TestTenantContext(tenantId));
    }

    private static ClaimsPrincipal Principal(Guid userId, Guid tenantId, string tokenRole) =>
        new(new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString("D")),
                new Claim(ClaimTypes.Role, tokenRole),
                new Claim("tenant_id", tenantId.ToString("D"))
            },
            "TestAuth"));

    private static IReadOnlyCollection<IReadOnlySet<string>> RoleSets(params string[] roleSets) =>
        roleSets
            .Select(roles => roles
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase))
            .Cast<IReadOnlySet<string>>()
            .ToArray();

    private static Tenant ActiveTenant(Guid tenantId, bool isActive = true) =>
        new()
        {
            Id = tenantId,
            Name = $"tenant-{tenantId:N}",
            Slug = $"tenant-{tenantId:N}",
            IsActive = isActive,
            Status = TenantStatus.Active
        };

    private static User User(Guid userId, Guid tenantId, UserRole role, bool isActive = true) =>
        new()
        {
            Id = userId,
            TenantId = tenantId,
            Username = $"user-{userId:N}",
            Email = $"user-{userId:N}@example.test",
            PasswordHash = "hash",
            FirstName = "Test",
            LastName = "Actor",
            Role = role,
            IsActive = isActive,
            CreatedAt = DateTime.UtcNow
        };

    private static string ApiPath(params string[] parts) =>
        Path.GetFullPath(Path.Combine(new[] { AppContext.BaseDirectory, "..", "..", "..", "..", "PointOfSale.API" }.Concat(parts).ToArray()));

    private static string InfrastructurePath(params string[] parts) =>
        Path.GetFullPath(Path.Combine(new[] { AppContext.BaseDirectory, "..", "..", "..", "..", "PointOfSale.Infrastructure" }.Concat(parts).ToArray()));
}
