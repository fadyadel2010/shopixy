namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubPromotionsAuthorityCertificationTests
{
    [Fact]
    public void PromoRuntime_MustValidateConsumeAndCalculateFromSqlAuthority()
    {
        var promoService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/PromoService.cs");
        var checkout = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");
        var usageConfiguration = ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/PromoCodeUsageConfiguration.cs");
        var promoConfiguration = ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/PromoCodeConfiguration.cs");

        Assert.Contains("FROM \"PromoCodes\"", promoService, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", promoService, StringComparison.Ordinal);
        Assert.Contains("LoadPromoForConsumptionAuthorityAsync", promoService, StringComparison.Ordinal);
        Assert.Contains("GetUsageSnapshotAsync", promoService, StringComparison.Ordinal);
        Assert.Contains("usageSnapshot.TotalUsageCount >= promo.UsageLimit.Value", promoService, StringComparison.Ordinal);
        Assert.Contains("usageSnapshot.CustomerUsageCount >= promo.MaxUsagePerCustomer.Value", promoService, StringComparison.Ordinal);
        Assert.Contains("promo.ExpiryDate.Value <= nowUtc", promoService, StringComparison.Ordinal);
        Assert.Contains("PromoCodeUsages.Add(new PromoCodeUsage", promoService, StringComparison.Ordinal);

        Assert.Contains("ValidatePromoForCheckoutAsync", checkout, StringComparison.Ordinal);
        Assert.Contains("PromoCalculationEngine.Apply", checkout, StringComparison.Ordinal);
        Assert.Contains("ConsumePromoAsync", checkout, StringComparison.Ordinal);
        Assert.Contains("aborting checkout to prevent silent repricing", checkout, StringComparison.Ordinal);
        Assert.Contains("PromoDiscountTotal = RoundCurrency", checkout, StringComparison.Ordinal);

        Assert.Contains("UX_PromoCodes_Code", promoConfiguration, StringComparison.Ordinal);
        Assert.Contains("CK_PromoCodes_DiscountValue", promoConfiguration, StringComparison.Ordinal);
        Assert.Contains("CK_PromoCodes_MaxUsagePerCustomerWithinUsageLimit", promoConfiguration, StringComparison.Ordinal);
        Assert.Contains("UX_PromoCodeUsages_MarketplaceOrderId", usageConfiguration, StringComparison.Ordinal);
        Assert.Contains("UX_PromoCodeUsages_PromoCodeId_MarketplaceOrderId", usageConfiguration, StringComparison.Ordinal);
    }

    [Fact]
    public void PromoPublication_MustRequireCurrentVendorAndCategoryAuthority()
    {
        var management = ReadRepoFile("PointOfSale.API/SuperAdmin/Services/SuperAdminPromoCodeManagementService.cs");

        Assert.Contains("CommerceHubVendorAuthority.LoadAsync", management, StringComparison.Ordinal);
        Assert.Contains("!vendorAuthority.IsMarketplaceEligible", management, StringComparison.Ordinal);
        Assert.Contains("PROMO_SCOPE_VENDOR_INELIGIBLE", management, StringComparison.Ordinal);
        Assert.Contains("CommerceHubCategoryLineageAuthority.IsLiveAsync", management, StringComparison.Ordinal);
        Assert.Contains("PROMO_SCOPE_CATEGORY_INACTIVE", management, StringComparison.Ordinal);
        Assert.Contains("await ValidateScopeTargetsAsync(promoCode.VendorId, promoCode.CategoryId", management, StringComparison.Ordinal);
        Assert.Contains("NormalizeScopeTarget", management, StringComparison.Ordinal);
    }

    [Fact]
    public void PromoPricingBoundary_MustRemainOrderSnapshotAndLedgerDerived()
    {
        var checkout = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");
        var financialWorkflow = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubFinancialWorkflowService.cs");
        var ledger = ReadRepoFile("PointOfSale.API/Services/CommerceHub/FinancialLedger/CommerceHubFinancialLedgerService.cs");

        Assert.Contains("item.FinalPlatformCommissionAmount", checkout, StringComparison.Ordinal);
        Assert.Contains("CommissionSnapshotJson", checkout, StringComparison.Ordinal);
        Assert.Contains("ResolveFinalCommissionAmount", financialWorkflow, StringComparison.Ordinal);
        Assert.Contains("PromoDiscountAmount", financialWorkflow, StringComparison.Ordinal);
        Assert.Contains("CommerceHubFinancialTransactionType.Promotion", ledger, StringComparison.Ordinal);
        Assert.Contains("promoDiscount > grossCommission", ledger, StringComparison.Ordinal);
    }

    [Fact]
    public void PromoAuthorityDocumentation_MustBeFrozen()
    {
        var findings = ReadRepoFile("docs/architecture/commercehub-promotions-authority-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        Assert.Contains("PASS M10 CommerceHub Promotions Authority", findings, StringComparison.Ordinal);
        Assert.Contains("CommerceHub Promotions Authority is production-certifiable", findings, StringComparison.Ordinal);
        Assert.Contains("PASS M10 CommerceHub Promotions Authority", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS M10 CommerceHub Promotions Authority Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS M10", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS M10 CommerceHub Promotions Authority", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS M10 CommerceHub Promotions Authority Freeze", codexRules, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
