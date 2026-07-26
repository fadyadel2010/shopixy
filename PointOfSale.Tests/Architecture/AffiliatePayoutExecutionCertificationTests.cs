namespace PointOfSale.Tests.Architecture;

using System;
using Xunit;

/// <summary>
/// PASS A4 — Affiliate Payout Execution Authority Certification
/// Regression-freeze tests for payout authorization, execution boundaries, and double-payment prevention.
/// DO NOT DELETE OR RELAX THESE TESTS — they are production financial authority guards.
/// </summary>
public sealed class AffiliatePayoutExecutionCertificationTests
{
    [Fact]
    public void LegacyPaymentMethod_MustBeDeprecatedAndThrow()
    {
        var service = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliatePayoutService.cs");

        // Legacy MarkPayoutPaidAsync must be marked Obsolete and throw NotSupportedException
        Assert.Contains("[Obsolete(\"AF-38H: Use IAffiliateSettlementOrchestrator.ExecuteAffiliatePayoutAsync. This method is no longer supported.\")]", service, StringComparison.Ordinal);
        Assert.Contains("throw new NotSupportedException", service, StringComparison.Ordinal);
        Assert.Contains("MarkPayoutPaidAsync is DEPRECATED", service, StringComparison.Ordinal);
    }

    [Fact]
    public void UnifiedOrchestrator_SingleAuthority()
    {
        var orchestrator = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateSettlementOrchestrator.cs");
        var service = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliatePayoutService.cs");

        // Payout must execute through the orchestrator's core execution method
        Assert.Contains("public async Task<PayoutExecutionResult> ExecuteAffiliatePayoutAsync", orchestrator, StringComparison.Ordinal);
        
        // Orchestrator transactions must use Serializable isolation level
        Assert.Contains("IsolationLevel.Serializable", orchestrator, StringComparison.Ordinal);

        // Advisory locks must be acquired at the service layer using pg_advisory_xact_lock or sp_getapplock
        Assert.Contains("pg_advisory_xact_lock", service, StringComparison.Ordinal);
        Assert.Contains("sp_getapplock", service, StringComparison.Ordinal);
    }

