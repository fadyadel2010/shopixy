namespace PointOfSale.Tests.Architecture;

public sealed class EmergencyGovernanceCertificationTests
{
    [Fact]
    public void Pass115EmergencyAuthority_MustStayOperationalNotBusinessAuthority()
    {
        AssertSourceContains(
            ApiPath("SuperAdmin", "Infrastructure", "SuperAdminPlatformAuthority.cs"),
            "_dbContext.SuperAdmins",
            "_dbContext.UserGovernanceProfiles",
            "STALE_SUPER_ADMIN_TOKEN",
            "SUPER_ADMIN_GOVERNANCE_REJECTED");

        AssertSourceContains(
            ApiPath("SuperAdmin", "Infrastructure", "SuperAdminPlatformAuthorityMiddleware.cs"),
            "ValidatePlatformActorAsync",
            "SUPER_ADMIN_SCOPE_FORBIDDEN");

        AssertSourceContains(
            ApiPath("SuperAdmin", "Infrastructure", "SuperAdminOperationalAuditMiddleware.cs"),
            "PLATFORM_OPERATION_COMPLETED",
            "SuperAdminPlatformAuthority.SuperAdminId",
            "SuperAdminPlatformAuthority.TokenVersion");

        AssertSourceContains(
            ApiPath("Controllers", "MaintenanceController.cs"),
            "OPERATION_DISABLED",
            "permanently disabled");

        AssertSourceContains(
            ApiPath("Controllers", "DebugController.cs"),
            "RequireDevelopment()",
            "SuperAdminSecurityConstants.PolicyName");
    }

    [Fact]
    public void Pass115EmergencyMarketplaceRecoveryAndSearch_MustRemainSqlDerived()
    {
        AssertSourceContains(
            ApiPath("Services", "CommerceHub", "CommerceHubVendorVisibilityService.cs"),
            "RestorePublicationOnActivate",
            "CommerceHubCategoryLineageAuthority.LoadLiveIdsAsync",
            "ClearGovernanceRestoreState",
            "product.IsOnlineVisible",
            "CommerceHubVendorAuthority.LoadAsync",
            "vendorAuthority.IsMarketplaceEligible");

        AssertSourceContains(
            InfrastructurePath("Jobs", "CommerceHubReindexService.cs"),
            "CommerceHubSearchSqlAuthorityOverlay.ApplyAsync",
            "LoadSqlVisibleListingIdentitiesAsync",
            "DeleteStaleSearchDocumentsAsync",
            "StaleDeleted");

        AssertSourceContains(
            InfrastructurePath("Elasticsearch", "Services", "CommerceHubSearchIndexService.cs"),
            "DeleteStaleAsync",
            "liveIdentities");

        AssertSourceContains(
            ApiPath("Services", "CommerceHub", "CommerceHubAutoHealingService.cs"),
            "RecoveryIdentity(Guid? TenantId, Guid ListingId)",
            "Filter.Eq(x => x.TenantId, item.TenantId)",
            "CommerceHubSearchSqlAuthorityOverlay.ApplyAsync");
    }

    [Fact]
    public void Pass115EmergencyReplayTenantIsolationRevocationAndMonitoring_MustRemainObservable()
    {
        AssertSourceContains(
            ApiPath("Security", "AdminTenantAuthority.cs"),
            "TenantId",
            "RecordAuthorityFailure");

        AssertSourceContains(
            InfrastructurePath("Eventing", "MarketplaceProjectionOutboxProcessor.cs"),
            "ClaimedUntilUtc",
            "OperationalConvergenceTelemetry.RecordReplayDecision");

        AssertSourceContains(
            InfrastructurePath("Payment", "PaymentCancellationOutboxProcessor.cs"),
            "ClaimedUntilUtc",
            "IPaymentLifecycleService",
            "OperationalConvergenceTelemetry.RecordReplayDecision");

        AssertSourceContains(
            InfrastructurePath("Reliability", "DerivativeProjectionMutationGuard.cs"),
            "DangerousPrimitiveRejected",
            "OperationalConvergenceTelemetry.RecordDangerousPrimitive");

        AssertSourceContains(
            InfrastructurePath("Instrumentation", "OperationalConvergenceTelemetry.cs"),
            "DangerousPrimitiveGovernanceTotal",
            "RecordAuthorityFailure",
            "RecordDriftDiagnostic");
    }

