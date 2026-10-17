using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Shipping.BackgroundJobs;

/// <summary>Read-only scheduler bootstrap over the authoritative SQL tenant directory.</summary>
public interface ITrackingTenantDirectory
{
    Task<IReadOnlyList<Guid>> ListEligibleTenantIdsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Tenants is a global directory, not a tenant-owned aggregate. This deliberately
/// creates a context without tenant affinity ONLY to project eligible directory IDs.
/// Tenant-owned query filters remain enabled and fail closed in this context.
/// No context, IQueryable, shipment, configuration or credential escapes bootstrap.
/// </summary>
public sealed class TrackingTenantDirectory(DbContextOptions<ApplicationDbContext> options) : ITrackingTenantDirectory
{
    public async Task<IReadOnlyList<Guid>> ListEligibleTenantIdsAsync(CancellationToken cancellationToken = default)
    {
        await using var directory = new ApplicationDbContext(options);
        return await directory.Tenants.AsNoTracking()
            .Where(tenant => tenant.Id != Guid.Empty && tenant.IsActive &&
                             tenant.Status == TenantStatus.Active && tenant.DeletedAtUtc == null)
            .OrderBy(tenant => tenant.Id)
            .Select(tenant => tenant.Id)
            .ToListAsync(cancellationToken);
    }
}
