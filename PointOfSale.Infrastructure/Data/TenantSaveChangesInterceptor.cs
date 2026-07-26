using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Infrastructure.Data;

/// <summary>
/// EF Core SaveChanges interceptor that enforces tenant isolation rules.
/// 
/// SECURITY: This is a defense-in-depth measure that prevents:
/// 1. Saving ITenantOwned entities without a valid TenantId
/// 2. Saving ITenantOwned entities with Guid.Empty TenantId
/// 
/// This catches developer mistakes at runtime before they corrupt the database.
/// </summary>
public class TenantSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly ILogger<TenantSaveChangesInterceptor> _logger;

    public TenantSaveChangesInterceptor(ILogger<TenantSaveChangesInterceptor> logger)
    {
        _logger = logger;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ValidateTenantIds(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ValidateTenantIds(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void ValidateTenantIds(DbContext? context)
    {
        if (context == null) return;

        SyncTenantLifecycleMirrors(context);

        // Try to resolve current tenant context to check for mismatch
        // This requires the DbContext to have access to the service provider
        var tenantContext = context.GetService<ITenantContext>();
        var currentTenantId = tenantContext?.TenantId;

        var entries = context.ChangeTracker.Entries()
            .Where(e => e.Entity is ITenantOwned &&
                       (e.State == EntityState.Added ||
                        e.State == EntityState.Modified ||
                        e.State == EntityState.Deleted))
            .Select(e => new
            {
                Entry = e,
                Entity = (ITenantOwned)e.Entity,
                EntityType = e.Entity.GetType().Name
            })
            .ToList();

        // AF-19: Global affiliates - AffiliateAccount and AffiliateUser can have Guid.Empty TenantId
        var globalAffiliateTypes = new[] { "AffiliateAccount", "AffiliateUser" };
        
        var invalidEmptyEntries = entries
            .Where(x => x.Entity.TenantId == Guid.Empty && !globalAffiliateTypes.Contains(x.EntityType))
            .ToList();

        if (invalidEmptyEntries.Any())
        {
            var entityNames = string.Join(", ", invalidEmptyEntries.Select(e => e.EntityType));
            
            _logger.LogCritical(
                "SECURITY VIOLATION: Attempted to save ITenantOwned entities without valid TenantId. " +
                "Entities: {EntityNames}. This is a critical bug that would cause data corruption.",
                entityNames);

            throw new InvalidOperationException(
                $"Cannot save ITenantOwned entity without a valid TenantId. " +
                $"The following entities have TenantId = Guid.Empty: {entityNames}. " +
                "This is a security violation that has been blocked.");
        }

        // Validate Mismatch against TenantContext (if present)
        // AF-19: Skip validation for global affiliates (they can be saved in any tenant context)
        if (currentTenantId.HasValue && currentTenantId.Value != Guid.Empty)
        {
            var mismatchEntries = entries
                .Where(x => x.Entity.TenantId != currentTenantId.Value && 
                           x.Entity.TenantId != Guid.Empty && // AF-19: Allow global affiliates
                           !globalAffiliateTypes.Contains(x.EntityType))
                .ToList();
            
            if (mismatchEntries.Any())
            {
                var details = string.Join("; ", mismatchEntries.Select(x => $"{x.EntityType}(ID={x.Entry.Property("Id").CurrentValue}, TenantId={x.Entity.TenantId})"));

                _logger.LogCritical(
                    "SECURITY VIOLATION: TenantId Mismatch detected! Context Tenant: {ContextTenantId}. " +
                    "Attempted to save entities belonging to different tenants: {Details}",
                    currentTenantId.Value, details);

                throw new InvalidOperationException(
                    $"Security Violation: Attempted to save data for a different tenant. " +
                    $"Current Context: {currentTenantId.Value}. Mismatched Entities: {details}");
            }
        }
    }

    private static void SyncTenantLifecycleMirrors(DbContext context)
    {
        foreach (var entry in context.ChangeTracker.Entries<Tenant>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.SyncLifecycleMirror();
            }
        }
    }
}
