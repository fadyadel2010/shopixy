using System.Diagnostics.Metrics;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Security;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Security;

public sealed class PosSessionAuthorityTests
{
    private const string CurrentBinding = "67ef1df995cb592dc9be8a0123d42e733840847413abeb32a72835a95f7d28bc";

    [Fact]
    public async Task ValidateAccessTokenAsync_AllowsCurrentSqlBoundPosSession()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var context = Context(tenantId);
        context.Tenants.Add(Tenant(tenantId));
        context.Users.Add(User(userId, tenantId, UserRole.Admin));
        await context.SaveChangesAsync();

        var result = await new PosSessionAuthority(context).ValidateAccessTokenAsync(
            Principal(userId, tenantId, "Admin", CurrentBinding),
            CancellationToken.None);

        Assert.True(result.Authorized);
        Assert.Equal("current_sql_pos_session_authority", result.Reason);
    }

    [Fact]
    public async Task ValidateAccessTokenAsync_RejectsLogoutOrRefreshRotatedSession()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var context = Context(tenantId);
        context.Tenants.Add(Tenant(tenantId));
        context.Users.Add(User(userId, tenantId, UserRole.Admin, refreshBinding: "rotated-current-binding"));
        await context.SaveChangesAsync();

        var stale = await new PosSessionAuthority(context).ValidateAccessTokenAsync(
            Principal(userId, tenantId, "Admin", CurrentBinding),
            CancellationToken.None);

        var user = await context.Users.IgnoreQueryFilters().SingleAsync();
        user.RefreshToken = null;
        await context.SaveChangesAsync();

        var revoked = await new PosSessionAuthority(context).ValidateAccessTokenAsync(
            Principal(userId, tenantId, "Admin", "rotated-current-binding"),
            CancellationToken.None);

        Assert.False(stale.Authorized);
        Assert.Equal("revoked_or_rotated_pos_session", stale.Reason);
        Assert.False(revoked.Authorized);
        Assert.Equal("revoked_or_rotated_pos_session", revoked.Reason);
    }

    [Fact]
    public async Task ValidateAccessTokenAsync_RejectsLegacyTokenWithoutSqlSessionBinding()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var context = Context(tenantId);
        context.Tenants.Add(Tenant(tenantId));
        context.Users.Add(User(userId, tenantId, UserRole.Admin));
        await context.SaveChangesAsync();

        var result = await new PosSessionAuthority(context).ValidateAccessTokenAsync(
            PrincipalWithoutSessionBinding(userId, tenantId, "Admin"),
            CancellationToken.None);

        Assert.False(result.Authorized);
        Assert.Equal("missing_pos_session_binding", result.Reason);
    }

    [Fact]
    public async Task ValidateAccessTokenAsync_RejectsExpiredCurrentSqlSessionBinding()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var context = Context(tenantId);
        context.Tenants.Add(Tenant(tenantId));
        context.Users.Add(User(
            userId,
            tenantId,
            UserRole.Admin,
            refreshBinding: CurrentBinding,
            refreshExpiryUtc: DateTime.UtcNow.AddMinutes(-1)));
        await context.SaveChangesAsync();

        var result = await new PosSessionAuthority(context).ValidateAccessTokenAsync(
            Principal(userId, tenantId, "Admin", CurrentBinding),
            CancellationToken.None);

        Assert.False(result.Authorized);
        Assert.Equal("revoked_or_rotated_pos_session", result.Reason);
    }

    [Fact]
    public async Task ValidateAccessTokenAsync_RejectsStaleRoleAndInactiveActorAuthority()
    {
        var tenantId = Guid.NewGuid();
        var roleChangedUserId = Guid.NewGuid();
        var inactiveUserId = Guid.NewGuid();
        await using var context = Context(tenantId);
        context.Tenants.Add(Tenant(tenantId));
        context.Users.Add(User(roleChangedUserId, tenantId, UserRole.Cashier));
        context.Users.Add(User(inactiveUserId, tenantId, UserRole.Admin, isActive: false));
        await context.SaveChangesAsync();

        var authority = new PosSessionAuthority(context);
        var staleRole = await authority.ValidateAccessTokenAsync(
            Principal(roleChangedUserId, tenantId, "Admin", CurrentBinding),
            CancellationToken.None);
        var inactive = await authority.ValidateAccessTokenAsync(
            Principal(inactiveUserId, tenantId, "Admin", CurrentBinding),
            CancellationToken.None);

        Assert.False(staleRole.Authorized);
        Assert.Equal("stale_actor_role_claim", staleRole.Reason);
        Assert.False(inactive.Authorized);
        Assert.Equal("inactive_or_deleted_actor", inactive.Reason);
    }

    [Fact]
    public async Task ValidateAccessTokenAsync_RejectsInactiveOrGovernanceBannedTenant()
    {
        var inactiveTenantId = Guid.NewGuid();
        var bannedTenantId = Guid.NewGuid();
        var suspendedTenantId = Guid.NewGuid();
        var inactiveTenantUserId = Guid.NewGuid();
        var bannedTenantUserId = Guid.NewGuid();
        var suspendedTenantUserId = Guid.NewGuid();
        await using var context = Context(inactiveTenantId);
        context.Tenants.Add(Tenant(inactiveTenantId, isActive: false));
        context.Tenants.Add(Tenant(bannedTenantId));
        context.Tenants.Add(Tenant(suspendedTenantId));
        context.Users.Add(User(inactiveTenantUserId, inactiveTenantId, UserRole.Admin));
        context.Users.Add(User(bannedTenantUserId, bannedTenantId, UserRole.Admin));
        context.Users.Add(User(suspendedTenantUserId, suspendedTenantId, UserRole.Admin));
        context.UserGovernanceProfiles.Add(new UserGovernanceProfile
        {
            Id = Guid.NewGuid(),
            SubjectId = bannedTenantId,
            Role = UserGovernanceRole.Vendor,
            Status = UserGovernanceStatus.Banned,
            CreatedAtUtc = DateTime.UtcNow
        });
        context.UserGovernanceProfiles.Add(new UserGovernanceProfile
        {
            Id = Guid.NewGuid(),
            SubjectId = suspendedTenantId,
            Role = UserGovernanceRole.Vendor,
            Status = UserGovernanceStatus.Suspended,
            CreatedAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var authority = new PosSessionAuthority(context);
        var inactiveTenant = await authority.ValidateAccessTokenAsync(
            Principal(inactiveTenantUserId, inactiveTenantId, "Admin", CurrentBinding),
            CancellationToken.None);
        var bannedTenant = await authority.ValidateAccessTokenAsync(
            Principal(bannedTenantUserId, bannedTenantId, "Admin", CurrentBinding),
            CancellationToken.None);
        var suspendedTenant = await authority.ValidateAccessTokenAsync(
            Principal(suspendedTenantUserId, suspendedTenantId, "Admin", CurrentBinding),
            CancellationToken.None);

        Assert.False(inactiveTenant.Authorized);
        Assert.Equal("tenant_inactive_or_deleted", inactiveTenant.Reason);
        Assert.False(bannedTenant.Authorized);
        Assert.Equal("tenant_governance_banned", bannedTenant.Reason);
        Assert.False(suspendedTenant.Authorized);
        Assert.Equal("tenant_governance_banned", suspendedTenant.Reason);
    }

    [Fact]
    public async Task ValidateAccessTokenAsync_EmitsBoundedRevokedSessionDiagnostic()
    {
        var measurements = new List<(string MetricName, KeyValuePair<string, object?>[] Tags)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == OperationalConvergenceTelemetry.MeterName)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
        {
            measurements.Add((instrument.Name, tags.ToArray()));
        });
        listener.Start();

        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var context = Context(tenantId);
        context.Tenants.Add(Tenant(tenantId));
        context.Users.Add(User(userId, tenantId, UserRole.Admin, refreshBinding: "rotated-current-binding"));
        await context.SaveChangesAsync();

        var result = await new PosSessionAuthority(context).ValidateAccessTokenAsync(
            Principal(userId, tenantId, "Admin", CurrentBinding),
            CancellationToken.None);

        Assert.False(result.Authorized);
        Assert.Contains(measurements, measurement =>
            measurement.MetricName == "operational_authority_failure_total" &&
            Tag(measurement.Tags, "surface") == "pos_session_authority" &&
            Tag(measurement.Tags, "authority") == "pos_session" &&
            Tag(measurement.Tags, "reason") == "session_revoked_or_rotated");
    }

    private static ApplicationDbContext Context(Guid tenantId) =>
        new(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options,
            new TestTenantContext(tenantId));

    private static Tenant Tenant(Guid tenantId, bool isActive = true) =>
        new()
        {
            Id = tenantId,
            Name = $"tenant-{tenantId:N}",
            Slug = $"tenant-{tenantId:N}",
            IsActive = isActive,
            Status = TenantStatus.Active
        };

    private static User User(
        Guid userId,
        Guid tenantId,
        UserRole role,
        bool isActive = true,
        string refreshBinding = CurrentBinding,
        DateTime? refreshExpiryUtc = null) =>
        new()
        {
            Id = userId,
            TenantId = tenantId,
            Username = $"user-{userId:N}",
            Email = $"user-{userId:N}@example.test",
            PasswordHash = "hash",
            FirstName = "Current",
            LastName = "Actor",
            Role = role,
            IsActive = isActive,
            RefreshToken = refreshBinding,
            RefreshTokenExpiryTime = refreshExpiryUtc ?? DateTime.UtcNow.AddMinutes(20),
            CreatedAt = DateTime.UtcNow
        };

    private static ClaimsPrincipal Principal(Guid userId, Guid tenantId, string role, string binding) =>
        new(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString("D")),
                new Claim(ClaimTypes.Role, role),
                new Claim("tenant_id", tenantId.ToString("D")),
                new Claim(PosSecurityConstants.SessionBindingClaim, binding)
            ],
            "Bearer"));

    private static ClaimsPrincipal PrincipalWithoutSessionBinding(Guid userId, Guid tenantId, string role) =>
        new(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString("D")),
                new Claim(ClaimTypes.Role, role),
                new Claim("tenant_id", tenantId.ToString("D"))
            ],
            "Bearer"));

    private static string? Tag(KeyValuePair<string, object?>[] tags, string name) =>
        tags.FirstOrDefault(tag => tag.Key == name).Value?.ToString();
}
