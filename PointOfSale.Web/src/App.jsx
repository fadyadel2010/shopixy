import { BrowserRouter, Route, Navigate } from 'react-router-dom'
import AdminNavigationRoutes from './navigation/AdminNavigationRoutes'
import ProtectedRoute from './components/ProtectedRoute'
import PosPermissionRouteGuard from './components/PosPermissionRouteGuard'
import ShippingAccessDenied from './modules/shipping/components/ShippingAccessDenied'
import { ToastProvider } from './components/Toast'
import { ConfirmDialogProvider } from './common/confirm/ConfirmDialogProvider'
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
import SupplierCardFundingInstruments from './pages/SupplierCardFundingInstruments'
import OutstandingBalances from './pages/OutstandingBalances'
import Adjustments from './pages/Adjustments'
import StockTransfers from './pages/StockTransfers'
import FinancialAccounts from './pages/FinancialAccounts'
import TreasuryInitialCapitalization from './pages/TreasuryInitialCapitalization'
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
import UsersManagementPage from './pages/UsersManagementPage'
import AcceptInvitationPage from './pages/AcceptInvitationPage'
import ResetPasswordPage from './pages/ResetPasswordPage'
// E-commerce pages
import PixelTrackingConfigPage from './modules/storefront/config/PixelTrackingConfigPage'
import ShippingMethods from './pages/ShippingMethods'
// Commerce AI Admin Control (Phase 6)
import CommerceAI from './pages/CommerceAI'
import CommerceHubSettings from './pages/CommerceHubSettings'
import CommerceHubFinance from './pages/CommerceHubFinance'
import CommerceHubEarnings from './pages/CommerceHubEarnings'
import CommerceHubPayments from './pages/CommerceHubPayments'
import CommerceHubOrders from './pages/CommerceHubOrders'
import PaymentMethods from './pages/PaymentMethods'
import ManualPayments from './pages/ManualPayments'
import Coupons from './pages/Coupons'
import Promotions from './pages/Promotions'
import SmartOffersPage from './pages/SmartOffersPage'
import Orders from './pages/Orders'
import SyntheticQaCertification from './pages/SyntheticQaCertification'
// Online Customers (Phase P1 - Storefront Customer Management)
import { OnlineCustomersPage } from './modules/onlineCustomers'
// Storefront Builder
import StorefrontDashboard from './pages/StorefrontDashboard'
import StorefrontSettings from './pages/StorefrontSettings'
import { StorefrontBuilderPage, ThemeMarketplacePage, ThemeEditorPage, VersionHistoryPage, AuthProvidersPage } from './modules/storefront/pages'
// CMS Pages & Navigation (Phase 9.11)
import { CmsPagesList, CmsPageEditor, NavigationEditor, StorefrontFooterSettings } from './modules/storefront/cms'
import { StorefrontAdminFrame } from './modules/storefront/informationArchitecture'
import { StorefrontWorkspaceBoundary } from './modules/storefront/StorefrontWorkspaceBoundary'
import { CheckoutFieldsPage } from './modules/storefront/checkoutFields'
// Domain Management (DOMAIN-3.1-A skeleton)
import {
    DomainsOverviewPage,
    DomainDetailPage,
    AddDomainWizardPage,
    DomainListPage,
    DnsVerificationPage,
    SslManagementPage,
    RedirectsPage,
    DomainHistoryPage,
    ActivityPage,
} from './modules/storefront/domains'
// Analytics Funnels (Phase 5A)
import { FunnelsListPage, FunnelDetailsPage } from './modules/analytics'
// Analytics Attribution (Phase 8A) & Campaign Cost & ROI (Phase 8B)
import { AttributionOverviewPage, AttributionModelsPage, AttributionOrdersPage, CampaignCostsPage, RoiDashboardPage } from './modules/analytics'
// Analytics Settings (Phase 6: External Analytics Integration)
import AnalyticsSettings from './pages/AnalyticsSettings'
import StorefrontAnalyticsDashboard from './pages/StorefrontAnalyticsDashboard'
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
    TreasuryManualCodSettlement,
    TreasuryManualWalletSettlement,
    TreasuryCarrierRemittance,
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
import { AdminAffiliatesList, AdminAffiliateDetail, AffiliateProducts } from './modules/affiliate'
// AF-10B: Public affiliate hub pages — NOT exported from module index (admin-only)
// These are standalone pages outside the POS Layout, served from the same monolith.
import AffiliateLanding from './modules/affiliate/pages/AffiliateLanding'
import AffiliateLogin from './modules/affiliate/pages/AffiliateLogin'
import AffiliateRegister from './modules/affiliate/pages/AffiliateRegister'
import AffiliatePortal from './modules/affiliate/pages/AffiliatePortal'
import AffiliateVerifyEmail from './modules/affiliate/pages/AffiliateVerifyEmail'
import AffiliateForgotPassword from './modules/affiliate/pages/AffiliateForgotPassword'
import AffiliateResetPassword from './modules/affiliate/pages/AffiliateResetPassword'
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

