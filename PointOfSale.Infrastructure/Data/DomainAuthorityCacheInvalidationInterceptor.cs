using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Redis;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Services;
using Microsoft.Extensions.Options;
using System.Data.Common;

namespace PointOfSale.Infrastructure.Data;

/// <summary>
/// Invalidates host authority for every persistence path, including background
/// certificate jobs and administrative tenant lifecycle changes. Invalidating
/// both before and after commit fences overlapping cache fills; an enabled but
/// unavailable Redis authority cache fails the mutation before commit.
/// </summary>
public sealed class DomainAuthorityCacheInvalidationInterceptor : SaveChangesInterceptor
{
    private static readonly HashSet<string> DomainAuthorityProperties = new(StringComparer.Ordinal)
    {
        nameof(Domain.NormalizedHostName),
        nameof(Domain.TenantId),
        nameof(Domain.LifecycleState),
        nameof(Domain.IsDeleted),
        nameof(Domain.RedirectType),
        nameof(Domain.RedirectTargetDomainId)
    };

    private static readonly HashSet<string> TenantAuthorityProperties = new(StringComparer.Ordinal)
    {
        nameof(Tenant.Slug),
        nameof(Tenant.StorefrontBaseUrl),
        nameof(Tenant.IsActive),
        nameof(Tenant.Status),
        nameof(Tenant.DeletedAtUtc)
    };

    private static readonly HashSet<string> ProductSlugProperties = new(StringComparer.Ordinal)
    {
        nameof(Product.Slug), nameof(Product.TenantId), nameof(Product.IsDeleted),
        nameof(Product.IsActive), nameof(Product.IsOnlineVisible),
        nameof(Product.PublishStatus), nameof(Product.CategoryId)
    };

    private static readonly HashSet<string> CategorySlugProperties = new(StringComparer.Ordinal)
    {
        nameof(Category.Name), nameof(Category.TenantId), nameof(Category.ParentCategoryId),
        nameof(Category.IsDeleted)
    };

    private static readonly HashSet<string> CmsSlugProperties = new(StringComparer.Ordinal)
    {
        nameof(CmsPage.Slug), nameof(CmsPage.TenantId), nameof(CmsPage.IsDeleted),
        nameof(CmsPage.IsPublished), nameof(CmsPage.IsDraft), nameof(CmsPage.PublishedAt)
    };

    private readonly IDomainResolutionCache _cache;
    private readonly DomainAuthorityCacheInvalidationState _state;
    private readonly IRedisJsonCacheService? _slugCache;
    private readonly string? _slugGenerationKey;

    public DomainAuthorityCacheInvalidationInterceptor(
        IDomainResolutionCache cache,
        DomainAuthorityCacheInvalidationState state,
        IRedisJsonCacheService? slugCache = null,
        IOptions<RedisSettings>? redisSettings = null)
    {
        _cache = cache;
        _state = state;
        _slugCache = slugCache;
        _slugGenerationKey = redisSettings == null ? null : SeoSlugAuthorityKeys.Generation(redisSettings.Value.InstanceName);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        InvalidateBeforeSaveAsync(eventData.Context, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        await InvalidateBeforeSaveAsync(eventData.Context, cancellationToken);
        return result;
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        InvalidateAfterAutoCommitAsync(eventData.Context).AsTask().GetAwaiter().GetResult();
        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        await InvalidateAfterAutoCommitAsync(eventData.Context);
        return result;
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        ClearAfterFailedAutoCommit(eventData.Context);
    }

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        ClearAfterFailedAutoCommit(eventData.Context);
        return Task.CompletedTask;
    }

    private async ValueTask InvalidateBeforeSaveAsync(DbContext? context, CancellationToken cancellationToken)
    {
        if (context == null)
        {
            return;
        }

        if (HasAuthorityMutation(context))
        {
            await _cache.InvalidateAllAsync(cancellationToken);
            _state.MarkPending();
        }

        if (HasSlugMutation(context) && _slugCache != null && _slugGenerationKey != null)
        {
            // The pre-save fence rejects a fill overlapping an uncommitted write;
            // the post-commit fence removes negatives filled against old SQL.
            await _slugCache.AdvanceInvalidationGenerationAsync(_slugGenerationKey, cancellationToken);
            _state.MarkSlugPending();
        }
    }

