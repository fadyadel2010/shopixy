namespace PointOfSale.Tests.Architecture;

public sealed class GlobalScopeTenantBoundaryCertificationTests
{
    [Fact]
    public void PlatformAndTenantAuthorityMiddlewareMustRemainSeparateAndSqlBacked()
    {
        var program = ReadRepoFile("PointOfSale.API/Program.cs");
        var platformMiddleware = ReadRepoFile("PointOfSale.API/SuperAdmin/Infrastructure/SuperAdminPlatformAuthorityMiddleware.cs");
        var tenantMiddleware = ReadRepoFile("PointOfSale.API/Middleware/AdminTenantAuthorityMiddleware.cs");
        var tenantAuthority = ReadRepoFile("PointOfSale.API/Security/AdminTenantAuthority.cs");

        Assert.Contains("AddScoped<PointOfSale.API.SuperAdmin.Infrastructure.ISuperAdminPlatformAuthority", program, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IAdminTenantAuthority, AdminTenantAuthority>", program, StringComparison.Ordinal);
        Assert.Contains("UseMiddleware<PointOfSale.API.SuperAdmin.Infrastructure.SuperAdminPlatformAuthorityMiddleware>", program, StringComparison.Ordinal);
        Assert.Contains("UseMiddleware<PointOfSale.API.SuperAdmin.Infrastructure.SuperAdminOperationalAuditMiddleware>", program, StringComparison.Ordinal);
        Assert.Contains("UseMiddleware<PointOfSale.API.Middleware.AdminTenantAuthorityMiddleware>", program, StringComparison.Ordinal);

        Assert.Contains("ValidatePlatformActorAsync", platformMiddleware, StringComparison.Ordinal);
        Assert.Contains("SuperAdminSecurityConstants.PolicyName", platformMiddleware, StringComparison.Ordinal);
        Assert.Contains("GetMetadata<IAllowAnonymous>()", platformMiddleware, StringComparison.Ordinal);
        Assert.Contains("SUPER_ADMIN_SCOPE_FORBIDDEN", platformMiddleware, StringComparison.Ordinal);

        Assert.Contains("AdminTenantAuthority.HasSuperAdminPolicy(endpoint)", tenantMiddleware, StringComparison.Ordinal);
        Assert.Contains("AdminTenantAuthority.ExtractTenantRoleSets(endpoint)", tenantMiddleware, StringComparison.Ordinal);
        Assert.Contains("ValidateTenantActorAsync", tenantMiddleware, StringComparison.Ordinal);
        Assert.Contains("ADMIN_SCOPE_FORBIDDEN", tenantMiddleware, StringComparison.Ordinal);

        Assert.Contains("_dbContext.Users", tenantAuthority, StringComparison.Ordinal);
        Assert.Contains("actor.TenantId != trustedTenantId.Value", tenantAuthority, StringComparison.Ordinal);
        Assert.Contains("TenantLineageAuthority.IsActiveTenantAsync", tenantAuthority, StringComparison.Ordinal);
        Assert.Contains("OperationalConvergenceAuthority.AdminTenant", tenantAuthority, StringComparison.Ordinal);
        Assert.Contains("actor_tenant_claim_mismatch", tenantAuthority, StringComparison.Ordinal);
        Assert.Contains("tenant_inactive_or_deleted", tenantAuthority, StringComparison.Ordinal);
    }

    [Fact]
    public void GlobalOperationsMustRequirePlatformAuthorityAndTenantOperationsMustBlockGlobalBehavior()
    {
        var superAdminBase = ReadRepoFile("PointOfSale.API/SuperAdmin/Controllers/SuperAdminBaseController.cs");
        var marketplace = ReadRepoFile("PointOfSale.API/Controllers/MarketplaceController.cs");
        var searchReindex = ReadRepoFile("PointOfSale.API/Controllers/SearchReindexController.cs");
        var adminCommerceHub = ReadRepoFile("PointOfSale.API/Controllers/AdminCommerceHubController.cs");
        var tenants = ReadRepoFile("PointOfSale.API/Controllers/TenantsController.cs");
        var maintenance = ReadRepoFile("PointOfSale.API/Controllers/MaintenanceController.cs");

        Assert.Contains("[Authorize(Policy = SuperAdminSecurityConstants.PolicyName)]", superAdminBase, StringComparison.Ordinal);
        Assert.Contains("[AllowAnonymousTenant]", superAdminBase, StringComparison.Ordinal);
        Assert.Contains("CurrentSuperAdminId", superAdminBase, StringComparison.Ordinal);

        Assert.Contains("ReindexServicesGlobally", marketplace, StringComparison.Ordinal);
        Assert.Contains("[Authorize(Policy = SuperAdminSecurityConstants.PolicyName)]", marketplace, StringComparison.Ordinal);
        Assert.Contains("Super admin requested global marketplace services reindex", marketplace, StringComparison.Ordinal);
        Assert.Contains("s => !s.IsDeleted && s.IsActive", marketplace, StringComparison.Ordinal);
        Assert.Contains("ReindexTenantServicesAsync", marketplace, StringComparison.Ordinal);
        Assert.Contains("Tenant admin requested marketplace services reindex", marketplace, StringComparison.Ordinal);
        Assert.Contains("s.TenantId == tenantId && !s.IsDeleted && s.IsActive", marketplace, StringComparison.Ordinal);
        Assert.Contains("GLOBAL_SEARCH_INDEX_UNAVAILABLE", marketplace, StringComparison.Ordinal);

        Assert.Contains("GLOBAL_SEARCH_CONTROL_FORBIDDEN", searchReindex, StringComparison.Ordinal);
        Assert.Contains("_tenantContext.RequiredTenantId", searchReindex, StringComparison.Ordinal);
        Assert.Contains("product.TenantId == tenantId", searchReindex, StringComparison.Ordinal);
        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyAsync", searchReindex, StringComparison.Ordinal);
        Assert.DoesNotContain("_indexService.DeleteIndexAsync", searchReindex, StringComparison.Ordinal);

        Assert.Contains("GLOBAL_SEARCH_CONTROL_FORBIDDEN", adminCommerceHub, StringComparison.Ordinal);
        Assert.Contains("_reindexService.ReindexTenantAsync(tenantId", adminCommerceHub, StringComparison.Ordinal);
        Assert.DoesNotContain("_reindexService.CatchUpReindexAsync(", adminCommerceHub, StringComparison.Ordinal);
        Assert.DoesNotContain("_searchIndexService.FinalizeAliasMigrationAsync(", adminCommerceHub, StringComparison.Ordinal);

        Assert.Contains("Authorize(Policy = SuperAdminSecurityConstants.PolicyName)", tenants, StringComparison.Ordinal);
        Assert.Contains("Authorize(Policy = SuperAdminSecurityConstants.PolicyName)", maintenance, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplayRecoveryReindexDiagnosticsMustPreserveTenantScopeAndSqlAuthority()
    {
        var affiliateReindex = ReadRepoFile("PointOfSale.Infrastructure/Scripts/AF16E_ReindexAffiliateProductsWithStorefrontUrl.cs");
        var commerceHubReindex = ReadRepoFile("PointOfSale.Infrastructure/Jobs/CommerceHubReindexService.cs");
        var autoHealing = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubAutoHealingService.cs");
        var searchAnalytics = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubSearchAnalyticsBackgroundService.cs");
        var discountRepair = ReadRepoFile("PointOfSale.API/Services/ReadModels/DiscountReadModelRepairService.cs");
        var marketplace = ReadRepoFile("PointOfSale.API/Controllers/MarketplaceController.cs");
        var eventPublisher = ReadRepoFile("PointOfSale.Infrastructure/EventBus/MassTransitEventPublisher.cs");

        Assert.Contains("ReindexTenantAsync(Guid tenantId", affiliateReindex, StringComparison.Ordinal);
        Assert.Contains("TenantScopedDerivativeKey.ForTenantEntity", affiliateReindex, StringComparison.Ordinal);
        Assert.Contains("SqlTruthRebuildGuard.RequireSqlAuthority", affiliateReindex, StringComparison.Ordinal);
        Assert.Contains("t.IsActive", affiliateReindex, StringComparison.Ordinal);

        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", commerceHubReindex, StringComparison.Ordinal);
        Assert.Contains("CountSqlVisibleCommerceHubListingsAsync", commerceHubReindex, StringComparison.Ordinal);
        Assert.Contains("targetCount.Value == publishedCount", commerceHubReindex, StringComparison.Ordinal);
        Assert.Contains("CommerceHubCategoryLineageAuthority.LoadLiveIdsAsync", commerceHubReindex, StringComparison.Ordinal);

        Assert.Contains("RecoveryIdentity(Guid? TenantId, Guid ListingId)", autoHealing, StringComparison.Ordinal);
        Assert.Contains("EnqueueDocumentsByTenant", autoHealing, StringComparison.Ordinal);
        Assert.Contains("Filter.Eq(x => x.TenantId, item.TenantId)", autoHealing, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", autoHealing, StringComparison.Ordinal);

        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", searchAnalytics, StringComparison.Ordinal);
        Assert.Contains("indexService.DeleteAsync", searchAnalytics, StringComparison.Ordinal);
        Assert.Contains("SqlTruthRebuildGuard.RequireSqlAuthority", discountRepair, StringComparison.Ordinal);
        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyAsync", discountRepair, StringComparison.Ordinal);

        Assert.Contains("Tenant marketplace service reindex failed to scan existing Elasticsearch documents", marketplace, StringComparison.Ordinal);
        Assert.Contains("f.Term(t => t.Field(d => d.TenantId).Value(tenantId))", marketplace, StringComparison.Ordinal);
        Assert.Contains("recentServices", marketplace, StringComparison.Ordinal);
        Assert.Contains("tenantId = GetTenantId()", marketplace, StringComparison.Ordinal);

        Assert.Contains("EnsureAuthoritativeTenantScope", eventPublisher, StringComparison.Ordinal);
        Assert.Contains("ValidateAuthoritativeEventContract", eventPublisher, StringComparison.Ordinal);
    }

    [Fact]
    public void ScopeChangingActionsMustRemainAuditableAndDocumented()
    {
        var superAdminAudit = ReadRepoFile("PointOfSale.API/SuperAdmin/Infrastructure/SuperAdminOperationalAuditMiddleware.cs");
        var tenantAuthority = ReadRepoFile("PointOfSale.API/Security/AdminTenantAuthority.cs");
        var findings = ReadRepoFile("docs/architecture/global-scope-tenant-boundary-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");

        Assert.Contains("PLATFORM_OPERATION_COMPLETED", superAdminAudit, StringComparison.Ordinal);
        Assert.Contains("SuperAdminPlatformAuthority.SuperAdminId", superAdminAudit, StringComparison.Ordinal);
        Assert.Contains("TokenVersion", superAdminAudit, StringComparison.Ordinal);
        Assert.Contains("OperationalConvergenceTelemetry.RecordAuthorityFailure", tenantAuthority, StringComparison.Ordinal);

        foreach (var marker in new[]
        {
            "POS-ADMIN-CERTIFICATION-01 PASS 11.2",
            "1. Scope Inventory Findings",
            "2. RequireSuperAdmin Findings",
            "3. Tenant-Boundary Findings",
            "4. Marketplace Governance Findings",
            "5. Replay And Recovery Findings",
            "6. Reindex Findings",
            "7. Diagnostics Findings",
            "8. Service-Layer Findings",
            "9. Tenant-Context Findings",
            "10. Auditability Findings",
            "11. Chaos Findings",
            "12. Runtime Hardening Performed",
            "13. Monitoring Improvements",
            "14. Governance Updates",
            "15. Honest Remaining Debt"
        })
        {
            Assert.Contains(marker, findings, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("PASS 11.2 Global Scope", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS 11.2 Global Scope / Tenant Boundary Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("Global scope and tenant-boundary authority now has a documented certification boundary", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS 11.2 Global Scope / Tenant Boundary Freeze", codexRules, StringComparison.Ordinal);
        Assert.Contains("PASS 11.2 Global Scope & Tenant Boundary", hardening, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string FindRepoRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root could not be found.");
    }
}
