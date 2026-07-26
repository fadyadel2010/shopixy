namespace PointOfSale.Tests.Architecture;

public sealed class PaymentFinancialAuthorityCertificationTests
{
    [Fact]
    public void SuperAdminPayoutFinancials_MustDeriveFromCurrentBackendQueueAuthority()
    {
        var service = ReadRepoFile("PointOfSale.Web/src/super-admin/payouts/payoutsService.ts");
        var page = ReadRepoFile("PointOfSale.Web/src/super-admin/payouts/PayoutsPage.tsx");
        var authority = ReadRepoFile("PointOfSale.Web/src/super-admin/payouts/payoutFinancialAuthority.ts");
        var paymentsPage = ReadRepoFile("PointOfSale.Web/src/super-admin/payments/PaymentsPage.tsx");

        Assert.Contains("requireSuperAdminPayoutQueueFinancialAuthority", service, StringComparison.Ordinal);
        Assert.DoesNotContain("totalPayableAmount: response.data.totalPayableAmount ?? 0", service, StringComparison.Ordinal);
        Assert.Contains("Super Admin payout financial authority rejected malformed queue response evidence", authority, StringComparison.Ordinal);
        Assert.Contains("Super Admin payout financial authority rejected queue summary contradiction", authority, StringComparison.Ordinal);
        Assert.Contains("requireCurrentPayoutExecutionAmount(Number(executeModal.amount), currentExecuteItem)", page, StringComparison.Ordinal);
        Assert.Contains("currentExecuteAmount - currentExecuteItem.eligibleAmount > 0.005", page, StringComparison.Ordinal);
        Assert.Contains("requireSuperAdminPayoutExecutionPresentation(response.data, vendorId, payload.amount)", service, StringComparison.Ordinal);
        Assert.DoesNotContain(".reduce((sum, request) => sum + request.amount, 0)", paymentsPage, StringComparison.Ordinal);
        Assert.Contains("Approved row count only", paymentsPage, StringComparison.Ordinal);
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