    private async ValueTask InvalidateAfterAutoCommitAsync(DbContext? context)
    {
        if (context?.Database.CurrentTransaction != null)
        {
            return;
        }

        if (_state.TryTakePending())
            await _cache.InvalidateAllAsync(CancellationToken.None);
        if (_state.TryTakeSlugPending() && _slugCache != null && _slugGenerationKey != null)
            await _slugCache.AdvanceInvalidationGenerationAsync(_slugGenerationKey, CancellationToken.None);
    }

    private void ClearAfterFailedAutoCommit(DbContext? context)
    {
        if (context?.Database.CurrentTransaction == null)
        {
            _state.Clear();
        }
    }

    private static bool HasAuthorityMutation(DbContext context)
    {
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is EntityState.Added or EntityState.Deleted &&
                entry.Entity is Domain or Tenant)
            {
                return true;
            }

            if (entry.State != EntityState.Modified)
            {
                continue;
            }

            var authorityProperties = entry.Entity switch
            {
                Domain => DomainAuthorityProperties,
                Tenant => TenantAuthorityProperties,
                _ => null
            };

            if (authorityProperties != null &&
                entry.Properties.Any(property => property.IsModified && authorityProperties.Contains(property.Metadata.Name)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasSlugMutation(DbContext context) =>
        context.ChangeTracker.Entries().Any(entry =>
        {
            var properties = entry.Entity switch
            {
                Product => ProductSlugProperties,
                Category => CategorySlugProperties,
                CmsPage => CmsSlugProperties,
                Tenant => TenantAuthorityProperties,
                _ => null
            };
            return properties != null &&
                   (entry.State is EntityState.Added or EntityState.Deleted ||
                    entry.State == EntityState.Modified &&
                    entry.Properties.Any(property => property.IsModified && properties.Contains(property.Metadata.Name)));
        });
}

public sealed class DomainAuthorityTransactionInvalidationInterceptor : DbTransactionInterceptor
{
    private readonly IDomainResolutionCache _cache;
    private readonly DomainAuthorityCacheInvalidationState _state;
    private readonly IRedisJsonCacheService? _slugCache;
    private readonly string? _slugGenerationKey;

    public DomainAuthorityTransactionInvalidationInterceptor(
        IDomainResolutionCache cache,
        DomainAuthorityCacheInvalidationState state,
        IRedisJsonCacheService? slugCache = null,
        IOptions<RedisSettings>? redisSettings = null)
    {
        _cache = cache;
        _state = state;
        _slugCache = slugCache;
        _slugGenerationKey = redisSettings == null ? null : SeoSlugAuthorityKeys.Generation(redisSettings.Value.InstanceName);
    }

    public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
    {
        InvalidateAfterCommitAsync().AsTask().GetAwaiter().GetResult();
    }

    public override async Task TransactionCommittedAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await InvalidateAfterCommitAsync();
    }

    public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData)
    {
        _state.Clear();
    }

    public override Task TransactionRolledBackAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        _state.Clear();
        return Task.CompletedTask;
    }

    public override void TransactionFailed(DbTransaction transaction, TransactionErrorEventData eventData)
    {
        _state.Clear();
    }

    public override Task TransactionFailedAsync(
        DbTransaction transaction,
        TransactionErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        _state.Clear();
        return Task.CompletedTask;
    }

    private async ValueTask InvalidateAfterCommitAsync()
    {
        if (_state.TryTakePending())
            await _cache.InvalidateAllAsync(CancellationToken.None);
        if (_state.TryTakeSlugPending() && _slugCache != null && _slugGenerationKey != null)
            await _slugCache.AdvanceInvalidationGenerationAsync(_slugGenerationKey, CancellationToken.None);
    }
}

public sealed class DomainAuthorityCacheInvalidationState
{
    private bool _pending;
    private bool _slugPending;

    public void MarkPending() => _pending = true;
    public void MarkSlugPending() => _slugPending = true;
    public bool TryTakeSlugPending()
    {
        if (!_slugPending) return false;
        _slugPending = false;
        return true;
    }

    public bool TryTakePending()
    {
        if (!_pending)
        {
            return false;
        }

        _pending = false;
        return true;
    }

    public void Clear() => _pending = _slugPending = false;
}
