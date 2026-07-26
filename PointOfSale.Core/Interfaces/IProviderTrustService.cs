using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Interfaces;

/// <summary>
/// Read-only service for querying provider trust data.
/// Trust aggregates are updated via event consumers, not directly through this service.
/// </summary>
public interface IProviderTrustService
{
    Task<ProviderTrustAggregate?> GetTrustAggregateAsync(Guid providerProfileId, CancellationToken cancellationToken = default);
    Task<TrustBadge> GetTrustBadgeAsync(Guid providerProfileId, CancellationToken cancellationToken = default);
    Task<Dictionary<int, int>> GetRatingDistributionAsync(Guid providerProfileId, CancellationToken cancellationToken = default);
    Task<IEnumerable<ProviderTrustAggregate>> GetTopRatedProvidersAsync(int count = 10, CancellationToken cancellationToken = default);
}
