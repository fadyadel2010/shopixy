namespace PointOfSale.Tests.Architecture;

public sealed class TenantIsolationCertificationFreezeTests
{
    [Fact]
    public void TrustedTenantResolution_MustStripInboundTenantHeaderAndReaddOnlyAfterResolution()
    {
        var source = File.ReadAllText(ApiPath("Middleware", "TenantResolutionMiddleware.cs"));

        Assert.Contains("context.Request.Headers.Remove(TenantIdHeaderName)", source, StringComparison.Ordinal);
        Assert.Contains("ResolveAuthenticatedTenantAsync", source, StringComparison.Ordinal);
        Assert.Contains("ResolveRequestTenantAsync", source, StringComparison.Ordinal);
        Assert.Contains("HeaderMatchesTenant", source, StringComparison.Ordinal);
        Assert.Contains("tenant.Status == TenantStatus.Active", source, StringComparison.Ordinal);
        Assert.Contains("tenant.DeletedAtUtc == null", source, StringComparison.Ordinal);
        Assert.Contains("context.Request.Headers[TenantIdHeaderName] = effectiveTenant.Id.ToString()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TenantAdminOperationalControls_MustUseTrustedTenantContextAndBlockGlobalRepair()
    {
        foreach (var relativePath in new[]
        {
            "Controllers/AdminDataSyncController.cs",
            "Controllers/AdminOrdersController.cs",
            "Controllers/AdminShipmentsController.cs",
            "Controllers/AdminInventoryController.cs",
            "Controllers/AdminAccountingController.cs",
            "Controllers/AdminCommerceHubController.cs",
            "Controllers/AdminStoreConfigController.cs",
            "Controllers/AdminCmsPagesController.cs",
            "Controllers/SearchReindexController.cs",
            "Controllers/Admin/AF16E_ReindexController.cs",
            "Controllers/Admin/AdminPixelTrackingController.cs",
            "Controllers/AdminAiProductGeneratorController.cs",
            "Controllers/AdminAiExplanationController.cs",
            "Controllers/AdminHelpAssistantController.cs",
            "Controllers/AdminCommerceAiController.cs",
            "Controllers/AdminAbandonedCheckoutsController.cs",
            "Controllers/AdminAffiliateController.cs",
            "Controllers/PaymentMethodsAdminController.cs",
            "Controllers/OnlineCustomersAdminController.cs",
            "Controllers/OnlineCustomerSessionsAdminController.cs",
            "Controllers/StorefrontAuthProvidersAdminController.cs",
            "Controllers/AdminStorefrontFooterController.cs",
            "Controllers/WishlistAnalyticsAdminController.cs",
            "Controllers/Admin/AdminAbandonedCartRecoveryController.cs",
            "Controllers/Admin/WhatsAppAdminController.cs",
            "Controllers/CouponsController.cs",
            "Controllers/CategoriesController.cs",
            "Controllers/AuditLogsController.cs",
            "Controllers/VendorPayoutMethodsController.cs",
            "Controllers/DashboardController.cs",
            "Controllers/FinancialReportsController.cs",
            "Controllers/BanksController.cs",
            "Controllers/ExpenseCategoriesController.cs",
            "Controllers/FinancialAccountsController.cs",
            "Controllers/ShippingMethodsController.cs"
        })
        {
            var source = File.ReadAllText(ApiPath(relativePath.Split('/')));
            Assert.Contains("RequiredTenantId", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Request.Headers[\"X-Tenant-Id\"]", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Request.Headers.TryGetValue(\"X-Tenant-Id\"", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Request.Headers[TenantIdHeader]", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Request.Headers.TryGetValue(TenantIdHeader", source, StringComparison.Ordinal);
        }

        var searchReindex = File.ReadAllText(ApiPath("Controllers", "SearchReindexController.cs"));
        Assert.Contains("GLOBAL_SEARCH_CONTROL_FORBIDDEN", searchReindex, StringComparison.Ordinal);
        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyAsync", searchReindex, StringComparison.Ordinal);

        var adminCommerceHub = File.ReadAllText(ApiPath("Controllers", "AdminCommerceHubController.cs"));
        Assert.Contains("GLOBAL_SEARCH_CONTROL_FORBIDDEN", adminCommerceHub, StringComparison.Ordinal);
        Assert.Contains("_reindexService.ReindexTenantAsync(tenantId", adminCommerceHub, StringComparison.Ordinal);
        Assert.Contains("item.TenantId == tenantId", adminCommerceHub, StringComparison.Ordinal);
        Assert.Contains("item.SubOrder.StoreId == tenantId", adminCommerceHub, StringComparison.Ordinal);
        Assert.Contains("GroupBy(item => new", adminCommerceHub, StringComparison.Ordinal);
        Assert.DoesNotContain("_searchIndexService.FinalizeAliasMigrationAsync(", adminCommerceHub, StringComparison.Ordinal);
        Assert.DoesNotContain("_reindexService.CatchUpReindexAsync(", adminCommerceHub, StringComparison.Ordinal);

        var tenantSaveInterceptor = File.ReadAllText(InfrastructurePath("Data", "TenantSaveChangesInterceptor.cs"));
        Assert.Contains("e.State == EntityState.Deleted", tenantSaveInterceptor, StringComparison.Ordinal);

        var abandonedRecovery = File.ReadAllText(ApiPath("Controllers", "Admin", "AdminAbandonedCartRecoveryController.cs"));
        // Recovery no longer accepts tenant authority from its body. Freeze the
        // current trusted-context assignment and service boundary instead.
        Assert.Contains("var tenantId = _tenantContext.RequiredTenantId;", abandonedRecovery, StringComparison.Ordinal);
        Assert.Contains("TenantId = tenantId", abandonedRecovery, StringComparison.Ordinal);
        Assert.Contains("_settingsService.UpdateSettingsAsync(tenantId, settings, ct)", abandonedRecovery, StringComparison.Ordinal);
        Assert.DoesNotContain("request.TenantId", abandonedRecovery, StringComparison.Ordinal);

        var paymentMethods = File.ReadAllText(ApiPath("Controllers", "PaymentMethodsAdminController.cs"));
        Assert.Contains("private Guid TenantId => _tenantContext.RequiredTenantId;", paymentMethods, StringComparison.Ordinal);
        Assert.Contains("Where(m => m.TenantId == tenantId && !m.IsDeleted)", paymentMethods, StringComparison.Ordinal);

        var affiliateAdmin = File.ReadAllText(ApiPath("Controllers", "AdminAffiliateController.cs"));
        Assert.Contains("private Guid TenantId => _tenantContext.RequiredTenantId;", affiliateAdmin, StringComparison.Ordinal);
        Assert.DoesNotContain("FindFirst(\"tenant_id\")", affiliateAdmin, StringComparison.Ordinal);
        Assert.DoesNotContain("GetTenantId()", affiliateAdmin, StringComparison.Ordinal);

        var onlineCustomers = File.ReadAllText(ApiPath("Controllers", "OnlineCustomersAdminController.cs"));
        Assert.Contains("private Guid TenantId => _tenantContext.RequiredTenantId;", onlineCustomers, StringComparison.Ordinal);
        Assert.Contains("Where(c => c.TenantId == tenantId)", onlineCustomers, StringComparison.Ordinal);
        Assert.Contains("FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId", onlineCustomers, StringComparison.Ordinal);
        Assert.Contains("CountAsync(", onlineCustomers, StringComparison.Ordinal);
        Assert.Contains("t => t.TenantId == tenantId &&", onlineCustomers, StringComparison.Ordinal);
        Assert.DoesNotContain("FindAsync(new object[] { id }", onlineCustomers, StringComparison.Ordinal);

        var coupons = File.ReadAllText(ApiPath("Controllers", "CouponsController.cs"));
        Assert.Contains("private Guid TenantId => _tenantContext.RequiredTenantId;", coupons, StringComparison.Ordinal);
        Assert.Contains("Where(c => c.TenantId == tenantId && !c.IsDeleted)", coupons, StringComparison.Ordinal);
        Assert.Contains("FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId && !c.IsDeleted", coupons, StringComparison.Ordinal);
        Assert.DoesNotContain("FindAsync([id]", coupons, StringComparison.Ordinal);

        var categories = File.ReadAllText(ApiPath("Controllers", "CategoriesController.cs"));
        Assert.Contains("private Guid TenantId => _tenantContext.RequiredTenantId;", categories, StringComparison.Ordinal);
        Assert.Contains("Where(c => c.TenantId == tenantId && !c.IsDeleted)", categories, StringComparison.Ordinal);
        Assert.Contains("FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId && !c.IsDeleted", categories, StringComparison.Ordinal);
        Assert.Contains("AnyAsync(c => c.TenantId == tenantId && !c.IsDeleted, cancellationToken)", categories, StringComparison.Ordinal);
        Assert.DoesNotContain("FindAsync([id]", categories, StringComparison.Ordinal);

        var auditLogs = File.ReadAllText(ApiPath("Controllers", "AuditLogsController.cs"));
        Assert.Contains("private Guid TenantId => _tenantContext.RequiredTenantId;", auditLogs, StringComparison.Ordinal);
        Assert.DoesNotContain("_tenantContext.TenantId", auditLogs, StringComparison.Ordinal);

        var vendorPayoutMethods = File.ReadAllText(ApiPath("Controllers", "VendorPayoutMethodsController.cs"));
        Assert.Contains("private Guid TenantId => _tenantContext.RequiredTenantId;", vendorPayoutMethods, StringComparison.Ordinal);
        Assert.DoesNotContain("_tenantContext.TenantId.Value", vendorPayoutMethods, StringComparison.Ordinal);

        var whatsAppAdmin = File.ReadAllText(ApiPath("Controllers", "Admin", "WhatsAppAdminController.cs"));
        Assert.Contains("private Guid TenantId => _tenantContext.RequiredTenantId;", whatsAppAdmin, StringComparison.Ordinal);
        Assert.Contains("_accountService.GetTenantAccountAsync(TenantId, id, ct)", whatsAppAdmin, StringComparison.Ordinal);
        Assert.Contains("_accountService.GetTenantTemplateAsync(TenantId, id, ct)", whatsAppAdmin, StringComparison.Ordinal);
        Assert.Contains("_accountService.ToggleAccountStatusAsync(tenantId, id, true, ct)", whatsAppAdmin, StringComparison.Ordinal);
        Assert.Contains("_accountService.DeleteTemplateAsync(tenantId, id, ct)", whatsAppAdmin, StringComparison.Ordinal);

        var whatsAppAccountService = File.ReadAllText(InfrastructurePath("WhatsApp", "Services", "WhatsAppAccountService.cs"));
        Assert.Contains("FirstOrDefaultAsync(x => x.Id == accountId && x.TenantId == tenantId, ct)", whatsAppAccountService, StringComparison.Ordinal);
        Assert.Contains("FirstOrDefaultAsync(x => x.Id == account.Id && x.TenantId == account.TenantId, ct)", whatsAppAccountService, StringComparison.Ordinal);
        Assert.Contains(".Where(x => x.TenantId == tenantId)", whatsAppAccountService, StringComparison.Ordinal);
        Assert.Contains("FirstOrDefaultAsync(t => t.Id == templateId && t.TenantId == tenantId, ct)", whatsAppAccountService, StringComparison.Ordinal);
        Assert.Contains("FirstOrDefaultAsync(t => t.Id == template.Id && t.TenantId == template.TenantId, ct)", whatsAppAccountService, StringComparison.Ordinal);
        Assert.DoesNotContain("FindAsync(new object[] { accountId }", whatsAppAccountService, StringComparison.Ordinal);
        Assert.DoesNotContain("FindAsync(new object[] { templateId }", whatsAppAccountService, StringComparison.Ordinal);

        var dashboard = File.ReadAllText(ApiPath("Controllers", "DashboardController.cs"));
        Assert.Contains("private Guid TenantId => _tenantContext.RequiredTenantId;", dashboard, StringComparison.Ordinal);
        Assert.Contains("Where(invoice => invoice.TenantId == tenantId)", dashboard, StringComparison.Ordinal);
        Assert.Contains("Where(product => product.TenantId == tenantId)", dashboard, StringComparison.Ordinal);
        Assert.Contains("Where(stock => stock.TenantId == tenantId)", dashboard, StringComparison.Ordinal);
        Assert.Contains("Where(customer => customer.TenantId == tenantId)", dashboard, StringComparison.Ordinal);
        Assert.Contains("entry.TenantId == tenantId", dashboard, StringComparison.Ordinal);
        Assert.Contains("entry.PartyType == PartyType.Supplier", dashboard, StringComparison.Ordinal);
        Assert.Contains("Where(item => item.SalesInvoice.TenantId == tenantId)", dashboard, StringComparison.Ordinal);
        Assert.Contains("Where(payment => payment.SalesInvoice.TenantId == tenantId)", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("_tenantContext.TenantId.Value", dashboard, StringComparison.Ordinal);

        var financialReports = File.ReadAllText(ApiPath("Controllers", "FinancialReportsController.cs"));
        Assert.Contains("private Guid TenantId => _tenantContext.RequiredTenantId;", financialReports, StringComparison.Ordinal);
        Assert.DoesNotContain("_tenantContext.TenantId.Value", financialReports, StringComparison.Ordinal);
        Assert.Contains("_reportService.GetTrialBalanceAsync(", financialReports, StringComparison.Ordinal);
        Assert.Contains("_reportService.GetProfitAndLossAsync(", financialReports, StringComparison.Ordinal);
        Assert.Contains("_reportService.GetBalanceSheetAsync(", financialReports, StringComparison.Ordinal);
        Assert.Contains("_reportService.GetCashFlowAsync(", financialReports, StringComparison.Ordinal);
        Assert.Contains("_reportService.GetClearingAgingAsync(", financialReports, StringComparison.Ordinal);
        Assert.Contains("_reportService.GetAccountSummariesAsync(", financialReports, StringComparison.Ordinal);

        var banks = File.ReadAllText(ApiPath("Controllers", "BanksController.cs"));
        Assert.Contains("private Guid TenantId => _tenantContext.RequiredTenantId;", banks, StringComparison.Ordinal);
        Assert.Contains("Where(b => b.TenantId == tenantId && !b.IsDeleted)", banks, StringComparison.Ordinal);
        Assert.Contains("FirstOrDefaultAsync(b => b.Id == id && b.TenantId == tenantId && !b.IsDeleted)", banks, StringComparison.Ordinal);
        Assert.Contains("Where(t => t.TenantId == tenantId && t.BankId == request.BankId)", banks, StringComparison.Ordinal);
        Assert.DoesNotContain("FindAsync(", banks, StringComparison.Ordinal);

        var expenseCategories = File.ReadAllText(ApiPath("Controllers", "ExpenseCategoriesController.cs"));
        Assert.Contains("private Guid TenantId => _tenantContext.RequiredTenantId;", expenseCategories, StringComparison.Ordinal);
        Assert.Contains("Where(c => c.TenantId == tenantId && !c.IsDeleted)", expenseCategories, StringComparison.Ordinal);
        Assert.Contains("FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId && !c.IsDeleted)", expenseCategories, StringComparison.Ordinal);
        Assert.Contains("AnyAsync(e => e.TenantId == tenantId && e.ExpenseCategoryId == id)", expenseCategories, StringComparison.Ordinal);
        Assert.DoesNotContain("FindAsync(", expenseCategories, StringComparison.Ordinal);

        var financialAccounts = File.ReadAllText(ApiPath("Controllers", "FinancialAccountsController.cs"));
        Assert.Contains("private Guid TenantId => _tenantContext.RequiredTenantId;", financialAccounts, StringComparison.Ordinal);
        Assert.Contains("Where(a => a.TenantId == tenantId && !a.IsDeleted)", financialAccounts, StringComparison.Ordinal);
        Assert.Contains("FirstOrDefaultAsync(a => a.Id == id && a.TenantId == tenantId && !a.IsDeleted)", financialAccounts, StringComparison.Ordinal);
        Assert.Contains("a.Id == request.ParentAccountId.Value", financialAccounts, StringComparison.Ordinal);
        Assert.Contains("t.TenantId == tenantId", financialAccounts, StringComparison.Ordinal);
        Assert.DoesNotContain("FindAsync(id)", financialAccounts, StringComparison.Ordinal);

        var shippingMethods = File.ReadAllText(ApiPath("Controllers", "ShippingMethodsController.cs"));
        Assert.Contains("private Guid TenantId => _tenantContext.RequiredTenantId;", shippingMethods, StringComparison.Ordinal);
        Assert.Contains("Where(s => s.TenantId == tenantId && !s.IsDeleted)", shippingMethods, StringComparison.Ordinal);
        Assert.Contains("FirstOrDefaultAsync(s => s.Id == id && s.TenantId == tenantId && !s.IsDeleted)", shippingMethods, StringComparison.Ordinal);
        Assert.Contains("s.Id == request.ShippingMethodId", shippingMethods, StringComparison.Ordinal);
        Assert.DoesNotContain("FindAsync(", shippingMethods, StringComparison.Ordinal);
    }

    [Fact]
    public void TenantScopedDerivativeSurfaces_MustRetainSqlLineageScopedIdsAndReplayGuards()
    {
        AssertSourceContains(
            InfrastructurePath("Consumers", "Affiliate", "AffiliateProductPolicyProjectionConsumer.cs"),
            "TenantLineageAuthority.ValidateCurrentTenantOwnerAsync<Product>",
            "TenantScopedDerivativeKey.ForTenantEntity",
            "BuildPublicAffiliateProductFilter",
            "ProjectionConsumerEventFence.TryAcquireAsync");

        AssertSourceContains(
            InfrastructurePath("ReadModels", "Marketplace", "MarketplaceReadModelProjectionServices.cs"),
            "TenantLineageAuthority.IsActiveTenantAsync",
            "SqlTruthRebuildGuard.RequireSqlAuthority",
            "CreateCompositeId(product.TenantId, product.Id)",
            "DeleteIfFreshAsync");

        AssertSourceContains(
            InfrastructurePath("Consumers", "CmsPageProjectionConsumer.cs"),
            "ProjectionEventFence.TryAcquireAsync",
            "TenantLineageAuthority.IsActiveTenantAsync",
            "FailClosedCmsPageProjectionAsync",
            "DerivativeProjectionMutationGuard.RequireReviewedMutation",
            "SqlAuthorityOverlayKind.StorefrontCms",
            "filter.Ne(item => item.LastEventId, eventId)");
    }

    [Fact]
    public void CertificationDocumentation_MustRecordGuaranteesWeaknessesAndFreezeInvariants()
    {
        var certification = File.ReadAllText(DocsPath("architecture", "tenant-isolation-certification-findings.md"));

        foreach (var marker in new[]
        {
            "TENANT-CERTIFICATION-01",
            "Boundaries Audited",
            "Chaos Scenarios Validated",
            "Verified Guarantees",
            "Runtime Hardening Added",
            "Regression Freeze Invariants",
            "Operational Confidence",
            "Remaining Tenant Risks",
            "Do not claim shared-helper adoption when a path uses an equivalent local SQL check",
            "docs/operations/operational-recovery-runbooks.md"
        })
        {
            Assert.Contains(marker, certification, StringComparison.Ordinal);
        }
    }

    private static void AssertSourceContains(string path, params string[] markers)
    {
        var source = File.ReadAllText(path);
        foreach (var marker in markers)
        {
            Assert.Contains(marker, source, StringComparison.Ordinal);
        }
    }

    private static string ApiPath(params string[] parts) =>
        Path.Combine([DiscoverSolutionRoot(AppContext.BaseDirectory), "PointOfSale.API", .. parts]);

    private static string InfrastructurePath(params string[] parts) =>
        Path.Combine([DiscoverSolutionRoot(AppContext.BaseDirectory), "PointOfSale.Infrastructure", .. parts]);

    private static string DocsPath(params string[] parts) =>
        Path.Combine([DiscoverSolutionRoot(AppContext.BaseDirectory), "docs", .. parts]);

    private static string DiscoverSolutionRoot(string startPath)
    {
        var currentDir = new DirectoryInfo(startPath);

        while (currentDir != null)
        {
            if (currentDir.GetFiles("*.sln").Any() ||
                currentDir.GetDirectories("PointOfSale.API").Any())
            {
                return currentDir.FullName;
            }

            currentDir = currentDir.Parent;
        }

        throw new InvalidOperationException($"Could not discover solution root from '{startPath}'.");
    }
}
