using PointOfSale.Core.Enums;

namespace PointOfSale.Core.DTOs.Marketplace;

public record UpdateServiceDto(
    string Title,
    string Description,
    string? Category = null,
    decimal? StartingPrice = null,
    PricingType? PricingType = null,
    string? Currency = null,
    string? ServiceImages = null, // JSON array of image URLs
    string? Packages = null // JSON array of package objects
);
