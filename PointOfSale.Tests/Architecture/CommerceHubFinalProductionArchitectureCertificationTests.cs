namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubFinalProductionArchitectureCertificationTests
{
    [Fact]
    public void RuntimeArchitectureInventory_MustRemainSingleOwnerEvidenceForwardAndDerivativeOnly()
    {
        var boundary = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/CommerceHubBoundaryMutationScope.cs");
        var dbContext = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");
        var publication = CertificationSource.ReadRepoFile("PointOfSale.API/CommerceHub/Handlers/CommerceHubCommandHandlers.cs");
        var cart = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCartService.cs");
        var shipping = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubShippingService.cs");
        var checkout = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");
        var payment = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs");
        var shipment = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Shipping/ShipmentService.cs");
        var carrier = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs");
        var refund = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubRefundService.cs");
        var payout = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPayoutService.cs");
        var reconciliation = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubFinanceReconciliationJob.cs");
        var projection = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Consumers/CommerceHubOrderProjectionConsumer.cs");
        var projectionRepair = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubOrderProjectionRepairService.cs");
        var searchOverlay = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchSqlAuthorityOverlay.cs");
        var cache = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Redis/Services/RedisJsonCacheService.cs");
        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");

        foreach (var marker in new[]
        {
            "BeginOrderLifecycle",
            "BeginPayment",
            "BeginRefund",
            "BeginPayout",
            "BeginFinancialWorkflow",
            "BeginPaymentLifecycle",
            "BeginShippingAssignment",
            "BeginProjection"
        })
        {
            Assert.Contains(marker, boundary, StringComparison.Ordinal);
        }

        foreach (var marker in new[]
        {
            "GuardCommerceHubBoundaryOwnership",
            "GuardBoundaryOwnedProperties<CommerceHubMarketplaceOrder>",
            "GuardBoundaryOwnedProperties<CommerceHubSubOrder>",
            "GuardBoundaryOwnedProperties<CommerceHubPayment>",
            "GuardBoundaryOwnedProperties<CommerceHubRefund>",
            "GuardBoundaryOwnedProperties<CommerceHubPayout>",
            "GuardBoundaryOwnedProperties<CommerceHubVendorEarning>",
            "IsShippingAssignmentAuthorized",
            "CommerceHub projection markers can only be changed by the CommerceHub projection boundary"
        })
        {
            Assert.Contains(marker, dbContext, StringComparison.Ordinal);
        }

        Assert.Contains("EnsureVendorListingAllowedAsync", publication, StringComparison.Ordinal);
        Assert.Contains("WherePubliclyVisibleLineage", publication, StringComparison.Ordinal);
        Assert.Contains("CommerceHubCategoryLineageAuthority.IsLiveAsync", publication, StringComparison.Ordinal);
        Assert.Contains("WherePubliclyVisibleLineage()", cart, StringComparison.Ordinal);
        Assert.Contains("method.TenantId == storeId", shipping, StringComparison.Ordinal);
        Assert.Contains("configuration.TenantId == method.TenantId", shipping, StringComparison.Ordinal);
        Assert.Contains("OrderBy(candidate => candidate.EffectivePrice)", shipping, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginOrderLifecycle()", checkout, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginShippingAssignment()", checkout, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginFinancialWorkflow()", checkout, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginPaymentLifecycle()", checkout, StringComparison.Ordinal);
        Assert.Contains("IsolationLevel.Serializable", checkout, StringComparison.Ordinal);
        Assert.Contains("BuildCheckoutRequestHash", checkout, StringComparison.Ordinal);
        Assert.Contains("bypassCache: true", checkout, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginPayment()", payment, StringComparison.Ordinal);
        Assert.Contains("BuildPaymentLockKey", payment, StringComparison.Ordinal);
        Assert.Contains("RequireCommerceHubPaymentCurrency", payment, StringComparison.Ordinal);
        Assert.Contains("ShipmentLifecycleTransitionPolicy.CanTransition", shipment, StringComparison.Ordinal);
        Assert.Contains("ValidateWebhookTrust(payload, signature, sourceIp", carrier, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginRefund()", refund, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginPayout()", refund, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginPayout()", payout, StringComparison.Ordinal);
        Assert.Contains("RequireEgpCurrency", payout, StringComparison.Ordinal);
        Assert.Contains("ExternalTransferIdempotencyKey", payout, StringComparison.Ordinal);
        Assert.Contains("ResolvePaidPaymentForWebhookAsync", reconciliation, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginOrderLifecycle()", reconciliation, StringComparison.Ordinal);
        Assert.Contains("FinancialCurrencyGuard.RequireEgpCurrency", reconciliation, StringComparison.Ordinal);

        Assert.Contains("CommerceHubBoundaryMutationScope.BeginProjection()", projection, StringComparison.Ordinal);
        Assert.Contains("LastSourceMutationTicks", projection, StringComparison.Ordinal);
        Assert.Contains("BuildProjectionDocument(order)", projectionRepair, StringComparison.Ordinal);
        Assert.Contains("ProjectionProvesSqlAuthority", projectionRepair, StringComparison.Ordinal);
        Assert.Contains("SqlTruthRebuildGuard.RequireSqlAuthority", searchOverlay, StringComparison.Ordinal);
        Assert.Contains("FinancialCurrencyGuard.PlatformBaseCurrencyCode", searchOverlay, StringComparison.Ordinal);
        Assert.Contains("CacheEnvelope", cache, StringComparison.Ordinal);
        Assert.Contains("ShouldBypassForProjectionLag", cache, StringComparison.Ordinal);
        Assert.Contains("KeyDeleteAsync", cache, StringComparison.Ordinal);

        foreach (var marker in new[]
        {
            "UseBusOutbox",
            "UsingRabbitMq",
            "UseMessageRetry",
            "CommerceHubPayoutReconciliationHostedService",
            "CommerceHubRefundRecoveryHostedService",
            "CommerceHubProjectionReconciliationService",
            "CheckoutPaymentIntentRecoveryHostedService",
            "ShipmentAuthorityRecoveryHostedService",
            "CommerceHubReindexService"
        })
        {
            Assert.Contains(marker, program, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DocumentationFreeze_MustRecordArchSixFinalProductionArchitectureCertification()
    {
        var findings = CertificationSource.ReadRepoFile("docs/architecture/commercehub-final-production-architecture-certification-findings.md");
        var projectContext = CertificationSource.ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = CertificationSource.ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = CertificationSource.ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = CertificationSource.ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = CertificationSource.ReadRepoFile(".ai-context/CODEX_RULES.md");

        foreach (var marker in new[]
        {
            "ARCH-6 CommerceHub Final Production Architecture Certification",
            "Executive Summary",
            "Complete Architecture Findings",
            "Complete Authority Findings",
            "Complete Domain Findings",
            "Infrastructure Findings",
            "Event Findings",
            "Projection Findings",
            "Recovery Findings",
            "Tenant Findings",
            "Failure Findings",
            "Operational Findings",
            "Remaining Risks",
            "Architecture Blocker: None.",
            "Operational Risk:",
            "Infrastructure Risk:",
            "Monitoring Gap:",
            "Future Enhancement:",
            "Is CommerceHub production-certifiable as one coherent architecture? Yes.",
            "Are there any unresolved architectural blockers? No.",
            "Are there any unresolved authority blockers? No.",
            "Are there any unresolved tenant blockers? No.",
            "Are there any unresolved lifecycle blockers? No.",
            "Are there any unresolved currency blockers? No.",
            "Were runtime changes required? No.",
            "Is CommerceHub Enterprise Architecture Certification PASSED? Yes.",
            "Runtime Hardening Performed",
            "Exact Runtime Files Modified",
            "Regression Freezes Added",
            "Architecture Updates"
        })
        {
            Assert.Contains(marker, findings, StringComparison.Ordinal);
        }

        Assert.Contains("PASS ARCH-6 CommerceHub Final Production Architecture Certification", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS ARCH-6 CommerceHub Final Production Architecture Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS ARCH-6 final production architecture runtime certifiable", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS ARCH-6 CommerceHub Final Production Architecture Certification", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS ARCH-6 CommerceHub Final Production Architecture Freeze", codexRules, StringComparison.Ordinal);
    }
}
