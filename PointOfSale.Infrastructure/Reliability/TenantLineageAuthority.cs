using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;

namespace PointOfSale.Infrastructure.Reliability;

public sealed record TenantLineageValidation(
    Guid ExpectedTenantId,
    Guid EntityId,
    Guid? CurrentTenantId,
    bool IsCurrent,
    string Reason);

public sealed record StoreConfigTenantLineage(
    StoreConfig StoreConfig,
    Tenant Tenant);

/// <summary>
/// SQL-backed tenant ownership checks for replay, rebuild, and derivative mutation paths.
/// Event payload tenant ids, Mongo documents, Redis keys, and search documents are evidence only.
/// </summary>
public static class TenantLineageAuthority
{
    public static async Task<bool> IsActiveTenantAsync(
        ApplicationDbContext dbContext,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty)
        {
            return false;
        }

        return await dbContext.Tenants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .AnyAsync(
                tenant => tenant.Id == tenantId &&
                          tenant.IsActive &&
                          tenant.Status == TenantStatus.Active &&
                          tenant.DeletedAtUtc == null,
                cancellationToken);
    }

    public static async Task<TenantLineageValidation> ValidateCurrentTenantOwnerAsync<TEntity>(
        ApplicationDbContext dbContext,
        Guid expectedTenantId,
        Guid entityId,
        CancellationToken cancellationToken)
        where TEntity : BaseEntity, ITenantOwned
    {
        if (expectedTenantId == Guid.Empty || entityId == Guid.Empty)
        {
            OperationalConvergenceTelemetry.RecordAuthorityFailure(
                typeof(TEntity).Name,
                OperationalConvergenceAuthority.TenantLineage,
                OperationalConvergenceReason.InvalidInput,
                expectedTenantId);
            return new TenantLineageValidation(
                expectedTenantId,
                entityId,
                null,
                IsCurrent: false,
                "invalid_input");
        }

        var row = await dbContext.Set<TEntity>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(entity => entity.Id == entityId)
            .Select(entity => new
            {
                entity.TenantId,
                entity.IsDeleted
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row == null)
        {
            OperationalConvergenceTelemetry.RecordAuthorityFailure(
                typeof(TEntity).Name,
                OperationalConvergenceAuthority.TenantLineage,
                OperationalConvergenceReason.MissingSqlAuthority,
                expectedTenantId);
            return new TenantLineageValidation(
                expectedTenantId,
                entityId,
                null,
                IsCurrent: false,
                "missing_sql_authority");
        }

        if (row.TenantId != expectedTenantId)
        {
            OperationalConvergenceTelemetry.RecordAuthorityFailure(
                typeof(TEntity).Name,
                OperationalConvergenceAuthority.TenantLineage,
                OperationalConvergenceReason.TenantMismatch,
                expectedTenantId);
            return new TenantLineageValidation(
                expectedTenantId,
                entityId,
                row.TenantId,
                IsCurrent: false,
                "tenant_mismatch");
        }

        if (row.IsDeleted)
        {
            OperationalConvergenceTelemetry.RecordAuthorityFailure(
                typeof(TEntity).Name,
                OperationalConvergenceAuthority.TenantLineage,
                OperationalConvergenceReason.DeletedSqlAuthority,
                expectedTenantId);
            return new TenantLineageValidation(
                expectedTenantId,
                entityId,
                row.TenantId,
                IsCurrent: false,
                "deleted_sql_authority");
        }

        if (!await IsActiveTenantAsync(dbContext, expectedTenantId, cancellationToken))
        {
            OperationalConvergenceTelemetry.RecordAuthorityFailure(
                typeof(TEntity).Name,
                OperationalConvergenceAuthority.TenantLineage,
                OperationalConvergenceReason.TenantInactiveOrDeleted,
                expectedTenantId);
            return new TenantLineageValidation(
                expectedTenantId,
                entityId,
                row.TenantId,
                IsCurrent: false,
                "tenant_inactive_or_deleted");
        }

        return new TenantLineageValidation(
            expectedTenantId,
            entityId,
            row.TenantId,
            IsCurrent: true,
            "current_sql_tenant_authority");
    }

    public static async Task<StoreConfigTenantLineage?> LoadCurrentStoreConfigProjectionAuthorityAsync(
        ApplicationDbContext dbContext,
        Guid tenantId,
        Guid storeConfigId,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || storeConfigId == Guid.Empty)
        {
            OperationalConvergenceTelemetry.RecordAuthorityFailure(
                "storefront_config_projection",
                OperationalConvergenceAuthority.TenantLineage,
                OperationalConvergenceReason.InvalidInput,
                tenantId);
            return null;
        }

        var row = await (
                from storeConfig in dbContext.StoreConfigs.AsNoTracking().IgnoreQueryFilters()
                join tenant in dbContext.Tenants.AsNoTracking().IgnoreQueryFilters()
                    on storeConfig.TenantId equals tenant.Id
                where storeConfig.Id == storeConfigId &&
                      storeConfig.TenantId == tenantId &&
                      tenant.IsActive &&
                      tenant.Status == TenantStatus.Active &&
                      tenant.DeletedAtUtc == null
                select new
                {
                    StoreConfig = storeConfig,
                    Tenant = tenant
                })
            .FirstOrDefaultAsync(cancellationToken);

        if (row == null)
        {
            OperationalConvergenceTelemetry.RecordAuthorityFailure(
                "storefront_config_projection",
                OperationalConvergenceAuthority.TenantLineage,
                OperationalConvergenceReason.MissingSqlAuthority,
                tenantId);
            return null;
        }

        return new StoreConfigTenantLineage(row.StoreConfig, row.Tenant);
    }
}
