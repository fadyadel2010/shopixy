namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubSettlementPayoutFinalCurrencyCertificationTests
{
    [Fact]
    public void SettlementAndPayoutProjectionCurrency_MustRemainCommerceHubOwnedAndEgpOnly()
    {
        var aggregateService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubDailyFinancialAggregateService.cs");
        var aggregateConfiguration = ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/CommerceHubDailyFinancialAggregateConfiguration.cs");
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");
        var payoutService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPayoutService.cs");
        var superAdminPayouts = ReadRepoFile("PointOfSale.API/SuperAdmin/Services/SuperAdminPayoutsService.cs");
        var vendorSettlement = ReadRepoFile("PointOfSale.Core/Entities/CommerceHubFinance/CommerceHubVendorSettlement.cs");

        Assert.Contains("FinancialCurrencyGuard.RequireEgpCurrency", aggregateService, StringComparison.Ordinal);
        Assert.Contains("commercehub.daily_financial_aggregate.currency", aggregateService, StringComparison.Ordinal);
        Assert.Contains("CurrencyCode = currencyCode", aggregateService, StringComparison.Ordinal);
        Assert.Contains("CK_CommerceHubDailyFinancialAggregates_CurrencyCode_EGP", aggregateConfiguration, StringComparison.Ordinal);
        Assert.Contains("CK_CommerceHubDailyFinancialAggregates_BaseCurrencyCode_EGP", aggregateConfiguration, StringComparison.Ordinal);
        Assert.Contains("CK_CommerceHubRefunds_Currency_EGP", dbContext, StringComparison.Ordinal);
        Assert.Contains("RequireEgpCurrency(", payoutService, StringComparison.Ordinal);
        Assert.Contains("FinancialCurrencyGuard.RequireEgpCurrency", superAdminPayouts, StringComparison.Ordinal);
        Assert.Contains("FinancialCurrencyGuard.RequireEgpCurrency", vendorSettlement, StringComparison.Ordinal);
    }

    [Fact]
    public void FinalCurrencyCertificationArtifacts_MustRecordC7GAuthorityTruth()
    {
        var findings = ReadRepoFile("docs/architecture/settlement-payout-final-currency-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        Assert.Contains("PASS C7-G Settlement & Payout Final Currency Certification", findings, StringComparison.Ordinal);
        Assert.Contains("Settlement & Payout Currency Sovereignty is production-certifiable", findings, StringComparison.Ordinal);
        Assert.Contains("PASS C7-G", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS C7-G", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS C7-G", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS C7-G", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS C7-G", codexRules, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
