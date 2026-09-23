using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.Storefront;

public interface IStorefrontPublicationStatusService
{
    Task<bool> IsPublishedAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

public sealed class StorefrontPublicationStatusService : IStorefrontPublicationStatusService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<StorefrontPublicationStatusService> _logger;

    public StorefrontPublicationStatusService(
        ApplicationDbContext dbContext,
        ILogger<StorefrontPublicationStatusService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<bool> IsPublishedAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
        {
            return false;
        }

        var authority = await _dbContext.StoreConfigs
            .AsNoTracking()
            .Where(config => config.TenantId == tenantId)
            .Join(
                _dbContext.Tenants.AsNoTracking(),
                config => config.TenantId,
                tenant => tenant.Id,
                (config, tenant) => new
                {
                    config.StorefrontStatus,
                    tenant.IsActive,
                    tenant.Status,
                    tenant.DeletedAtUtc
                })
            .SingleOrDefaultAsync(cancellationToken);

        if (authority == null)
        {
            _logger.LogDebug(
                "Storefront publication check failed because no SQL config authority exists | TenantId: {TenantId}",
                tenantId);
            return false;
        }

        return authority.IsActive &&
               authority.Status == TenantStatus.Active &&
               authority.DeletedAtUtc == null &&
               IsLive(authority.StorefrontStatus);
    }

    private static bool IsLive(string? storefrontStatus)
    {
        return string.Equals(storefrontStatus?.Trim(), "live", StringComparison.OrdinalIgnoreCase);
    }
}
