namespace PointOfSale.Tests.Architecture;

public sealed class MarketplaceFinanceRiskCertificationTests
{
    [Fact]
    public void CommerceHubMoneyAuthority_MustRemainSqlLedgerAndSnapshotOwned()
    {
        var workflow = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubFinancialWorkflowService.cs"));
        var ledger = File.ReadAllText(ApiPath("Services", "CommerceHub", "FinancialLedger", "CommerceHubFinancialLedgerService.cs"));
        var transaction = File.ReadAllText(CorePath("Entities", "CommerceHubFinance", "CommerceHubFinancialTransaction.cs"));
        var dbContext = File.ReadAllText(InfrastructurePath("Data", "ApplicationDbContext.cs"));

        Assert.Contains("EnsureFinancialSnapshotIntegrity", workflow, StringComparison.Ordinal);
        Assert.Contains("ValidateCommissionSnapshotJsonMatchesFields", workflow, StringComparison.Ordinal);
        Assert.Contains("COMMERCEHUB_FINANCIAL_SNAPSHOT_MISMATCH", workflow, StringComparison.Ordinal);
        Assert.Contains("COMMERCEHUB_FINANCIAL_ARITHMETIC_MISMATCH", workflow, StringComparison.Ordinal);
        Assert.Contains("EnsureExistingEarningMatchesFinancialSnapshot", workflow, StringComparison.Ordinal);
        Assert.Contains("COMMERCEHUB_EARNING_AMOUNT_LOCKED", workflow, StringComparison.Ordinal);
        AssertContainsAndOrdered(
            workflow,
            "BeginPaymentLifecycle",
            "MarkCodSubOrderCollected",
            "COD delivery financial workflow must open the payment lifecycle boundary before changing payment fields.");

        Assert.Contains("ValidateOrderFinancialIntegrity", ledger, StringComparison.Ordinal);
        Assert.Contains("ValidateRefundFinancialIntegrity", ledger, StringComparison.Ordinal);
        Assert.Contains("ValidatePayoutFinancialIntegrity", ledger, StringComparison.Ordinal);
        Assert.Contains("FinancialCurrencyGuard.RequireEgpCurrency", ledger, StringComparison.Ordinal);
        Assert.Contains("FinancialCurrencyGuard.RequireEgpCurrency", transaction, StringComparison.Ordinal);
        Assert.Contains("CommerceHubFinancialTransaction", dbContext, StringComparison.Ordinal);
        Assert.Contains("records are append-only and cannot be updated", dbContext, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubReplayRecovery_MustNotCreateOrDeleteMoney()
    {
        var ledger = File.ReadAllText(ApiPath("Services", "CommerceHub", "FinancialLedger", "CommerceHubFinancialLedgerService.cs"));
        var payout = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPayoutService.cs"));
        var refund = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubRefundService.cs"));
        var refundExecution = File.ReadAllText(ApiPath("Services", "CommerceHub", "RefundExecutionService.cs"));
        var reconciliation = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubFinanceReconciliationJob.cs"));

        Assert.Contains("AppendTransactionIfMissingAsync", ledger, StringComparison.Ordinal);
        Assert.Contains("EnsureIdempotencyKeyMatchesFinancialFingerprint", ledger, StringComparison.Ordinal);
        Assert.Contains("HasSameFinancialFingerprint", ledger, StringComparison.Ordinal);
        Assert.Contains("EnsureDuplicateLedgerWriteIsSafeAsync", ledger, StringComparison.Ordinal);
        Assert.Contains("DetachAssociatedOutboxMessage", ledger, StringComparison.Ordinal);
        Assert.Contains("IsolationLevel.Serializable", ledger, StringComparison.Ordinal);

        AssertContainsAndOrdered(
            payout,
            "EnsureEarningsHaveLedgerConfirmationAsync",
            "_dbContext.CommerceHubPayouts.Add",
            "Payout creation must prove order-ledger backing before reserving earnings.");
        AssertContainsAndOrdered(
            payout,
            "CapturePayoutFinancialStateForCompletionAsync",
            "ApplyCompletedPayoutAllocation",
            "Payout completion must capture ledger truth before marking earnings paid out.");
        AssertContainsAndOrdered(
            payout,
            "EnsureExternalTransferHandoffMatchesPayout",
            "CapturePayoutFinancialStateForCompletionAsync",
            "Payout completion must be tied to a committed processing handoff before ledger mutation.");
        Assert.Contains("PAYOUT_EXTERNAL_EVIDENCE_ALREADY_USED", payout, StringComparison.Ordinal);

        foreach (var source in new[] { refund, refundExecution })
        {
            AssertContainsAndOrdered(
                source,
                "EnsureRefundCompletionDoesNotOverrunAsync",
                "RecordRefundAsync",
                "Refund completion must validate cumulative totals before ledger reversal.");
            Assert.Contains("EnsureNoProcessingPayoutForRefundEarningsAsync", source, StringComparison.Ordinal);
            Assert.Contains("CommerceHubRefundStatus.ReconciliationRequired", source, StringComparison.Ordinal);
        }

        Assert.Contains("IFinancialReconciliationService", reconciliation, StringComparison.Ordinal);
        Assert.Contains("RunAsync", reconciliation, StringComparison.Ordinal);
        Assert.Contains("RefundFlowMismatchCount", reconciliation, StringComparison.Ordinal);
        Assert.Contains("PayoutFlowMismatchCount", reconciliation, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubFinanceRiskControls_MustFenceOutwardMoneyMovement()
    {
        var payout = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPayoutService.cs"));
        var refund = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubRefundService.cs"));
        var refundExecution = File.ReadAllText(ApiPath("Services", "CommerceHub", "RefundExecutionService.cs"));
        var readiness = File.ReadAllText(ApiPath("Services", "CommerceHub", "Finance", "CommerceHubFinancialReadinessGate.cs"));
        var safety = File.ReadAllText(ApiPath("Services", "CommerceHub", "Finance", "CommerceHubFinancialSafetyGuardService.cs"));

        Assert.Contains("EnsureOutwardMoneyMovementAllowedAsync", payout, StringComparison.Ordinal);
        Assert.Contains("EnsureOutwardMoneyMovementAllowedAsync", refund, StringComparison.Ordinal);
        Assert.Contains("EnsureOutwardMoneyMovementAllowedAsync", refundExecution, StringComparison.Ordinal);
        Assert.Contains("HasBlockingPayoutLockAsync", payout, StringComparison.Ordinal);
        Assert.Contains("EnsureNoProcessingPayoutForRefundEarningsAsync", refund, StringComparison.Ordinal);
        Assert.Contains("EnsureNoProcessingPayoutForRefundEarningsAsync", refundExecution, StringComparison.Ordinal);
        Assert.Contains("CaptureStateForUpdateAsync", refund, StringComparison.Ordinal);
        Assert.Contains("CaptureStateForUpdateAsync", refundExecution, StringComparison.Ordinal);
        Assert.Contains("FinancialCurrencyGuard.RequireEgpCurrency", safety, StringComparison.Ordinal);

        Assert.Contains("critical", readiness, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("blocked", readiness, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ReconciliationRequired", readiness, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubFinanceCertification_DocumentationMustAnswerRequiredProductionQuestions()
    {
        var findings = File.ReadAllText(DocsPath("commercehub-finance-risk-certification-findings.md"));

        foreach (var requiredText in new[]
        {
            "Who owns money authority?",
            "Can money be created through replay?",
            "Can money be destroyed through recovery?",
            "Can payouts exceed earned balances?",
            "Can refunds drift from settlements?",
            "Are commissions trustworthy?",
            "Are balances trustworthy?",
            "Are reconciliation controls sufficient?",
            "Are financial audits complete?",
            "Is CommerceHub finance production-ready?"
        })
        {
            Assert.Contains(requiredText, findings, StringComparison.Ordinal);
        }

        Assert.Contains("Financial authority inventory", findings, StringComparison.Ordinal);
        Assert.Contains("Money flow", findings, StringComparison.Ordinal);
        Assert.Contains("Remaining debt", findings, StringComparison.Ordinal);
    }

    private static void AssertContainsAndOrdered(
        string source,
        string firstMarker,
        string secondMarker,
        string failureMessage)
    {
        var firstIndex = source.IndexOf(firstMarker, StringComparison.Ordinal);
        var secondIndex = source.IndexOf(secondMarker, StringComparison.Ordinal);

        Assert.True(firstIndex >= 0, $"Missing '{firstMarker}'. {failureMessage}");
        Assert.True(secondIndex >= 0, $"Missing '{secondMarker}'. {failureMessage}");
        Assert.True(firstIndex < secondIndex, $"'{firstMarker}' must appear before '{secondMarker}'. {failureMessage}");
    }

    private static string ApiPath(params string[] parts)
    {
        var solutionRoot = DiscoverSolutionRoot(AppContext.BaseDirectory);
        return Path.GetFullPath(Path.Combine(new[] { solutionRoot, "PointOfSale.API" }.Concat(parts).ToArray()));
    }

    private static string CorePath(params string[] parts)
    {
        var solutionRoot = DiscoverSolutionRoot(AppContext.BaseDirectory);
        return Path.GetFullPath(Path.Combine(new[] { solutionRoot, "PointOfSale.Core" }.Concat(parts).ToArray()));
    }

    private static string InfrastructurePath(params string[] parts)
    {
        var solutionRoot = DiscoverSolutionRoot(AppContext.BaseDirectory);
        return Path.GetFullPath(Path.Combine(new[] { solutionRoot, "PointOfSale.Infrastructure" }.Concat(parts).ToArray()));
    }

    private static string DocsPath(params string[] parts)
    {
        var solutionRoot = DiscoverSolutionRoot(AppContext.BaseDirectory);
        return Path.GetFullPath(Path.Combine(new[] { solutionRoot, "docs", "architecture" }.Concat(parts).ToArray()));
    }

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
