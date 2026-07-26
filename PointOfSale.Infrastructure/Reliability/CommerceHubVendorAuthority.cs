using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Reliability;

public sealed record CommerceHubVendorAuthoritySnapshot(
    Guid VendorId,
    bool TenantExists,
    bool TenantActive,
    bool GovernanceProfileExists,
    UserGovernanceStatus? GovernanceStatus,
    bool IsKycVerified,
    bool IsMarketplaceApproved)
{
    public bool IsMarketplaceEligible =>
        TenantExists &&
        TenantActive &&
        GovernanceProfileExists &&
        GovernanceStatus == UserGovernanceStatus.Active &&
        IsKycVerified &&
        IsMarketplaceApproved;
}

public static class CommerceHubVendorAuthority
{
    public static async Task<CommerceHubVendorAuthoritySnapshot> LoadAsync(
        ApplicationDbContext dbContext,
        Guid vendorId,
        CancellationToken cancellationToken = default)
    {
        if (vendorId == Guid.Empty)
        {
            return Missing(vendorId);
        }

        var tenant = await dbContext.Tenants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(item => item.Id == vendorId)
            .Select(item => new
            {
                Active = item.IsActive &&
                         item.Status == TenantStatus.Active &&
                         item.DeletedAtUtc == null
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (tenant == null)
        {
            return Missing(vendorId);
        }

        var profile = await dbContext.UserGovernanceProfiles
            .AsNoTracking()
            .Where(item =>
                item.Role == UserGovernanceRole.Vendor &&
                item.SubjectId == vendorId)
            .Select(item => new
            {
                item.Status,
                item.IsKycVerified,
                item.IsMarketplaceApproved
            })
            .SingleOrDefaultAsync(cancellationToken);

        return new CommerceHubVendorAuthoritySnapshot(
            vendorId,
            TenantExists: true,
            TenantActive: tenant.Active,
            GovernanceProfileExists: profile != null,
            GovernanceStatus: profile?.Status,
            IsKycVerified: profile?.IsKycVerified ?? false,
            IsMarketplaceApproved: profile?.IsMarketplaceApproved ?? false);
    }

    public static async Task<HashSet<Guid>> LoadEligibleVendorIdsAsync(
        ApplicationDbContext dbContext,
        IEnumerable<Guid> vendorIds,
        CancellationToken cancellationToken = default)
    {
        var normalizedIds = vendorIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        if (normalizedIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var eligibleIds = await (
                from tenant in dbContext.Tenants.AsNoTracking().IgnoreQueryFilters()
                join profile in dbContext.UserGovernanceProfiles.AsNoTracking()
                    on tenant.Id equals profile.SubjectId
                where normalizedIds.Contains(tenant.Id) &&
                      tenant.Status == TenantStatus.Active &&
                      tenant.DeletedAtUtc == null &&
                      profile.Role == UserGovernanceRole.Vendor &&
                      profile.Status == UserGovernanceStatus.Active &&
                      profile.IsKycVerified &&
                      profile.IsMarketplaceApproved
                select tenant.Id)
            .Distinct()
            .ToListAsync(cancellationToken);

        return eligibleIds.ToHashSet();
    }

    private static CommerceHubVendorAuthoritySnapshot Missing(Guid vendorId) =>
        new(
            vendorId,
            TenantExists: false,
            TenantActive: false,
            GovernanceProfileExists: false,
            GovernanceStatus: null,
            IsKycVerified: false,
            IsMarketplaceApproved: false);
}
