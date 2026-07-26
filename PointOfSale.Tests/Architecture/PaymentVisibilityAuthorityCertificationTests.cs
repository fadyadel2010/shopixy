namespace PointOfSale.Tests.Architecture;

public sealed class PaymentVisibilityAuthorityCertificationTests
{
    [Fact]
    public void CustomerRefundVisibility_MustBeCurrentBackendValidatedBeforePresentation()
    {
        var authority = ReadRepoFile("PointOfSale.CommerceHub/src/features/customer/paymentVisibilityAuthority.ts");
        var refundsService = ReadRepoFile("PointOfSale.CommerceHub/src/services/customer-refunds-service.ts");
        var orderIdentity = ReadRepoFile("PointOfSale.CommerceHub/src/features/customer/orderIdentityAuthority.ts");
        var refundsPage = ReadRepoFile("PointOfSale.CommerceHub/src/pages/CustomerRefundsPage.tsx");

        Assert.Contains("requireCustomerRefundVisibilityAuthority", authority, StringComparison.Ordinal);
        Assert.Contains("Customer payment visibility authority rejected malformed refund visibility evidence", authority, StringComparison.Ordinal);
        Assert.Contains("requireCustomerExistingRefundVisibilityAuthority", authority, StringComparison.Ordinal);
        Assert.Contains("requireCustomerRefundVisibilityAuthority(requireCustomerRefundFinancialAuthority(refund))", refundsService, StringComparison.Ordinal);
        Assert.Contains("requireCustomerExistingRefundVisibilityAuthority(refund", orderIdentity, StringComparison.Ordinal);
        Assert.Contains("acceptedRefunds = refundsQuery.isFetching || refundsQuery.isError ? []", refundsPage, StringComparison.Ordinal);
        Assert.Contains("acceptedRefunds.find((refund) => refund.refundId === activeRefundId)", refundsPage, StringComparison.Ordinal);
    }

    [Fact]
    public void SuperAdminPaymentVisibility_MustValidateMethodsAndCurrentSelections()
    {
        var authority = ReadRepoFile("PointOfSale.Web/src/super-admin/payments/paymentOwnershipAuthority.ts");
        var service = ReadRepoFile("PointOfSale.Web/src/super-admin/payments/paymentsService.ts");
        var page = ReadRepoFile("PointOfSale.Web/src/super-admin/payments/PaymentsPage.tsx");

        Assert.Contains("requireSuperAdminPaymentMethodPresentation", authority, StringComparison.Ordinal);
        Assert.Contains("return requireSuperAdminPaymentMethodPresentation(response.data)", service, StringComparison.Ordinal);
        Assert.Contains("requests = paymentsQuery.isFetching || paymentsQuery.isError ? []", page, StringComparison.Ordinal);
        Assert.Contains("requireCurrentSuperAdminPaymentSelection(actionModal?.paymentRequestId ?? null, requests)", page, StringComparison.Ordinal);
        Assert.Contains("!paymentsQuery.isFetching && !currentActionRequest", page, StringComparison.Ordinal);
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
