namespace PointOfSale.Tests.Architecture;

public sealed class GlobalBusinessConfigurationAuthorityCertificationTests
{
    [Fact]
    public void GlobalBusinessConfigurationMutations_MustBeBackendTenantRoleBoundAndNoStore()
    {
        var settingsController = ReadRepoFile("PointOfSale.API/Controllers/SettingsController.cs");

        Assert.Contains("[Authorize]", settingsController, StringComparison.Ordinal);
        Assert.Contains("[Authorize(Roles = \"Owner,Admin\")]", settingsController, StringComparison.Ordinal);
        Assert.Contains("public async Task<ActionResult<SettingsDto>> UpdateSettings", settingsController, StringComparison.Ordinal);
        Assert.Contains("public async Task<ActionResult<TenantCountryDto>> SetDefaultCountry", settingsController, StringComparison.Ordinal);
        Assert.Contains("var tenantId = _tenantContext.RequiredTenantId", settingsController, StringComparison.Ordinal);
        Assert.Contains("UpdateSettingAsync(tenant.Id, \"Company.Name\"", settingsController, StringComparison.Ordinal);
        Assert.Contains("UpdateSettingAsync(tenant.Id, \"Tax.CurrencyCode\"", settingsController, StringComparison.Ordinal);
        Assert.Contains("UpdateSettingAsync(tenant.Id, \"Tax.Rate\"", settingsController, StringComparison.Ordinal);
        Assert.Contains("tenant.Currency = normalizedCurrencyCode", settingsController, StringComparison.Ordinal);
        Assert.Contains("new TenantCurrencyChangedEvent", settingsController, StringComparison.Ordinal);
        Assert.Contains("[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]", settingsController, StringComparison.Ordinal);
        Assert.DoesNotContain("Request.Headers[\"X-Tenant-Id\"]", settingsController, StringComparison.Ordinal);
    }

    [Fact]
    public void GlobalBusinessConfigurationFrontend_MustNotOptimisticallyReplaceSharedAuthority()
    {
        var settingsContext = ReadRepoFile("PointOfSale.Web/src/contexts/SettingsContext.jsx");
        var settingsPage = ReadRepoFile("PointOfSale.Web/src/pages/Settings.jsx");

        Assert.Contains("'Cache-Control': 'no-store'", settingsContext, StringComparison.Ordinal);
        Assert.Contains("Pragma: 'no-cache'", settingsContext, StringComparison.Ordinal);
        Assert.Contains("await api.put('/settings', normalizedSettings", settingsContext, StringComparison.Ordinal);
        Assert.Contains("await fetchSettings()", settingsContext, StringComparison.Ordinal);
        Assert.DoesNotContain("setSettings(normalizedSettings)", settingsContext, StringComparison.Ordinal);

        Assert.Contains("'Cache-Control': 'no-store'", settingsPage, StringComparison.Ordinal);
        Assert.Contains("Pragma: 'no-cache'", settingsPage, StringComparison.Ordinal);
        Assert.Contains("const [settings, setSettings] = useState(null)", settingsPage, StringComparison.Ordinal);
        Assert.Contains("setSettingsLoadFailed(true)", settingsPage, StringComparison.Ordinal);
        Assert.Contains("const acceptedSettingsLoaded = await fetchSettings()", settingsPage, StringComparison.Ordinal);
        Assert.DoesNotContain("currencyCode: 'EGP'", settingsPage, StringComparison.Ordinal);
    }

