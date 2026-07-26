namespace PointOfSale.Tests.Architecture;

public sealed class StorefrontConfigurationAuthorityCertificationTests
{
    [Fact]
    public void PublicConfiguration_MustReadSqlLiveTruthWithoutProjectionFallback()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/StorefrontStoreConfigController.cs");
        var mapper = ReadRepoFile("PointOfSale.API/Services/Storefront/StorefrontDraftConfigMapper.cs");

        Assert.Contains("_dbContext.StoreConfigs", controller, StringComparison.Ordinal);
        Assert.Contains("_dbContext.Tenants", controller, StringComparison.Ordinal);
        Assert.Contains("StorefrontDraftConfigMapper.MapLive", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("GetStoreConfigJsonAsync", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("GetDefaultConfig", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("IMongoCollection<StorefrontConfigDocument>", controller, StringComparison.Ordinal);
        Assert.Contains("public static StorefrontStoreConfigDto MapLive", mapper, StringComparison.Ordinal);
        Assert.Contains("useDraftContent: false", mapper, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicationAndFeatureFlags_MustUseSqlAndFailClosed()
    {
        var publication = ReadRepoFile("PointOfSale.API/Services/Storefront/StorefrontPublicationStatusService.cs");
        var promotions = ReadRepoFile("PointOfSale.API/Services/Storefront/PromotionRankingService.cs");
        var chat = ReadRepoFile("PointOfSale.API/Controllers/StorefrontChatController.cs");

        Assert.Contains("_dbContext.StoreConfigs", publication, StringComparison.Ordinal);
        Assert.Contains("authority.DeletedAtUtc == null", publication, StringComparison.Ordinal);
        Assert.DoesNotContain("GetStoreConfigJsonAsync", publication, StringComparison.Ordinal);
        Assert.DoesNotContain("IMongoCollection", publication, StringComparison.Ordinal);

        Assert.Contains("_dbContext.StoreConfigs", promotions, StringComparison.Ordinal);
        Assert.Contains("new PromotionRankingConfig { Enabled = false }", promotions, StringComparison.Ordinal);
        Assert.DoesNotContain("_cacheService.GetAsync", promotions, StringComparison.Ordinal);
        Assert.DoesNotContain("DefaultConfig", promotions, StringComparison.Ordinal);

        Assert.Contains("_dbContext.StoreConfigs", chat, StringComparison.Ordinal);
        Assert.Contains("Select(config => config.CommerceAiEnabled)", chat, StringComparison.Ordinal);
    }

    [Fact]
    public void IdentityDomainSeoAndCurrency_MustResolveFromSqlAuthority()
    {
        var middleware = ReadRepoFile("PointOfSale.API/Middleware/TenantResolutionMiddleware.cs");
        var tenantContext = ReadRepoFile("PointOfSale.API/Helpers/StorefrontTenantContextResolver.cs");
        var urlResolver = ReadRepoFile("PointOfSale.API/Services/StorefrontUrlResolver.cs");
        var seo = ReadRepoFile("PointOfSale.API/Services/Storefront/SeoService.cs");
        var cart = ReadRepoFile("PointOfSale.API/Services/Cart/CartService.cs");
        var affiliateAnalytics = ReadRepoFile("PointOfSale.API/Controllers/AffiliateAnalyticsController.cs");

        Assert.Contains(".Take(2)", middleware, StringComparison.Ordinal);
        Assert.Contains("matches.Count == 1", middleware, StringComparison.Ordinal);
        Assert.DoesNotContain("Request.Headers[\"X-Tenant-Id\"]", tenantContext, StringComparison.Ordinal);
        Assert.Contains("ValidateAndNormalizeBaseUrl", urlResolver, StringComparison.Ordinal);
        Assert.Contains("uri.GetLeftPart(UriPartial.Authority)", urlResolver, StringComparison.Ordinal);
        Assert.Contains("t.DeletedAtUtc == null", urlResolver, StringComparison.Ordinal);
        Assert.Contains("baseUrl = await _storefrontUrlResolver.ResolveByTenantIdAsync(tenantId)", seo, StringComparison.Ordinal);
        Assert.Contains("tenant.DeletedAtUtc == null", seo, StringComparison.Ordinal);
        Assert.Contains("_dbContext.Tenants", cart, StringComparison.Ordinal);
        Assert.Contains(".Select(tenant => tenant.Currency)", cart, StringComparison.Ordinal);
        Assert.Contains("_dbContext.StoreConfigs", affiliateAnalytics, StringComparison.Ordinal);
        Assert.DoesNotContain("Resolve store names from StorefrontConfigs (MongoDB)", affiliateAnalytics, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigurationWrites_MustBeTenantBoundRoleBoundAndConcurrencyProtected()
    {
        var primary = ReadRepoFile("PointOfSale.API/Controllers/AdminStoreConfigController.cs");
        var primaryService = ReadRepoFile("PointOfSale.API/Services/Admin/AdminStoreConfigService.cs");
        var commerceAi = ReadRepoFile("PointOfSale.API/Controllers/AdminCommerceAiController.cs");

        Assert.Contains("Roles = \"Owner,Admin\"", primary, StringComparison.Ordinal);
        Assert.Contains("If-Match", primary, StringComparison.Ordinal);
        Assert.Contains("ApplyExpectedRowVersion", primaryService, StringComparison.Ordinal);
        Assert.Contains("DbUpdateConcurrencyException", primaryService, StringComparison.Ordinal);
        Assert.Contains("Roles = \"Owner,Admin\"", commerceAi, StringComparison.Ordinal);
        Assert.Contains("StatusCodes.Status428PreconditionRequired", commerceAi, StringComparison.Ordinal);
        Assert.Contains("Property(config => config.RowVersion).OriginalValue", commerceAi, StringComparison.Ordinal);
        Assert.Contains("DbUpdateConcurrencyException", commerceAi, StringComparison.Ordinal);
        Assert.Contains("STORE_CONFIG_NOT_FOUND", commerceAi, StringComparison.Ordinal);
    }

    [Fact]
    public void StorefrontSettingsManagement_MustUsePosAdminAuthorityNotCommerceHubLifecycleNavigation()
    {
        var primary = ReadRepoFile("PointOfSale.API/Controllers/AdminStoreConfigController.cs");
        var settingsShell = ReadRepoFile("PointOfSale.Web/src/pages/Settings.jsx");
        var settingsPage = ReadRepoFile("PointOfSale.Web/src/pages/StorefrontSettings.jsx");
        var authority = ReadRepoFile("PointOfSale.Web/src/auth/storefrontAdminAuthority.js");
        var statusCard = ReadRepoFile("PointOfSale.Web/src/components/StorefrontStatusCard.jsx");

        Assert.Contains("Roles = \"Owner,Admin\"", primary, StringComparison.Ordinal);
        Assert.Contains("canManageStorefrontSettings(user)", settingsShell, StringComparison.Ordinal);
        Assert.Contains("canManageStorefrontSettings(user)", settingsPage, StringComparison.Ordinal);
        Assert.Contains("new Set(['Owner', 'Admin'])", authority, StringComparison.Ordinal);
        Assert.Contains("navigate('/settings?tab=storefront')", statusCard, StringComparison.Ordinal);

        Assert.DoesNotContain("canAccessCommerceHubVendorNavigationCapability(user, 'commercehub.storefront.manage')", settingsShell, StringComparison.Ordinal);
        Assert.DoesNotContain("canAccessCommerceHubVendorNavigationCapability(user, 'commercehub.storefront.manage')", settingsPage, StringComparison.Ordinal);
    }

    [Fact]
    public void StoreConfigurationReadAuthority_MustFailClosedAndAvoidCacheAuthority()
    {
        var adminController = ReadRepoFile("PointOfSale.API/Controllers/AdminStoreConfigController.cs");
        var publicController = ReadRepoFile("PointOfSale.API/Controllers/StorefrontStoreConfigController.cs");
        var frontendAuthority = ReadRepoFile("PointOfSale.Web/src/auth/commerceHubVendorIdentityAuthority.js");
        var settingsPage = ReadRepoFile("PointOfSale.Web/src/pages/StorefrontSettings.jsx");
        var statusCard = ReadRepoFile("PointOfSale.Web/src/components/StorefrontStatusCard.jsx");
        var storeConfigService = ReadRepoFile("PointOfSale.Web/src/modules/storefront/services/storeConfigService.js");

        Assert.Contains("[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]", adminController, StringComparison.Ordinal);
        Assert.Contains("[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]", publicController, StringComparison.Ordinal);
        Assert.Contains("_dbContext.StoreConfigs", publicController, StringComparison.Ordinal);
        Assert.Contains("_dbContext.Tenants", publicController, StringComparison.Ordinal);
        Assert.Contains("STOREFRONT_CONFIG_NOT_FOUND", publicController, StringComparison.Ordinal);

        Assert.Contains("rowVersion", frontendAuthority, StringComparison.Ordinal);
        Assert.Contains("themeId", frontendAuthority, StringComparison.Ordinal);
        Assert.Contains("return null", frontendAuthority, StringComparison.Ordinal);
        Assert.Contains("deriveCurrentStoreIdentityPresentation(response, user)", settingsPage, StringComparison.Ordinal);
        Assert.Contains("setBaselineConfig(null)", settingsPage, StringComparison.Ordinal);
        Assert.Contains("setCandidateConfig(null)", settingsPage, StringComparison.Ordinal);
        Assert.Contains("deriveCurrentStoreIdentityPresentation(response, user)", statusCard, StringComparison.Ordinal);
        Assert.Contains("'Cache-Control': 'no-store'", storeConfigService, StringComparison.Ordinal);
        Assert.Contains("Pragma: 'no-cache'", storeConfigService, StringComparison.Ordinal);
        Assert.DoesNotContain("response.data.storefrontStatus || 'live'", settingsPage, StringComparison.Ordinal);
    }

    [Fact]
    public void StoreConfigurationCandidateAuthority_MustKeepDraftStateSeparateFromBackendBaseline()
    {
        var settingsPage = ReadRepoFile("PointOfSale.Web/src/pages/StorefrontSettings.jsx");
        var settingsTest = ReadRepoFile("PointOfSale.Web/src/pages/StorefrontSettings.test.tsx");

        Assert.Contains("baselineConfig", settingsPage, StringComparison.Ordinal);
        Assert.Contains("candidateConfig", settingsPage, StringComparison.Ordinal);
        Assert.Contains("dirtyRef", settingsPage, StringComparison.Ordinal);
        Assert.Contains("resetCandidateToBaseline", settingsPage, StringComparison.Ordinal);
        Assert.Contains("Unsaved storefront settings are candidate-only until the backend accepts them.", settingsPage, StringComparison.Ordinal);
        Assert.Contains("setBaselineConfig(nextStoreConfig)", settingsPage, StringComparison.Ordinal);
        Assert.Contains("if (!dirtyRef.current)", settingsPage, StringComparison.Ordinal);
        Assert.Contains("setCandidateConfig(nextStoreConfig)", settingsPage, StringComparison.Ordinal);
        Assert.Contains("setCandidateConfig(baselineConfig)", settingsPage, StringComparison.Ordinal);

        Assert.Contains("keeps unsaved status edits candidate-only until backend acceptance", settingsTest, StringComparison.Ordinal);
        Assert.Contains("resets candidate edits to the accepted backend baseline", settingsTest, StringComparison.Ordinal);
        Assert.Contains("does not preserve candidate edits across remount refresh recovery", settingsTest, StringComparison.Ordinal);
    }

    [Fact]
    public void StoreConfigurationMutationAuthority_MustSubmitVersionedIntentAndReplaceBaselineOnlyAfterBackendAcceptance()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/AdminStoreConfigController.cs");
        var primaryService = ReadRepoFile("PointOfSale.API/Services/Admin/AdminStoreConfigService.cs");
        var storeConfigService = ReadRepoFile("PointOfSale.Web/src/modules/storefront/services/storeConfigService.js");
        var settingsPage = ReadRepoFile("PointOfSale.Web/src/pages/StorefrontSettings.jsx");
        var settingsTest = ReadRepoFile("PointOfSale.Web/src/pages/StorefrontSettings.test.tsx");
        var serviceTest = ReadRepoFile("PointOfSale.Web/src/modules/storefront/services/storeConfigService.test.ts");

        Assert.Contains("RequireExpectedRowVersion(traceId, request.RowVersion)", controller, StringComparison.Ordinal);
        Assert.Contains("StatusCodes.Status428PreconditionRequired", controller, StringComparison.Ordinal);
        Assert.Contains("ConcurrencyConflictException", controller, StringComparison.Ordinal);
        Assert.Contains("ApplyExpectedRowVersion(config!, expectedRowVersion)", primaryService, StringComparison.Ordinal);
        Assert.Contains(".Property(x => x.RowVersion)", primaryService, StringComparison.Ordinal);
        Assert.Contains("DbUpdateConcurrencyException", primaryService, StringComparison.Ordinal);
        Assert.Contains("RowVersion = config.RowVersion.ToString", primaryService, StringComparison.Ordinal);

        Assert.Contains("[HttpPut(\"status\")]", controller, StringComparison.Ordinal);
        Assert.Contains("UpdateStorefrontStatusAsync", primaryService, StringComparison.Ordinal);
        Assert.Contains("config!.StorefrontStatus = requestedStorefrontStatus", primaryService, StringComparison.Ordinal);
        Assert.Contains("storeConfigService.updateStorefrontStatus", settingsPage, StringComparison.Ordinal);
        Assert.Contains("rowVersion: candidateConfig.rowVersion", settingsPage, StringComparison.Ordinal);
        Assert.Contains("saveInFlightRef.current", settingsPage, StringComparison.Ordinal);
        Assert.Contains("deriveCurrentStoreIdentityPresentation(acceptedResponse, user)", settingsPage, StringComparison.Ordinal);
        Assert.Contains("setBaselineConfig(acceptedConfig)", settingsPage, StringComparison.Ordinal);
        Assert.Contains("setCandidateConfig(acceptedConfig)", settingsPage, StringComparison.Ordinal);
        Assert.DoesNotContain("api.put('/admin/store-config'", settingsPage, StringComparison.Ordinal);
        Assert.DoesNotContain("commerceAi", settingsPage, StringComparison.Ordinal);

        Assert.Contains("'If-Match': `\"${rowVersion}\"`", storeConfigService, StringComparison.Ordinal);
        Assert.Contains("api.put('/admin/store-config/status'", storeConfigService, StringComparison.Ordinal);
        Assert.Contains("submits candidate intent with backend rowVersion evidence", settingsTest, StringComparison.Ordinal);
        Assert.Contains("not.toHaveBeenCalledWith(expect.objectContaining({", settingsTest, StringComparison.Ordinal);
        Assert.Contains("does not replace baseline when the mutation fails or returns malformed acceptance evidence", settingsTest, StringComparison.Ordinal);
        Assert.Contains("suppresses duplicate save clicks while one mutation is in flight", settingsTest, StringComparison.Ordinal);
        Assert.Contains("submits store configuration mutations with If-Match rowVersion evidence", serviceTest, StringComparison.Ordinal);
        Assert.Contains("submits storefront status mutations to the status-only authority endpoint", serviceTest, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationArtifacts_MustRecordP2GaTruth()
    {
        Assert.Contains(
            "PASS P2-GA Storefront Configuration Authority",
            ReadRepoFile("docs/architecture/storefront-configuration-authority-certification-findings.md"),
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
            Assert.Contains("PASS P2-GA", ReadRepoFile(path), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CertificationArtifacts_MustRecordSfPa12Truth()
    {
        Assert.Contains(
            "PASS SF-PA1.2 Store Configuration Read Authority Certification",
            ReadRepoFile("docs/architecture/store-configuration-read-authority-certification-findings.md"),
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
            Assert.Contains("PASS SF-PA1.2", ReadRepoFile(path), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CertificationArtifacts_MustRecordSfAdm12Truth()
    {
        Assert.Contains(
            "PASS SF-ADM1.2 Store Configuration Read Authority Certification",
            ReadRepoFile("docs/architecture/store-administration-configuration-read-authority-certification-findings.md"),
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
            Assert.Contains("PASS SF-ADM1.2", ReadRepoFile(path), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CertificationArtifacts_MustRecordSfAdm13Truth()
    {
        Assert.Contains(
            "PASS SF-ADM1.3 Store Configuration Candidate Authority Certification",
            ReadRepoFile("docs/architecture/store-administration-configuration-candidate-authority-certification-findings.md"),
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
            Assert.Contains("PASS SF-ADM1.3", ReadRepoFile(path), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CertificationArtifacts_MustRecordSfAdm14Truth()
    {
        Assert.Contains(
            "PASS SF-ADM1.4 Store Configuration Mutation Authority Certification",
            ReadRepoFile("docs/architecture/store-administration-configuration-mutation-authority-certification-findings.md"),
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
            Assert.Contains("PASS SF-ADM1.4", ReadRepoFile(path), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CertificationArtifacts_MustRecordSfPa13Truth()
    {
        Assert.Contains(
            "PASS SF-PA1.3 Store Configuration Candidate Authority Certification",
            ReadRepoFile("docs/architecture/store-configuration-candidate-authority-certification-findings.md"),
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
            Assert.Contains("PASS SF-PA1.3", ReadRepoFile(path), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CertificationArtifacts_MustRecordSfPa14Truth()
    {
        Assert.Contains(
            "PASS SF-PA1.4 Store Configuration Mutation Authority Certification",
            ReadRepoFile("docs/architecture/store-configuration-mutation-authority-certification-findings.md"),
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
            Assert.Contains("PASS SF-PA1.4", ReadRepoFile(path), StringComparison.Ordinal);
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
