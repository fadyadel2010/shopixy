using System.Collections.Frozen;
using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Security;

public static class PosPermissions
{
    public const string DashboardView = "dashboard.view";
    public const string CatalogView = "catalog.view";
    public const string CatalogManage = "catalog.manage";
    public const string CatalogDelete = "catalog.delete";
    public const string CatalogPublish = "catalog.publish";
    public const string InventoryView = "inventory.view";
    public const string InventoryAdjust = "inventory.adjust";
    public const string InventoryTransfer = "inventory.transfer";
    public const string InventoryApprove = "inventory.approve";
    public const string CustomersView = "customers.view";
    public const string CustomersManage = "customers.manage";
    public const string CustomersBalanceManage = "customers.balance.manage";
    public const string SuppliersView = "suppliers.view";
    public const string SuppliersManage = "suppliers.manage";
    public const string SuppliersBalanceManage = "suppliers.balance.manage";
    public const string SalesView = "sales.view";
    public const string SalesOperate = "sales.operate";
    public const string SalesRefund = "sales.refund";
    public const string SalesDelete = "sales.delete";
    public const string PurchasesView = "purchases.view";
    public const string PurchasesManage = "purchases.manage";
    public const string PurchasesReturn = "purchases.return";
    public const string FinanceView = "finance.view";
    public const string FinanceOperate = "finance.operate";
    public const string FinanceAdmin = "finance.admin";
    public const string FinanceInitialize = "finance.initialize";
    public const string ReportsOperationalView = "reports.operational.view";
    public const string ReportsFinancialView = "reports.financial.view";
    public const string StorefrontOrdersView = "storefront.orders.view";
    public const string StorefrontOrdersManage = "storefront.orders.manage";
    public const string StorefrontPaymentsView = "storefront.payments.view";
    public const string StorefrontPaymentsManage = "storefront.payments.manage";
    public const string StorefrontRefundsManage = "storefront.refunds.manage";
    public const string StorefrontPromotionsManage = "storefront.promotions.manage";
    public const string StorefrontAnalyticsView = "storefront.analytics.view";
    public const string StorefrontAnalyticsConfigure = "storefront.analytics.configure";
    public const string StorefrontDesignManage = "storefront.design.manage";
    public const string StorefrontDomainsManage = "storefront.domains.manage";
    public const string StorefrontAuthManage = "storefront.auth.manage";
    public const string ShippingView = "shipping.view";
    public const string ShippingManage = "shipping.manage";
    public const string ShippingFulfill = "shipping.fulfill";
    public const string AffiliatesManage = "affiliates.manage";
    public const string AffiliatesPayoutManage = "affiliates.payout.manage";
    public const string SettingsView = "settings.view";
    public const string SettingsManage = "settings.manage";
    public const string AuditView = "audit.view";
    public const string AuditExport = "audit.export";
    public const string UsersView = "users.view";
    public const string UsersInvite = "users.invite";
    public const string UsersProfileManage = "users.profile.manage";
    public const string UsersRoleManage = "users.role.manage";
    public const string UsersStatusManage = "users.status.manage";
    public const string UsersPasswordResetInitiate = "users.password-reset.initiate";
    public const string UsersSessionRevoke = "users.session.revoke";
    public const string UsersOwnershipTransfer = "users.ownership.transfer";

    public static readonly FrozenSet<string> All = new[]
    {
        DashboardView,
        CatalogView, CatalogManage, CatalogDelete, CatalogPublish,
        InventoryView, InventoryAdjust, InventoryTransfer, InventoryApprove,
        CustomersView, CustomersManage, CustomersBalanceManage,
        SuppliersView, SuppliersManage, SuppliersBalanceManage,
        SalesView, SalesOperate, SalesRefund, SalesDelete,
        PurchasesView, PurchasesManage, PurchasesReturn,
        FinanceView, FinanceOperate, FinanceAdmin, FinanceInitialize,
        ReportsOperationalView, ReportsFinancialView,
        StorefrontOrdersView, StorefrontOrdersManage,
        StorefrontPaymentsView, StorefrontPaymentsManage, StorefrontRefundsManage,
        StorefrontPromotionsManage,
        StorefrontAnalyticsView, StorefrontAnalyticsConfigure,
        StorefrontDesignManage, StorefrontDomainsManage, StorefrontAuthManage,
        ShippingView, ShippingManage, ShippingFulfill,
        AffiliatesManage, AffiliatesPayoutManage,
        SettingsView, SettingsManage,
        AuditView, AuditExport,
        UsersView, UsersInvite, UsersProfileManage, UsersRoleManage, UsersStatusManage,
        UsersPasswordResetInitiate, UsersSessionRevoke, UsersOwnershipTransfer
    }.ToFrozenSet(StringComparer.Ordinal);
}

