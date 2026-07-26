using PointOfSale.API.SuperAdmin.DTOs;
using PointOfSale.API.SuperAdmin.Models;

namespace PointOfSale.API.SuperAdmin.Services;

public interface ISuperAdminAuthService
{
    Task<SuperAdminAuthResult> LoginAsync(
        SuperAdminLoginRequest request,
        CancellationToken cancellationToken = default);

    Task<SuperAdminAuthResult> RefreshAsync(
        SuperAdminRefreshRequest request,
        CancellationToken cancellationToken = default);

    Task<SuperAdminAuthResult> LogoutAsync(
        Guid superAdminId,
        CancellationToken cancellationToken = default);
}
