using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.SuperAdmin.Models;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.SuperAdmin.Services;

public sealed class SuperAdminManagementService : ISuperAdminManagementService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IPasswordHasher<SuperAdminUser> _passwordHasher;

    public SuperAdminManagementService(
        ApplicationDbContext dbContext,
        IPasswordHasher<SuperAdminUser> passwordHasher)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
    }

    public async Task UpdatePasswordAsync(
        Guid superAdminId,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newPassword))
        {
            throw new InvalidOperationException("Password is required.");
        }

        var superAdmin = await LoadRequiredAsync(superAdminId, cancellationToken);
        EnsureMutable(superAdmin);

        superAdmin.UpdatePasswordHash(_passwordHasher.HashPassword(superAdmin, newPassword));
        superAdmin.ClearRefreshToken();
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(
        Guid superAdminId,
        CancellationToken cancellationToken = default)
    {
        var superAdmin = await LoadRequiredAsync(superAdminId, cancellationToken);
        EnsureMutable(superAdmin);

        var remainingAdmins = await _dbContext.SuperAdmins.CountAsync(cancellationToken);
        if (remainingAdmins <= 1)
        {
            throw new InvalidOperationException("At least one Super Admin must always exist.");
        }

        _dbContext.SuperAdmins.Remove(superAdmin);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<SuperAdminUser> LoadRequiredAsync(Guid superAdminId, CancellationToken cancellationToken)
    {
        var superAdmin = await _dbContext.SuperAdmins
            .FirstOrDefaultAsync(admin => admin.Id == superAdminId, cancellationToken);

        return superAdmin ?? throw new KeyNotFoundException("Super Admin was not found.");
    }

    private static void EnsureMutable(SuperAdminUser superAdmin)
    {
        if (superAdmin.IsSystem)
        {
            throw new InvalidOperationException("The system root Super Admin cannot be modified or deleted.");
        }
    }
}