public static class PosRolePermissionCatalog
{
    private static readonly FrozenDictionary<UserRole, FrozenSet<string>> Grants =
        new Dictionary<UserRole, FrozenSet<string>>
        {
            [UserRole.Owner] = PosPermissions.All,
            [UserRole.Admin] = Except(PosPermissions.FinanceInitialize, PosPermissions.UsersOwnershipTransfer),
            [UserRole.Manager] = Set(
                PosPermissions.DashboardView,
                PosPermissions.CatalogView, PosPermissions.CatalogManage, PosPermissions.CatalogPublish,
                PosPermissions.InventoryView, PosPermissions.InventoryAdjust, PosPermissions.InventoryTransfer, PosPermissions.InventoryApprove,
                PosPermissions.CustomersView, PosPermissions.CustomersManage, PosPermissions.CustomersBalanceManage,
                PosPermissions.SuppliersView, PosPermissions.SuppliersManage, PosPermissions.SuppliersBalanceManage,
                PosPermissions.SalesView, PosPermissions.SalesOperate, PosPermissions.SalesRefund,
                PosPermissions.PurchasesView, PosPermissions.PurchasesManage, PosPermissions.PurchasesReturn,
                PosPermissions.FinanceView, PosPermissions.FinanceOperate,
                PosPermissions.ReportsOperationalView,
                PosPermissions.StorefrontOrdersView, PosPermissions.StorefrontOrdersManage,
                PosPermissions.StorefrontPaymentsView,
                PosPermissions.StorefrontPromotionsManage,
                PosPermissions.StorefrontAnalyticsView,
                PosPermissions.ShippingView, PosPermissions.ShippingManage, PosPermissions.ShippingFulfill),
            [UserRole.Cashier] = Set(
                PosPermissions.DashboardView,
                PosPermissions.CatalogView,
                PosPermissions.CustomersView, PosPermissions.CustomersManage,
                PosPermissions.SalesView, PosPermissions.SalesOperate),
            [UserRole.Warehouse] = Set(
                PosPermissions.DashboardView,
                PosPermissions.CatalogView,
                PosPermissions.InventoryView, PosPermissions.InventoryAdjust, PosPermissions.InventoryTransfer,
                PosPermissions.PurchasesView,
                PosPermissions.ShippingView, PosPermissions.ShippingFulfill)
        }.ToFrozenDictionary();

    public static IReadOnlySet<string> GetPermissions(UserRole role) =>
        Grants.TryGetValue(role, out var permissions)
            ? permissions
            : FrozenSet<string>.Empty;

    public static bool IsGranted(UserRole role, string permission) =>
        PosPermissions.All.Contains(permission) &&
        Grants.TryGetValue(role, out var permissions) &&
        permissions.Contains(permission);

    public static bool TryParseKnownRole(string? value, out UserRole role)
    {
        if (!Enum.TryParse(value, ignoreCase: false, out role) ||
            !Enum.IsDefined(role) ||
            !Grants.ContainsKey(role))
        {
            role = default;
            return false;
        }

        return true;
    }

    private static FrozenSet<string> Except(params string[] denied) =>
        PosPermissions.All.Except(denied, StringComparer.Ordinal).ToFrozenSet(StringComparer.Ordinal);

    private static FrozenSet<string> Set(params string[] permissions) =>
        permissions.ToFrozenSet(StringComparer.Ordinal);
}
