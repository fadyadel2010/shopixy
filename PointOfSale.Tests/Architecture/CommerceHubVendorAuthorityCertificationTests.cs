using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubVendorAuthorityCertificationTests
{
    [Fact]
    public async Task MarketplaceEligibility_MustRequireCurrentTenantVerificationApprovalAndGovernance()
    {
        await using var dbContext = CreateDbContext();
        var vendorId = Guid.NewGuid();

        dbContext.Tenants.Add(new Tenant
        {
            Id = vendorId,
            Name = "Vendor",
            Slug = $"vendor-{Guid.NewGuid():N}",
            Currency = "EGP",
            IsActive = true,
            Status = TenantStatus.Active
        });
        dbContext.UserGovernanceProfiles.Add(new UserGovernanceProfile
        {
            Id = Guid.NewGuid(),
            SubjectId = vendorId,
            Role = UserGovernanceRole.Vendor,
            Status = UserGovernanceStatus.Active,
            IsKycVerified = false,
            IsMarketplaceApproved = false,
            CreatedAtUtc = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var unverified = await CommerceHubVendorAuthority.LoadAsync(dbContext, vendorId);
        Assert.False(unverified.IsMarketplaceEligible);

        var profile = await dbContext.UserGovernanceProfiles.SingleAsync();
        using (UserGovernanceMutationScope.BeginCertificationMutation())
        {
            profile.IsKycVerified = true;
            profile.IsMarketplaceApproved = true;
            await dbContext.SaveChangesAsync();
        }

        var eligible = await CommerceHubVendorAuthority.LoadAsync(dbContext, vendorId);
        Assert.True(eligible.IsMarketplaceEligible);

        profile.Status = UserGovernanceStatus.Suspended;
        await dbContext.SaveChangesAsync();

        var suspended = await CommerceHubVendorAuthority.LoadAsync(dbContext, vendorId);
        Assert.False(suspended.IsMarketplaceEligible);
    }

    [Fact]
    public void RuntimeBoundaries_MustUseSharedSqlVendorAuthority()
    {
        var governance = ReadRepoFile("PointOfSale.API/Services/Governance/UserGovernanceService.cs");
        var publicVisibility = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPublicVendorVisibilityService.cs");
        var visibility = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubVendorVisibilityService.cs");
        var query = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubQueryService.cs");
        var overlay = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchSqlAuthorityOverlay.cs");
        var reindex = ReadRepoFile("PointOfSale.Infrastructure/Jobs/CommerceHubReindexService.cs");
        var checkout = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");

        Assert.Contains("EnsureMarketplaceEligibility", governance, StringComparison.Ordinal);
        Assert.Contains("CommerceHubVendorAuthority.LoadEligibleVendorIdsAsync", publicVisibility, StringComparison.Ordinal);
        Assert.Contains("vendorAuthority.IsMarketplaceEligible", visibility, StringComparison.Ordinal);
        Assert.Contains("CommerceHubVendorAuthority.LoadEligibleVendorIdsAsync", query, StringComparison.Ordinal);
        Assert.Contains("vendorAuthority.IsMarketplaceEligible", overlay, StringComparison.Ordinal);
        Assert.Contains("item.governanceProfile.IsKycVerified", reindex, StringComparison.Ordinal);
        Assert.Contains("item.governanceProfile.IsMarketplaceApproved", reindex, StringComparison.Ordinal);
        Assert.Contains("EnsureVendorOrderAcceptanceAllowedAsync", checkout, StringComparison.Ordinal);
    }

    [Fact]
    public void VerificationApprovalAndOwnership_MustRemainSuperAdminOwnedAndDurable()
    {
        var controller = ReadRepoFile("PointOfSale.API/SuperAdmin/Controllers/SuperAdminUsersController.cs");
        var service = ReadRepoFile("PointOfSale.API/Services/Governance/UserGovernanceService.cs");
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");
        var migration = ReadRepoFile("PointOfSale.Infrastructure/Migrations/20260610170000_CommerceHubVendorAuthorityHardening.cs");
        var findings = ReadRepoFile("docs/architecture/commercehub-vendor-authority-certification-findings.md");

        Assert.Contains("SetVendorVerificationAsync", controller, StringComparison.Ordinal);
        Assert.Contains("SetVendorMarketplaceApprovalAsync", controller, StringComparison.Ordinal);
        Assert.Contains("IsolationLevel.Serializable", service, StringComparison.Ordinal);
        Assert.Contains("VENDOR_VERIFICATION_REQUIRED", service, StringComparison.Ordinal);
        Assert.Contains("UserGovernanceMutationScope.BeginCertificationMutation", service, StringComparison.Ordinal);
        Assert.Contains("ownership cannot be reassigned", dbContext, StringComparison.Ordinal);
        Assert.Contains("CK_UserGovernanceProfiles_MarketplaceApprovalRequiresKyc", migration, StringComparison.Ordinal);
        Assert.Contains("trg_user_governance_profile_ownership_immutable", migration, StringComparison.Ordinal);
        Assert.Contains("PASS M1 CommerceHub Vendor Authority", findings, StringComparison.Ordinal);
        Assert.Contains("does not infer verification or approval", findings, StringComparison.Ordinal);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"vendor-authority-{Guid.NewGuid():N}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static string ReadRepoFile(string relativePath) =>
        CertificationSource.ReadRepoFile(relativePath);
}
