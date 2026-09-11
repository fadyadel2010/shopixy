namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubPayoutCertificationTests
{
    [Fact]
    public void MerchantPayoutLifecycle_MustStayLedgerConfirmedExternallyFencedAndReconciliationSafe()
    {
        var payout = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPayoutService.cs");
        var payoutEntity = ReadRepoFile("PointOfSale.Core/Entities/CommerceHubPayout.cs");
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        Assert.Contains("CommerceHubBoundaryMutationScope.BeginPayout()", payout, StringComparison.Ordinal);
        Assert.Contains("BeginTransactionAsync(IsolationLevel.Serializable", payout, StringComparison.Ordinal);
        Assert.Contains("FindPayoutByIdempotencyAsync", payout, StringComparison.Ordinal);
        Assert.Contains("LockVendorEarningsForPayoutCreationAsync", payout, StringComparison.Ordinal);
        Assert.Contains("EnsureEarningsHaveLedgerConfirmationAsync", payout, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_LEDGER_CONFIRMATION_REQUIRED", payout, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_AMOUNT_INVALID", payout, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_CURRENCY_MISMATCH", payout, StringComparison.Ordinal);
        Assert.Contains("negativeAdjustments", payout, StringComparison.Ordinal);
        Assert.Contains("PayoutReservedAmount", payout, StringComparison.Ordinal);

        Assert.Contains("EnsureOutwardMoneyMovementAllowedAsync", payout, StringComparison.Ordinal);
        Assert.Contains("HasBlockingPayoutLockAsync", payout, StringComparison.Ordinal);
        Assert.Contains("CaptureAndValidatePayoutFinancialReadinessAsync", payout, StringComparison.Ordinal);
        Assert.Contains("ExternalTransferIdempotencyKey", payout, StringComparison.Ordinal);
        Assert.Contains("ExternalTransferHandoffToken", payout, StringComparison.Ordinal);
        Assert.Contains("ExternalTransferHandoffCommittedAtUtc", payout, StringComparison.Ordinal);
        Assert.Contains("EnsureExternalTransferIntentMatchesPayout", payout, StringComparison.Ordinal);
        Assert.Contains("EnsureExternalTransferHandoffMatchesPayout", payout, StringComparison.Ordinal);
        Assert.Contains("EnsureRequestedPayoutEvidenceMatchesStored", payout, StringComparison.Ordinal);
        Assert.Contains("EnsureExternalPayoutEvidenceIsUniqueAsync", payout, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_EXTERNAL_EVIDENCE_ALREADY_USED", payout, StringComparison.Ordinal);
        Assert.Contains("EnsureTerminalPayoutEvidence", payout, StringComparison.Ordinal);
        Assert.Contains("EnsureCompletionEvidenceFingerprint", payout, StringComparison.Ordinal);
        Assert.Contains("RecordPayoutAsync", payout, StringComparison.Ordinal);
        Assert.Contains("ApplyCompletedPayoutAllocation", payout, StringComparison.Ordinal);
        Assert.Contains("confirmed as not executed", payout, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPayoutStatus.ReconciliationRequired", payout, StringComparison.Ordinal);

        Assert.Contains("CompletionEvidenceFingerprint", payoutEntity, StringComparison.Ordinal);
        Assert.Contains("ExternalTransferHandoffCommittedAtUtc", payoutEntity, StringComparison.Ordinal);
        Assert.Contains("UX_CommerceHubPayouts_OneProcessingPerTenant", dbContext, StringComparison.Ordinal);
        Assert.Contains("UX_CommerceHubPayouts_Live_ExternalReferenceId", dbContext, StringComparison.Ordinal);
        Assert.Contains("UX_CommerceHubPayouts_Live_BankTransactionId", dbContext, StringComparison.Ordinal);
        Assert.Contains("UX_CommerceHubPayouts_ExternalTransferIdempotencyKey", dbContext, StringComparison.Ordinal);
        Assert.Contains("CK_CommerceHubPayouts_Completed_Evidence", dbContext, StringComparison.Ordinal);
        Assert.Contains("CK_CommerceHubPayouts_Processing_HandoffCommitted", dbContext, StringComparison.Ordinal);
    }

    [Fact]
    public void SuperAdminPayoutExecution_MustBindVendorMethodExternalEvidenceActorAndLedgerState()
    {
        var service = ReadRepoFile("PointOfSale.API/SuperAdmin/Services/SuperAdminPayoutsService.cs");
        var orchestration = ReadRepoFile("PointOfSale.API/SuperAdmin/Services/SuperAdminPayoutOrchestrationService.cs");
        var payoutService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPayoutService.cs");
        var controller = ReadRepoFile("PointOfSale.API/SuperAdmin/Controllers/SuperAdminPayoutsController.cs");
        var frontend = ReadRepoFile("PointOfSale.Web/src/super-admin/payouts/PayoutsPage.tsx");
        var tests = ReadRepoFile("PointOfSale.Tests/CommerceHub/SuperAdminPayoutOrchestrationServiceTests.cs");

        Assert.Contains("SuperAdminBaseController", controller, StringComparison.Ordinal);
        Assert.Contains("CurrentSuperAdminId", controller, StringComparison.Ordinal);
        Assert.Contains("VENDOR_PAYOUT_HANDOFF_COMMITTED", controller, StringComparison.Ordinal);
        Assert.Contains("VENDOR_PAYOUT_COMPLETED", controller, StringComparison.Ordinal);
        Assert.Contains("VENDOR_PAYOUT_QUEUE_VIEWED", controller, StringComparison.Ordinal);

        Assert.Contains("NormalizeActorId", orchestration, StringComparison.Ordinal);
        Assert.Contains("SUPER_ADMIN_ACTOR_REQUIRED", orchestration, StringComparison.Ordinal);
        Assert.Contains("method.Id == request.PayoutMethodId", orchestration, StringComparison.Ordinal);
        Assert.Contains("method.VendorId == vendorId", orchestration, StringComparison.Ordinal);
        Assert.Contains("_canonicalPayoutService.CreatePayoutAsync", orchestration, StringComparison.Ordinal);
        Assert.Contains("_canonicalPayoutService.MarkProcessingAsync", orchestration, StringComparison.Ordinal);
        Assert.Contains("_canonicalPayoutService.MarkCompletedAsync", orchestration, StringComparison.Ordinal);
        Assert.Contains("EnsureTerminalPayoutEvidence", payoutService, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_AMOUNT_EXCEEDS_PAYABLE", payoutService, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_PLATFORM_CASH_INSUFFICIENT", payoutService, StringComparison.Ordinal);
        Assert.Contains("CaptureStateForUpdateAsync", payoutService, StringComparison.Ordinal);
        Assert.Contains("VerifyStateTransitionAsync", payoutService, StringComparison.Ordinal);
        Assert.Contains("EnsureExternalPayoutEvidenceIsUniqueAsync", payoutService, StringComparison.Ordinal);
        Assert.Contains("BuildCompletionEvidenceFingerprint", payoutService, StringComparison.Ordinal);
        Assert.Contains("RecordPayoutAsync", payoutService, StringComparison.Ordinal);
        Assert.Contains("AppendCommittedOperationAudit", payoutService, StringComparison.Ordinal);
        Assert.Contains("CanPreparePayout", service, StringComparison.Ordinal);
        Assert.Contains("CanCompletePayout", service, StringComparison.Ordinal);
        Assert.Contains("ActivePayout", service, StringComparison.Ordinal);
        Assert.Contains("ExecutionBlockedCode", service, StringComparison.Ordinal);

        Assert.Contains("Commit this payout before sending money", frontend, StringComparison.Ordinal);
        Assert.Contains("Record completion only after verifying the external transfer executed", frontend, StringComparison.Ordinal);

        Assert.Contains("PrepareAsync_CommitsProcessingHandoffWithoutCompletingPayout", tests, StringComparison.Ordinal);
        Assert.Contains("CompleteAsync_ReconcilesCommittedHandoffWithoutCreatingOrProcessingPayout", tests, StringComparison.Ordinal);
        Assert.Contains("CompleteAsync_WithoutExplicitExecutionConfirmation_DoesNotCallCanonicalCompletion", tests, StringComparison.Ordinal);
    }

    [Fact]
    public void PayoutHttpMutation_MustRequireSuperAdminExecutionWorkflow()
    {
        var directController = ReadRepoFile("PointOfSale.API/Controllers/CommerceHubPayoutsController.cs");
        var superAdminController = ReadRepoFile("PointOfSale.API/SuperAdmin/Controllers/SuperAdminPayoutsController.cs");
        var orchestration = ReadRepoFile("PointOfSale.API/SuperAdmin/Services/SuperAdminPayoutOrchestrationService.cs");
        var payoutService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPayoutService.cs");

        Assert.Contains("PAYOUT_EXECUTION_WORKFLOW_REQUIRED", directController, StringComparison.Ordinal);
        Assert.Contains("Direct CommerceHub payout execution mutation is disabled", directController, StringComparison.Ordinal);
        Assert.Contains("[Authorize(Roles = \"Owner,Admin\")]", directController, StringComparison.Ordinal);
        Assert.DoesNotContain("_payoutService.MarkProcessingAsync(", directController, StringComparison.Ordinal);
        Assert.DoesNotContain("_payoutService.MarkCompletedAsync(", directController, StringComparison.Ordinal);
        Assert.DoesNotContain("_payoutService.MarkFailedAsync(", directController, StringComparison.Ordinal);

        Assert.Contains("api/super-admin/payouts", superAdminController, StringComparison.Ordinal);
        Assert.Contains("_payoutOrchestrationService.PrepareAsync", superAdminController, StringComparison.Ordinal);
        Assert.Contains("_payoutOrchestrationService.CompleteAsync", superAdminController, StringComparison.Ordinal);
        Assert.Contains("NormalizeActorId", orchestration, StringComparison.Ordinal);
        Assert.Contains("_canonicalPayoutService.MarkCompletedAsync", orchestration, StringComparison.Ordinal);
        Assert.Contains("EnsureTerminalPayoutEvidence", payoutService, StringComparison.Ordinal);
        Assert.Contains("EnsureOutwardMoneyMovementAllowedAsync", payoutService, StringComparison.Ordinal);
        Assert.Contains("RecordPayoutAsync", payoutService, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
