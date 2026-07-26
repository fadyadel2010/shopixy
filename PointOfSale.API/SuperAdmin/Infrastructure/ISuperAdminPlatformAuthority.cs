using System.Security.Claims;

namespace PointOfSale.API.SuperAdmin.Infrastructure;

public interface ISuperAdminPlatformAuthority
{
    Task<SuperAdminPlatformAuthorityResult> ValidatePlatformActorAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default);
}

public sealed record SuperAdminPlatformAuthorityResult(
    bool Authorized,
    string Reason,
    Guid? SuperAdminId = null,
    string? Email = null,
    int? CurrentTokenVersion = null);
