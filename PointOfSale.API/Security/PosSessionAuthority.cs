using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.API.Security;

public static class PosSecurityConstants
{
    public const string SessionBindingClaim = "pos_session";
    public const string SqlAuthorityClaim = "pos_sql_authority";
    public const string SqlAuthorityValue = "current";
}

public sealed record PosSessionAuthorityResult(bool Authorized, string Reason, Guid? UserId = null, Guid? TenantId = null)
{
    public static PosSessionAuthorityResult Allow(Guid userId, Guid tenantId) =>
        new(true, "current_sql_pos_session_authority", userId, tenantId);

    public static PosSessionAuthorityResult Deny(string reason, Guid? userId = null, Guid? tenantId = null) =>
        new(false, reason, userId, tenantId);
}

public interface IPosSessionAuthority
{
    Task<PosSessionAuthorityResult> ValidateAccessTokenAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken);
}

/// <summary>
/// Revalidates a POS access token against its current SQL session and actor scope.
/// Access-token claims identify the candidate session only; current SQL state decides access.
/// </summary>
public sealed class PosSessionAuthority : IPosSessionAuthority
{
    private readonly ApplicationDbContext _dbContext;

    public PosSessionAuthority(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PosSessionAuthorityResult> ValidateAccessTokenAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        if (!TryGetGuidClaim(principal, ClaimTypes.NameIdentifier, out var userId) ||
            !TryGetGuidClaim(principal, "tenant_id", out var tokenTenantId))
        {
            return Deny("invalid_actor_or_tenant_claim");
        }

        var tokenRole = principal.FindFirstValue(ClaimTypes.Role);
        var sessionBinding = principal.FindFirstValue(PosSecurityConstants.SessionBindingClaim);
        if (string.IsNullOrWhiteSpace(tokenRole) || string.IsNullOrWhiteSpace(sessionBinding))
        {
            return Deny("missing_pos_session_binding", userId, tokenTenantId);
        }

        var actor = await _dbContext.Users
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(user => user.Id == userId)
            .Select(user => new
            {
                user.TenantId,
                user.Role,
                user.IsActive,
                user.IsDeleted,
                user.RefreshToken,
                user.RefreshTokenExpiryTime
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (actor == null)
        {
            return Deny("missing_current_actor_sql_authority", userId, tokenTenantId);
        }

        if (actor.IsDeleted || !actor.IsActive)
        {
            return Deny("inactive_or_deleted_actor", userId, actor.TenantId);
        }

        if (actor.TenantId != tokenTenantId)
        {
            return Deny("current_actor_tenant_mismatch", userId, tokenTenantId);
        }

        if (!string.Equals(actor.Role.ToString(), tokenRole, StringComparison.OrdinalIgnoreCase))
        {
            return Deny("stale_actor_role_claim", userId, actor.TenantId);
        }

        if (actor.RefreshTokenExpiryTime is null ||
            actor.RefreshTokenExpiryTime <= DateTime.UtcNow ||
            !MatchesCurrentSession(sessionBinding, actor.RefreshToken))
        {
            return Deny("revoked_or_rotated_pos_session", userId, actor.TenantId);
        }

        if (!await TenantLineageAuthority.IsActiveTenantAsync(_dbContext, actor.TenantId, cancellationToken))
        {
            return Deny("tenant_inactive_or_deleted", userId, actor.TenantId);
        }

        var governanceStatus = await _dbContext.UserGovernanceProfiles
            .AsNoTracking()
            .Where(profile =>
                profile.SubjectId == actor.TenantId &&
                profile.Role == UserGovernanceRole.Vendor)
            .OrderByDescending(profile => profile.UpdatedAtUtc ?? profile.CreatedAtUtc)
            .Select(profile => (UserGovernanceStatus?)profile.Status)
            .FirstOrDefaultAsync(cancellationToken);

        if (governanceStatus == UserGovernanceStatus.Banned || governanceStatus == UserGovernanceStatus.Suspended)
        {
            return Deny("tenant_governance_banned", userId, actor.TenantId);
        }

        return PosSessionAuthorityResult.Allow(userId, actor.TenantId);
    }

    private static bool TryGetGuidClaim(ClaimsPrincipal principal, string claimType, out Guid value)
    {
        var claimValue = principal.FindFirstValue(claimType);
        return Guid.TryParse(claimValue, out value) && value != Guid.Empty;
    }

    private static bool MatchesCurrentSession(string presentedBinding, string? currentBinding)
    {
        if (string.IsNullOrWhiteSpace(currentBinding))
        {
            return false;
        }

        var presentedBytes = Encoding.UTF8.GetBytes(presentedBinding);
        var currentBytes = Encoding.UTF8.GetBytes(currentBinding);
        return presentedBytes.Length == currentBytes.Length &&
               CryptographicOperations.FixedTimeEquals(presentedBytes, currentBytes);
    }

    private static PosSessionAuthorityResult Deny(string reason, Guid? userId = null, Guid? tenantId = null)
    {
        OperationalConvergenceTelemetry.RecordAuthorityFailure(
            "pos_session_authority",
            OperationalConvergenceAuthority.PosSession,
            reason switch
            {
                "revoked_or_rotated_pos_session" => OperationalConvergenceReason.SessionRevokedOrRotated,
                "stale_actor_role_claim" => OperationalConvergenceReason.StaleActorRole,
                "tenant_inactive_or_deleted" => OperationalConvergenceReason.TenantInactiveOrDeleted,
                "tenant_governance_banned" => OperationalConvergenceReason.GovernanceRestricted,
                _ => OperationalConvergenceReason.ActorScopeRejected
            },
            tenantId);

        return PosSessionAuthorityResult.Deny(reason, userId, tenantId);
    }
}
