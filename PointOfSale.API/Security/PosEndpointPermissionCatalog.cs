using System.Collections.Frozen;
using PointOfSale.Core.Security;

namespace PointOfSale.API.Security;

public static class PosEndpointPermissionCatalog
{
    private sealed record Rule(
        string Read,
        string Write,
        string? Delete = null,
        IReadOnlyDictionary<string, string>? Overrides = null)
    {
        public string Resolve(string actionName, IReadOnlyCollection<string> httpMethods)
        {
            if (Overrides?.TryGetValue(actionName, out var permission) == true)
            {
                return permission;
            }

            if (Delete != null &&
                (actionName.Contains("Delete", StringComparison.OrdinalIgnoreCase) ||
                 actionName.Contains("Remove", StringComparison.OrdinalIgnoreCase)))
            {
                return Delete;
            }

            return httpMethods.Count > 0 && httpMethods.All(IsReadMethod) ? Read : Write;
        }

        private static bool IsReadMethod(string method) =>
            method is "GET" or "HEAD" or "OPTIONS";
    }

    private static readonly FrozenDictionary<string, Rule> Rules =
        new Dictionary<string, Rule>(StringComparer.Ordinal)
        {
            ["Dashboard"] = R(PosPermissions.DashboardView),
            ["SyntheticQa"] = R(PosPermissions.StorefrontOrdersManage),

            ["Categories"] = CatalogRule(),
            ["ProductMedia"] = CatalogRule(),
            ["Products"] = CatalogRule(O(
                ("AdjustStock", PosPermissions.InventoryAdjust),
                ("CanDeleteProduct", PosPermissions.CatalogDelete),
                ("CanDeleteVariant", PosPermissions.CatalogDelete))),
            ["ProductVariants"] = CatalogRule(O(
                ("UpdateVariantStock", PosPermissions.InventoryAdjust))),
            ["Units"] = CatalogRule(),
            ["AdminAiProductGenerator"] = R(PosPermissions.CatalogView, PosPermissions.CatalogManage),
            ["AdminProductFeedTokens"] = R(PosPermissions.CatalogPublish),
            ["AdminSearchSync"] = R(PosPermissions.CatalogPublish),
            ["AdminSync"] = R(PosPermissions.CatalogPublish),
            ["SearchReindex"] = R(PosPermissions.CatalogPublish),
            ["AF16E_Reindex"] = R(PosPermissions.CatalogPublish),
            ["Upload"] = R(PosPermissions.CatalogManage),

            ["AdminInventory"] = R(PosPermissions.InventoryView, PosPermissions.InventoryAdjust,
                overrides: O(("ApproveAdjustment", PosPermissions.InventoryApprove))),
            ["Warehouses"] = R(PosPermissions.InventoryView, PosPermissions.InventoryAdjust,
                overrides: O(
                    ("CreateWarehouse", PosPermissions.InventoryApprove),
                    ("UpdateWarehouse", PosPermissions.InventoryApprove),
                    ("GetWarehouseAddressPlaces", PosPermissions.InventoryApprove),
                    ("AdmitWarehouseAddress", PosPermissions.InventoryApprove),
                    ("TransferStock", PosPermissions.InventoryTransfer))),
            ["StockTransfers"] = R(PosPermissions.InventoryView, PosPermissions.InventoryTransfer,
                overrides: O(
                    ("ApproveTransfer", PosPermissions.InventoryApprove),
                    ("RejectTransfer", PosPermissions.InventoryApprove))),

            ["Customers"] = R(PosPermissions.CustomersView, PosPermissions.CustomersManage,
                overrides: Balance(
                    PosPermissions.CustomersBalanceManage,
                    "GetCustomerPayments", "CreateCustomerPayment",
                    "GetCustomerOpeningBalances", "SaveCustomerOpeningBalances")),
            ["Suppliers"] = R(PosPermissions.SuppliersView, PosPermissions.SuppliersManage,
                overrides: Balance(
                    PosPermissions.SuppliersBalanceManage,
                    "GetSupplierPayments", "CreateSupplierPayment",
                    "GetSupplierOpeningBalances", "SaveSupplierOpeningBalances")),
            ["SupplierCardFundingInstruments"] = R(
                PosPermissions.SuppliersView,
                PosPermissions.SuppliersBalanceManage),

            ["PosCarts"] = R(PosPermissions.SalesView, PosPermissions.SalesOperate,
                overrides: O(("GetWarehouseOptions", PosPermissions.SalesOperate))),
            ["Sales"] = R(PosPermissions.SalesView, PosPermissions.SalesOperate, PosPermissions.SalesDelete,
                O(
                    ("CreateReturn", PosPermissions.SalesRefund),
                    ("ReturnSale", PosPermissions.SalesRefund),
                    ("RefundSale", PosPermissions.SalesRefund))),
            ["SalesReturns"] = R(PosPermissions.SalesView, PosPermissions.SalesRefund, PosPermissions.SalesDelete),
            ["PosAdminReturns"] = R(PosPermissions.SalesView, PosPermissions.SalesRefund),
            ["SalesRepresentatives"] = R(PosPermissions.ReportsOperationalView, PosPermissions.SalesOperate),

            ["Purchases"] = R(PosPermissions.PurchasesView, PosPermissions.PurchasesManage,
                overrides: O(("CanDeletePurchase", PosPermissions.PurchasesManage))),
            ["PurchaseReturns"] = R(PosPermissions.PurchasesView, PosPermissions.PurchasesReturn),

            ["Banks"] = FinanceAdminRule(),
            ["FinancialAccounts"] = R(PosPermissions.FinanceView, PosPermissions.FinanceAdmin,
                overrides: O(
                    ("Transfer", PosPermissions.FinanceOperate),
                    ("TransferBalance", PosPermissions.FinanceOperate))),
            ["ExpenseCategories"] = FinanceAdminRule(),
            ["Expenses"] = R(PosPermissions.FinanceView, PosPermissions.FinanceOperate),
            ["Cashbox"] = R(PosPermissions.FinanceView, PosPermissions.FinanceOperate,
                overrides: InitializeOverrides()),
            ["StatementOfAccount"] = R(PosPermissions.FinanceView, PosPermissions.FinanceOperate),
            ["Treasury"] = R(PosPermissions.FinanceView, PosPermissions.FinanceOperate,
                overrides: InitializeOverrides()),
            ["TreasuryInitialCapitalizations"] = R(PosPermissions.FinanceInitialize),
            ["CarrierRemittances"] = R(PosPermissions.StorefrontPaymentsManage),
            ["AdminAccounting"] = R(PosPermissions.FinanceView, PosPermissions.FinanceAdmin),
            ["LegacyBankStatement"] = R(PosPermissions.ReportsFinancialView),
            ["LegacyVisaStatement"] = R(PosPermissions.ReportsFinancialView),

            ["Reports"] = R(PosPermissions.ReportsOperationalView),
            ["SalesRepReports"] = R(PosPermissions.ReportsOperationalView),
            ["FinancialReports"] = R(PosPermissions.ReportsFinancialView),

            ["Orders"] = StorefrontOrdersRule(),
            ["AdminOrders"] = StorefrontOrdersRule(O(
                ("DownloadCheckoutAttachment", PosPermissions.StorefrontOrderAttachmentsDownload))),
            ["AdminAbandonedCheckouts"] = StorefrontOrdersRule(),
            ["AdminAbandonedCartRecovery"] = StorefrontOrdersRule(),
            ["OnlineCustomersAdmin"] = StorefrontOrdersRule(),
            ["OnlineCustomerSessionsAdmin"] = StorefrontOrdersRule(),
            ["OrderFulfillment"] = R(PosPermissions.StorefrontOrdersView, PosPermissions.ShippingFulfill),
            ["AdminPhones"] = StorefrontOrdersRule(),
            ["AdminProviderDelivery"] = R(PosPermissions.ShippingView, PosPermissions.ShippingFulfill),

            ["PaymentMethodsAdmin"] = R(PosPermissions.StorefrontPaymentsView, PosPermissions.StorefrontPaymentsManage),
            ["AdminManualPayment"] = R(PosPermissions.StorefrontPaymentsView, PosPermissions.StorefrontPaymentsManage,
                overrides: O(
                    ("Refund", PosPermissions.StorefrontRefundsManage),
                    ("RefundPayment", PosPermissions.StorefrontRefundsManage))),
            ["PaymentLifecycle"] = R(PosPermissions.StorefrontPaymentsView, PosPermissions.StorefrontPaymentsManage,
                overrides: O(
                    ("GetManualSettlementEligibility", PosPermissions.StorefrontPaymentsManage),
                    ("GetManualWalletSettlementEligibility", PosPermissions.StorefrontPaymentsManage),
                    ("GetManualWalletSettlement", PosPermissions.StorefrontPaymentsManage),
                    ("GetManualCodSettlement", PosPermissions.StorefrontPaymentsManage),
                    ("Refund", PosPermissions.StorefrontRefundsManage),
                    ("RefundPayment", PosPermissions.StorefrontRefundsManage))),

            ["Coupons"] = R(PosPermissions.StorefrontPromotionsManage),
            ["Promotions"] = R(PosPermissions.StorefrontPromotionsManage),
            ["AdminSmartOffers"] = R(PosPermissions.StorefrontPromotionsManage),

            ["AdminAnalytics"] = AnalyticsRule(),
            ["AdminAnalyticsAlerts"] = AnalyticsRule(),
            ["AdminAnalyticsAttribution"] = AnalyticsRule(),
            ["AdminAnalyticsFunnels"] = AnalyticsRule(),
            ["AdminAnalyticsSessions"] = AnalyticsRule(),
            ["AdminCampaignCost"] = AnalyticsRule(),
            ["AdminPixelTracking"] = AnalyticsRule(),
            ["AdminStorefrontAnalyticsDashboard"] = AnalyticsRule(),
            ["AdminStorefrontLiveMonitoring"] = AnalyticsRule(),
            ["WishlistAnalyticsAdmin"] = AnalyticsRule(),

            ["AdminCheckoutFieldDefinitions"] = StorefrontDesignRule(),
            ["AdminCmsPages"] = StorefrontDesignRule(),
            ["AdminCommerceAi"] = StorefrontDesignRule(),
            ["AdminDataSync"] = R(PosPermissions.SettingsView, PosPermissions.SettingsManage),
            ["AdminDomains"] = R(PosPermissions.StorefrontDomainsManage),
            ["AdminGeographicAdministration"] = R(PosPermissions.ShippingManage),
            ["AdminBostaGeography"] = R(PosPermissions.ShippingManage),
            ["AdminBostaSetup"] = R(PosPermissions.ShippingManage),
            ["AdminBostaDestinations"] = R(PosPermissions.ShippingManage, overrides: O(("Save", PosPermissions.ShippingFulfill))),
            ["AdminHelpAssistant"] = R(PosPermissions.SettingsView),
            ["AdminStoreConfig"] = StorefrontDesignRule(),
            ["AdminStorefrontFooter"] = StorefrontDesignRule(),
            ["HomepageMedia"] = StorefrontDesignRule(),
            ["StorefrontAuthProvidersAdmin"] = R(PosPermissions.StorefrontAuthManage),

            ["ShippingMethods"] = R(PosPermissions.ShippingManage),
            ["AdminCarrierConfig"] = R(PosPermissions.ShippingManage),
            ["AdminShippingAdministration"] = R(PosPermissions.ShippingManage),
            ["AdminShipments"] = R(PosPermissions.ShippingView, PosPermissions.ShippingFulfill),
            ["Shipment"] = R(PosPermissions.ShippingView, PosPermissions.ShippingFulfill),

            ["AdminAffiliate"] = R(PosPermissions.AffiliatesManage),
            ["AdminAffiliateCategory"] = R(PosPermissions.AffiliatesManage),
            ["AdminAffiliateProducts"] = R(PosPermissions.AffiliatesManage),
            ["AdminAffiliateReadiness"] = R(PosPermissions.AffiliatesManage),
            ["AdminProductAffiliate"] = R(PosPermissions.AffiliatesManage),
            ["AffiliatePayoutEvidence"] = R(PosPermissions.AffiliatesPayoutManage),

            ["AdminCommerceHub"] = R(PosPermissions.CatalogPublish),
            ["AdminCommerceHubFinance"] = R(PosPermissions.ReportsFinancialView),
            ["AdminCommerceHubPayments"] = R(PosPermissions.StorefrontPaymentsView, PosPermissions.StorefrontPaymentsManage),
            ["CommerceHubOrders"] = StorefrontOrdersRule(O(
                ("GetVendorEarnings", PosPermissions.ReportsFinancialView))),
            ["CommerceHubPayouts"] = R(PosPermissions.StorefrontPaymentsView, PosPermissions.StorefrontPaymentsManage),
            ["CommerceHubRefunds"] = R(PosPermissions.StorefrontPaymentsView, PosPermissions.StorefrontRefundsManage),
            ["VendorPayoutMethods"] = R(PosPermissions.StorefrontPaymentsView, PosPermissions.StorefrontPaymentsManage),

            // Settings GET returns operational display data (currency, receipt, company)
            // needed by every POS operator, so read requires dashboard.view which all
            // staff roles hold. Writes remain restricted to settings.manage.
            ["Settings"] = R(PosPermissions.DashboardView, PosPermissions.SettingsManage),
            ["AuditLogs"] = R(PosPermissions.AuditView, PosPermissions.AuditView,
                overrides: O(("ExportAuditLogs", PosPermissions.AuditExport))),
            ["WhatsAppAdmin"] = R(PosPermissions.StorefrontOrdersView, PosPermissions.StorefrontOrdersManage),
            ["AdminAiExplanation"] = R(PosPermissions.StorefrontAnalyticsView),
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public static IReadOnlySet<string> ClassifiedControllers => Rules.Keys.ToFrozenSet(StringComparer.Ordinal);

    public static bool TryResolve(
        string controllerName,
        string actionName,
        IReadOnlyCollection<string> httpMethods,
        out string permission)
    {
        if (Rules.TryGetValue(controllerName, out var rule))
        {
            permission = rule.Resolve(actionName, httpMethods);
            return PosPermissions.All.Contains(permission);
        }

        permission = string.Empty;
        return false;
    }

    private static Rule R(
        string read,
        string? write = null,
        string? delete = null,
        IReadOnlyDictionary<string, string>? overrides = null) =>
        new(read, write ?? read, delete, overrides);

    private static Rule CatalogRule(IReadOnlyDictionary<string, string>? additionalOverrides = null) => R(
        PosPermissions.CatalogView,
        PosPermissions.CatalogManage,
        PosPermissions.CatalogDelete,
        MergeOverrides(additionalOverrides,
            ("Publish", PosPermissions.CatalogPublish),
            ("PublishProduct", PosPermissions.CatalogPublish),
            ("Unpublish", PosPermissions.CatalogPublish),
            ("UnpublishProduct", PosPermissions.CatalogPublish)));

    private static Rule FinanceAdminRule() => R(
        PosPermissions.FinanceView,
        PosPermissions.FinanceAdmin,
        overrides: InitializeOverrides());

    private static Rule StorefrontOrdersRule(IReadOnlyDictionary<string, string>? additionalOverrides = null) => R(
        PosPermissions.StorefrontOrdersView,
        PosPermissions.StorefrontOrdersManage,
        overrides: MergeOverrides(additionalOverrides,
            ("Refund", PosPermissions.StorefrontRefundsManage),
            ("RefundOrder", PosPermissions.StorefrontRefundsManage),
            ("ApproveRefund", PosPermissions.StorefrontRefundsManage)));

    private static Rule AnalyticsRule() => R(
        PosPermissions.StorefrontAnalyticsView,
        PosPermissions.StorefrontAnalyticsConfigure);

    private static Rule StorefrontDesignRule() => R(PosPermissions.StorefrontDesignManage);

    private static IReadOnlyDictionary<string, string> InitializeOverrides() => O(
        ("SetOpeningBalance", PosPermissions.FinanceInitialize),
        ("SetOpeningBalances", PosPermissions.FinanceInitialize),
        ("Initialize", PosPermissions.FinanceInitialize),
        ("InitializeCapital", PosPermissions.FinanceInitialize));

    private static IReadOnlyDictionary<string, string> Balance(string permission, params string[] additionalActions) =>
        new[] { "SetOpeningBalance", "AddPayment", "RecordPayment", "AdjustBalance" }
            .Concat(additionalActions)
            .Distinct(StringComparer.Ordinal)
            .ToFrozenDictionary(action => action, _ => permission, StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, string> MergeOverrides(
        IReadOnlyDictionary<string, string>? additionalOverrides,
        params (string Action, string Permission)[] values)
    {
        var overrides = values.ToDictionary(value => value.Action, value => value.Permission, StringComparer.Ordinal);
        if (additionalOverrides != null)
        {
            foreach (var (action, permission) in additionalOverrides)
            {
                overrides[action] = permission;
            }
        }

        return overrides.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<string, string> O(params (string Action, string Permission)[] values) =>
        values.ToFrozenDictionary(value => value.Action, value => value.Permission, StringComparer.Ordinal);
}
