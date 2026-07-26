namespace PointOfSale.Core.DTOs.Marketplace;

public record UpdateMarketplaceProfileDto(
    string DisplayName,
    string? Bio = null,
    string? ProfileImageUrl = null,
    string? CoverImageUrl = null,
    string? Location = null,
    string? Skills = null, // JSON array of skill tags
    string? PortfolioLinks = null // JSON array of URLs
);
