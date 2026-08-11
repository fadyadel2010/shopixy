using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Architecture;

public sealed class PosTenantAuthorityCertificationTests
{
    [Fact]
    public void TenantAuthority_MustComeFromCurrentActiveSqlTruth()
    {
        var lineage = Read(InfrastructurePath("Reliability", "TenantLineageAuthority.cs"));
        Assert.Contains(".IgnoreQueryFilters()", lineage, StringComparison.Ordinal);
        Assert.Contains("tenant.IsActive", lineage, StringComparison.Ordinal);
        Assert.Contains("tenant.Status == TenantStatus.Active", lineage, StringComparison.Ordinal);
        Assert.Contains("tenant.DeletedAtUtc == null", lineage, StringComparison.Ordinal);

        var tenantService = Read(InfrastructurePath("Services", "TenantService.cs"));
        Assert.Contains(".IgnoreQueryFilters()", tenantService, StringComparison.Ordinal);
        Assert.Contains("t.Status == TenantStatus.Active", tenantService, StringComparison.Ordinal);
        Assert.Contains("t.DeletedAtUtc == null", tenantService, StringComparison.Ordinal);
    }

    [Fact]
    public void PosSessionAuthority_MustRemainTenantBoundAndSqlCurrent()
    {
        var source = Read(ApiPath("Security", "PosSessionAuthority.cs"));
        Assert.Contains(".IgnoreQueryFilters()", source, StringComparison.Ordinal);
        Assert.Contains("actor.TenantId != tokenTenantId", source, StringComparison.Ordinal);
        Assert.Contains("stale_actor_role_claim", source, StringComparison.Ordinal);
        Assert.Contains("revoked_or_rotated_pos_session", source, StringComparison.Ordinal);
        Assert.Contains("TenantLineageAuthority.IsActiveTenantAsync", source, StringComparison.Ordinal);
        Assert.Contains("UserGovernanceStatus.Banned", source, StringComparison.Ordinal);
        Assert.Contains("UserGovernanceStatus.Suspended", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TenantResolutionAndQueryFilters_MustFailClosed()
    {
        var middleware = Read(ApiPath("Middleware", "TenantResolutionMiddleware.cs"));
        Assert.Contains("ResolveAuthenticatedTenantAsync", middleware, StringComparison.Ordinal);
        Assert.Contains("HeaderMatchesTenant", middleware, StringComparison.Ordinal);
        Assert.Contains("tenant.IsActive", middleware, StringComparison.Ordinal);
        Assert.Contains("tenant.Status == TenantStatus.Active", middleware, StringComparison.Ordinal);
        Assert.Contains("tenant.DeletedAtUtc == null", middleware, StringComparison.Ordinal);

        var dbContext = Read(InfrastructurePath("Data", "ApplicationDbContext.cs"));
        Assert.Contains("NO `CurrentTenantId == null` bypasses", dbContext, StringComparison.Ordinal);
        Assert.Contains("e.TenantId == CurrentTenantId", dbContext, StringComparison.Ordinal);
    }

    [Fact]
    public void TenantReadsAndWrites_MustRetainExplicitDefenseInDepth()
    {
        var commerceHub = Read(ApiPath("Controllers", "AdminCommerceHubController.cs"));
        Assert.Contains("CommerceHubSearchImpressions", commerceHub, StringComparison.Ordinal);
        Assert.Contains("item.TenantId == tenantId", commerceHub, StringComparison.Ordinal);
        Assert.Contains("item.SubOrder.StoreId == tenantId", commerceHub, StringComparison.Ordinal);

        var settings = Read(ApiPath("Controllers", "SettingsController.cs"));
        Assert.Contains("_tenantContext.RequiredTenantId", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("var tenantId = _tenantContext.TenantId", settings, StringComparison.Ordinal);

        var reports = Read(ApiPath("Controllers", "ReportsController.cs"));
        Assert.Contains("IActionFilter", reports, StringComparison.Ordinal);
        Assert.Contains("_tenantContext.RequiredTenantId", reports, StringComparison.Ordinal);
        Assert.DoesNotContain("IgnoreQueryFilters()", reports, StringComparison.Ordinal);

        var interceptor = Read(InfrastructurePath("Data", "TenantSaveChangesInterceptor.cs"));
        Assert.Contains("e.State == EntityState.Deleted", interceptor, StringComparison.Ordinal);
        Assert.Contains("Security Violation: Attempted to save data for a different tenant", interceptor, StringComparison.Ordinal);
    }

    [Fact]
    public void TenantLifecycleAndGovernance_MustRevokeRefreshAuthority()
    {
        var source = Read(InfrastructurePath("Data", "PrivilegedRefreshTokenRevocationInterceptor.cs"));
        Assert.Contains("TENANT_AUTHORITY_REDUCED", source, StringComparison.Ordinal);
        Assert.Contains("TenantStatus.Suspended or TenantStatus.Deleted", source, StringComparison.Ordinal);
        Assert.Contains("UserGovernanceStatus.Suspended or UserGovernanceStatus.Banned", source, StringComparison.Ordinal);
        Assert.Contains("user.RefreshToken = null", source, StringComparison.Ordinal);
        Assert.Contains("user.RefreshTokenExpiryTime = null", source, StringComparison.Ordinal);
    }

    private static string Read(string path) => File.ReadAllText(path);

    private static string ApiPath(params string[] parts) =>
        ProjectPath("PointOfSale.API", parts);

    private static string InfrastructurePath(params string[] parts) =>
        ProjectPath("PointOfSale.Infrastructure", parts);

    private static string ProjectPath(string project, params string[] parts) =>
        Path.GetFullPath(
            Path.Combine(
                new[] { TestConfiguration.GetSolutionRoot(), project }
                    .Concat(parts)
                    .ToArray()));
}
