using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Tenancy;

/// <summary>
/// Memoizes canonical tenant authority per scoped DbContext. Faulted or cancelled
/// loads are evicted, and no verdict is retained across request scopes.
/// </summary>
public static class TenantAuthorityMemo
{
    private static readonly ConditionalWeakTable<ApplicationDbContext, MemoEntries> Memos = new();

    private sealed class MemoEntries
    {
        public ConcurrentDictionary<Guid, Lazy<Task<Tenant?>>> TenantsById { get; } = new();
    }

    public static Task<Tenant?> GetTenantByIdAsync(
        ApplicationDbContext dbContext,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        if (tenantId == Guid.Empty)
        {
            return Task.FromResult<Tenant?>(null);
        }

        var entries = Memos.GetOrCreateValue(dbContext);
        var lazy = entries.TenantsById.GetOrAdd(
            tenantId,
            id => new Lazy<Task<Tenant?>>(
                () => LoadTenantByIdAsync(dbContext, id, cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication));
        var task = lazy.Value;

        _ = task.ContinueWith(
            completed =>
            {
                if (completed.Status != TaskStatus.RanToCompletion)
                {
                    entries.TenantsById.TryRemove(
                        new KeyValuePair<Guid, Lazy<Task<Tenant?>>>(tenantId, lazy));
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        return task;
    }

    public static bool TryGetMemoized(ApplicationDbContext dbContext, Guid tenantId, out Tenant? tenant)
    {
        tenant = null;
        ArgumentNullException.ThrowIfNull(dbContext);
        if (tenantId == Guid.Empty || !Memos.TryGetValue(dbContext, out var entries))
        {
            return false;
        }

        if (entries.TenantsById.TryGetValue(tenantId, out var lazy) &&
            lazy.IsValueCreated &&
            lazy.Value.Status == TaskStatus.RanToCompletion)
        {
            tenant = lazy.Value.Result;
            return true;
        }

        return false;
    }

    public static void Store(ApplicationDbContext dbContext, Tenant tenant)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(tenant);
        if (tenant.Id == Guid.Empty)
        {
            return;
        }

        Memos.GetOrCreateValue(dbContext).TenantsById.TryAdd(
            tenant.Id,
            new Lazy<Task<Tenant?>>(
                () => Task.FromResult<Tenant?>(tenant),
                LazyThreadSafetyMode.ExecutionAndPublication));
    }

    private static async Task<Tenant?> LoadTenantByIdAsync(
        ApplicationDbContext dbContext,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        return await dbContext.Tenants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                tenant => tenant.Id == tenantId &&
                          tenant.IsActive &&
                          tenant.Status == TenantStatus.Active &&
                          tenant.DeletedAtUtc == null,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
