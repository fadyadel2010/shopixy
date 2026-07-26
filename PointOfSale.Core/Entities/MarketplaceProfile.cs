namespace PointOfSale.Core.Entities;

/// <summary>
/// A user becomes a Provider by creating a MarketplaceProfile.
/// Admin status does NOT grant provider privileges.
/// </summary>
public class MarketplaceProfile : TenantEntity
{
    public Guid UserId { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public string? Bio { get; private set; }
    public string? ProfileImageUrl { get; private set; }
    public string? CoverImageUrl { get; private set; }
    public string? Location { get; private set; }
    public string? Skills { get; private set; } // JSON array of skill tags
    public string? PortfolioLinks { get; private set; } // JSON array of URLs
    public bool IsActive { get; private set; }

    private MarketplaceProfile() { }

    public static MarketplaceProfile Create(Guid tenantId, Guid userId, string displayName, string? bio = null)
    {
        return new MarketplaceProfile
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            DisplayName = displayName,
            Bio = bio,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void UpdateProfile(string displayName, string? bio, string? profileImageUrl, string? coverImageUrl = null, string? location = null, string? skills = null, string? portfolioLinks = null)
    {
        DisplayName = displayName;
        Bio = bio;
        ProfileImageUrl = profileImageUrl;
        CoverImageUrl = coverImageUrl;
        Location = location;
        Skills = skills;
        PortfolioLinks = portfolioLinks;
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
