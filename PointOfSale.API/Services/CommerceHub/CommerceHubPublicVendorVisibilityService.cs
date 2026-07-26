using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Services.CommerceHub.Finance;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.API.Services.CommerceHub;

public interface ICommerceHubPublicVendorVisibilityService
{
    Task<HashSet<Guid>> GetSuppressedVendorIdsAsync(
        IEnumerable<Guid> vendorIds,
        CancellationToken cancellationToken = default);

    Task<HashSet<Guid>> FilterVisibleListingIdsAsync(
        IEnumerable<Guid> listingIds,
        CancellationToken cancellationToken = default);

    Task<bool> IsVendorSuppressedAsync(
        Guid vendorId,
        CancellationToken cancellationToken = default);
}

public sealed class CommerceHubPublicVendorVisibilityService : ICommerceHubPublicVendorVisibilityService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ICommerceHubVendorFinancialControlService _vendorFinancialControlService;

    public CommerceHubPublicVendorVisibilityService(
        ApplicationDbContext dbContext,
        ICommerceHubVendorFinancialControlService vendorFinancialControlService)
    {
        _dbContext = dbContext;
        _vendorFinancialControlService = vendorFinancialControlService;
    }

    public async Task<HashSet<Guid>> GetSuppressedVendorIdsAsync(
        IEnumerable<Guid> vendorIds,
        CancellationToken cancellationToken = default)
    {
        var normalizedVendorIds = vendorIds
            .Where(vendorId => vendorId != Guid.Empty)
            .Distinct()
            .ToList();

        if (normalizedVendorIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var eligibleVendorIds = await CommerceHubVendorAuthority.LoadEligibleVendorIdsAsync(
            _dbContext,
            normalizedVendorIds,
            cancellationToken);

        var suspendedVendorIds = await _vendorFinancialControlService.GetVendorsSuspendedFromNewOrdersAsync(
            normalizedVendorIds,
            cancellationToken);

        return normalizedVendorIds
            .Where(vendorId => !eligibleVendorIds.Contains(vendorId))
            .Concat(suspendedVendorIds)
            .Where(vendorId => vendorId != Guid.Empty)
            .ToHashSet();
    }

    public async Task<bool> IsVendorSuppressedAsync(
        Guid vendorId,
        CancellationToken cancellationToken = default)
    {
        if (vendorId == Guid.Empty)
        {
            return false;
        }

        var suppressedVendorIds = await GetSuppressedVendorIdsAsync(
            new[] { vendorId },
            cancellationToken);

        return suppressedVendorIds.Contains(vendorId);
    }

    public async Task<HashSet<Guid>> FilterVisibleListingIdsAsync(
        IEnumerable<Guid> listingIds,
        CancellationToken cancellationToken = default)
    {
        var normalizedListingIds = listingIds
            .Where(listingId => listingId != Guid.Empty)
            .Distinct()
            .ToList();

        if (normalizedListingIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var listingScopes = await _dbContext.CommerceHubListings
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(listing =>
                normalizedListingIds.Contains(listing.Id) &&
                !listing.IsDeleted &&
                listing.IsPublished)
            .Select(listing => new
            {
                listing.Id,
                listing.TenantId
            })
            .ToListAsync(cancellationToken);

        if (listingScopes.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var suppressedVendorIds = await GetSuppressedVendorIdsAsync(
            listingScopes.Select(item => item.TenantId),
            cancellationToken);

        return listingScopes
            .Where(item => !suppressedVendorIds.Contains(item.TenantId))
            .Select(item => item.Id)
            .ToHashSet();
    }
}
