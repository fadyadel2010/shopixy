namespace PointOfSale.Tests.Architecture;

public sealed class TreasuryReconciliationAuthorityCertificationTests
{
    [Fact]
    public void ReconciliationRunsMismatchLineageAndHistory_MustBeDerivativeAppendOnlySqlEvidence()
    {
        var reconciliation = ReadRepoFile("PointOfSale.API/Services/CommerceHub/FinancialReconciliationService.cs");
        var reportEntity = ReadRepoFile("PointOfSale.Core/Entities/CommerceHubFinance/CommerceHubFinancialReconciliationReport.cs");
        var reportConfig = ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/CommerceHubFinancialReconciliationReportConfiguration.cs");
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        Assert.Contains("ValidateMarketplaceOrders", reconciliation, StringComparison.Ordinal);
        Assert.Contains("ValidateSubOrders", reconciliation, StringComparison.Ordinal);
        Assert.Contains("ValidateRefunds", reconciliation, StringComparison.Ordinal);
        Assert.Contains("ValidateExternalMoneyLifecycle", reconciliation, StringComparison.Ordinal);
        Assert.Contains("ValidateAggregatesAsync", reconciliation, StringComparison.Ordinal);
        Assert.Contains("CommerceHubFinancialReconciliationReport", reconciliation, StringComparison.Ordinal);
        Assert.Contains("DetailsJson", reportEntity, StringComparison.Ordinal);
        Assert.Contains("CK_CommerceHubFinancialReconciliationReports_MismatchCount", reportConfig, StringComparison.Ordinal);
        Assert.Contains("CK_CommerceHubFinancialReconciliationReports_Status", reportConfig, StringComparison.Ordinal);
        Assert.Contains("records are append-only and cannot be deleted", dbContext, StringComparison.Ordinal);
        Assert.Contains("records are append-only and cannot be updated", dbContext, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderAndBankEvidence_MustRemainEvidenceOnlyAndRequireCertifiedSqlAuthorityBeforeMoneyMovement()
    {
        var reconciliation = ReadRepoFile("PointOfSale.API/Services/CommerceHub/FinancialReconciliationService.cs");
        var paymentRecovery = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubFinanceReconciliationJob.cs");
        var payoutRecovery = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPayoutReconciliationHostedService.cs");
        var storefrontRecovery = ReadRepoFile("PointOfSale.API/Services/Payments/StorefrontUnresolvedPaymentReconciliationJob.cs");
        var canonicalPayoutService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPayoutService.cs");

        Assert.Contains("EXTERNAL_PAYMENT_REFERENCE_MISSING", reconciliation, StringComparison.Ordinal);
        Assert.Contains("FAILED_EXTERNAL_PAYMENT_HAS_TRANSACTION", reconciliation, StringComparison.Ordinal);
        Assert.Contains("EXTERNAL_REFUND_CONFIRMATION_MISSING", reconciliation, StringComparison.Ordinal);
        Assert.Contains("EXTERNAL_PAYOUT_REFERENCE_MISSING", reconciliation, StringComparison.Ordinal);
        Assert.Contains("FAILED_PAYOUT_HAS_EXTERNAL_REFERENCE", reconciliation, StringComparison.Ordinal);
        Assert.Contains("reconcile the bank statement", reconciliation, StringComparison.Ordinal);

        Assert.Contains("ResolvePaidPaymentForWebhookAsync", paymentRecovery, StringComparison.Ordinal);
        Assert.Contains("payment.Status == CommerceHubPaymentRecordStatus.Paid", paymentRecovery, StringComparison.Ordinal);
        Assert.Contains("PaymentAmountMatches", paymentRecovery, StringComparison.Ordinal);
        Assert.Contains("LoadMarketplaceOrderForRecoveryAsync", paymentRecovery, StringComparison.Ordinal);
        Assert.Contains("ReconciliationRequired", payoutRecovery, StringComparison.Ordinal);

        Assert.Contains("HandlePaymentResultAsync", storefrontRecovery, StringComparison.Ordinal);
        Assert.Contains("PaymentLifecycleSource.GatewayWebhook", storefrontRecovery, StringComparison.Ordinal);
        Assert.Contains("MISSING_PROVIDER_AUTHORITY", storefrontRecovery, StringComparison.Ordinal);

        Assert.Contains("EnsureTerminalPayoutEvidence", canonicalPayoutService, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_EXTERNAL_EVIDENCE_ALREADY_USED", canonicalPayoutService, StringComparison.Ordinal);
        Assert.Contains("VerifyStateTransitionAsync", canonicalPayoutService, StringComparison.Ordinal);
    }

    [Fact]
    public void RecoveryCompensationAndReplay_MustQuarantineOrReenterCertifiedAuthoritiesWithoutCreatingValue()
    {
        var financeRecovery = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubFinanceReconciliationJob.cs");
        var payoutRecovery = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPayoutReconciliationHostedService.cs");
        var refundRecovery = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubRefundRecoveryHostedService.cs");
        var refundExecution = ReadRepoFile("PointOfSale.API/Services/CommerceHub/RefundExecutionService.cs");
        var accountingOutbox = ReadRepoFile("PointOfSale.Infrastructure/Accounting/AccountingOutboxService.cs");
        var commerceAccounting = ReadRepoFile("PointOfSale.Infrastructure/Accounting/CommerceAccountingService.cs");

        Assert.Contains("IsolationLevel.Serializable", financeRecovery, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", financeRecovery, StringComparison.Ordinal);
        Assert.Contains("QueueSuspenseClearanceAsync", financeRecovery, StringComparison.Ordinal);
        Assert.Contains("CH_RESOLVED_UNRESOLVED_PAYMENT", accountingOutbox, StringComparison.Ordinal);
        Assert.Contains("PostResolvedUnresolvedCommerceHubPaymentAsync", commerceAccounting, StringComparison.Ordinal);
        Assert.Contains("CommerceHubUnresolvedPaymentClearance", commerceAccounting, StringComparison.Ordinal);

        Assert.Contains("IsolationLevel.Serializable", payoutRecovery, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPayoutStatus.ReconciliationRequired", payoutRecovery, StringComparison.Ordinal);
        Assert.Contains("External transfer outcome is unknown", payoutRecovery, StringComparison.Ordinal);
        Assert.Contains("PayoutReconciliationRequired", payoutRecovery, StringComparison.Ordinal);

        Assert.Contains("ReconciliationRequired", refundRecovery, StringComparison.Ordinal);
        Assert.Contains("RecoverReconciliationRequiredRefundsAsync", refundExecution, StringComparison.Ordinal);
        Assert.Contains("manual_transfer_reconciliation_required", refundExecution, StringComparison.Ordinal);
    }

    [Fact]
    public void CrossSystemIsolationAndHistoricalConsistency_MustDeriveFromCurrentSqlScopeOnly()
    {
        var reconciliation = ReadRepoFile("PointOfSale.API/Services/CommerceHub/FinancialReconciliationService.cs");
        var readService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubFinanceReconciliationReadService.cs");
        var financeRecovery = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubFinanceReconciliationJob.cs");
        var storefrontRecovery = ReadRepoFile("PointOfSale.API/Services/Payments/StorefrontUnresolvedPaymentReconciliationJob.cs");

        Assert.Contains("CheckedVendorIds", reconciliation, StringComparison.Ordinal);
        Assert.Contains("AffectedVendorIds", reconciliation, StringComparison.Ordinal);
        Assert.Contains("StoreIdsByMarketplaceOrderId", reconciliation, StringComparison.Ordinal);
        Assert.Contains("payout.TenantId", reconciliation, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSubOrders", readService, StringComparison.Ordinal);
        Assert.Contains("CommerceHubRefunds", readService, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPayouts", readService, StringComparison.Ordinal);
        Assert.Contains("CommerceHubFinancialTransactions", readService, StringComparison.Ordinal);

        Assert.Contains("webhook.TenantId ??= affectedStoreIds.Count == 1", financeRecovery, StringComparison.Ordinal);
        Assert.Contains("BuildSuspenseAllocations", financeRecovery, StringComparison.Ordinal);
        Assert.Contains("TenantId", storefrontRecovery, StringComparison.Ordinal);
        Assert.Contains("LoadPaymentIntentForRecoveryAsync", storefrontRecovery, StringComparison.Ordinal);
        Assert.Contains("LoadOrderForRecoveryAsync", storefrontRecovery, StringComparison.Ordinal);
    }

    [Fact]
    public void ArchitectureTruth_MustDeclareReconciliationDerivativeAndNonMonetary()
    {
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var rules = ReadRepoFile(".ai-context/CODEX_RULES.md");
        var findings = ReadRepoFile("docs/architecture/treasury-reconciliation-authority-certification-findings.md");

        Assert.Contains("PASS T6 Treasury Reconciliation Authority", projectContext, StringComparison.Ordinal);
        Assert.Contains("Treasury reconciliation authority is derivative", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("No inspected T6 runtime blocker remains", knownRisks, StringComparison.Ordinal);
        Assert.Contains("Treasury reconciliation can detect", hardening, StringComparison.Ordinal);
        Assert.Contains("Treasury reconciliation may detect", rules, StringComparison.Ordinal);
        Assert.Contains("Can reconciliation create money? No", findings, StringComparison.Ordinal);
        Assert.Contains("Can recovery create money? No", findings, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
