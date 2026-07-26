using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.DTOs.Marketplace;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Exceptions;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Services;

public class MarketplaceProfileService : IMarketplaceProfileService
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;

    public MarketplaceProfileService(ApplicationDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    public async Task<MarketplaceProfile> CreateProfileAsync(Guid userId, CreateMarketplaceProfileDto dto, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId ?? throw new InvalidOperationException("TenantId is required");

        var existingProfile = await _context.Set<MarketplaceProfile>()
            .FirstOrDefaultAsync(p => p.UserId == userId && !p.IsDeleted, cancellationToken);

        if (existingProfile != null)
            throw MarketplaceDomainException.ProfileAlreadyExists();

        var profile = MarketplaceProfile.Create(tenantId, userId, dto.DisplayName, dto.Bio);
        
        _context.Set<MarketplaceProfile>().Add(profile);
        await _context.SaveChangesAsync(cancellationToken);

        return profile;
    }

    public async Task<MarketplaceProfile> UpdateProfileAsync(Guid profileId, Guid userId, UpdateMarketplaceProfileDto dto, CancellationToken cancellationToken = default)
    {
        var profile = await _context.Set<MarketplaceProfile>()
            .FirstOrDefaultAsync(p => p.Id == profileId && !p.IsDeleted, cancellationToken);

        if (profile == null)
            throw MarketplaceDomainException.ProfileNotFound();

        if (profile.UserId != userId)
            throw MarketplaceDomainException.Unauthorized("update this profile");

        profile.UpdateProfile(dto.DisplayName, dto.Bio, dto.ProfileImageUrl, dto.CoverImageUrl, dto.Location, dto.Skills, dto.PortfolioLinks);
        await _context.SaveChangesAsync(cancellationToken);

        return profile;
    }

    public async Task<MarketplaceProfile?> GetProfileByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId ?? throw new InvalidOperationException("TenantId is required");

        return await _context.Set<MarketplaceProfile>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p =>
                p.TenantId == tenantId &&
                p.UserId == userId &&
                !p.IsDeleted,
                cancellationToken);
    }

    public async Task<MarketplaceProfile?> GetProfileByIdAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId ?? throw new InvalidOperationException("TenantId is required");

        return await _context.Set<MarketplaceProfile>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p =>
                p.TenantId == tenantId &&
                p.Id == profileId &&
                !p.IsDeleted,
                cancellationToken);
    }

    public async Task DeactivateProfileAsync(Guid profileId, Guid userId, CancellationToken cancellationToken = default)
    {
        var profile = await _context.Set<MarketplaceProfile>()
            .FirstOrDefaultAsync(p => p.Id == profileId && !p.IsDeleted, cancellationToken);

        if (profile == null)
            throw MarketplaceDomainException.ProfileNotFound();

        if (profile.UserId != userId)
            throw MarketplaceDomainException.Unauthorized("deactivate this profile");

        profile.Deactivate();
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task ActivateProfileAsync(Guid profileId, Guid userId, CancellationToken cancellationToken = default)
    {
        var profile = await _context.Set<MarketplaceProfile>()
            .FirstOrDefaultAsync(p => p.Id == profileId && !p.IsDeleted, cancellationToken);

        if (profile == null)
            throw MarketplaceDomainException.ProfileNotFound();

        if (profile.UserId != userId)
            throw MarketplaceDomainException.Unauthorized("activate this profile");

        profile.Activate();
        await _context.SaveChangesAsync(cancellationToken);
    }
}
