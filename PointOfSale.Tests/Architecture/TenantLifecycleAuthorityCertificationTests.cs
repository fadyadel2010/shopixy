namespace PointOfSale.Tests.Architecture;

public sealed class TenantLifecycleAuthorityCertificationTests
{
    [Fact]
    public void TenantStatus_MustBeLifecycleAuthorityAndIsActiveMirror()
    {
        var tenant = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/Tenant.cs");
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/TenantsController.cs");
        var interceptor = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/TenantSaveChangesInterceptor.cs");
        var lineage = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Reliability/TenantLineageAuthority.cs");
        var tenantMemo = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Tenancy/TenantAuthorityMemo.cs");
        var tenantResolution = CertificationSource.ReadRepoFile("PointOfSale.API/Middleware/TenantResolutionMiddleware.cs");
        var checkout = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");
        var cart = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCartService.cs");
        var query = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubQueryService.cs");
        var homepage = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Homepage/HomepageSectionReadService.cs");
        var governance = CertificationSource.ReadRepoFile("PointOfSale.API/SuperAdmin/Controllers/SuperAdminMarketplaceGovernanceController.cs");

        Assert.Contains("public bool IsOperational => Status == TenantStatus.Active", tenant, StringComparison.Ordinal);
        Assert.Contains("SetLifecycleStatus(TenantStatus status", tenant, StringComparison.Ordinal);
        Assert.Contains("IsActive = status == TenantStatus.Active", tenant, StringComparison.Ordinal);
        Assert.Contains("SyncLifecycleMirror()", tenant, StringComparison.Ordinal);

        Assert.Contains("Status = TenantStatus.Active", controller, StringComparison.Ordinal);
        Assert.Contains("tenant.SetLifecycleStatus(", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("tenant.IsActive = request.IsActive", controller, StringComparison.Ordinal);

        Assert.Contains("SyncTenantLifecycleMirrors(context)", interceptor, StringComparison.Ordinal);
        Assert.Contains("Entries<Tenant>()", interceptor, StringComparison.Ordinal);

        var activeTenantMethod = Slice(lineage, "public static async Task<bool> IsActiveTenantAsync", "public static async Task<TenantLineageValidation>");
        Assert.Contains("TenantAuthorityMemo.GetTenantByIdAsync", activeTenantMethod, StringComparison.Ordinal);
        Assert.Contains("tenant.IsActive", tenantMemo, StringComparison.Ordinal);
        Assert.Contains("tenant.Status == TenantStatus.Active", tenantMemo, StringComparison.Ordinal);
        Assert.Contains("tenant.DeletedAtUtc == null", tenantMemo, StringComparison.Ordinal);

        Assert.Contains("tenant.IsActive", tenantResolution, StringComparison.Ordinal);
        Assert.Contains("tenant.Status == TenantStatus.Active", tenantResolution, StringComparison.Ordinal);

        Assert.DoesNotContain("tenant.IsActive", checkout, StringComparison.Ordinal);
        Assert.DoesNotContain("[IsActive] = CAST(1 AS bit)", checkout, StringComparison.Ordinal);
        Assert.DoesNotContain("tenant.IsActive", cart, StringComparison.Ordinal);
        Assert.Contains("tenant.IsActive", query, StringComparison.Ordinal);
        Assert.Contains("tenant.Status == TenantStatus.Active", query, StringComparison.Ordinal);
        Assert.DoesNotContain("tenant.IsActive", homepage, StringComparison.Ordinal);

        Assert.DoesNotContain("tenant.SetLifecycleStatus(", governance, StringComparison.Ordinal);
        Assert.DoesNotContain("tenant.Status = targetStatus", governance, StringComparison.Ordinal);
        Assert.DoesNotContain("tenant.IsActive = targetStatus", governance, StringComparison.Ordinal);
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing start marker: {startMarker}");
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"Missing end marker: {endMarker}");
        return source[start..end];
    }
}
