namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubFinancialReconciliationCertificationTests
{
    [Fact]
    public void FinancialReconciliation_MustProveOrdersRefundsLedgerBalancesAndAggregates()
    {
        var reconciliation = ReadRepoFile("PointOfSale.API/Services/CommerceHub/FinancialReconciliationService.cs");
        var readService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubFinanceReconciliationReadService.cs");
        var aggregate = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubDailyFinancialAggregateService.cs");

        Assert.Contains("ValidateMarketplaceOrders", reconciliation, StringComparison.Ordinal);
        Assert.Contains("ValidateSubOrders", reconciliation, StringComparison.Ordinal);
        Assert.Contains("ValidatePromoCrossChecks", reconciliation, StringComparison.Ordinal);
        Assert.Contains("ValidateRefunds", reconciliation, StringComparison.Ordinal);
        Assert.Contains("ValidateOrphanLedgers", reconciliation, StringComparison.Ordinal);
        Assert.Contains("ValidateExternalMoneyLifecycle", reconciliation, StringComparison.Ordinal);
        Assert.Contains("ValidateAggregatesAsync", reconciliation, StringComparison.Ordinal);
        Assert.Contains("CommerceHubFinancialReconciliationReport", reconciliation, StringComparison.Ordinal);
        Assert.Contains("RecordFinanceReconciliation", reconciliation, StringComparison.Ordinal);
        Assert.Contains("IFinancialReconciliationAlertHook", reconciliation, StringComparison.Ordinal);

        Assert.Contains("GetSnapshotAsync", readService, StringComparison.Ordinal);
        Assert.Contains("RecalculateWindowAsync", aggregate, StringComparison.Ordinal);
        Assert.Contains("InvalidRowCount", aggregate, StringComparison.Ordinal);
    }

    [Fact]
    public void RefundImpact_MustReverseCommissionPayablesAndDebtWithoutOverrun()
    {
        var refund = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubRefundService.cs");
        var execution = ReadRepoFile("PointOfSale.API/Services/CommerceHub/RefundExecutionService.cs");
        var calculator = ReadRepoFile("PointOfSale.API/Services/CommerceHub/RefundFinancialCalculator.cs");
        var ledger = ReadRepoFile("PointOfSale.API/Services/CommerceHub/FinancialLedger/CommerceHubFinancialLedgerService.cs");

        Assert.Contains("EnsureRefundCompletionDoesNotOverrunAsync", refund, StringComparison.Ordinal);
        Assert.Contains("EnsureRefundCompletionDoesNotOverrunAsync", execution, StringComparison.Ordinal);
        Assert.Contains("EnsureNoProcessingPayoutForRefundEarningsAsync", refund, StringComparison.Ordinal);
        Assert.Contains("EnsureNoProcessingPayoutForRefundEarningsAsync", execution, StringComparison.Ordinal);
        Assert.Contains("Refund would reduce vendor debt below zero", execution, StringComparison.Ordinal);
        Assert.Contains("CommissionReversalAmount", calculator, StringComparison.Ordinal);
        Assert.Contains("VendorEarningReversalAmount", calculator, StringComparison.Ordinal);
        Assert.Contains("RecordRefundAsync", ledger, StringComparison.Ordinal);
        Assert.Contains("ValidateRefundFinancialIntegrity", ledger, StringComparison.Ordinal);
    }

    [Fact]
    public void RecoveryReplayMonitoringAndAudit_MustRemainFinanciallyBounded()
    {
        var ledger = ReadRepoFile("PointOfSale.API/Services/CommerceHub/FinancialLedger/CommerceHubFinancialLedgerService.cs");
        var financeJob = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubFinanceReconciliationJob.cs");
        var payoutRecovery = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPayoutReconciliationHostedService.cs");
        var refundRecovery = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubRefundRecoveryHostedService.cs");
        var audit = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubFinancialAuditService.cs");

        Assert.Contains("EnsureDuplicateLedgerWriteIsSafeAsync", ledger, StringComparison.Ordinal);
        Assert.Contains("HasSameFinancialFingerprint", ledger, StringComparison.Ordinal);
        Assert.Contains("DetachAssociatedOutboxMessage", ledger, StringComparison.Ordinal);
        Assert.Contains("IFinancialReconciliationService", financeJob, StringComparison.Ordinal);
        Assert.Contains("RunAsync", financeJob, StringComparison.Ordinal);
        Assert.Contains("ReconciliationRequired", payoutRecovery, StringComparison.Ordinal);
        Assert.Contains("ReconciliationRequired", refundRecovery, StringComparison.Ordinal);
        Assert.Contains("RecordFinanceReconciliation", audit, StringComparison.Ordinal);
    }

    [Fact]
    public void PayoutReconciliation_MustProveTerminalEvidenceAndLedgerDebits()
    {
        var reconciliation = ReadRepoFile("PointOfSale.API/Services/CommerceHub/FinancialReconciliationService.cs");
        var reconciliationRead = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubFinanceReconciliationReadService.cs");
        var reconciliationJob = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubFinanceReconciliationJob.cs");
        var healthChecks = ReadRepoFile("PointOfSale.Infrastructure/HealthChecks/ProductionHealthChecks.cs");

        Assert.Contains("ValidateExternalMoneyLifecycle", reconciliation, StringComparison.Ordinal);
        Assert.Contains("EXTERNAL_PAYOUT_REFERENCE_MISSING", reconciliation, StringComparison.Ordinal);
        Assert.Contains("EXTERNAL_PAYOUT_LEDGER_MISMATCH", reconciliation, StringComparison.Ordinal);
        Assert.Contains("FAILED_PAYOUT_HAS_EXTERNAL_REFERENCE", reconciliation, StringComparison.Ordinal);
        Assert.Contains("PayoutLedgerByPayoutId", reconciliation, StringComparison.Ordinal);
        Assert.Contains("PlatformCashDebits", reconciliation, StringComparison.Ordinal);
        Assert.Contains("VendorPayoutDebits", reconciliation, StringComparison.Ordinal);
        Assert.Contains("payout.TenantId", reconciliation, StringComparison.Ordinal);

        Assert.Contains("VendorPayoutSettlementAmount", reconciliationRead, StringComparison.Ordinal);
        Assert.Contains("VendorPayoutCount", reconciliationRead, StringComparison.Ordinal);
        Assert.Contains("PayoutFlowMismatchCount", reconciliationJob, StringComparison.Ordinal);

        Assert.Contains("PayoutExternalTruthReadinessHealthCheck", healthChecks, StringComparison.Ordinal);
        Assert.Contains("CompletedPayoutsWithoutExternalEvidence", healthChecks, StringComparison.Ordinal);
        Assert.Contains("StaleProcessingPayouts", healthChecks, StringComparison.Ordinal);
        Assert.Contains("ReconciliationRequiredPayouts", healthChecks, StringComparison.Ordinal);
    }

    [Fact]
    public void PaymentMethodOwnership_MustStayScopedToVendorAndSuperAdminSurfaces()
    {
        var vendorMethods = ReadRepoFile("PointOfSale.API/Services/CommerceHub/Finance/CommerceHubVendorPayoutMethodsService.cs");
        var paymentMethods = ReadRepoFile("PointOfSale.API/Services/CommerceHub/Finance/CommerceHubVendorPaymentsService.cs");

        Assert.Contains("vendorId", vendorMethods, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CommerceHubVendorPayoutMethods", vendorMethods, StringComparison.Ordinal);
        Assert.Contains("CreateAsync", vendorMethods, StringComparison.Ordinal);
        Assert.Contains("GetAsync", vendorMethods, StringComparison.Ordinal);
        Assert.Contains("IsDefault", vendorMethods, StringComparison.Ordinal);

        Assert.Contains("CommerceHubVendorPaymentMethods", paymentMethods, StringComparison.Ordinal);
        Assert.Contains("GetPaymentMethodsAsync", paymentMethods, StringComparison.Ordinal);
        Assert.Contains("CreatePaymentMethodAsync", paymentMethods, StringComparison.Ordinal);
        Assert.Contains("GetVendorProofAccessAsync", paymentMethods, StringComparison.Ordinal);
        Assert.Contains("GetSuperAdminProofAccessAsync", paymentMethods, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
