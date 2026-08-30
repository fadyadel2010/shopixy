namespace PointOfSale.Tests.Architecture;

/// <summary>
/// O-4.6 regression guards for historical payout authority and partial-refund lifecycle behavior.
/// </summary>
public sealed class AffiliateCommissionO46RemediationTests
{
    [Fact]
    public void Payout_MustUseCheckoutTimeCapSnapshot_NotCurrentProductPolicy()
    {
        var orchestrator = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/Services/Affiliate/AffiliateSettlementOrchestrator.cs");

        Assert.Contains("AffiliateMaxCommissionPerItemSnapshot", orchestrator, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "join policy in _db.AffiliateProductPolicies",
            orchestrator,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PartialRefund_MustRemainApprovableAndPayableForItsNetRemainder()
    {
        var service = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs");
        var admin = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Controllers/AdminAffiliateController.cs");

        Assert.Contains("IsFullyReversed(earned, reversalTotalsByOriginalEntryId)", service, StringComparison.Ordinal);
        Assert.Contains("e.CommissionAmount - reversalAmountsByOrderItem", service, StringComparison.Ordinal);
        Assert.Contains("approved > reversed && !hasPaid", admin, StringComparison.Ordinal);
        Assert.Contains("CommissionEntryType.Approved", admin, StringComparison.Ordinal);
    }

    [Fact]
    public void PartialRefund_ReadContracts_MustExposeGrossReversedAndNetAmounts()
    {
        var admin = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Controllers/AdminAffiliateController.cs");
        var portal = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Controllers/AffiliatePortalController.cs");

        foreach (var source in new[] { admin, portal })
        {
            Assert.Contains("ReversedAmount", source, StringComparison.Ordinal);
            Assert.Contains("NetCommissionAmount", source, StringComparison.Ordinal);
            Assert.Contains("IsPartiallyReversed", source, StringComparison.Ordinal);
            Assert.Contains("PartiallyReversed", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Approval_MustUseRecognizedPaymentWhitelistAndRejectTerminalOrderAuthority()
    {
        var service = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs");

        Assert.Contains("!HasPendingCommissionPaymentAuthority(order.PaymentStatus)", service, StringComparison.Ordinal);
        Assert.Contains("OrderStatus.Cancelled or OrderStatus.Refunded", service, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_DELIVERY_ORDER_AUTHORITY_NOT_ELIGIBLE", service, StringComparison.Ordinal);
    }

    [Fact]
    public void FinancialMutationErrors_MustUseStableSafeCodesWithoutRawExceptionDetails()
    {
        var contract = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Controllers/AffiliateFinancialErrorContract.cs");
        var admin = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Controllers/AdminAffiliateController.cs");

        Assert.Contains("AFFILIATE_CONCURRENCY_CONFLICT", contract, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_VALIDATION_FAILED", contract, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_FINANCIAL_CONFLICT", contract, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_PAYOUT_AUTHORITY_CONFLICT", contract, StringComparison.Ordinal);
        Assert.Contains("AffiliateFinancialErrorContract.From(ex)", admin, StringComparison.Ordinal);
        Assert.DoesNotContain("details = ex.Message", admin, StringComparison.Ordinal);
        Assert.DoesNotContain("Errors = new List<string> { ex.Message }", admin, StringComparison.Ordinal);
    }
}
