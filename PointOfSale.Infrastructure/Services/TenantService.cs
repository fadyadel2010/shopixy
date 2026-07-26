using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Services;

/// <summary>
/// Service for tenant validation and management operations.
/// </summary>
public class TenantService : ITenantService
{
    private readonly ApplicationDbContext _context;

    public TenantService(ApplicationDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Checks if a tenant exists and is active (Status = Active).
    /// </summary>
    public async Task<bool> IsTenantActiveAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return await _context.Tenants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(t => t.Id == tenantId &&
                        t.IsActive &&
                        t.Status == TenantStatus.Active &&
                        t.DeletedAtUtc == null)
            .AnyAsync(cancellationToken);
    }
}
