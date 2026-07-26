namespace PointOfSale.Tests.Architecture;

public sealed class PaymentImplementation3CanonicalCompletionPipelineTests
{
    [Fact]
    public void PaymentRuntime_MustOwnCanonicalCompletionPipeline()
    {
        var paymentRuntime = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs");
        var financialWorkflow = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubFinancialWorkflowService.cs");
        var inventory = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/InventoryConsistencyService.cs");

        Assert.Contains("PaymentCompletionStarted", paymentRuntime, StringComparison.Ordinal);
        Assert.Contains("CanonicalPaymentCompletionEvidence", paymentRuntime, StringComparison.Ordinal);
        Assert.Contains("FinalizeMarketplaceOrderReservationsForPaymentAsync", paymentRuntime, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPaymentLifecycleAuthority.MarkOnlinePaymentCollected", paymentRuntime, StringComparison.Ordinal);
        Assert.Contains("ApplyOnlinePaymentSuccessAsync", paymentRuntime, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPaymentCompletedEvent", paymentRuntime, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSettlementCreatedEvent", paymentRuntime, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPaymentNotificationsRequestedEvent", paymentRuntime, StringComparison.Ordinal);

        Assert.Contains("RecordOrderAsync", financialWorkflow, StringComparison.Ordinal);
        Assert.Contains("AccountingOutboxMessage", CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/FinancialLedger/CommerceHubFinancialLedgerService.cs"), StringComparison.Ordinal);
        Assert.Contains("ExpiresAtUtc = null", inventory, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderAdapter_MustRemainProtocolOnlyAndNotOwnBusinessCompletion()
    {
        var adapter = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Paymob/PaymobAdapter.cs");

        Assert.Contains("IPaymentProviderAdapter", adapter, StringComparison.Ordinal);
        Assert.Contains("ParseWebhookAsync", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("CommerceHubMarketplaceOrder", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("CommerceHubPaymentService", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("CommerceHubPaymentCompletedEvent", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryReservation", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("CommerceHubVendorSettlement", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("CommerceHubVendorEarning", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("OutboxMessage", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("AccountingOutboxMessage", adapter, StringComparison.Ordinal);
    }
}
