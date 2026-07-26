namespace PointOfSale.API.SuperAdmin.Services;

public interface ISuperAdminManagementService
{
    Task UpdatePasswordAsync(
        Guid superAdminId,
        string newPassword,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        Guid superAdminId,
        CancellationToken cancellationToken = default);
}
