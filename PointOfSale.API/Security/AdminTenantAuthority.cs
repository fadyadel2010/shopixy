using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.SuperAdmin.Models;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.API.Security;

public sealed record AdminTenantAuthorityResult(
    bool Authorized,
    string Reason,
    Guid? ActorUserId = null,
    Guid? TenantId = null,
    string? CurrentRole = null)
{
    public static AdminTenantAuthorityResult Allow(Guid actorUserId, Guid tenantId, string currentRole) =>
        new(true, "current_sql_actor_authority", actorUserId, tenantId, currentRole);

    public static AdminTenantAuthorityResult Deny(string reason, Guid? actorUserId = null, Guid? tenantId = null, string? currentRole = null) =>
        new(false, reason, actorUserId, tenantId, currentRole);
}

public interface IAdminTenantAuthority
{
    Task<AdminTenantAuthorityResult> ValidateTenantActorAsync(
        ClaimsPrincipal principal,
        Guid? trustedTenantId,
        IReadOnlyCollection<IReadOnlySet<string>> requiredRoleSets,
        CancellationToken cancellationToken);
}

/// <summary>
/// SQL-backed actor authority for tenant-scoped admin and operational endpoints.
/// JWT roles and tenant claims are evidence only; current PostgreSQL user and tenant
/// rows decide whether the actor still owns the requested tenant scope.
/// </summary>
public sealed class AdminTenantAuthority : IAdminTenantAuthority
{
    public static readonly ISet<string> TenantActorRoles = new HashSet<string>(
        new[] { "Owner", "Admin", "Manager", "Cashier", "Warehouse" },
        StringComparer.OrdinalIgnoreCase);

    private readonly ApplicationDbContext _dbContext;

    public AdminTenantAuthority(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<AdminTenantAuthorityResult> ValidateTenantActorAsync(
        ClaimsPrincipal principal,
        Guid? trustedTenantId,
        IReadOnlyCollection<IReadOnlySet<string>> requiredRoleSets,
        CancellationToken cancellationToken)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            RecordDenied("admin_tenant_authority", "unauthenticated_actor", trustedTenantId);
            return AdminTenantAuthorityResult.Deny("unauthenticated_actor");
        }

        if (trustedTenantId is null || trustedTenantId.Value == Guid.Empty)
        {
            RecordDenied("admin_tenant_authority", "missing_trusted_tenant_context", trustedTenantId);
            return AdminTenantAuthorityResult.Deny("missing_trusted_tenant_context");
        }

        if (requiredRoleSets.Count == 0)
        {
            RecordDenied("admin_tenant_authority", "missing_admin_role_boundary", trustedTenantId);
            return AdminTenantAuthorityResult.Deny("missing_admin_role_boundary", tenantId: trustedTenantId);
        }

        if (!TryGetUserId(principal, out var actorUserId))
        {
            RecordDenied("admin_tenant_authority", "missing_actor_user_id", trustedTenantId);
            return AdminTenantAuthorityResult.Deny("missing_actor_user_id", tenantId: trustedTenantId);
        }

        if (!TryGetTenantClaim(principal, out var tokenTenantId))
        {
            RecordDenied("admin_tenant_authority", "missing_actor_tenant_claim", trustedTenantId);
            return AdminTenantAuthorityResult.Deny("missing_actor_tenant_claim", actorUserId, trustedTenantId);
        }

        if (tokenTenantId != trustedTenantId.Value)
        {
            RecordDenied("admin_tenant_authority", "actor_tenant_claim_mismatch", trustedTenantId);
            return AdminTenantAuthorityResult.Deny("actor_tenant_claim_mismatch", actorUserId, trustedTenantId);
        }

        var actor = await _dbContext.Users
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(user => user.Id == actorUserId)
            .Select(user => new
            {
                user.TenantId,
                user.Role,
                user.IsActive,
                user.IsDeleted
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (actor == null)
        {
            RecordDenied("admin_tenant_authority", "missing_current_actor_sql_authority", trustedTenantId);
            return AdminTenantAuthorityResult.Deny("missing_current_actor_sql_authority", actorUserId, trustedTenantId);
        }

        var currentRole = actor.Role.ToString();
        if (actor.IsDeleted || !actor.IsActive)
        {
            RecordDenied("admin_tenant_authority", "inactive_or_deleted_actor", trustedTenantId);
            return AdminTenantAuthorityResult.Deny("inactive_or_deleted_actor", actorUserId, trustedTenantId, currentRole);
        }

        if (actor.TenantId != trustedTenantId.Value)
        {
            RecordDenied("admin_tenant_authority", "current_actor_tenant_mismatch", trustedTenantId);
            return AdminTenantAuthorityResult.Deny("current_actor_tenant_mismatch", actorUserId, trustedTenantId, currentRole);
        }

        if (!await TenantLineageAuthority.IsActiveTenantAsync(_dbContext, trustedTenantId.Value, cancellationToken))
        {
            RecordDenied("admin_tenant_authority", "tenant_inactive_or_deleted", trustedTenantId);
            return AdminTenantAuthorityResult.Deny("tenant_inactive_or_deleted", actorUserId, trustedTenantId, currentRole);
        }

        foreach (var requiredRoles in requiredRoleSets)
        {
            if (!requiredRoles.Contains(currentRole))
            {
                RecordDenied("admin_tenant_authority", "current_actor_role_not_authorized", trustedTenantId);
                return AdminTenantAuthorityResult.Deny("current_actor_role_not_authorized", actorUserId, trustedTenantId, currentRole);
            }
        }

        return AdminTenantAuthorityResult.Allow(actorUserId, trustedTenantId.Value, currentRole);
    }

    public static IReadOnlyList<IReadOnlySet<string>> ExtractTenantRoleSets(Endpoint? endpoint)
    {
        if (endpoint == null)
        {
            return Array.Empty<IReadOnlySet<string>>();
        }

        return endpoint.Metadata
            .GetOrderedMetadata<IAuthorizeData>()
            .Select(data => data.Roles)
            .Where(roles => !string.IsNullOrWhiteSpace(roles))
            .Select(roles => SplitRoles(roles!))
            .Where(roles => roles.Count > 0 && roles.Any(TenantActorRoles.Contains))
            .Cast<IReadOnlySet<string>>()
            .ToArray();
    }

    public static bool HasSuperAdminPolicy(Endpoint? endpoint)
    {
        return endpoint?.Metadata
            .GetOrderedMetadata<IAuthorizeData>()
            .Any(data => string.Equals(
                data.Policy,
                SuperAdminSecurityConstants.PolicyName,
                StringComparison.Ordinal)) == true;
    }

    private static HashSet<string> SplitRoles(string roles)
    {
        return roles
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier) ??
                    principal.FindFirstValue("sub");
        return Guid.TryParse(value, out userId) && userId != Guid.Empty;
    }

    private static bool TryGetTenantClaim(ClaimsPrincipal principal, out Guid tenantId)
    {
        var value = principal.FindFirstValue("tenant_id") ??
                    principal.FindFirstValue("TenantId") ??
                    principal.FindFirstValue("tenantId");
        return Guid.TryParse(value, out tenantId) && tenantId != Guid.Empty;
    }

    private static void RecordDenied(string surface, string reason, Guid? tenantId)
    {
        OperationalConvergenceTelemetry.RecordAuthorityFailure(
            surface,
            OperationalConvergenceAuthority.AdminTenant,
            reason == "tenant_inactive_or_deleted"
                ? OperationalConvergenceReason.TenantInactiveOrDeleted
                : OperationalConvergenceReason.ActorScopeRejected,
            tenantId);
    }
}
