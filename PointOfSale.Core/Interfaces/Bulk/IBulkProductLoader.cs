using PointOfSale.Core.DTOs.Bulk;
using PointOfSale.Core.Entities;

namespace PointOfSale.Core.Interfaces.Bulk;

/// <summary>
/// Tenant-scoped, set-based loader for bulk product state. Resolves the
/// tenant exclusively through <see cref="PointOfSale.Core.Interfaces.ITenantContext.RequiredTenantId"/>,
/// performs a single bounded EF query retaining the global tenant/delete filter,
/// and projects only identity and current row version.
/// </summary>
public interface IBulkProductLoader
{
    IReadOnlyDictionary<Guid, Product> LoadedProducts { get; }

    Task<IReadOnlyList<BulkProductProjection>> LoadProjectionsAsync(
        IReadOnlySet<Guid> productIds,
        CancellationToken cancellationToken = default);
}
