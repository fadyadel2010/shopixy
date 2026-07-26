using System.Text.RegularExpressions;

namespace PointOfSale.Tests.Architecture;

public sealed class PosAdminStage1CompositionShellCertificationTests
{
    [Fact]
    public void PosAdmin_MustRemainCompositionShellWithoutRuntimePersistenceOrAggregates()
    {
        var app = CertificationSource.ReadRepoFile("PointOfSale.Web/src/App.jsx");
        var sidebar = CertificationSource.ReadRepoFile("PointOfSale.Web/src/components/Sidebar.jsx");
        var dbContext = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");
        var productionSource = ReadProductionSource();

        Assert.Contains("<BrowserRouter>", app, StringComparison.Ordinal);
        Assert.Contains("<ProtectedRoute />", app, StringComparison.Ordinal);
        Assert.Contains("Sidebar", CertificationSource.ReadRepoFile("PointOfSale.Web/src/components/Layout.jsx"), StringComparison.Ordinal);
        Assert.Contains("const menuItems = [", sidebar, StringComparison.Ordinal);

        Assert.DoesNotContain("DbSet<PosAdmin", dbContext, StringComparison.Ordinal);
        Assert.DoesNotContain("PosAdminDbContext", productionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PosAdminAggregate", productionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ToTable(\"PosAdmin", productionSource, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"\bclass\s+PosAdmin(?:Runtime|Aggregate|DbContext)\b"), productionSource);
    }

    [Fact]
    public void WorkspaceComposition_MustUseRoleCapabilityAndRuntimeRouting()
    {
        var app = CertificationSource.ReadRepoFile("PointOfSale.Web/src/App.jsx");
        var sidebar = CertificationSource.ReadRepoFile("PointOfSale.Web/src/components/Sidebar.jsx");
        var routeGuard = CertificationSource.ReadRepoFile("PointOfSale.Web/src/components/CommerceHubVendorRouteGuard.jsx");
        var roleGuard = CertificationSource.ReadRepoFile("PointOfSale.Web/src/components/PosRoleRouteGuard.jsx");
        var marketplaceAuthority = CertificationSource.ReadRepoFile("PointOfSale.Web/src/marketplace/authority.ts");

        Assert.Contains("canAccessCommerceHubVendorNavigationCapability", sidebar, StringComparison.Ordinal);
        Assert.Contains("requiredRoles", sidebar, StringComparison.Ordinal);
        Assert.Contains("requiresMarketplaceActor", sidebar, StringComparison.Ordinal);
        Assert.Contains("isServiceMarketplaceWorkflowActor(user, true)", sidebar, StringComparison.Ordinal);
        Assert.Contains("CommerceHubVendorRouteGuard capability=", app, StringComparison.Ordinal);
        Assert.Contains("PosRoleRouteGuard roles=", app, StringComparison.Ordinal);
        Assert.Contains("canAccessCommerceHubVendorNavigationCapability(user, capability)", routeGuard, StringComparison.Ordinal);
        Assert.Contains("allowedRoles.has(user?.role)", roleGuard, StringComparison.Ordinal);
        Assert.Contains("isServiceMarketplaceWorkflowActor", marketplaceAuthority, StringComparison.Ordinal);

        foreach (var route in CanonicalRuntimeRoutes)
        {
            Assert.Contains(route, app, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void LegacyBankOpeningBalance_MustNotRemainCallableAsAShellWriteScreen()
    {
        var app = CertificationSource.ReadRepoFile("PointOfSale.Web/src/App.jsx");
        var banksController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/BanksController.cs");

        Assert.DoesNotContain("import BankOpeningBalance", app, StringComparison.Ordinal);
        Assert.Contains("path=\"opening-balance/banks\" element={<Navigate to=\"/treasury/account/1030\" replace />}", app, StringComparison.Ordinal);

        Assert.Contains("StatusCodes.Status410Gone", banksController, StringComparison.Ordinal);
        Assert.Contains("Legacy bank opening-balance writes are disabled", banksController, StringComparison.Ordinal);
        Assert.Contains("Bank opening balances must be posted through ledger-backed treasury operations", banksController, StringComparison.Ordinal);
    }

    [Fact]
    public void PosAdminNamedAdapters_MustDelegateToOwningRuntimesAndNotOwnSql()
    {
        var posAdminReturnsController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/PosAdminReturnsController.cs");

        Assert.Contains("IReturnManagementService", posAdminReturnsController, StringComparison.Ordinal);
        Assert.Contains("_returnManagementService.GetReturnDetailsAsync", posAdminReturnsController, StringComparison.Ordinal);
        Assert.Contains("_tenantContext.RequiredTenantId", posAdminReturnsController, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplicationDbContext", posAdminReturnsController, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveChanges", posAdminReturnsController, StringComparison.Ordinal);
        Assert.DoesNotContain("DbSet<", posAdminReturnsController, StringComparison.Ordinal);
    }

    [Fact]
    public void AggregateOwnership_MustHaveSingleSqlAuthorityAndNoPosAdminOwners()
    {
        var dbContext = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");
        var productionSource = ReadProductionSource();

        foreach (var aggregate in CanonicalAggregates)
        {
            Assert.Contains($"DbSet<{aggregate}>", dbContext, StringComparison.Ordinal);
            Assert.DoesNotMatch(new Regex($@"\bPosAdmin\w*\s*:\s*.*\b{Regex.Escape(aggregate)}\b"), productionSource);
        }

        Assert.Contains("DbSet<Account> LedgerAccounts", dbContext, StringComparison.Ordinal);
        Assert.Contains("DbSet<JournalEntry> JournalEntries", dbContext, StringComparison.Ordinal);
        Assert.Contains("Entity<ServiceRequest>()", dbContext, StringComparison.Ordinal);
        Assert.Contains("ToTable(\"ServiceRequests\")", CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/ServiceRequestConfiguration.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void PosRuntime_MustNotOwnPricingPromotionCouponsCheckoutCommerceHubShippingOrIdentity()
    {
        var salesController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/SalesController.cs");
        var purchasesController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/PurchasesController.cs");
        var posRuntimeSource = salesController + "\n" + purchasesController;

        foreach (var forbiddenDependency in new[]
                 {
                     "StorefrontPricingService",
                     "PromotionEngine",
                     "CouponUsageService",
                     "StorefrontCouponService",
                     "CheckoutService",
                     "CommerceHubCheckoutService",
                     "IPaymentLifecycleService",
                     "CarrierShipmentService",
                     "SuperAdmin"
                 })
        {
            Assert.DoesNotContain(forbiddenDependency, posRuntimeSource, StringComparison.Ordinal);
        }

        Assert.Contains("UnitPrice", salesController, StringComparison.Ordinal);
        Assert.Contains("DiscountAmount", salesController, StringComparison.Ordinal);
        Assert.Contains("TaxRate", salesController, StringComparison.Ordinal);
        Assert.Contains("AccountingOutbox", salesController, StringComparison.Ordinal);
    }

    [Fact]
    public void PosRuntimePrinciples_MustBeExecutableInImplementation()
    {
        var salesController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/SalesController.cs");
        var salesReturnsController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/SalesReturnsController.cs");
        var stockMutationSupport = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Inventory/ProductVariantStockMutationSupport.cs");
        var sessionAuthority = CertificationSource.ReadRepoFile("PointOfSale.API/Security/PosSessionAuthority.cs");
        var financialIntegrity = CertificationSource.ReadRepoFile("PointOfSale.API/Services/SaleFinancialIntegrityService.cs");
        var accountingOutboxProcessor = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Accounting/AccountingOutboxProcessor.cs");
        var cashboxController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/CashboxController.cs");
        var tenantMiddleware = CertificationSource.ReadRepoFile("PointOfSale.API/Middleware/TenantResolutionMiddleware.cs");
        var tenantInterceptor = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/TenantSaveChangesInterceptor.cs");

        Assert.Contains("BeginTransactionAsync", salesController, StringComparison.Ordinal);
        Assert.Contains("BeginTransactionAsync", salesReturnsController, StringComparison.Ordinal);
        Assert.Contains("BuildSalesRequestHash", salesController, StringComparison.Ordinal);
        Assert.Contains("RequestHash", salesController, StringComparison.Ordinal);
        Assert.Contains("AccountingOutboxMessages", salesController, StringComparison.Ordinal);
        Assert.Contains("CriticalFinancialTruthEventTypes", accountingOutboxProcessor, StringComparison.Ordinal);
        Assert.Contains(".OrderBy(t => t.WarehouseId)", stockMutationSupport, StringComparison.Ordinal);
        Assert.Contains(".ThenBy(t => t.VariantId)", stockMutationSupport, StringComparison.Ordinal);
        Assert.Contains("ValidateAvailableQuantity", stockMutationSupport, StringComparison.Ordinal);
        Assert.Contains("TenantLineageAuthority.IsActiveTenantAsync", sessionAuthority, StringComparison.Ordinal);
        Assert.Contains("Deny(\"tenant_inactive_or_deleted\"", sessionAuthority, StringComparison.Ordinal);
        Assert.Contains("failed accounting outbox work", financialIntegrity, StringComparison.Ordinal);
        Assert.Contains("DO NOT use CurrentBalance", cashboxController, StringComparison.Ordinal);
        Assert.Contains("context.Request.Headers.Remove(TenantIdHeaderName)", tenantMiddleware, StringComparison.Ordinal);
        Assert.Contains("TenantId Mismatch detected", tenantInterceptor, StringComparison.Ordinal);
    }

    private static readonly string[] CanonicalRuntimeRoutes =
    {
        "path=\"pos\"",
        "path=\"sales-returns\"",
        "path=\"purchase-invoice\"",
        "path=\"purchase-returns\"",
        "path=\"products\"",
        "path=\"warehouses\"",
        "path=\"customers\"",
        "path=\"suppliers\"",
        "path=\"treasury\"",
        "path=\"ecommerce/orders\"",
        "path=\"admin/commercehub\"",
        "path=\"admin/affiliates\"",
        "path=\"marketplace\"",
        "path=\"storefront\"",
        "path=\"admin/shipping/setup\"",
        "path=\"settings\""
    };

    private static readonly string[] CanonicalAggregates =
    {
        "SalesInvoice",
        "PurchaseInvoice",
        "Return",
        "Cashbox",
        "Product",
        "ProductVariant",
        "Warehouse",
        "Customer",
        "Supplier",
        "CommerceHubListing",
        "AffiliateAccount",
        "StoreConfig"
    };

    private static string ReadProductionSource()
    {
        var root = DiscoverSolutionRoot(AppContext.BaseDirectory);
        var files = new[]
            {
                "PointOfSale.API",
                "PointOfSale.Core",
                "PointOfSale.Infrastructure"
            }
            .Select(project => Path.Combine(root, project))
            .SelectMany(projectPath => Directory.GetFiles(projectPath, "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(File.ReadAllText);

        return string.Join('\n', files);
    }

    private static string DiscoverSolutionRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root could not be found.");
    }
}
