namespace PointOfSale.Core.DTOs.Marketplace;

public record CreateMarketplaceProfileDto(
    string DisplayName,
    string? Bio = null
);
