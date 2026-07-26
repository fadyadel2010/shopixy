namespace PointOfSale.Tests.Architecture;

public sealed class PaymentLifecycleAuthorityCertificationTests
{
    [Fact]
    public void StorefrontCanonicalPaymentStatus_MustValidateLifecycleVocabularyAndContradictions()
    {
        var flow = ReadRepoFile("PointOfSale.Storefront/src/features/checkout/paymentFlow.ts");
        var pending = ReadRepoFile("PointOfSale.Storefront/src/pages/CheckoutPendingPage.tsx");
        var statusApi = ReadRepoFile("PointOfSale.Storefront/src/services/api/paymentStatusApi.ts");

        Assert.Contains("CANONICAL_PAYMENT_STATES", flow, StringComparison.Ordinal);
        Assert.Contains("ORDER_PAYMENT_STATUSES", flow, StringComparison.Ordinal);
        Assert.Contains("PAYMENT_INTENT_STATUSES", flow, StringComparison.Ordinal);
        Assert.Contains("contradicted current payment lifecycle evidence", flow, StringComparison.Ordinal);
        Assert.Contains("requireCanonicalPaymentStatusForOrder(orderId, response.data)", statusApi, StringComparison.Ordinal);
        Assert.Contains("resolvePendingCheckoutAction(currentOrderId, response)", pending, StringComparison.Ordinal);
    }

    [Fact]
    public void SuperAdminRefundLifecycle_MustRemainCurrentBackendValidatedAndSelectionDerived()
    {
        var authority = ReadRepoFile("PointOfSale.Web/src/super-admin/refunds/refundLifecycleAuthority.ts");
        var service = ReadRepoFile("PointOfSale.Web/src/super-admin/refunds/refundsService.ts");
        var page = ReadRepoFile("PointOfSale.Web/src/super-admin/refunds/RefundsPage.tsx");

        Assert.Contains("requireSuperAdminRefundsPresentation", authority, StringComparison.Ordinal);
        Assert.Contains("requireSuperAdminRefundDetailsPresentation", authority, StringComparison.Ordinal);
        Assert.Contains("requireCurrentSuperAdminRefundSelection", authority, StringComparison.Ordinal);
        Assert.Contains("completed refund without execution evidence", authority, StringComparison.Ordinal);
        Assert.Contains("requireSuperAdminRefundsPresentation", service, StringComparison.Ordinal);
        Assert.Contains("requireSuperAdminRefundDetailsPresentation(response.data, refundId)", service, StringComparison.Ordinal);
        Assert.DoesNotContain("placeholderData: (previousData) => previousData", page, StringComparison.Ordinal);
        Assert.Contains("currentSelectedRefund = refundsQuery.isFetching || refundsQuery.isError", page, StringComparison.Ordinal);
        Assert.Contains("requireCurrentSuperAdminRefundSelection(selectedRefundId, refunds)", page, StringComparison.Ordinal);
        Assert.Contains("enabled: Boolean(currentSelectedRefundId)", page, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory!.FullName, relativePath));
    }
}