    [Fact]
    public void DoublePaymentResistance_PaymentReferenceUniqueness()
    {
        var orchestrator = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateSettlementOrchestrator.cs");

        // Checks for duplicate reference usage across active payout requests
        Assert.Contains("EnsureLegacyExternalReferenceNotAlreadySpentAsync", orchestrator, StringComparison.Ordinal);
        Assert.Contains("paidRequestAlreadyUsesReference", orchestrator, StringComparison.Ordinal);
        Assert.Contains("balanceLedgerAlreadyUsesReference", orchestrator, StringComparison.Ordinal);
        
        // Assert duplicate reference error messages
        Assert.Contains("AFFILIATE_PAYOUT_EXTERNAL_EVIDENCE_ALREADY_USED", orchestrator, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_PAYOUT_EXTERNAL_EVIDENCE_IDEMPOTENCY_CONFLICT", orchestrator, StringComparison.Ordinal);
    }

    [Fact]
    public void PayoutExecution_MustFailClosedWithoutVerifiedExternalEvidence()
    {
        var options = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateSettlementOptions.cs");
        var orchestrator = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateSettlementOrchestrator.cs");
        var verifier = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliatePayoutEvidenceService.cs");
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AffiliatePayoutEvidenceController.cs");

        Assert.Contains("RequireVerifiedExternalPayoutEvidence", options, StringComparison.Ordinal);
        Assert.Contains("= true", options, StringComparison.Ordinal);
        Assert.Contains("AllowManualPayoutEvidenceOverride", options, StringComparison.Ordinal);
        Assert.Contains("= false", options, StringComparison.Ordinal);
        Assert.Contains("RequireVerifiedManualDeliveryEvidence", options, StringComparison.Ordinal);
        Assert.Contains("AllowManualDeliveryEvidenceOverride", options, StringComparison.Ordinal);
        Assert.Contains("AffiliateSettlementOptionsProductionGuard", options, StringComparison.Ordinal);
        Assert.Contains("IsProductionSafe", options, StringComparison.Ordinal);
        Assert.Contains("EnsureVerifiedExternalPayoutEvidenceAllowed", orchestrator, StringComparison.Ordinal);
        Assert.Contains("TrustedVerificationSource", orchestrator, StringComparison.Ordinal);
        Assert.Contains("VerificationFingerprint.All(Uri.IsHexDigit)", orchestrator, StringComparison.Ordinal);
        Assert.Contains("HMAC_RECONCILIATION", verifier, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_PAYOUT_VERIFIED_EXTERNAL_EVIDENCE_REQUIRED", orchestrator, StringComparison.Ordinal);
        Assert.Contains("Affiliate:Payout:BreakGlass", orchestrator, StringComparison.Ordinal);

        Assert.Contains("HMACSHA256", verifier, StringComparison.Ordinal);
        Assert.Contains("CryptographicOperations.FixedTimeEquals", verifier, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_PAYOUT_EVIDENCE_EXPIRED", verifier, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_PAYOUT_EVIDENCE_REPLAY_CONFLICT", verifier, StringComparison.Ordinal);
        Assert.Contains("_tenantContext.RequiredTenantId", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionStartup_MustRejectAffiliateSettlementBreakGlassFlags()
    {
        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");

        Assert.Contains("builder.Environment.IsProduction()", program, StringComparison.Ordinal);
        Assert.Contains("AffiliateSettlementOptionsProductionGuard.IsProductionSafe", program, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_SETTLEMENT_PRODUCTION_GUARDRAIL_VIOLATION", program, StringComparison.Ordinal);
        Assert.Contains("ValidateOnStart", program, StringComparison.Ordinal);
    }

    [Fact]
    public void Execution_ExactFifoPayoutPlanning()
    {
        var orchestrator = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateSettlementOrchestrator.cs");

        // Payout plan must match commission ledger approved entries in FIFO order
        Assert.Contains("BuildExactPayoutPlan", orchestrator, StringComparison.Ordinal);
        
        // Exact payout plan construction checks for reversals on each order item
        Assert.Contains("reversalAmountsByOrderItem.TryGetValue", orchestrator, StringComparison.Ordinal);
        Assert.Contains("var payableCommissionAmount = approved.CommissionAmount - reversedCommissionAmount;", orchestrator, StringComparison.Ordinal);
        
        // Net payable amount must be positive, and must match requested amount exactly
        Assert.Contains("AFFILIATE_PAYOUT_AMOUNT_NOT_EXACTLY_PAYABLE", orchestrator, StringComparison.Ordinal);
        Assert.Contains("Requested payout amount {requestedAmount:F2} does not match an exact FIFO set", orchestrator, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectedPayoutRequest_MustNotReconcileOtherRequests()
    {
        var orchestrator = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateSettlementOrchestrator.cs");
        var request = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/Affiliate/AffiliatePayoutRequest.cs");

        Assert.Contains("if (!payoutRequestId.HasValue)", orchestrator, StringComparison.Ordinal);
        Assert.Contains("selected withdrawal request owns its own payout lineage", orchestrator, StringComparison.Ordinal);
        Assert.Contains("RemainingAmount = 0m;", request, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_PAYOUT_EXTERNAL_CONFIRMATION_REQUIRED", request, StringComparison.Ordinal);
    }

    [Fact]
    public void PaymentExecutionAuthority_MustRequireApprovedRequestAndBackendExecutorIdentity()
    {
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AdminAffiliateController.cs");
        var orchestrator = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateSettlementOrchestrator.cs");
        var request = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/Affiliate/AffiliatePayoutRequest.cs");
        var frontend = CertificationSource.ReadRepoFile("PointOfSale.Web/src/services/adminAffiliateApi.js");

        Assert.Contains("if (request.Status != PayoutRequestStatus.Approved)", controller, StringComparison.Ordinal);
        Assert.Contains("TryGetCurrentAdminUserId(out var adminUserId)", controller, StringComparison.Ordinal);
        Assert.Contains("paidByUserId: adminUserId", controller, StringComparison.Ordinal);
        Assert.Contains("paidBy = adminUserId", controller, StringComparison.Ordinal);

        Assert.Contains("EnsurePayoutRequestDestinationAuthorityAsync", orchestrator, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_PAYOUT_EXECUTOR_IDENTITY_REQUIRED", orchestrator, StringComparison.Ordinal);
        Assert.Contains("payoutRequest.MarkPaid(paidByUserId!.Value, paymentReference, adminNotes)", orchestrator, StringComparison.Ordinal);

        Assert.Contains("public Guid? PaidBy { get; private set; }", request, StringComparison.Ordinal);
        Assert.Contains("public void MarkPaid(Guid paidBy", request, StringComparison.Ordinal);
        Assert.Contains("PaidBy = paidBy;", request, StringComparison.Ordinal);

        Assert.Contains("markPayoutPaid: (requestId, paymentReference = '', adminNotes = '')", frontend, StringComparison.Ordinal);
        Assert.Contains("{ paymentReference, adminNotes }", frontend, StringComparison.Ordinal);
    }
}
