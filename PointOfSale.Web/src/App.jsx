import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom'
import ProtectedRoute from './components/ProtectedRoute'
import PosRoleRouteGuard from './components/PosRoleRouteGuard'
import { ToastProvider } from './components/Toast'
import Login from './pages/Login'
import Register from './pages/Register'
import VerifyEmail from './pages/VerifyEmail'
import Dashboard from './pages/Dashboard'
import Products from './pages/Products'
import ProductWorkspacePage from './pages/ProductWorkspacePage'
import Categories from './pages/Categories'
import Units from './pages/Units'
import Customers from './pages/Customers'
import Suppliers from './pages/Suppliers'
import POS from './pages/POS'
import SalesHistory from './pages/SalesHistory'
import PurchaseInvoice from './pages/PurchaseInvoice'
import PurchaseReturns from './pages/PurchaseReturns'
import SalesReturns from './pages/SalesReturns'
import SalesRepresentatives from './pages/SalesRepresentatives'
import SalesRepReports from './pages/SalesRepReports'
import Warehouses from './pages/Warehouses'
import Inventory from './pages/Inventory'
// PHASE LTSR: Legacy Cashbox screen retired - redirects to Ledger
// import Cashbox from './pages/Cashbox'
import StatementOfAccount from './pages/StatementOfAccount'
import Payments from './pages/Payments'
import OutstandingBalances from './pages/OutstandingBalances'
import Adjustments from './pages/Adjustments'
import StockTransfers from './pages/StockTransfers'
import FinancialAccounts from './pages/FinancialAccounts'
import BalanceTransfer from './pages/BalanceTransfer'
import CashboxOpeningBalance from './pages/CashboxOpeningBalance'
import CustomerOpeningBalance from './pages/CustomerOpeningBalance'
import SupplierOpeningBalance from './pages/SupplierOpeningBalance'
import WarehouseOpeningStock from './pages/WarehouseOpeningStock'
import Banks from './pages/Banks'
// PHASE LTSR: Legacy BankStatement retired - redirects to Ledger
// import BankStatement from './pages/BankStatement'
import ExpenseCategories from './pages/ExpenseCategories'
import Expenses from './pages/Expenses'
import ExpenseReport from './pages/ExpenseReport'
// PHASE LTSR: Legacy TreasuryMovements & VisaAccountStatement retired - redirects to Ledger
// import TreasuryMovements from './pages/TreasuryMovements'
// import VisaAccountStatement from './pages/VisaAccountStatement'
import SalesAnalysis from './pages/SalesAnalysis'
import SalesSummaryReport from './pages/SalesSummaryReport'
import SalesByCategoryReport from './pages/SalesByCategoryReport'
import SalesByPaymentMethodReport from './pages/SalesByPaymentMethodReport'
import InventoryValuation from './pages/InventoryValuation'
import DailyCloseReport from './pages/DailyCloseReport'
import ProductMovementReport from './pages/ProductMovementReport'
import AccountsReceivableAging from './pages/AccountsReceivableAging'
import AccountsPayableAging from './pages/AccountsPayableAging'
import SalesReturnReport from './pages/SalesReturnReport'
import PurchaseReturnReport from './pages/PurchaseReturnReport'
import PurchaseRegisterReport from './pages/PurchaseRegisterReport'
import PurchasesBySupplierReport from './pages/PurchasesBySupplierReport'
import CostAnalysisReport from './pages/CostAnalysisReport'
// REMOVED: Legacy financial report imports (Phase C2 - Domain Consolidation)
// TrialBalance, IncomeStatement, BalanceSheet now exclusively in /treasury/*
import BarcodePrinting from './pages/BarcodePrinting'
import Settings from './pages/Settings'
import AuditLog from './pages/AuditLog'
import ResetDatabase from './pages/ResetDatabase'
// E-commerce pages
import PixelTrackingConfigPage from './modules/storefront/config/PixelTrackingConfigPage'
import ShippingMethods from './pages/ShippingMethods'
// Commerce AI Admin Control (Phase 6)
import CommerceAI from './pages/CommerceAI'
import CommerceHubSettings from './pages/CommerceHubSettings'
import CommerceHubFinance from './pages/CommerceHubFinance'
import CommerceHubEarnings from './pages/CommerceHubEarnings'
import CommerceHubPayments from './pages/CommerceHubPayments'
import PaymentMethods from './pages/PaymentMethods'
import ManualPayments from './pages/ManualPayments'
import Coupons from './pages/Coupons'
import Promotions from './pages/Promotions'
import SmartOffersPage from './pages/SmartOffersPage'
import Orders from './pages/Orders'
// Online Customers (Phase P1 - Storefront Customer Management)
import { OnlineCustomersPage } from './modules/onlineCustomers'
// Storefront Builder
import StorefrontDashboard from './pages/StorefrontDashboard'
import StorefrontSettings from './pages/StorefrontSettings'
import { StorefrontBuilderPage, ThemeMarketplacePage, ThemeEditorPage, VersionHistoryPage, AuthProvidersPage } from './modules/storefront/pages'
// CMS Pages & Navigation (Phase 9.11)
import { CmsPagesList, CmsPageEditor, NavigationEditor, StorefrontFooterSettings } from './modules/storefront/cms'
import { StorefrontAdminFrame } from './modules/storefront/informationArchitecture'
// Analytics Funnels (Phase 5A)
import { FunnelsListPage, FunnelDetailsPage } from './modules/analytics'
// Analytics Attribution (Phase 8A) & Campaign Cost & ROI (Phase 8B)
import { AttributionOverviewPage, AttributionModelsPage, AttributionOrdersPage, CampaignCostsPage, RoiDashboardPage } from './modules/analytics'
// Analytics Settings (Phase 6: External Analytics Integration)
import AnalyticsSettings from './pages/AnalyticsSettings'
// Shipping Admin Dashboard (Shipping Frontend Phase + Unified Configuration)
import { ShipmentsListPage, ShipmentDetailsPage, ShippingHealthPage } from './modules/shipping'
import ShippingAdministrationWorkspace from './modules/shipping-administration/ShippingAdministrationWorkspace'
// WhatsApp Admin (Phase 14)
import { WhatsAppSettings } from './modules/whatsapp'
// Smart Cart Recovery (Abandoned Cart Hardening)
import AbandonedCartRecovery from './pages/AbandonedCartRecovery'
// Abandoned Checkouts (Lost Orders Phase)
import AbandonedCheckouts from './pages/AbandonedCheckouts'
import BlockedPhones from './pages/BlockedPhones'
// Storefront Operations Overview (Wave 1 - Information Architecture Migration)
import OperationsOverview from './pages/OperationsOverview'
// Treasury & Finance Control Center (Phase 5 + Phase FTM)
import {
    TreasuryDashboard,
    TreasuryTrialBalance,
    TreasuryProfitLoss,
    TreasuryBalanceSheet,
    TreasuryCashFlow,
    TreasuryGeneralLedger,
    TreasuryClearingAging,
    TreasuryTransfers,
    TreasuryHub,
    TreasuryAccountViewer
} from './pages/treasury'
// Services Marketplace (Phase P2 - Full Tab System)
import { MarketplaceShell } from './marketplace/pages/MarketplaceShell'
import { ServiceDetailsPage } from './marketplace/pages/ServiceDetailsPage'
import { ChatPage } from './marketplace/pages/ChatPage'
import { MarketplaceAuthorityBoundary } from './marketplace/components/MarketplaceAuthorityBoundary'
// Affiliate (AF-4B Admin Management — POS-only, via module index)
import { AdminAffiliatesList, AdminAffiliateDetail } from './modules/affiliate'
// AF-10B: Public affiliate hub pages — NOT exported from module index (admin-only)
// These are standalone pages outside the POS Layout, served from the same monolith.
import AffiliateLanding from './modules/affiliate/pages/AffiliateLanding'
import AffiliateLogin from './modules/affiliate/pages/AffiliateLogin'
import AffiliateRegister from './modules/affiliate/pages/AffiliateRegister'
import AffiliatePortal from './modules/affiliate/pages/AffiliatePortal'
import AffiliateProductDetail from './modules/affiliate/pages/AffiliateProductDetail'
import AffiliateCategoryPage from './modules/affiliate/pages/AffiliateCategoryPage'
import AffiliateProviders from './modules/affiliate/components/AffiliateProviders'

