namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubPayoutExecutionCurrencyAuthorityCertificationTests
{
    [Fact]
    public void PayoutExecutionCurrency_MustRemainCommerceHubOwnedAndEgpOnly()
    {
        var superAdminPayouts = ReadRepoFile("PointOfSale.API/SuperAdmin/Services/SuperAdminPayoutsService.cs");
        var payoutOrchestration = ReadRepoFile("PointOfSale.API/SuperAdmin/Services/SuperAdminPayoutOrchestrationService.cs");
        var payoutService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPayoutService.cs");
        var financeRead = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubFinanceReadService.cs");
        var reconciliationRead = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubFinanceReconciliationReadService.cs");
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");
        var frontendPayoutAuthority = ReadRepoFile("PointOfSale.Web/src/super-admin/payouts/payoutFinancialAuthority.ts");

        Assert.Contains("FinancialCurrencyGuard.RequireEgpCurrency", superAdminPayouts, StringComparison.Ordinal);
        Assert.Contains("FinancialCurrencyGuard.PlatformBaseCurrencyCode", payoutOrchestration, StringComparison.Ordinal);
        Assert.Contains("earning.SubOrder.Currency == platformCurrency", payoutOrchestration, StringComparison.Ordinal);
        Assert.Contains("RequireEgpCurrency(", payoutService, StringComparison.Ordinal);
        Assert.Contains("commercehub.payout.financial_readiness.currency", payoutService, StringComparison.Ordinal);
        Assert.Contains("commercehub.payout.completion.currency", payoutService, StringComparison.Ordinal);
        Assert.Contains("commercehub.payout.dto.currency", payoutService, StringComparison.Ordinal);
        Assert.Contains("commercehub.admin_finance.payout.currency", financeRead, StringComparison.Ordinal);
        Assert.Contains("payout.Currency == FinancialCurrencyGuard.PlatformBaseCurrencyCode", reconciliationRead, StringComparison.Ordinal);
        Assert.Contains("CK_CommerceHubPayouts_Currency_EGP", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardCommerceHubCurrencySovereignty", dbContext, StringComparison.Ordinal);
        Assert.Contains("authorityCurrency(payout.currency)", frontendPayoutAuthority, StringComparison.Ordinal);
    }

    [Fact]
    public void PayoutExecutionCurrency_MustNotInheritTenantVendorProviderDtoReplayOrCacheCurrency()
    {
        var superAdminPayouts = ReadRepoFile("PointOfSale.API/SuperAdmin/Services/SuperAdminPayoutsService.cs");
        var payoutDtos = ReadRepoFile("PointOfSale.API/SuperAdmin/DTOs/SuperAdminPayoutDtos.cs");
        var payoutsService = ReadRepoFile("PointOfSale.Web/src/super-admin/payouts/payoutsService.ts");
        var frontendPayoutAuthority = ReadRepoFile("PointOfSale.Web/src/super-admin/payouts/payoutFinancialAuthority.ts");

        Assert.DoesNotContain("request.Currency", superAdminPayouts, StringComparison.Ordinal);
        Assert.DoesNotContain("vendor authority currency", superAdminPayouts, StringComparison.Ordinal);
        Assert.DoesNotContain("vendor.Currency", superAdminPayouts, StringComparison.Ordinal);
        var executeRequestDto = payoutDtos.Substring(
            payoutDtos.IndexOf("public sealed class SuperAdminPrepareVendorPayoutRequestDto", StringComparison.Ordinal));
        var executeRequestStart = payoutsService.IndexOf("export interface SuperAdminPrepareVendorPayoutRequest", StringComparison.Ordinal);
        var executeRequestEnd = payoutsService.IndexOf("export interface SuperAdminVendorPayoutQueueMeta", executeRequestStart, StringComparison.Ordinal);
        var executeRequestType = payoutsService[executeRequestStart..executeRequestEnd];

        Assert.DoesNotContain("Currency", executeRequestDto, StringComparison.Ordinal);
        Assert.DoesNotContain("currency", executeRequestType, StringComparison.Ordinal);
        Assert.Contains("requireSuperAdminPayoutQueueFinancialAuthority", payoutsService, StringComparison.Ordinal);
        Assert.Contains("requireSuperAdminPayoutExecutionPresentation(response.data, vendorId, payload.amount, ['Processing', 'Completed'])", payoutsService, StringComparison.Ordinal);
        Assert.Contains("const COMMERCEHUB_AUTHORITY_CURRENCY = 'EGP'", frontendPayoutAuthority, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
