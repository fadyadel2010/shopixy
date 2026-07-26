namespace PointOfSale.Tests.Architecture;

public sealed class ReturnFinancialAuthorityCertificationTests
{
    [Fact]
    public void PosReturnCreatePayloads_MustNotSubmitFrontendMoneyAsAuthority()
    {
        var salesReturns = CertificationSource.ReadRepoFile("PointOfSale.Web/src/pages/SalesReturns.jsx");
        var purchaseReturns = CertificationSource.ReadRepoFile("PointOfSale.Web/src/pages/PurchaseReturns.jsx");

        Assert.Contains("api.post('/salesreturns', payload", salesReturns, StringComparison.Ordinal);
        Assert.Contains("api.post('/purchasereturns', payload", purchaseReturns, StringComparison.Ordinal);

        Assert.DoesNotContain("unitPrice: roundCurrency(item.originalUnitPrice)", salesReturns, StringComparison.Ordinal);
        Assert.DoesNotContain("taxRate: roundCurrency(item.originalTaxRate)", salesReturns, StringComparison.Ordinal);
        Assert.DoesNotContain("cashAmount:", salesReturns, StringComparison.Ordinal);
        Assert.DoesNotContain("cardAmount:", salesReturns, StringComparison.Ordinal);
        Assert.DoesNotContain("cashRefundAmount", salesReturns, StringComparison.Ordinal);
        Assert.DoesNotContain("cardRefundAmount", salesReturns, StringComparison.Ordinal);
        Assert.DoesNotContain("refundMixed", salesReturns, StringComparison.Ordinal);

        Assert.DoesNotContain("unitPrice: i.unitPrice", purchaseReturns, StringComparison.Ordinal);
        Assert.DoesNotContain("taxRate: i.taxRate", purchaseReturns, StringComparison.Ordinal);
        Assert.DoesNotContain("refundToCash:", purchaseReturns, StringComparison.Ordinal);
        Assert.DoesNotContain("refundAmount:", purchaseReturns, StringComparison.Ordinal);
        Assert.DoesNotContain("setRefundAmount", purchaseReturns, StringComparison.Ordinal);
    }

    [Fact]
    public void PosReturnBackend_MustDeriveFinancialAuthorityFromCurrentOriginalInvoice()
    {
        var salesReturnsController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/SalesReturnsController.cs");
        var purchaseReturnsController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/PurchaseReturnsController.cs");

        Assert.Contains("LoadOriginalInvoiceForReturnMutationAsync(request.OriginalInvoiceId.Value)", salesReturnsController, StringComparison.Ordinal);
        Assert.Contains("RoundCurrency(snapshot.UnitPrice * itemRequest.Quantity)", salesReturnsController, StringComparison.Ordinal);
        Assert.Contains("UnitPrice = line.Snapshot.UnitPrice", salesReturnsController, StringComparison.Ordinal);
        Assert.Contains("TaxRate = line.Snapshot.TaxRate", salesReturnsController, StringComparison.Ordinal);
        Assert.Contains("ResolveRefundAllocation(request, salesReturn.TotalAmount)", salesReturnsController, StringComparison.Ordinal);
        Assert.Contains("cashAmount = authoritativeReturnTotal", salesReturnsController, StringComparison.Ordinal);
        Assert.Contains("cardAmount = authoritativeReturnTotal", salesReturnsController, StringComparison.Ordinal);

        Assert.Contains("LoadOriginalPurchaseInvoiceForReturnMutationAsync(request.OriginalInvoiceId.Value)", purchaseReturnsController, StringComparison.Ordinal);
        Assert.Contains("authoritativeUnitPrice", purchaseReturnsController, StringComparison.Ordinal);
        Assert.Contains("authoritativeTaxRate", purchaseReturnsController, StringComparison.Ordinal);
        Assert.Contains("UnitPrice = line.UnitPrice", purchaseReturnsController, StringComparison.Ordinal);
        Assert.Contains("TaxRate = line.TaxRate", purchaseReturnsController, StringComparison.Ordinal);
        Assert.Contains("RefundAmount = purchaseReturn.TotalAmount", purchaseReturnsController, StringComparison.Ordinal);
    }

    [Fact]
    public void ReturnReplayFingerprints_MustExcludeIgnoredClientMoney()
    {
        var salesReturnsController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/SalesReturnsController.cs");
        var purchaseReturnsController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/PurchaseReturnsController.cs");

        var salesFingerprint = ExtractMethod(
            salesReturnsController,
            "BuildSalesReturnRequestFingerprint");
        var purchaseFingerprint = ExtractMethod(
            purchaseReturnsController,
            "BuildPurchaseReturnRequestFingerprint");

        Assert.DoesNotContain("item.UnitPrice", salesFingerprint, StringComparison.Ordinal);
        Assert.DoesNotContain("item.TaxRate", salesFingerprint, StringComparison.Ordinal);
        Assert.DoesNotContain("request.CashAmount", salesFingerprint, StringComparison.Ordinal);
        Assert.DoesNotContain("request.CardAmount", salesFingerprint, StringComparison.Ordinal);

        Assert.DoesNotContain("item.UnitPrice", purchaseFingerprint, StringComparison.Ordinal);
        Assert.DoesNotContain("item.TaxRate", purchaseFingerprint, StringComparison.Ordinal);
        Assert.DoesNotContain("request.RefundAmount", purchaseFingerprint, StringComparison.Ordinal);
        Assert.DoesNotContain("request.RefundToCash", purchaseFingerprint, StringComparison.Ordinal);
        Assert.DoesNotContain("DeterministicRequestHasher.ComputeHash(request)", purchaseReturnsController, StringComparison.Ordinal);
    }

    [Fact]
    public void SuperAdminReturnFinancialPresentation_MustRemainRefundBoundEvidence()
    {
        var authority = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/refunds/refundLifecycleAuthority.ts");
        var service = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/refunds/refundsService.ts");
        var page = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/refunds/RefundsPage.tsx");

        Assert.Contains("requireRefundFinancialPresentation(refund)", authority, StringComparison.Ordinal);
        Assert.Contains("!money(preview.commissionReversalAmount)", authority, StringComparison.Ordinal);
        Assert.Contains("!money(preview.vendorEarningChange)", authority, StringComparison.Ordinal);
        Assert.Contains("!signedMoney(preview.platformCashDelta)", authority, StringComparison.Ordinal);
        Assert.Contains("!guid(execution.financialRefundId)", authority, StringComparison.Ordinal);
        Assert.Contains("!money(execution.commissionReversalAmount)", authority, StringComparison.Ordinal);
        Assert.Contains("!money(execution.vendorEarningReversalAmount)", authority, StringComparison.Ordinal);
        Assert.Contains("returnRequest.refundId.toLowerCase() !== currentRefund.refundId.toLowerCase()", authority, StringComparison.Ordinal);
        Assert.Contains("getSuperAdminRefundDetails(currentSelectedRefundId as string, currentSelectedRefund ?? undefined)", page, StringComparison.Ordinal);
        Assert.Contains("executeSuperAdminRefund(", service, StringComparison.Ordinal);
    }

    private static string ExtractMethod(string source, string methodName)
    {
        var start = source.IndexOf(methodName, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find method marker {methodName}.");

        var braceStart = source.IndexOf('{', start);
        Assert.True(braceStart >= 0, $"Could not find method body for {methodName}.");

        var depth = 0;
        for (var index = braceStart; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source.Substring(start, index - start + 1);
                }
            }
        }

        throw new InvalidOperationException($"Could not extract method body for {methodName}.");
    }
}
