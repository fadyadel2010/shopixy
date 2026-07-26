using PointOfSale.Core.DTOs.Marketplace;

namespace PointOfSale.Core.Interfaces;

/// <summary>
/// Service for marketplace service discovery and search.
/// Elasticsearch supplies candidate discovery hits; current SQL service listing
/// authority decides which service identity, ownership, visibility, pricing,
/// category, and media fields may be returned.
/// </summary>
public interface IMarketplaceSearchService
{
    Task<MarketplaceSearchResponse> SearchServicesAsync(MarketplaceSearchRequest request, CancellationToken cancellationToken = default);
}
