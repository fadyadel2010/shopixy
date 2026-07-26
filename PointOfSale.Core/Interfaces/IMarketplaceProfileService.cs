using PointOfSale.Core.DTOs.Marketplace;
using PointOfSale.Core.Entities;

namespace PointOfSale.Core.Interfaces;

public interface IMarketplaceProfileService
{
    Task<MarketplaceProfile> CreateProfileAsync(Guid userId, CreateMarketplaceProfileDto dto, CancellationToken cancellationToken = default);
    Task<MarketplaceProfile> UpdateProfileAsync(Guid profileId, Guid userId, UpdateMarketplaceProfileDto dto, CancellationToken cancellationToken = default);
    Task<MarketplaceProfile?> GetProfileByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<MarketplaceProfile?> GetProfileByIdAsync(Guid profileId, CancellationToken cancellationToken = default);
    Task DeactivateProfileAsync(Guid profileId, Guid userId, CancellationToken cancellationToken = default);
    Task ActivateProfileAsync(Guid profileId, Guid userId, CancellationToken cancellationToken = default);
}