import Layout from './components/Layout'
import { SettingsProvider } from './contexts/SettingsContext'
import { QueryClientProvider } from '@tanstack/react-query'
import SuperAdminProtectedRoute from './super-admin/auth/SuperAdminProtectedRoute'
import SuperAdminCapabilityRoute from './super-admin/auth/SuperAdminCapabilityRoute'
import CommerceHubVendorRouteGuard from './components/CommerceHubVendorRouteGuard'
import SuperAdminLoginPage from './super-admin/auth/SuperAdminLoginPage'
import SuperAdminLayout from './super-admin/layout/SuperAdminLayout'
import FinanceDashboardPage from './super-admin/finance/FinanceDashboardPage'
import CommissionManagementPage from './super-admin/commission/CommissionManagementPage'
import PromoCodesPage from './super-admin/promo/PromoCodesPage'
import OrdersPage from './super-admin/orders/OrdersPage'
import ReviewModerationPage from './super-admin/reviews/ReviewModerationPage'
import RefundsPage from './super-admin/refunds/RefundsPage'
import PaymentsPage from './super-admin/payments/PaymentsPage'
import PaymentProvidersPage from './super-admin/payment-providers/PaymentProvidersPage'
import PayoutsPage from './super-admin/payouts/PayoutsPage'
import UsersPage from './super-admin/users/UsersPage'
import MarketplaceGovernancePage from './super-admin/marketplace-governance/MarketplaceGovernancePage'
import MarketplaceOperationsPage from './super-admin/marketplace-operations/MarketplaceOperationsPage'
import HomepageBuilderPage from './super-admin/homepage/HomepageBuilderPage'
import HomepageManagerPage from './super-admin/homepage/HomepageManagerPage'
import { MarketplaceCmsPagesPage } from './super-admin/marketplace-cms/MarketplaceCmsPagesPage'
import { MarketplaceCmsPageEditor } from './super-admin/marketplace-cms/MarketplaceCmsPageEditor'
import { MarketplaceFooterSettingsPage } from './super-admin/marketplace-cms/MarketplaceFooterSettingsPage'
import { StorefrontFooterDefaultsPage } from './super-admin/storefront-footer/StorefrontFooterDefaultsPage'
import { MarketplaceCmsPublicPage } from './marketplace-cms/MarketplaceCmsPublicPage'
import { MarketplaceProductPublicPage } from './marketplace-cms/MarketplaceProductPublicPage'
import { MarketplaceProductsPublicPage } from './marketplace-cms/MarketplaceProductsPublicPage'
import AppErrorBoundary from './components/AppErrorBoundary'
import AuthBootstrap from './components/AuthBootstrap'
import { queryClient } from './queryClient'
import { MarketplaceQueryBoundary, SuperAdminQueryBoundary } from './components/RuntimeQueryBoundaries'

