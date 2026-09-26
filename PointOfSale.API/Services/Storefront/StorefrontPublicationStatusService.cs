using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Tenancy;

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

        var published = await StorefrontPublicationAuthorityMemo.GetAsync(
            _dbContext,
            tenantId,
            cancellationToken);
        if (!published)
        {
            _logger.LogDebug(
                "Storefront publication SQL authority rejected the request | TenantId: {TenantId}",
                tenantId);
        }

        return published;
    }

    internal static bool IsLive(string? storefrontStatus)
    {
        return string.Equals(storefrontStatus?.Trim(), "live", StringComparison.OrdinalIgnoreCase);
    }
}

internal static class StorefrontPublicationAuthorityMemo
{
    private static readonly ConditionalWeakTable<
        ApplicationDbContext,
        ConcurrentDictionary<Guid, Lazy<Task<bool>>>> Memos = new();

    public static Task<bool> GetAsync(
        ApplicationDbContext dbContext,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var entries = Memos.GetOrCreateValue(dbContext);
        var lazy = entries.GetOrAdd(
            tenantId,
            id => new Lazy<Task<bool>>(
                () => LoadAsync(dbContext, id, cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication));
        var task = lazy.Value;

        _ = task.ContinueWith(
            completed =>
            {
                if (completed.Status != TaskStatus.RanToCompletion)
                {
                    entries.TryRemove(new KeyValuePair<Guid, Lazy<Task<bool>>>(tenantId, lazy));
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        return task;
    }

    private static async Task<bool> LoadAsync(
        ApplicationDbContext dbContext,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        if (TenantAuthorityMemo.TryGetMemoized(dbContext, tenantId, out var tenant))
        {
            if (tenant == null)
            {
                return false;
            }

            var status = await dbContext.StoreConfigs
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(config => config.TenantId == tenantId)
                .Select(config => config.StorefrontStatus)
                .SingleOrDefaultAsync(cancellationToken);
            return StorefrontPublicationStatusService.IsLive(status);
        }

        var authority = await dbContext.StoreConfigs
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(config => config.TenantId == tenantId)
            .Join(
                dbContext.Tenants.AsNoTracking().IgnoreQueryFilters(),
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

        return authority != null &&
               authority.IsActive &&
               authority.Status == TenantStatus.Active &&
               authority.DeletedAtUtc == null &&
               StorefrontPublicationStatusService.IsLive(authority.StorefrontStatus);
    }
}
