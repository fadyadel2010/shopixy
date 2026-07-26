using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.SuperAdmin.Models;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;

namespace PointOfSale.API.SuperAdmin.Infrastructure;

/// <summary>
/// Revalidates platform-owner authority from current SQL state for every
/// super-admin operation. JWT claims identify the candidate actor only.
/// </summary>
public sealed class SuperAdminPlatformAuthority : ISuperAdminPlatformAuthority
{
    private readonly ApplicationDbContext _dbContext;

    public SuperAdminPlatformAuthority(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<SuperAdminPlatformAuthorityResult> ValidatePlatformActorAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        if (!SuperAdminTokenClaimsValidator.HasRequiredClaims(principal))
        {
            RecordFailure(OperationalConvergenceReason.InvalidInput);
            return new SuperAdminPlatformAuthorityResult(false, "INVALID_SUPER_ADMIN_CLAIMS");
        }

        var rawSuperAdminId = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                              ?? principal.FindFirstValue("sub");
        if (!Guid.TryParse(rawSuperAdminId, out var superAdminId))
        {
            RecordFailure(OperationalConvergenceReason.InvalidInput);
            return new SuperAdminPlatformAuthorityResult(false, "INVALID_SUPER_ADMIN_ID");
        }

        var rawTokenVersion = principal.FindFirstValue(SuperAdminSecurityConstants.TokenVersionClaim);
        if (!int.TryParse(rawTokenVersion, out var tokenVersion))
        {
            RecordFailure(OperationalConvergenceReason.InvalidInput);
            return new SuperAdminPlatformAuthorityResult(false, "INVALID_TOKEN_VERSION", superAdminId);
        }

        var authority = await _dbContext.SuperAdmins
            .AsNoTracking()
            .Where(admin => admin.Id == superAdminId)
            .Select(admin => new
            {
                admin.Id,
                admin.Email,
                admin.TokenVersion
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (authority == null)
        {
            RecordFailure(OperationalConvergenceReason.MissingSqlAuthority);
            return new SuperAdminPlatformAuthorityResult(false, "SUPER_ADMIN_NOT_FOUND", superAdminId);
        }

        if (authority.TokenVersion != tokenVersion)
        {
            RecordFailure(OperationalConvergenceReason.ActorScopeRejected);
            return new SuperAdminPlatformAuthorityResult(
                false,
                "STALE_SUPER_ADMIN_TOKEN",
                authority.Id,
                authority.Email,
                authority.TokenVersion);
        }

        var governanceStatus = await _dbContext.UserGovernanceProfiles
            .AsNoTracking()
            .Where(profile =>
                profile.Role == UserGovernanceRole.Admin &&
                profile.SubjectId == authority.Id)
            .OrderByDescending(profile => profile.UpdatedAtUtc ?? profile.CreatedAtUtc)
            .Select(profile => (UserGovernanceStatus?)profile.Status)
            .FirstOrDefaultAsync(cancellationToken);

        if (governanceStatus.HasValue &&
            governanceStatus.Value != UserGovernanceStatus.Active)
        {
            RecordFailure(OperationalConvergenceReason.ActorScopeRejected);
            return new SuperAdminPlatformAuthorityResult(
                false,
                "SUPER_ADMIN_GOVERNANCE_REJECTED",
                authority.Id,
                authority.Email,
                authority.TokenVersion);
        }

        return new SuperAdminPlatformAuthorityResult(
            true,
            "AUTHORIZED",
            authority.Id,
            authority.Email,
            authority.TokenVersion);
    }

    private static void RecordFailure(OperationalConvergenceReason reason)
    {
        OperationalConvergenceTelemetry.RecordAuthorityFailure(
            "SuperAdminPlatformAuthority",
            OperationalConvergenceAuthority.PlatformAdmin,
            reason);
    }
}
