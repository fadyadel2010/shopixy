using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Entities;

/// <summary>
/// A service offered by a provider (MarketplaceProfile owner).
/// Only the profile owner can create services.
/// </summary>
public class Service : TenantEntity
{
    public Guid ProfileId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string? Category { get; private set; }
    public bool IsActive { get; private set; }
    
    // Phase FE-3: Pricing & Media
    public decimal? StartingPrice { get; private set; }
    public PricingType? PricingType { get; private set; }
    public string? Currency { get; private set; } // ISO 4217 code
    public string? ServiceImages { get; private set; } // JSON array of image URLs
    public string? Packages { get; private set; } // JSON array of package objects

    public MarketplaceProfile? Profile { get; private set; }

    private Service() { }

    public static Service Create(
            Guid tenantId, 
            Guid profileId, 
            string title, 
            string description, 
            string? category = null,
            decimal? startingPrice = null,
            PricingType? pricingType = null,
            string? currency = null,
            string? serviceImages = null,
            string? packages = null)
        {
            return new Service
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProfileId = profileId,
                Title = title,
                Description = description,
                Category = category,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                // Phase FE-3 fields
                StartingPrice = startingPrice,
                PricingType = pricingType,
                Currency = currency,
                ServiceImages = serviceImages,
                Packages = packages
            };
        }


    public void Update(string title, string description, string? category, decimal? startingPrice = null, PricingType? pricingType = null, string? currency = null, string? serviceImages = null, string? packages = null)
    {
        Title = title;
        Description = description;
        Category = category;
        StartingPrice = startingPrice;
        PricingType = pricingType;
        Currency = currency;
        ServiceImages = serviceImages;
        Packages = packages;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Activate()
    {
        IsActive = true;
        UpdatedAt = DateTime.UtcNow;
    }
}
