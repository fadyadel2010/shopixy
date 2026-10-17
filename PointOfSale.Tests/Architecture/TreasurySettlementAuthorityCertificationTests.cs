namespace PointOfSale.Tests.Architecture;

public sealed class TreasurySettlementAuthorityCertificationTests
{
    [Fact]
    public void RuntimeSettlementAuthority_MustBlockGenericPlatformToVendorSettlementBypass()
    {
        var superAdminVendorFinance = ReadRepoFile("PointOfSale.API/SuperAdmin/Services/SuperAdminVendorFinanceService.cs");
        var superAdminPayoutOrchestration = ReadRepoFile("PointOfSale.API/SuperAdmin/Services/SuperAdminPayoutOrchestrationService.cs");
        var canonicalPayoutService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPayoutService.cs");
        var controller = ReadRepoFile("PointOfSale.API/SuperAdmin/Controllers/SuperAdminVendorFinanceController.cs");

        Assert.Contains("PAYOUT_EXECUTION_WORKFLOW_REQUIRED", superAdminVendorFinance, StringComparison.Ordinal);
        Assert.Contains("SuperAdminVendorSettlementFlow.PlatformPaysVendor", superAdminVendorFinance, StringComparison.Ordinal);
        Assert.Contains("certified Super Admin payout execution workflow", superAdminVendorFinance, StringComparison.Ordinal);

        Assert.Contains("ICommerceHubPayoutService", superAdminPayoutOrchestration, StringComparison.Ordinal);
        Assert.Contains("MarkProcessingAsync", superAdminPayoutOrchestration, StringComparison.Ordinal);
        Assert.Contains("MarkCompletedAsync", superAdminPayoutOrchestration, StringComparison.Ordinal);
        Assert.Contains("ExternalTransferConfirmedExecuted = true", superAdminPayoutOrchestration, StringComparison.Ordinal);

        Assert.Contains("EnsureOutwardMoneyMovementAllowedAsync", canonicalPayoutService, StringComparison.Ordinal);
        Assert.Contains("EnsureTerminalPayoutEvidence", canonicalPayoutService, StringComparison.Ordinal);
        Assert.Contains("ApplyCompletedPayoutAllocation", canonicalPayoutService, StringComparison.Ordinal);
        Assert.Contains("EnsureEarningsHaveLedgerConfirmationAsync", canonicalPayoutService, StringComparison.Ordinal);
        Assert.Contains("VerifyStateTransitionAsync", canonicalPayoutService, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_EXTERNAL_EVIDENCE_ALREADY_USED", canonicalPayoutService, StringComparison.Ordinal);

        Assert.Contains("[HttpPost(\"{vendorId:guid}/settlement\")]", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void SettlementRowsAndBatches_MustBeSqlOwnedIdempotentAndLineageBound()
    {
        var settlementEntity = ReadRepoFile("PointOfSale.Core/Entities/CommerceHubFinance/CommerceHubVendorSettlement.cs");
        var settlementConfig = ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/CommerceHubVendorSettlementConfiguration.cs");
        var paymentLifecycle = ReadRepoFile("PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs");

        Assert.Contains("CommerceHubVendorSettlement", settlementEntity, StringComparison.Ordinal);
        Assert.Contains("VendorId", settlementEntity, StringComparison.Ordinal);
        Assert.Contains("IdempotencyKey", settlementEntity, StringComparison.Ordinal);
        Assert.Contains("ExternalReference", settlementEntity, StringComparison.Ordinal);
        Assert.Contains("rejectLegacyPrefix: true", settlementEntity, StringComparison.Ordinal);
        Assert.Contains("UX_CommerceHubVendorSettlements_Vendor_IdempotencyKey", settlementConfig, StringComparison.Ordinal);
        Assert.Contains("UX_CommerceHubVendorSettlements_ExternalReference", settlementConfig, StringComparison.Ordinal);

        Assert.Contains("RecordOfflineSettlementAsync", paymentLifecycle, StringComparison.Ordinal);
        Assert.Contains("RecordGatewaySettlementAsync", paymentLifecycle, StringComparison.Ordinal);
        Assert.Contains("LoadSettlementBatchAsync", paymentLifecycle, StringComparison.Ordinal);
        Assert.Contains("SettlementBatchMatches", paymentLifecycle, StringComparison.Ordinal);
        Assert.Contains("duplicate_settlement_mismatch", paymentLifecycle, StringComparison.Ordinal);
        Assert.Contains("PaymentStatus.Settled", paymentLifecycle, StringComparison.Ordinal);
        Assert.Contains("PaymentStatus.Collected", paymentLifecycle, StringComparison.Ordinal);
    }

    [Fact]
    public void SettlementEligibilityAndRelease_MustDeriveFromSqlLedgerReservationAndRefundTruth()
    {
        var payoutService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPayoutService.cs");
        var payoutReadService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/Finance/CommerceHubVendorPayoutReadService.cs");
        var readinessGate = ReadRepoFile("PointOfSale.API/Services/CommerceHub/Finance/CommerceHubFinancialReadinessGate.cs");
        var refundService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubRefundService.cs");

        Assert.Contains("EnsureOutwardMoneyMovementAllowedAsync", payoutService, StringComparison.Ordinal);
        Assert.Contains("EnsureEarningsHaveLedgerConfirmationAsync", payoutService, StringComparison.Ordinal);
        Assert.Contains("PayoutReservedAmount", payoutService, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_DELAY_NOT_ELAPSED", payoutService, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_EARNING_NOT_AVAILABLE", payoutService, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_EARNING_STATE_INVALID", payoutService, StringComparison.Ordinal);
        Assert.Contains("ApplyCompletedPayoutAllocation", payoutService, StringComparison.Ordinal);
        Assert.Contains("PayoutBlockedByAuthorityConflict", payoutReadService, StringComparison.Ordinal);
        Assert.Contains("EligibleAmount", payoutReadService, StringComparison.Ordinal);
        Assert.Contains("HoldbackAmount", payoutReadService, StringComparison.Ordinal);
        Assert.Contains("CommerceHubFinancialReadinessGate", readinessGate, StringComparison.Ordinal);
        Assert.Contains("ReconciliationRequired", readinessGate, StringComparison.Ordinal);
        Assert.Contains("PayoutReservedAmount", refundService, StringComparison.Ordinal);
    }

    [Fact]
    public void SettlementReplayCompletionAndEvidence_MustCollapseOrFailClosed()
    {
        var payoutService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPayoutService.cs");
        var superAdminPayoutOrchestration = ReadRepoFile("PointOfSale.API/SuperAdmin/Services/SuperAdminPayoutOrchestrationService.cs");
        var financialLedger = ReadRepoFile("PointOfSale.API/Services/CommerceHub/FinancialLedger/CommerceHubFinancialLedgerService.cs");
        var affiliateSettlement = ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateSettlementOrchestrator.cs");

        Assert.Contains("NormalizeIdempotencyKey", superAdminPayoutOrchestration, StringComparison.Ordinal);
        Assert.Contains("ExternalTransferIdempotencyKey", superAdminPayoutOrchestration, StringComparison.Ordinal);
        Assert.Contains("EnsureCommittedExternalHandoff", superAdminPayoutOrchestration, StringComparison.Ordinal);
        Assert.Contains("ExternalTransferConfirmedExecuted = true", superAdminPayoutOrchestration, StringComparison.Ordinal);
        Assert.Contains("IsIdempotencyKeyUniqueViolation", payoutService, StringComparison.Ordinal);
        Assert.Contains("EnsureRequestedPayoutEvidenceMatchesStored", payoutService, StringComparison.Ordinal);
        Assert.Contains("EnsureCompletionEvidenceFingerprint", payoutService, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_EXTERNAL_EVIDENCE_ALREADY_USED", payoutService, StringComparison.Ordinal);
        Assert.Contains("RecordPayoutAsync", financialLedger, StringComparison.Ordinal);
        Assert.Contains("CommerceHubFinancialReferenceType.Payout", financialLedger, StringComparison.Ordinal);
        Assert.Contains("IsolationLevel.Serializable", affiliateSettlement, StringComparison.Ordinal);
        Assert.Contains("AllocateExternalTransferEvidenceAsync", affiliateSettlement, StringComparison.Ordinal);
    }

    [Fact]
    public void SettlementRecoveryCrossSystemAndIsolation_MustNotPromoteDtosEventsOrProjections()
    {
        var reconciliation = ReadRepoFile("PointOfSale.API/Services/CommerceHub/FinancialReconciliationService.cs");
        var commerceHubPayoutController = ReadRepoFile("PointOfSale.API/Controllers/CommerceHubPayoutsController.cs");
        var paymentLifecycle = ReadRepoFile("PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs");
        var applicationDbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        Assert.Contains("reconcile the bank statement", reconciliation, StringComparison.Ordinal);
        Assert.Contains("Hold vendor payout availability", reconciliation, StringComparison.Ordinal);
        Assert.Contains("Completed payout does not reconcile", reconciliation, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_EXECUTION_WORKFLOW_REQUIRED", commerceHubPayoutController, StringComparison.Ordinal);
        Assert.DoesNotContain("MarkCompletedAsync(tenantId", commerceHubPayoutController, StringComparison.Ordinal);
        Assert.Contains("Tenant context is required", paymentLifecycle, StringComparison.Ordinal);
        Assert.Contains("order tenant mismatch", paymentLifecycle, StringComparison.Ordinal);
        Assert.Contains("currency mismatch", paymentLifecycle, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.IsPayoutAuthorized", applicationDbContext, StringComparison.Ordinal);
        Assert.Contains("CommerceHub vendor earning payout reservation amount can only be changed by the CommerceHub payout boundary", applicationDbContext, StringComparison.Ordinal);
    }

    [Fact]
    public void ClearingAgingAndGenericTransfers_MustNotAdvertiseManualSettlement()
    {
        var clearingAging = ReadRepoFile("PointOfSale.Web/src/pages/treasury/TreasuryClearingAging.jsx");
        var transfers = ReadRepoFile("PointOfSale.Web/src/pages/treasury/TreasuryTransfers.jsx");
        var adminAccounting = ReadRepoFile("PointOfSale.API/Controllers/AdminAccountingController.cs");

        Assert.DoesNotContain("navigate('/treasury/transfers'", clearingAging, StringComparison.Ordinal);
        Assert.DoesNotContain("toAccountCode: '1010'", clearingAging, StringComparison.Ordinal);
        Assert.Contains("settlementRequiresEvidence", clearingAging, StringComparison.Ordinal);
        Assert.Contains("disabled", clearingAging, StringComparison.Ordinal);
        var catalog = ReadRepoFile("PointOfSale.Web/src/services/treasuryAccountCatalog.js");
        Assert.Contains("filter(isTransferEligibleAccount)", transfers, StringComparison.Ordinal);
        Assert.Contains("isAvailableCashAccount(account)", catalog, StringComparison.Ordinal);
        Assert.Contains("account?.accountType === TREASURY_ACCOUNT_TYPES.CASHBOX", catalog, StringComparison.Ordinal);
        Assert.Contains("account?.accountType === TREASURY_ACCOUNT_TYPES.BANK", catalog, StringComparison.Ordinal);
        Assert.Contains("SETTLEMENT_AUTHORITY_REQUIRED", adminAccounting, StringComparison.Ordinal);
        Assert.Contains("StatusCodes.Status410Gone", adminAccounting, StringComparison.Ordinal);
    }

    [Fact]
    public void GatewaySettlement_MustSelectAnExplicitClearingAuthorityOrFailClosed()
    {
        var paymentLifecycle = ReadRepoFile("PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs");
        var commerceAccounting = ReadRepoFile("PointOfSale.Infrastructure/Accounting/CommerceAccountingService.cs");

        Assert.Contains("IsSupportedGatewaySettlementProvider", paymentLifecycle, StringComparison.Ordinal);
        Assert.Contains("unsupported_gateway", paymentLifecycle, StringComparison.Ordinal);
        Assert.Contains("\"STRIPE\" => AccountCodes.StripeClearing", commerceAccounting, StringComparison.Ordinal);
        Assert.Contains("\"PAYMOB\" or \"CARD\" or \"VISA\" => AccountCodes.VisaClearing", commerceAccounting, StringComparison.Ordinal);
        Assert.Contains("has no configured clearing-account authority", commerceAccounting, StringComparison.Ordinal);
        Assert.DoesNotContain("_ => AccountCodes.StripeClearing", commerceAccounting, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