function App() {

    return (
        <AppErrorBoundary>
            <QueryClientProvider client={queryClient}>
                <ToastProvider>
                    <AuthBootstrap />
                    <SettingsProvider>
                        <BrowserRouter>
                            <Routes>
                                <Route path="/login" element={<Login />} />
                                <Route path="/register" element={<Register />} />
                                <Route path="/super-admin/login" element={<SuperAdminLoginPage />} />
                                {/* Phase 8.4: Public email verification route */}
                                <Route path="/verify-email" element={<VerifyEmail />} />
                                <Route path="/:lang/products" element={<MarketplaceProductsPublicPage />} />
                                <Route path="/:lang/product/:productId" element={<MarketplaceProductPublicPage />} />
                                <Route path="/:lang/:slug" element={<MarketplaceCmsPublicPage />} />

                                {/* AF-10B: Public affiliate hub — outside POS Layout, no POS auth required */}
                                {/* These routes are NOT in the POS sidebar — they're the external affiliate experience */}
                                <Route path="/affiliate" element={<AffiliateProviders />}>
                                    <Route index element={<AffiliateLanding />} />
                                    <Route path="login" element={<AffiliateLogin />} />
                                    <Route path="register" element={<AffiliateRegister />} />
                                    <Route path="portal" element={<AffiliatePortal />} />
                                    <Route path="products/:slug" element={<AffiliateProductDetail />} />
                                    {/* AF-31: Category products page */}
                                    <Route path="category/:slug" element={<AffiliateCategoryPage />} />
                                    {/* Catch-all: keep unknown /affiliate/* URLs in the affiliate context */}
                                    <Route path="*" element={<Navigate to="/affiliate" replace />} />
                                </Route>

                                <Route element={<SuperAdminQueryBoundary><SuperAdminProtectedRoute /></SuperAdminQueryBoundary>}>
                                    <Route path="/super-admin" element={<SuperAdminLayout />}>
                                        <Route index element={<Navigate to="finance" replace />} />
                                        <Route element={<SuperAdminCapabilityRoute capability="super-admin.finance.view" />}>
                                            <Route path="finance" element={<FinanceDashboardPage />} />
                                        </Route>
                                        <Route path="vendors/finance" element={<Navigate to="/super-admin/finance" replace />} />
                                        <Route element={<SuperAdminCapabilityRoute capability="super-admin.orders.view" />}><Route path="orders" element={<OrdersPage />} /></Route>
                                        <Route element={<SuperAdminCapabilityRoute capability="super-admin.reviews.view" />}><Route path="reviews" element={<ReviewModerationPage />} /></Route>
                                        <Route element={<SuperAdminCapabilityRoute capability="super-admin.users.govern" />}><Route path="users" element={<UsersPage />} /></Route>
                                        <Route element={<SuperAdminCapabilityRoute capability="super-admin.marketplace.govern" />}><Route path="marketplace/governance" element={<MarketplaceGovernancePage />} /></Route>
                                        <Route element={<SuperAdminCapabilityRoute capability="super-admin.marketplace.operate" />}><Route path="marketplace/operations" element={<MarketplaceOperationsPage />} /></Route>
                                        <Route element={<SuperAdminCapabilityRoute capability="super-admin.payments.view" />}><Route path="payments" element={<PaymentsPage />} /></Route>
                                        <Route element={<SuperAdminCapabilityRoute capability="super-admin.payment-providers.view" />}><Route path="commercehub/payment-providers" element={<PaymentProvidersPage />} /></Route>
                                        <Route element={<SuperAdminCapabilityRoute capability="super-admin.refunds.manage" />}><Route path="refunds" element={<RefundsPage />} /></Route>
                                        <Route element={<SuperAdminCapabilityRoute capability="super-admin.payouts.manage" />}><Route path="payouts" element={<PayoutsPage />} /></Route>
                                        <Route element={<SuperAdminCapabilityRoute capability="super-admin.commission.manage" />}><Route path="commission" element={<CommissionManagementPage />} /></Route>
                                        <Route element={<SuperAdminCapabilityRoute capability="super-admin.promotions.manage" />}><Route path="promo-codes" element={<PromoCodesPage />} /></Route>
                                        <Route element={<SuperAdminCapabilityRoute capability="super-admin.homepage.manage" />}><Route path="homepage" element={<HomepageBuilderPage />} /></Route>
                                        <Route element={<SuperAdminCapabilityRoute capability="super-admin.homepage.manage" />}><Route path="homepage/manager" element={<HomepageManagerPage />} /></Route>
                                        <Route element={<SuperAdminCapabilityRoute capability="super-admin.storefront-defaults.manage" />}><Route path="storefront/footer" element={<StorefrontFooterDefaultsPage />} /></Route>
                                        <Route element={<SuperAdminCapabilityRoute capability="super-admin.marketplace-cms.manage" />}>
                                            <Route path="marketplace/pages" element={<MarketplaceCmsPagesPage />} />
                                            <Route path="marketplace/pages/:pageId" element={<MarketplaceCmsPageEditor />} />
                                        </Route>
                                        <Route element={<SuperAdminCapabilityRoute capability="super-admin.marketplace-footer.manage" />}><Route path="marketplace/footer" element={<MarketplaceFooterSettingsPage />} /></Route>
                                        <Route path="*" element={<Navigate to="finance" replace />} />
                                    </Route>
                                </Route>


                                <Route element={<ProtectedRoute />}>
                                    <Route path="/" element={<Layout />}>
                                        <Route index element={<Dashboard />} />
                                        <Route path="products" element={<Products />} />
                                        <Route path="products/:productId/edit" element={<ProductWorkspacePage />} />
                                        <Route path="categories" element={<Categories />} />
                                        <Route path="units" element={<Units />} />
                                        <Route path="customers" element={<Customers />} />
                                        <Route path="suppliers" element={<Suppliers />} />
                                        <Route path="pos" element={<POS />} />
                                        <Route path="sales-history" element={<SalesHistory />} />
                                        <Route path="sales-representatives" element={<SalesRepresentatives />} />
                                        <Route path="sales-rep-reports" element={<SalesRepReports />} />
                                        <Route path="purchase-invoice" element={<PurchaseInvoice />} />
                                        <Route path="purchase-returns" element={<PurchaseReturns />} />
                                        <Route path="sales-returns" element={<SalesReturns />} />
                                        <Route path="warehouses" element={<Warehouses />} />
                                        <Route path="inventory" element={<Inventory />} />
                                        {/* PHASE LTSR: Legacy cashbox → Ledger redirect */}
                                        <Route path="cashbox" element={<Navigate to="/treasury/account/1010" replace />} />
                                        <Route path="opening-balance/cashbox" element={<CashboxOpeningBalance />} />
                                        <Route path="opening-balance/customers" element={<CustomerOpeningBalance />} />
                                        <Route path="opening-balance/suppliers" element={<SupplierOpeningBalance />} />
                                        <Route path="opening-balance/warehouse-stock" element={<WarehouseOpeningStock />} />
                                        <Route path="opening-balance/banks" element={<Navigate to="/treasury/account/1030" replace />} />
                                        <Route path="statement-of-account" element={<StatementOfAccount />} />
                                        <Route path="payments" element={<Payments />} />
                                        <Route path="outstanding-balances" element={<OutstandingBalances />} />
                                        <Route path="adjustments" element={<Adjustments />} />
                                        <Route path="stock-transfers" element={<StockTransfers />} />
                                        <Route path="financial-accounts" element={<FinancialAccounts />} />
                                        <Route path="balance-transfer" element={<BalanceTransfer />} />
                                        <Route path="banks" element={<Banks />} />
                                        <Route path="expense-categories" element={<ExpenseCategories />} />
                                        <Route path="expenses" element={<Expenses />} />
                                        {/* PHASE LTSR: Legacy treasury-movements → Ledger redirect */}
                                        <Route path="treasury-movements" element={<Navigate to="/treasury/hub" replace />} />
                                        {/* PHASE LTSR: Legacy bank-statement → Ledger redirect */}
                                        <Route path="reports/bank-statement" element={<Navigate to="/treasury/account/1030" replace />} />
                                        {/* PHASE LTSR: Legacy visa-statement → Ledger redirect */}
                                        <Route path="reports/visa-statement" element={<Navigate to="/treasury/account/1110" replace />} />
                                        <Route path="reports/sales-analysis" element={<SalesAnalysis />} />
                                        <Route path="reports/sales-summary" element={<SalesSummaryReport />} />
                                        <Route path="reports/sales-by-category" element={<SalesByCategoryReport />} />
                                        <Route path="reports/sales-by-payment-method" element={<SalesByPaymentMethodReport />} />
                                        <Route path="reports/inventory-valuation" element={<InventoryValuation />} />
                                        <Route path="reports/daily-close" element={<DailyCloseReport />} />
                                        <Route path="reports/product-movement" element={<ProductMovementReport />} />
                                        <Route path="reports/ar-aging" element={<AccountsReceivableAging />} />
                                        <Route path="reports/ap-aging" element={<AccountsPayableAging />} />
                                        <Route path="reports/sales-return-report" element={<SalesReturnReport />} />
                                        <Route path="reports/purchase-return-report" element={<PurchaseReturnReport />} />
                                        <Route path="reports/purchase-register" element={<PurchaseRegisterReport />} />
                                        <Route path="reports/purchases-by-supplier" element={<PurchasesBySupplierReport />} />
                                        <Route path="reports/cost-analysis" element={<CostAnalysisReport />} />
                                        {/* REMOVED: Legacy financial report routes (Phase C2 - Domain Consolidation)
                                    Trial Balance, P&L, Balance Sheet now at /treasury/* */}
                                        <Route path="reports/expenses" element={<ExpenseReport />} />
                                        <Route path="barcode-printing" element={<BarcodePrinting />} />
                                        <Route path="settings" element={<Settings />} />
                                        <Route path="settings/audit-log" element={<AuditLog />} />
                                        <Route path="maintenance/reset-database" element={<ResetDatabase />} />
                                        {/* E-commerce Routes */}
                                        {/* Storefront Operations domain — POS Admin screens (Owner/Admin) */}
                                        {/* These are the store owner's own storefront screens, NOT CommerceHub marketplace routes */}
                                        <Route element={<PosRoleRouteGuard roles={['Owner', 'Admin']} />}>
                                            {/* Operations Overview — canonical entry point */}
                                            <Route path="ecommerce/overview" element={<OperationsOverview />} />
                                            {/* Orders */}
                                            <Route path="ecommerce/orders" element={<Orders />} />
                                            <Route path="ecommerce/orders/:orderId" element={<Orders />} />
                                            <Route path="ecommerce/blocked-phones" element={<BlockedPhones />} />
                                            <Route path="ecommerce/online-customers" element={<OnlineCustomersPage />} />
                                            <Route path="ecommerce/whatsapp" element={<WhatsAppSettings />} />
                                            <Route path="ecommerce/cart-recovery" element={<AbandonedCartRecovery />} />
                                            <Route path="ecommerce/abandoned-checkouts" element={<AbandonedCheckouts />} />
                                            {/* Payments */}
                                            <Route path="ecommerce/payment-methods" element={<PaymentMethods />} />
                                            <Route path="ecommerce/manual-payments" element={<ManualPayments />} />
                                            {/* Catalog */}
                                            <Route path="ecommerce/coupons" element={<Coupons />} />
                                            <Route path="ecommerce/promotions" element={<Promotions />} />
                                            <Route path="ecommerce/smart-offers" element={<SmartOffersPage />} />
                                            {/* Commerce AI */}
                                            <Route path="ecommerce/commerce-ai" element={<CommerceAI />} />
                                            {/* Pixel Tracking */}
                                            <Route path="ecommerce/pixel-tracking" element={<PixelTrackingConfigPage />} />
                                            {/* Auth Providers */}
                                            <Route path="ecommerce/auth-providers" element={<AuthProvidersPage />} />
                                            {/* Analytics */}
                                            <Route path="analytics/funnels" element={<FunnelsListPage />} />
                                            <Route path="analytics/funnels/:funnelKey" element={<FunnelDetailsPage />} />
                                            <Route path="analytics/attribution" element={<AttributionOverviewPage />} />
                                            <Route path="analytics/attribution/models" element={<AttributionModelsPage />} />
                                            <Route path="analytics/attribution/orders" element={<AttributionOrdersPage />} />
                                            <Route path="analytics/attribution/costs" element={<CampaignCostsPage />} />
                                            <Route path="analytics/attribution/roi" element={<RoiDashboardPage />} />
                                            {/* Settings */}
                                            <Route path="settings/analytics" element={<AnalyticsSettings />} />
                                        </Route>
                                        {/* Shipping — Owner/Admin/Manager (Manager needs shipping access) */}
                                        <Route element={<PosRoleRouteGuard roles={['Owner', 'Admin', 'Manager']} />}>
                                            <Route path="ecommerce/shipping-methods" element={<ShippingMethods />} />
                                        </Route>
                                        {/* Storefront administration: canonical IA routes plus legacy aliases */}
                                        <Route element={<PosRoleRouteGuard roles={['Owner', 'Admin']} />}>
                                            <Route path="storefront/builder" element={<StorefrontBuilderPage />} />
                                            <Route path="storefront/homepage" element={<StorefrontBuilderPage />} />
                                            <Route path="storefront/catalog/product-template" element={<StorefrontBuilderPage />} />

                                            <Route element={<StorefrontAdminFrame />}>
                                                <Route path="storefront" element={<StorefrontDashboard />} />
                                                <Route path="storefront/overview" element={<StorefrontDashboard />} />
                                                <Route path="storefront/status" element={<StorefrontSettings />} />

                                                <Route path="storefront/themes" element={<ThemeMarketplacePage />} />
                                                <Route path="storefront/appearance/themes" element={<ThemeMarketplacePage />} />
                                                <Route path="storefront/theme" element={<ThemeEditorPage />} />
                                                <Route path="storefront/appearance/colors-typography" element={<ThemeEditorPage />} />
                                                <Route path="storefront/versions" element={<VersionHistoryPage />} />
                                                <Route path="storefront/storeconfig-version-history" element={<VersionHistoryPage />} />

                                                <Route path="storefront/cms/pages" element={<CmsPagesList />} />
                                                <Route path="storefront/cms/pages/:pageId" element={<CmsPageEditor />} />
                                                <Route path="storefront/content/pages" element={<CmsPagesList />} />
                                                <Route path="storefront/content/pages/:pageId" element={<CmsPageEditor />} />
                                                <Route path="storefront/cms/navigation" element={<NavigationEditor />} />
                                                <Route path="storefront/navigation" element={<NavigationEditor />} />
                                                <Route path="storefront/cms/footer" element={<StorefrontFooterSettings />} />
                                                <Route path="storefront/footer-content" element={<StorefrontFooterSettings />} />
                                            </Route>
                                        </Route>
                                        {/* Shipping Admin Dashboard (Shipping Frontend Phase + Unified Configuration) */}
                                        <Route element={<PosRoleRouteGuard roles={['Owner', 'Admin', 'Manager']} />}>
                                            <Route path="admin/shipping" element={<ShippingAdministrationWorkspace />} />
                                            <Route path="admin/shipping/setup" element={<ShippingAdministrationWorkspace />} />
                                        </Route>
                                        <Route element={<CommerceHubVendorRouteGuard capability="commercehub.storefront.manage" />}>
                                            <Route path="admin/shipping/shipments" element={<ShipmentsListPage />} />
                                            <Route path="admin/shipping/shipments/:id" element={<ShipmentDetailsPage />} />
                                            <Route path="admin/shipping/health" element={<ShippingHealthPage />} />
                                        </Route>

                                        {/* Services Marketplace - Tab-Based System */}
                                        <Route path="marketplace" element={<MarketplaceQueryBoundary><MarketplaceAuthorityBoundary><MarketplaceShell /></MarketplaceAuthorityBoundary></MarketplaceQueryBoundary>} />
                                        <Route path="marketplace/service/:serviceId" element={<MarketplaceQueryBoundary><MarketplaceAuthorityBoundary><ServiceDetailsPage /></MarketplaceAuthorityBoundary></MarketplaceQueryBoundary>} />
                                        <Route path="marketplace/chat/:requestId" element={<MarketplaceQueryBoundary><MarketplaceAuthorityBoundary><ChatPage /></MarketplaceAuthorityBoundary></MarketplaceQueryBoundary>} />

                                        {/* Admin Affiliate Management (AF-4B) */}
                                        <Route element={<PosRoleRouteGuard roles={['Owner', 'Admin']} />}>
                                            <Route path="admin/affiliates" element={<AdminAffiliatesList />} />
                                            <Route path="admin/affiliates/:id" element={<AdminAffiliateDetail />} />
                                        </Route>

                                        {/* CommerceHub Admin (Global Marketplace Publishing) */}
                                        <Route element={<CommerceHubVendorRouteGuard capability="commercehub.catalog.manage" />}>
                                            <Route path="admin/commercehub" element={<CommerceHubSettings />} />
                                        </Route>
                                        <Route path="admin/commercehub/finance" element={<Navigate to="/commercehub/finance" replace />} />
                                        <Route element={<CommerceHubVendorRouteGuard capability="commercehub.finance.view" />}>
                                            <Route path="commercehub/finance" element={<CommerceHubFinance />} />
                                            <Route path="commercehub/earnings" element={<CommerceHubEarnings />} />
                                        </Route>
                                        <Route element={<CommerceHubVendorRouteGuard capability="commercehub.finance.view" />}><Route path="admin/commercehub/earnings" element={<CommerceHubEarnings />} /></Route>
                                        <Route element={<CommerceHubVendorRouteGuard capability="commercehub.payments.view" />}><Route path="admin/commercehub/payments" element={<CommerceHubPayments />} /></Route>
                                        <Route element={<CommerceHubVendorRouteGuard capability="commercehub.payments.view" />}>
                                            <Route path="commercehub/payments" element={<CommerceHubPayments />} />
                                        </Route>

                                        {/* Treasury & Finance Control Center (Phase 5 + Phase FTM) */}
                                        <Route path="treasury" element={<TreasuryDashboard />} />
                                        <Route path="treasury/trial-balance" element={<TreasuryTrialBalance />} />
                                        <Route path="treasury/profit-loss" element={<TreasuryProfitLoss />} />
                                        <Route path="treasury/balance-sheet" element={<TreasuryBalanceSheet />} />
                                        <Route path="treasury/cash-flow" element={<TreasuryCashFlow />} />
                                        <Route path="treasury/general-ledger" element={<TreasuryGeneralLedger />} />
                                        <Route path="treasury/clearing-aging" element={<TreasuryClearingAging />} />
                                        <Route path="treasury/transfers" element={<TreasuryTransfers />} />
                                        {/* Phase FTM: Ledger-driven Treasury UI */}
                                        <Route path="treasury/hub" element={<TreasuryHub />} />
                                        <Route path="treasury/account/:accountCode" element={<TreasuryAccountViewer />} />
                                        {/* Catch-all: keep authenticated users in the protected layout */}
                                        <Route path="*" element={<Navigate to="/" replace />} />
                                    </Route>
                                </Route>
                                <Route path="*" element={<Navigate to="/login" replace />} />
                            </Routes>
                        </BrowserRouter>
                    </SettingsProvider>
                </ToastProvider>
            </QueryClientProvider>
        </AppErrorBoundary>
    )
}

export default App