    [Fact]
    public void Pass115DocumentationAndGovernance_MustRecordCurrentEmergencyCertification()
    {
        var findings = File.ReadAllText(DocsPath("architecture", "emergency-governance-certification-findings.md"));
        var projectContext = File.ReadAllText(ContextPath("PROJECT_CONTEXT.md"));
        var architectureTruth = File.ReadAllText(ContextPath("ARCHITECTURE_TRUTH.md"));
        var knownRisks = File.ReadAllText(ContextPath("KNOWN_RISKS.md"));
        var codexRules = File.ReadAllText(ContextPath("CODEX_RULES.md"));
        var hardening = File.ReadAllText(ContextPath("CROSS_SYSTEM_AUTHORITY_HARDENING.md"));
        var primitives = File.ReadAllText(DocsPath("architecture", "dangerous-operational-primitives.md"));
        var runbook = File.ReadAllText(DocsPath("operations", "operational-recovery-runbooks.md"));

        foreach (var marker in new[]
        {
            "POS-ADMIN-CERTIFICATION-01 PASS 11.5",
            "1. Emergency Inventory Findings",
            "2. Break-Glass Findings",
            "3. Emergency Marketplace Findings",
            "4. Emergency Recovery Findings",
            "5. Emergency Search Findings",
            "6. Emergency Tenant-Isolation Findings",
            "7. Revocation Findings",
            "8. Auditability Findings",
            "9. Runbook Findings",
            "10. Chaos Findings",
            "11. Runtime Hardening Performed",
            "12. Monitoring Improvements",
            "13. Governance Updates",
            "14. Honest Remaining Debt",
            "Emergency authority is operational authority"
        })
        {
            Assert.Contains(marker, findings, StringComparison.Ordinal);
        }

        Assert.Contains("PASS 11.5 Emergency Operations", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS 11.5 Emergency Operations / Break-Glass Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("Emergency operations and break-glass authority now have a documented certification boundary", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS 11.5 Emergency Operations / Break-Glass Freeze", codexRules, StringComparison.Ordinal);
        Assert.Contains("PASS 11.5 Emergency Operations & Break-Glass", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS 11.5 Emergency Operations / Break-Glass Freeze", primitives, StringComparison.Ordinal);
        Assert.Contains("Emergency authority is operational authority", runbook, StringComparison.Ordinal);
    }

    [Fact]
    public void SuperAdminMutations_MustHaveRequestLevelOperationalAuditCoverage()
    {
        AssertSourceContains(
            ApiPath("SuperAdmin", "Infrastructure", "SuperAdminOperationalAuditMiddleware.cs"),
            "GetOrderedMetadata<IAuthorizeData>()",
            "SuperAdminSecurityConstants.PolicyName",
            "PLATFORM_OPERATION_COMPLETED",
            "SuperAdminPlatformAuthority.SuperAdminId",
            "SuperAdminPlatformAuthority.TokenVersion",
            "CancellationToken.None");

        var program = File.ReadAllText(ApiPath("Program.cs"));
        Assert.Contains("SuperAdminPlatformAuthorityMiddleware", program, StringComparison.Ordinal);
        Assert.Contains("SuperAdminOperationalAuditMiddleware", program, StringComparison.Ordinal);
        Assert.True(
            program.IndexOf("SuperAdminPlatformAuthorityMiddleware", StringComparison.Ordinal) <
            program.IndexOf("SuperAdminOperationalAuditMiddleware", StringComparison.Ordinal),
            "Operational audit must run after current-SQL platform actor authority so audit attribution is SQL-recomposed.");
    }

    [Fact]
    public void LegacyPlatformPolicyEndpoints_MustRemainBehindSuperAdminAuthority()
    {
        foreach (var relativePath in new[]
        {
            Path.Combine("Controllers", "TenantsController.cs"),
            Path.Combine("Controllers", "MaintenanceController.cs"),
            Path.Combine("Controllers", "DevelopmentCommerceHubController.cs"),
            Path.Combine("Controllers", "DebugController.cs"),
            Path.Combine("Controllers", "AdminFinanceController.cs")
        })
        {
            AssertSourceContains(
                ApiPath(relativePath),
                "SuperAdminSecurityConstants.PolicyName",
                "[AllowAnonymousTenant]");
        }
    }

    [Fact]
    public void SupportImpersonationAndOverrideGovernance_MustRemainExplicitlyDocumented()
    {
        var findings = File.ReadAllText(DocsPath("architecture", "emergency-governance-certification-findings.md"));

        foreach (var marker in new[]
        {
            "SUPERADMIN-CON-01 Phase 4",
            "Emergency Override Governance",
            "Support Tooling And Impersonation",
            "Rollback And Escalation Governance",
            "Dangerous Action Observability",
            "Remaining Auditability Debt",
            "No production support impersonation endpoint was found",
            "request-level operational audit"
        })
        {
            Assert.Contains(marker, findings, StringComparison.Ordinal);
        }

        AssertSourceContains(
            DocsPath("operations", "operational-recovery-runbooks.md"),
            "Support Tooling And Impersonation Governance",
            "Rollback And Emergency Override Governance");

        AssertSourceContains(
            DocsPath("architecture", "dangerous-operational-primitives.md"),
            "SUPERADMIN-CON-01 Phase 4 Emergency Governance Freeze",
            "request-level operational audit");
    }

    private static void AssertSourceContains(string path, params string[] markers)
    {
        var source = File.ReadAllText(path);
        foreach (var marker in markers)
        {
            Assert.Contains(marker, source, StringComparison.Ordinal);
        }
    }

    private static string ApiPath(params string[] parts) =>
        Path.Combine([DiscoverSolutionRoot(AppContext.BaseDirectory), "PointOfSale.API", .. parts]);

    private static string InfrastructurePath(params string[] parts) =>
        Path.Combine([DiscoverSolutionRoot(AppContext.BaseDirectory), "PointOfSale.Infrastructure", .. parts]);

    private static string DocsPath(params string[] parts) =>
        Path.Combine([DiscoverSolutionRoot(AppContext.BaseDirectory), "docs", .. parts]);

    private static string ContextPath(string fileName) =>
        Path.Combine(DiscoverSolutionRoot(AppContext.BaseDirectory), ".ai-context", fileName);

    private static string DiscoverSolutionRoot(string startPath)
    {
        var currentDir = new DirectoryInfo(startPath);

        while (currentDir != null)
        {
            if (currentDir.GetFiles("*.sln").Any() ||
                currentDir.GetDirectories("PointOfSale.API").Any())
            {
                return currentDir.FullName;
            }

            currentDir = currentDir.Parent;
        }

        throw new InvalidOperationException($"Could not discover solution root from '{startPath}'.");
    }
}
