namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubSettlementIdentityCurrencyAuthorityCertificationTests
{
    [Fact]
    public void SettlementIdentityCurrency_MustRemainEgpBoundAcrossRuntimePersistenceAndPresentation()
    {
        var settlement = ReadRepoFile("PointOfSale.Core/Entities/CommerceHubFinance/CommerceHubVendorSettlement.cs");
        var settlementConfiguration = ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/CommerceHubVendorSettlementConfiguration.cs");
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");
        var superAdminVendorFinance = ReadRepoFile("PointOfSale.API/SuperAdmin/Services/SuperAdminVendorFinanceService.cs");
        var financialDashboard = ReadRepoFile("PointOfSale.API/Services/CommerceHub/Finance/CommerceHubFinancialDashboardService.cs");
        var frontendAuthority = ReadRepoFile("PointOfSale.Web/src/auth/commerceHubVendorFinancialParticipationAuthority.js");
        var frontendAuthorityTests = ReadRepoFile("PointOfSale.Web/src/auth/commerceHubVendorFinancialParticipationAuthority.test.ts");

        Assert.Contains("FinancialCurrencyGuard.RequireEgpCurrency", settlement, StringComparison.Ordinal);
        Assert.Contains("CK_CommerceHubVendorSettlements_Currency_EGP", settlementConfiguration, StringComparison.Ordinal);
        Assert.Contains("GuardCommerceHubCurrencySovereignty", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardCommerceHubVendorSettlementImmutability", dbContext, StringComparison.Ordinal);
        Assert.Contains("ChangeTracker.Entries<CommerceHubVendorSettlement>()", dbContext, StringComparison.Ordinal);
        Assert.Contains("records are append-only and cannot be deleted", dbContext, StringComparison.Ordinal);
        Assert.Contains("records are append-only and cannot be updated", dbContext, StringComparison.Ordinal);
        Assert.Contains("NormalizeCurrency(currency ?? DefaultCurrency)", superAdminVendorFinance, StringComparison.Ordinal);
        Assert.Contains("CommerceHub financial actions support EGP only", superAdminVendorFinance, StringComparison.Ordinal);
        Assert.Contains("FinancialCurrencyGuard.RequireEgpCurrency", financialDashboard, StringComparison.Ordinal);
        Assert.Contains("const hasAuthorityCurrency = (value) => normalizeString(value) === 'EGP'", frontendAuthority, StringComparison.Ordinal);
        Assert.Contains("recentSettlements: [{ ...financeDashboard.recentSettlements[0], currency: 'USD' }]", frontendAuthorityTests, StringComparison.Ordinal);
    }

    [Fact]
    public void SettlementIdentityCurrency_MustNotInheritTenantVendorOrPayoutCurrency()
    {
        var superAdminVendorFinance = ReadRepoFile("PointOfSale.API/SuperAdmin/Services/SuperAdminVendorFinanceService.cs");
        var payoutRead = ReadRepoFile("PointOfSale.API/Services/CommerceHub/Finance/CommerceHubVendorPayoutReadService.cs");
        var payoutService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPayoutService.cs");
        var settlement = ReadRepoFile("PointOfSale.Core/Entities/CommerceHubFinance/CommerceHubVendorSettlement.cs");

        Assert.DoesNotContain("Currency = tenant.Currency", superAdminVendorFinance, StringComparison.Ordinal);
        Assert.DoesNotContain("currency ?? vendor.Currency", superAdminVendorFinance, StringComparison.Ordinal);
        Assert.DoesNotContain("Currency = tenant.Currency", payoutRead, StringComparison.Ordinal);
        Assert.Contains("FinancialCurrencyGuard.PlatformBaseCurrencyCode", payoutRead, StringComparison.Ordinal);
        Assert.Contains("RequireEgpCurrency(", payoutService, StringComparison.Ordinal);
        Assert.Contains("FinancialCurrencyGuard.PlatformBaseCurrencyCode", settlement, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