    [Fact]
    public void StoreAdministrationGlobalBusinessConfiguration_MustKeepFrontendPresentationOnly()
    {
        var settingsPage = ReadRepoFile("PointOfSale.Web/src/pages/Settings.jsx");
        var settingsContext = ReadRepoFile("PointOfSale.Web/src/contexts/SettingsContext.jsx");
        var settingsController = ReadRepoFile("PointOfSale.API/Controllers/SettingsController.cs");

        Assert.Contains("const [settings, setSettings] = useState(null)", settingsPage, StringComparison.Ordinal);
        Assert.Contains("if (!settings)", settingsPage, StringComparison.Ordinal);
        Assert.Contains("return true", settingsPage, StringComparison.Ordinal);
        Assert.Contains("return false", settingsPage, StringComparison.Ordinal);
        Assert.Contains("const acceptedSettingsLoaded = await fetchSettings()", settingsPage, StringComparison.Ordinal);
        Assert.Contains("await api.put('/settings', settings", settingsPage, StringComparison.Ordinal);
        Assert.Contains("'Cache-Control': 'no-store'", settingsPage, StringComparison.Ordinal);
        Assert.Contains("Pragma: 'no-cache'", settingsPage, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage.setItem('settings", settingsPage, StringComparison.Ordinal);
        Assert.DoesNotContain("sessionStorage.setItem('settings", settingsPage, StringComparison.Ordinal);

        Assert.Contains("await api.put('/settings', normalizedSettings", settingsContext, StringComparison.Ordinal);
        Assert.Contains("await fetchSettings()", settingsContext, StringComparison.Ordinal);
        Assert.DoesNotContain("setSettings(normalizedSettings)", settingsContext, StringComparison.Ordinal);

        Assert.Contains("[Authorize(Roles = \"Owner,Admin\")]", settingsController, StringComparison.Ordinal);
        Assert.Contains("var tenantId = _tenantContext.RequiredTenantId", settingsController, StringComparison.Ordinal);
        Assert.Contains("UpdateSettingAsync(tenant.Id, \"Company.Name\"", settingsController, StringComparison.Ordinal);
        Assert.Contains("UpdateSettingAsync(tenant.Id, \"Tax.CurrencyCode\"", settingsController, StringComparison.Ordinal);
        Assert.Contains("UpdateSettingAsync(tenant.Id, \"Receipt.Header\"", settingsController, StringComparison.Ordinal);
        Assert.Contains("tenant.Currency = normalizedCurrencyCode", settingsController, StringComparison.Ordinal);
        Assert.Contains("LoadActiveTenantSettingsDictionaryAsync(tenantId)", settingsController, StringComparison.Ordinal);
    }

    [Fact]
    public void CurrencyProjectionAndCrossSystemConsumers_MustReenterBackendAuthority()
    {
        var settingsController = ReadRepoFile("PointOfSale.API/Controllers/SettingsController.cs");
        var tenantCurrencyProjection = ReadRepoFile("PointOfSale.Infrastructure/Consumers/TenantCurrencyProjectionConsumer.cs");
        var storefrontConfigProjection = ReadRepoFile("PointOfSale.Infrastructure/Consumers/StorefrontConfigProjectionConsumer.cs");
        var storefrontRuntime = ReadRepoFile("PointOfSale.API/Controllers/StorefrontStoreConfigController.cs");
        var commerceHubCart = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCartService.cs");
        var commerceHubCheckout = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");
        var affiliatePayout = ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliatePayoutService.cs");

        Assert.Contains("TenantCurrencyChangedEvent", settingsController, StringComparison.Ordinal);
        Assert.Contains("ResolveCurrentSqlTenantCurrencyAsync", tenantCurrencyProjection, StringComparison.Ordinal);
        Assert.Contains(".Set(x => x.Currency, sqlCurrency)", tenantCurrencyProjection, StringComparison.Ordinal);
        Assert.Contains("TenantLineageAuthority.LoadCurrentStoreConfigProjectionAuthorityAsync", storefrontConfigProjection, StringComparison.Ordinal);
        Assert.Contains("_dbContext.Tenants", storefrontRuntime, StringComparison.Ordinal);
        Assert.Contains("authority.Tenant.Currency", storefrontRuntime, StringComparison.Ordinal);
        Assert.Contains("tenant.Currency != PointOfSale.Core.Financial.FinancialCurrencyGuard.PlatformBaseCurrencyCode", commerceHubCart, StringComparison.Ordinal);
        Assert.Contains("tenant.Currency != PointOfSale.Core.Financial.FinancialCurrencyGuard.PlatformBaseCurrencyCode", commerceHubCheckout, StringComparison.Ordinal);
        Assert.Contains("ResolveStoreBaseCurrencyAsync", affiliatePayout, StringComparison.Ordinal);
        Assert.Contains("tenant.Currency", affiliatePayout, StringComparison.Ordinal);
    }

    [Fact]
    public void HistoricalBusinessTruth_MustUsePersistedSnapshotsAndNotLiveConfiguration()
    {
        var order = ReadRepoFile("PointOfSale.Core/Entities/Order.cs");
        var orderItem = ReadRepoFile("PointOfSale.Core/Entities/OrderItem.cs");
        var salesInvoice = ReadRepoFile("PointOfSale.Core/Entities/SalesInvoice.cs");
        var salesInvoiceItem = ReadRepoFile("PointOfSale.Core/Entities/SalesInvoiceItem.cs");
        var checkout = ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var taxCalculation = ReadRepoFile("PointOfSale.API/Services/Pricing/TaxCalculationService.cs");

        Assert.Contains("public string Currency { get; set; }", order, StringComparison.Ordinal);
        Assert.Contains("public decimal TaxAmount { get; set; }", order, StringComparison.Ordinal);
        Assert.Contains("public string? PricingSnapshotJson { get; set; }", order, StringComparison.Ordinal);
        Assert.Contains("public decimal UnitPrice { get; set; }", orderItem, StringComparison.Ordinal);
        Assert.Contains("public decimal TaxAmount { get; set; }", orderItem, StringComparison.Ordinal);
        Assert.Contains("public decimal TaxAmount { get; set; }", salesInvoice, StringComparison.Ordinal);
        Assert.Contains("public decimal TaxRate { get; set; }", salesInvoiceItem, StringComparison.Ordinal);
        Assert.Contains("public decimal TaxAmount { get; set; }", salesInvoiceItem, StringComparison.Ordinal);
        Assert.Contains("PricingSnapshotJson", checkout, StringComparison.Ordinal);
        Assert.Contains("setting.TenantId == tenantId &&", taxCalculation, StringComparison.Ordinal);
        Assert.DoesNotContain("_context.SystemSettings.ToListAsync()", taxCalculation, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationArtifacts_MustRecordSfPa15Truth()
    {
        Assert.Contains(
            "PASS SF-PA1.5 Global Business Configuration Authority Certification",
            ReadRepoFile("docs/architecture/global-business-configuration-authority-certification-findings.md"),
            StringComparison.Ordinal);

        foreach (var path in new[]
                 {
                     ".ai-context/PROJECT_CONTEXT.md",
                     ".ai-context/ARCHITECTURE_TRUTH.md",
                     ".ai-context/KNOWN_RISKS.md",
                     ".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md",
                     ".ai-context/CODEX_RULES.md"
                 })
        {
            Assert.Contains("PASS SF-PA1.5", ReadRepoFile(path), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CertificationArtifacts_MustRecordSfAdm15Truth()
    {
        Assert.Contains(
            "PASS SF-ADM1.5 Global Business Configuration Authority Certification",
            ReadRepoFile("docs/architecture/store-administration-global-business-configuration-authority-certification-findings.md"),
            StringComparison.Ordinal);

        foreach (var path in new[]
                 {
                     ".ai-context/PROJECT_CONTEXT.md",
                     ".ai-context/ARCHITECTURE_TRUTH.md",
                     ".ai-context/KNOWN_RISKS.md",
                     ".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md",
                     ".ai-context/CODEX_RULES.md"
                 })
        {
            Assert.Contains("PASS SF-ADM1.5", ReadRepoFile(path), StringComparison.Ordinal);
        }
    }

    private static string ReadRepoFile(string relativePath)
    {
        var root = DiscoverSolutionRoot(AppContext.BaseDirectory);
        return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string DiscoverSolutionRoot(string startPath)
    {
        var current = new DirectoryInfo(startPath);
        while (current != null)
        {
            if (current.GetFiles("*.sln").Any() || current.GetDirectories("PointOfSale.API").Any())
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException($"Could not discover solution root from '{startPath}'.");
    }
}
