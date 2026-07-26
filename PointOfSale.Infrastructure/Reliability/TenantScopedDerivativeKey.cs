namespace PointOfSale.Infrastructure.Reliability;

/// <summary>
/// Builds deterministic tenant-scoped identifiers for derivative stores that do not
/// have relational tenant ownership constraints.
/// </summary>
public static class TenantScopedDerivativeKey
{
    public static string ForTenantEntity(Guid tenantId, Guid entityId, string separator = "_")
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant-scoped derivative keys require a non-empty tenant id.", nameof(tenantId));
        }

        if (entityId == Guid.Empty)
        {
            throw new ArgumentException("Tenant-scoped derivative keys require a non-empty entity id.", nameof(entityId));
        }

        if (string.IsNullOrEmpty(separator))
        {
            separator = "_";
        }

        return $"{tenantId:N}{separator}{entityId:N}";
    }
}