export function appRouteElements() {
    return (
                            <>
                                <Route path="/login" element={<Login />} />
                                <Route path="/register" element={<Register />} />
                                <Route path="/super-admin/login" element={<SuperAdminLoginPage />} />
                                {/* Phase 8.4: Public email verification route */}
                                <Route path="/verify-email" element={<VerifyEmail />} />
                                {/* Users Management (Waves 2-3): anonymous invitation/reset ceremonies */}
                                <Route path="/accept-invitation" element={<AcceptInvitationPage />} />
                                <Route path="/reset-password" element={<ResetPasswordPage />} />
                                <Route path="/:lang/products" element={<MarketplaceProductsPublicPage />} />
                                <Route path="/:lang/product/:productId" element={<MarketplaceProductPublicPage />} />
                                <Route path="/:lang/:slug" element={<MarketplaceCmsPublicPage />} />

                                {/* AF-10B: Public affiliate hub — outside POS Layout, no POS auth required */}
                                {/* These routes are NOT in the POS sidebar — they're the external affiliate experience */}
                                <Route path="/affiliate" element={<AffiliateProviders />}>
                                    <Route index element={<AffiliateLanding />} />
                                    <Route path="login" element={<AffiliateLogin />} />
                                    <Route path="register" element={<AffiliateRegister />} />
                                    <Route path="verify-email" element={<AffiliateVerifyEmail />} />
                                    <Route path="forgot-password" element={<AffiliateForgotPassword />} />
                                    <Route path="reset-password" element={<AffiliateResetPassword />} />
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
                                    <Route path="/" element={<Layout />} handle={{ application: 'pos' }}>
                                        <Route element={<PosPermissionRouteGuard permission="dashboard.view" />}>
                                            <Route index element={<Dashboard />} />
                                        </Route>
                                        <Route element={<PosPermissionRouteGuard permission="catalog.view" />}>
                                            <Route path="products" element={<Products />} />
                                            <Route path="categories" element={<Categories />} />
                                            <Route path="units" element={<Units />} />
                                            <Route path="barcode-printing" element={<BarcodePrinting />} />
                                        </Route>
                                        <Route element={<PosPermissionRouteGuard permission="catalog.manage" />}>
                                            <Route path="products/:productId/edit" element={<ProductWorkspacePage />} />
                                        </Route>
                                        <Route element={<PosPermissionRouteGuard permission="customers.view" />}>
                                            <Route path="customers" element={<Customers />} />
                                        </Route>
                                        <Route element={<PosPermissionRouteGuard permission="suppliers.view" />}>
                                            <Route path="suppliers" element={<Suppliers />} />
                                        </Route>
                                        <Route element={<PosPermissionRouteGuard permission="sales.operate" />}>
                                            <Route path="pos" element={<POS />} />
                                        </Route>
                                        <Route element={<PosPermissionRouteGuard permission="sales.view" />}>
                                            <Route path="sales-history" element={<SalesHistory />} />
                                        </Route>
                                        <Route element={<PosPermissionRouteGuard permission="sales.refund" />}>
                                            <Route path="sales-returns" element={<SalesReturns />} />
                                        </Route>
                                        <Route element={<PosPermissionRouteGuard permission="sales.operate" />}>
                                            <Route path="sales-representatives" element={<SalesRepresentatives />} />
                                        </Route>
                                        <Route element={<PosPermissionRouteGuard permission="reports.operational.view" />}>
                                            <Route path="sales-rep-reports" element={<SalesRepReports />} />
                                        </Route>
                                        <Route element={<PosPermissionRouteGuard permission="purchases.manage" />}>
                                            <Route path="purchase-invoice" element={<PurchaseInvoice />} />
                                        </Route>
                                        <Route element={<PosPermissionRouteGuard permission="purchases.return" />}>
                                            <Route path="purchase-returns" element={<PurchaseReturns />} />
                                        </Route>
                                        <Route element={<PosPermissionRouteGuard permission="inventory.view" />}>
                                            <Route path="warehouses" element={<Warehouses />} />
                                            <Route path="inventory" element={<Inventory />} />
                                        </Route>
                                        {/* PHASE LTSR: Legacy cashbox → dynamic Treasury Hub (no fixed account identity) */}
                                        <Route path="cashbox" element={<Navigate to="/treasury/hub" replace />} />
                                        <Route path="opening-balance/cashbox" element={<Navigate to="/treasury/initial-capitalization" replace />} />
                                        <Route element={<PosPermissionRouteGuard permission="customers.balance.manage" />}>
                                            <Route path="opening-balance/customers" element={<CustomerOpeningBalance />} />
                                        </Route>
                                        <Route element={<PosPermissionRouteGuard permission="suppliers.balance.manage" />}>
                                            <Route path="opening-balance/suppliers" element={<SupplierOpeningBalance />} />
                                        </Route>
                                        <Route element={<PosPermissionRouteGuard permission="inventory.adjust" />}>
                                            <Route path="opening-balance/warehouse-stock" element={<WarehouseOpeningStock />} />
                                        </Route>
                                        <Route path="opening-balance/banks" element={<Navigate to="/treasury/initial-capitalization" replace />} />
                                        <Route element={<PosPermissionRouteGuard permission="finance.view" />}>
                                            <Route path="statement-of-account" element={<StatementOfAccount />} />
                                            <Route path="outstanding-balances" element={<OutstandingBalances />} />
                                        </Route>
                                        <Route element={<PosPermissionRouteGuard permission="finance.operate" />}>
                                            <Route path="payments" element={<Payments />} />
                                            <Route path="adjustments" element={<Adjustments />} />
                                        </Route>
                                        <Route element={<PosPermissionRouteGuard permission="suppliers.balance.manage" />}>
                                            <Route path="supplier-card-funding" element={<SupplierCardFundingInstruments />} />
                                        </Route>
                                        <Route element={<PosPermissionRouteGuard permission="finance.initialize" />}>
                                            <Route path="treasury/initial-capitalization" element={<TreasuryInitialCapitalization />} />
                                        </Route>
                                        <Route element={<PosPermissionRouteGuard permission="inventory.transfer" />}>
                                            <Route path="stock-transfers" element={<StockTransfers />} />
                                        </Route>
                                        <Route element={<PosPermissionRouteGuard permission="finance.view" />}>
                                            <Route path="financial-accounts" element={<FinancialAccounts />} />
                                            <Route path="banks" element={<Banks />} />
                                            <Route path="expense-categories" element={<ExpenseCategories />} />
                                            <Route path="expenses" element={<Expenses />} />
                                        </Route>
                                        <Route path="balance-transfer" element={<Navigate to="/treasury/transfers" replace />} />
                                        {/* PHASE LTSR: Legacy treasury-movements → Ledger redirect */}
                                        <Route path="treasury-movements" element={<Navigate to="/treasury/hub" replace />} />
                                        {/* PHASE LTSR: Legacy bank-statement → dynamic Treasury Hub (no fixed account identity) */}
                                        <Route path="reports/bank-statement" element={<Navigate to="/treasury/hub" replace />} />
                                        {/* PHASE LTSR: Legacy visa-statement → dynamic Clearing Aging (no fixed account identity) */}
                                        <Route path="reports/visa-statement" element={<Navigate to="/treasury/clearing-aging" replace />} />
                                        <Route element={<PosPermissionRouteGuard permission="reports.operational.view" />}>
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
                                            <Route path="reports/expenses" element={<ExpenseReport />} />
                                        </Route>
                                        {/* REMOVED: Legacy financial report routes (Phase C2 - Domain Consolidation)
                                    Trial Balance, P&L, Balance Sheet now at /treasury/* */}
                                        <Route element={<PosPermissionRouteGuard permission="settings.view" />}>
                                            <Route path="settings" element={<Settings />} />
                                        </Route>
                                        <Route element={<PosPermissionRouteGuard permission="audit.view" />}>
                                            <Route path="settings/audit-log" element={<AuditLog />} />
                                        </Route>
                                        <Route element={<PosPermissionRouteGuard permission="users.view" />}>
                                            <Route path="users" element={<UsersManagementPage />} />
                                        </Route>
                                        {/* E-commerce Routes */}
                                        {/* Storefront Operations domain — POS permission projection */}
                                        {/* These are the store owner's own storefront screens, NOT CommerceHub marketplace routes */}
                                        <Route element={<PosPermissionRouteGuard permission="storefront.orders.view" />}>
                                          <Route element={<StorefrontWorkspaceBoundary />}>
                                            {/* Operations Overview — canonical entry point */}
                                            <Route path="ecommerce/overview" element={<OperationsOverview />} handle={{ ownsNavigationBreadcrumb: true }} />
                                            {/* Orders */}
                                            <Route path="ecommerce/orders" element={<Orders />} />
                                            <Route path="ecommerce/orders/:orderId" element={<Orders />} />
                                            <Route path="ecommerce/blocked-phones" element={<BlockedPhones />} />
                                            <Route path="ecommerce/online-customers" element={<OnlineCustomersPage />} />
                                            <Route path="ecommerce/whatsapp" element={<WhatsAppSettings />} />
                                            <Route path="ecommerce/cart-recovery" element={<AbandonedCartRecovery />} />
                                             <Route path="ecommerce/abandoned-checkouts" element={<AbandonedCheckouts />} />
                                          </Route>
                                        </Route>
                                        {import.meta.env.DEV && <Route path="qa/affiliate-certification" element={<SyntheticQaCertification />} />}
                                        <Route element={<PosPermissionRouteGuard permission="storefront.payments.manage" />}>
                                          <Route element={<StorefrontWorkspaceBoundary />}>
                                            <Route path="ecommerce/payment-methods" element={<PaymentMethods />} />
                                            <Route path="ecommerce/manual-payments" element={<ManualPayments />} />
                                          </Route>
                                        </Route>
                                        <Route element={<PosPermissionRouteGuard permission="storefront.promotions.manage" />}>
                                          <Route element={<StorefrontWorkspaceBoundary />}>
                                            <Route path="ecommerce/coupons" element={<Coupons />} />
                                            <Route path="ecommerce/promotions" element={<Promotions />} />
                                            <Route path="ecommerce/smart-offers" element={<SmartOffersPage />} />
                                          </Route>
                                        </Route>
                                        <Route element={<PosPermissionRouteGuard permission="storefront.design.manage" />}>
                                          <Route element={<StorefrontWorkspaceBoundary />}>
                                             <Route path="ecommerce/commerce-ai" element={<CommerceAI />} />
                                          </Route>
                                        </Route>
                                        <Route element={<PosPermissionRouteGuard permission="storefront.analytics.configure" />}>
                                          <Route element={<StorefrontWorkspaceBoundary />}>
                                            <Route path="ecommerce/pixel-tracking" element={<PixelTrackingConfigPage />} />
                                             <Route path="settings/analytics" element={<AnalyticsSettings />} />
                                          </Route>
                                        </Route>
                                        <Route element={<PosPermissionRouteGuard permission="storefront.auth.manage" />}>
                                          <Route element={<StorefrontWorkspaceBoundary />}>
                                             <Route path="ecommerce/auth-providers" element={<AuthProvidersPage />} />
                                          </Route>
                                        </Route>
                                        <Route element={<PosPermissionRouteGuard permission="storefront.analytics.view" />}>
                                          <Route element={<StorefrontWorkspaceBoundary />}>
                                            <Route path="analytics/funnels" element={<FunnelsListPage />} />
                                            <Route path="analytics/funnels/:funnelKey" element={<FunnelDetailsPage />} />
                                            <Route path="analytics/attribution" element={<AttributionOverviewPage />} />
                                            <Route path="analytics/attribution/models" element={<AttributionModelsPage />} />
                                            <Route path="analytics/attribution/orders" element={<AttributionOrdersPage />} />
                                            <Route path="analytics/attribution/costs" element={<CampaignCostsPage />} />
                                            <Route path="analytics/attribution/roi" element={<RoiDashboardPage />} />
                                             <Route path="analytics/storefront" element={<StorefrontAnalyticsDashboard />} />
                                          </Route>
                                        </Route>
                                        <Route element={<PosPermissionRouteGuard permission="shipping.manage" />}>
                                          <Route element={<StorefrontWorkspaceBoundary />}>
                                            <Route path="ecommerce/shipping-methods" element={<ShippingMethods />} />
                                          </Route>
                                        </Route>
                                        {/* Storefront administration: canonical IA routes plus legacy aliases */}
                                        <Route element={<PosPermissionRouteGuard permission="storefront.design.manage" />}>
                                            <Route element={<StorefrontWorkspaceBoundary />}>
                                            <Route path="storefront/builder" element={<StorefrontBuilderPage />} handle={{ ownsNavigationBreadcrumb: true }} />
                                            <Route path="storefront/homepage" element={<StorefrontBuilderPage />} handle={{ ownsNavigationBreadcrumb: true }} />
                                            <Route path="storefront/catalog/product-template" element={<StorefrontBuilderPage />} handle={{ ownsNavigationBreadcrumb: true }} />

                                            <Route element={<StorefrontAdminFrame />} handle={{ ownsNavigationBreadcrumb: true }}>
                                                <Route path="storefront" element={<StorefrontDashboard />} />
                                                <Route path="storefront/overview" element={<StorefrontDashboard />} />
                                                <Route path="storefront/status" element={<StorefrontSettings />} />
                                                <Route path="storefront/checkout-fields" element={<CheckoutFieldsPage />} />

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

                                                {/* Domain Management routes — DOMAIN-3.1-A skeleton */}
                                                <Route path="storefront/domains" element={<DomainsOverviewPage />} />
                                                <Route path="storefront/domains/list" element={<DomainListPage />} />
                                                <Route path="storefront/domains/add" element={<AddDomainWizardPage />} />
                                                <Route path="storefront/domains/activity" element={<ActivityPage />} />
                                                <Route path="storefront/domains/:id" element={<DomainDetailPage />} />
                                                <Route path="storefront/domains/:id/verification" element={<DnsVerificationPage />} />
                                                <Route path="storefront/domains/:id/ssl" element={<SslManagementPage />} />
                                                <Route path="storefront/domains/:id/redirects" element={<RedirectsPage />} />
                                                <Route path="storefront/domains/:id/history" element={<DomainHistoryPage />} />
                                            </Route>
                                            </Route>
                                        </Route>
                                        {/* Shipping Admin Dashboard (Shipping Frontend Phase + Unified Configuration) */}
                                        <Route element={<PosPermissionRouteGuard permission="shipping.manage" />}>
                                          <Route element={<StorefrontWorkspaceBoundary />}>
                                            <Route path="admin/shipping" element={<ShippingAdministrationWorkspace />} />
                                            <Route path="admin/shipping/setup" element={<ShippingAdministrationWorkspace />} />
                                          </Route>
                                        </Route>
                                        <Route element={<PosPermissionRouteGuard permission="shipping.view" deniedElement={<ShippingAccessDenied reason="permission" />} />}>
                                            <Route element={<CommerceHubVendorRouteGuard capability="commercehub.storefront.manage" deniedElement={<ShippingAccessDenied reason="storefront" />} />}>
                                              <Route element={<StorefrontWorkspaceBoundary />}>
                                                <Route path="admin/shipping/shipments" element={<ShipmentsListPage />} />
                                                <Route path="admin/shipping/shipments/:id" element={<ShipmentDetailsPage />} />
                                                <Route path="admin/shipping/health" element={<ShippingHealthPage />} />
                                              </Route>
                                            </Route>
                                        </Route>

                                        {/* Services Marketplace - Tab-Based System */}
                                        <Route path="marketplace" element={<MarketplaceQueryBoundary><MarketplaceAuthorityBoundary><MarketplaceShell /></MarketplaceAuthorityBoundary></MarketplaceQueryBoundary>} />
                                        <Route path="marketplace/service/:serviceId" element={<MarketplaceQueryBoundary><MarketplaceAuthorityBoundary><ServiceDetailsPage /></MarketplaceAuthorityBoundary></MarketplaceQueryBoundary>} />
                                        <Route path="marketplace/chat/:requestId" element={<MarketplaceQueryBoundary><MarketplaceAuthorityBoundary><ChatPage /></MarketplaceAuthorityBoundary></MarketplaceQueryBoundary>} />

                                        {/* Admin Affiliate Management (AF-4B) */}
                                        <Route element={<PosPermissionRouteGuard permission="affiliates.manage" />}>
                                            <Route path="admin/affiliates" element={<AdminAffiliatesList />} />
                                            <Route path="admin/affiliates/products" element={<AffiliateProducts />} />
                                            <Route path="admin/affiliates/:id" element={<AdminAffiliateDetail />} />
                                        </Route>

                                        {/* CommerceHub Admin (Global Marketplace Publishing) */}
                                        <Route element={<PosPermissionRouteGuard permission="storefront.orders.view" />}>
                                            <Route element={<CommerceHubVendorRouteGuard capability="commercehub.orders.view" />}>
                                                <Route path="commercehub/orders" element={<CommerceHubOrders />} />
                                                <Route path="commercehub/orders/:subOrderId" element={<CommerceHubOrders />} />
                                            </Route>
                                        </Route>
                                        <Route element={<PosPermissionRouteGuard permission="catalog.publish" />}>
                                            <Route element={<CommerceHubVendorRouteGuard capability="commercehub.catalog.manage" />}>
                                                <Route path="admin/commercehub" element={<CommerceHubSettings />} />
                                            </Route>
                                        </Route>
                                        <Route path="admin/commercehub/finance" element={<Navigate to="/commercehub/finance" replace />} />
                                        <Route element={<PosPermissionRouteGuard permission="reports.financial.view" />}>
                                            <Route element={<CommerceHubVendorRouteGuard capability="commercehub.finance.view" />}>
                                                <Route path="commercehub/finance" element={<CommerceHubFinance />} />
                                                <Route path="commercehub/earnings" element={<CommerceHubEarnings />} />
                                                <Route path="admin/commercehub/earnings" element={<CommerceHubEarnings />} />
                                            </Route>
                                        </Route>
                                        <Route element={<PosPermissionRouteGuard permission="storefront.payments.view" />}>
                                            <Route element={<CommerceHubVendorRouteGuard capability="commercehub.payments.view" />}>
                                                <Route path="admin/commercehub/payments" element={<CommerceHubPayments />} />
                                                <Route path="commercehub/payments" element={<CommerceHubPayments />} />
                                            </Route>
                                        </Route>

                                        {/* Treasury & Finance Control Center (Phase 5 + Phase FTM) */}
                                        <Route element={<PosPermissionRouteGuard permission="storefront.payments.manage" />}>
                                            <Route path="treasury/manual-cod-settlements" element={<TreasuryManualCodSettlement />} />
                                            <Route path="treasury/manual-wallet-settlements" element={<TreasuryManualWalletSettlement />} />
                                            <Route path="treasury/carrier-remittances" element={<TreasuryCarrierRemittance />} />
                                        </Route>
                                        <Route element={<PosPermissionRouteGuard permission="reports.financial.view" />}>
                                            <Route path="treasury" element={<TreasuryDashboard />} />
                                            <Route path="treasury/trial-balance" element={<TreasuryTrialBalance />} />
                                            <Route path="treasury/profit-loss" element={<TreasuryProfitLoss />} />
                                            <Route path="treasury/balance-sheet" element={<TreasuryBalanceSheet />} />
                                            <Route path="treasury/cash-flow" element={<TreasuryCashFlow />} />
                                            <Route path="treasury/general-ledger" element={<TreasuryGeneralLedger />} />
                                            <Route path="treasury/clearing-aging" element={<TreasuryClearingAging />} />
                                        </Route>
                                        <Route element={<PosPermissionRouteGuard permission="finance.operate" />}>
                                            <Route path="treasury/transfers" element={<TreasuryTransfers />} />
                                        </Route>
                                        <Route element={<PosPermissionRouteGuard permission="finance.view" />}>
                                            <Route path="treasury/hub" element={<TreasuryHub />} />
                                            <Route path="treasury/account/:accountCode" element={<TreasuryAccountViewer />} />
                                        </Route>
                                        {/* Catch-all: keep authenticated users in the protected layout */}
                                        <Route path="*" element={<Navigate to="/" replace />} />
                                    </Route>
                                </Route>
                                <Route path="*" element={<Navigate to="/login" replace />} />
                            </>
    )
}

function App() {
    return <AppErrorBoundary>
        <QueryClientProvider client={queryClient}>
            <ToastProvider>
                <ConfirmDialogProvider>
                    <AuthBootstrap />
                    <SettingsProvider>
                        <BrowserRouter>
                            <AdminNavigationRoutes>{appRouteElements()}</AdminNavigationRoutes>
                        </BrowserRouter>
                    </SettingsProvider>
                </ConfirmDialogProvider>
            </ToastProvider>
        </QueryClientProvider>
    </AppErrorBoundary>
}

export default App
