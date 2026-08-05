import { useEffect, useState } from 'react'
import { Link, useLocation } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import {
    Activity,
    FileText,
    Files,
    History,
    Home,
    LayoutDashboard,
    LayoutTemplate,
    Navigation as NavigationIcon,
    Paintbrush,
    Palette,
    PanelBottom,
    PanelBottomDashed,
    PanelsTopLeft,
    PanelTop,
    ShoppingBag,
    Store,
} from 'lucide-react'
import { useAuthStore } from '../store/authStore'
import { useRTL } from '../hooks/useRTL'
import { isServiceMarketplaceWorkflowActor } from '../marketplace/authority'
import { canAccessCommerceHubVendorNavigationCapability } from '../auth/posVendorSessionAuthority'
import {
    STOREFRONT_NAVIGATION,
    STOREFRONT_SIDEBAR_STATE_KEY,
    isStorefrontNodeActive,
    resolveStorefrontScreen,
} from '../modules/storefront/informationArchitecture'

const STORE_ADMIN_NAVIGATION_CAPABILITY_BY_PATH = new Map([
    ['/ecommerce/blocked-phones', 'commercehub.orders.view'],
    ['/ecommerce/whatsapp', 'commercehub.orders.view'],
    ['/ecommerce/cart-recovery', 'commercehub.orders.view'],
    ['/ecommerce/abandoned-checkouts', 'commercehub.orders.view'],
    ['/ecommerce/online-customers', 'commercehub.orders.view'],
    ['/ecommerce/coupons', 'commercehub.storefront.manage'],
    ['/ecommerce/promotions', 'commercehub.storefront.manage'],
    ['/ecommerce/smart-offers', 'commercehub.storefront.manage'],
    ['/ecommerce/commerce-ai', 'commercehub.storefront.manage'],
])

const STOREFRONT_ICONS = {
    Activity,
    FileText,
    Files,
    History,
    Home,
    LayoutDashboard,
    LayoutTemplate,
    Navigation: NavigationIcon,
    Paintbrush,
    Palette,
    PanelBottom,
    PanelBottomDashed,
    PanelsTopLeft,
    PanelTop,
    ShoppingBag,
    Store,
}

const readExpandedMenus = () => {
    try {
        const storedValue = window.localStorage.getItem(STOREFRONT_SIDEBAR_STATE_KEY)
        const parsedValue = storedValue ? JSON.parse(storedValue) : null
        return parsedValue && typeof parsedValue === 'object' && !Array.isArray(parsedValue) ? parsedValue : {}
    } catch {
        return {}
    }
}

export default function Sidebar({ isOpen, onClose }) {
    const { t } = useTranslation()
    const { user } = useAuthStore()
    const location = useLocation()
    const [expandedMenus, setExpandedMenus] = useState(readExpandedMenus)
    const { dir, isRTL } = useRTL()

    const toggleMenu = (menuKey) => {
        setExpandedMenus(prev => ({
            ...prev,
            [menuKey]: !prev[menuKey]
        }))
    }

    const isActive = (path) => {
        // Handle paths with query parameters (e.g., /marketplace?tab=my-requests)
        if (path.includes('?')) {
            const [pathname, search] = path.split('?');
            return location.pathname === pathname && location.search === `?${search}`;
        }
        return location.pathname === path;
    };

    const activeStorefrontScreenId = resolveStorefrontScreen(location.pathname, location.search)?.id || null

    const renderIcon = (icon, size = 20) => {
        const Icon = STOREFRONT_ICONS[icon]
        return Icon
            ? <Icon aria-hidden="true" size={size} />
            : <span aria-hidden="true">{icon}</span>
    }

    const localizeStorefrontNode = (node) => ({
        ...node,
        label: t(node.labelKey, node.fallbackLabel),
        requiredRoles: ['Owner', 'Admin'],
        children: node.children?.map(localizeStorefrontNode),
    })

    // New navigation structure with renamed items and proper order
    const menuItems = [
        // 1) Dashboard
        {
            key: 'dashboard',
            label: t('nav.dashboard'),
            icon: '📊',
            path: '/'
        },
        // 2) Database
        {
            key: 'database',
            label: t('nav.database'),
            icon: '🗄️',
            children: [
                { path: '/customers', label: t('nav.customersRegistration'), icon: '👥' },
                { path: '/suppliers', label: t('nav.suppliersRegistration'), icon: '🏢' },
                { path: '/sales-representatives', label: t('nav.salesRepRegistration'), icon: '👤' },
                { path: '/categories', label: t('nav.mainSubCategories'), icon: '📑' },
                { path: '/units', label: t('units.title'), icon: '📏' },
                { path: '/warehouses', label: t('nav.warehouses'), icon: '🏭' },
                { path: '/banks', label: t('banks.title'), icon: '🏦' },
                { path: '/products', label: t('nav.itemCard'), icon: '🏷️' }
            ]
        },
        // 3) Accounts
        {
            key: 'accounts',
            label: t('nav.accounts'),
            icon: '💼',
            children: [
                { path: '/financial-accounts?tab=main', label: t('accounts.mainAccounts'), icon: '🏦' },
                { path: '/financial-accounts?tab=sub', label: t('accounts.subAccounts'), icon: '📂' },
                { path: '/balance-transfer', label: t('nav.accountsDepositWithdrawal'), icon: '💸' }
            ]
        },
        // 4) Opening Balance
        {
            key: 'opening_balance',
            label: t('nav.openingBalance'),
            icon: '🏁',
            children: [
                { path: '/opening-balance/cashbox', label: t('nav.treasuryOpeningBalance'), icon: '💵' },
                { path: '/opening-balance/customers', label: t('nav.customersOpeningBalance'), icon: '👥' },
                { path: '/opening-balance/suppliers', label: t('nav.suppliersOpeningBalance'), icon: '🏢' },
                { path: '/opening-balance/warehouse-stock', label: t('nav.warehouseOpeningBalance'), icon: '📦' }
            ]
        },
        // 5) Transactions
        {
            key: 'transactions',
            label: t('nav.transactions'),
            icon: '🔄',
            children: [
                { path: '/pos', label: t('nav.sales'), icon: '💳' },
                { path: '/sales-returns', label: t('nav.salesReturn'), icon: '↩️' },
                { path: '/purchase-invoice', label: t('nav.purchases'), icon: '➕' },
                { path: '/purchase-returns', label: t('nav.purchaseReturn'), icon: '↩️' }
            ]
        },
        // 6) Treasury (Phase LTSR: Ledger-only)
        {
            key: 'treasury',
            label: t('nav.treasury'),
            icon: '💰',
            children: [
                // All Ledger-based routes
                { path: '/treasury', label: t('nav.financeDashboard'), icon: '📊' },
                { path: '/treasury/hub', label: t('nav.treasuryHub'), icon: '🏦' },
                { path: '/treasury/account/1010', label: t('nav.cashAccount'), icon: '💵' },
                { path: '/treasury/account/1030', label: t('nav.bankAccount'), icon: '🏦' },
                { path: '/treasury/account/1110', label: t('nav.visaClearing'), icon: '💳' },
                // Operational screens (not showing balance)
                { path: '/payments', label: t('nav.customerSupplierPayments'), icon: '💸' },
                { path: '/adjustments', label: t('nav.customerSupplierSettlement'), icon: '⚙️' },
                { path: '/outstanding-balances', label: t('accounts.outstandingBalances'), icon: '⚠️' }
            ]
        },
        // 7) Inventory
        {
            key: 'inventory',
            label: t('nav.inventory'),
            icon: '🏭',
            children: [
                { path: '/inventory', label: t('nav.inventoryBalance'), icon: '📊' },
                { path: '/stock-transfers', label: t('nav.inventoryTransfers'), icon: '🔄' },
                { path: '/barcode-printing', label: t('nav.barcodePrinting'), icon: '🏷️' }
            ]
        },
        // 8) Expenses
        {
            key: 'expenses',
            label: t('nav.expenses'),
            icon: '💸',
            children: [
                { path: '/expense-categories', label: t('nav.expensesRegistration'), icon: '📁' },
                { path: '/expenses', label: t('nav.expensesMovement'), icon: '💰' },
                { path: '/reports/expenses', label: t('nav.expensesReport'), icon: '📈' }
            ]
        },
        // 9) Account Statement
        {
            key: 'account_statement',
            label: t('nav.accountStatement'),
            icon: '📄',
            children: [
                { path: '/statement-of-account', label: t('nav.supplierCustomerStatement'), icon: '📋' },
                { path: '/reports/bank-statement', label: t('nav.bankStatement'), icon: '🏦' },
                { path: '/reports/visa-statement', label: t('nav.visaStatement'), icon: '💳' }
            ]
        },
        // 10) E-commerce
        {
            key: 'ecommerce',
            label: t('nav.ecommerce'),
            icon: '🛒',
            children: [
                { path: '/ecommerce/orders', label: t('orders.title'), icon: '📦', capability: 'commercehub.orders.view' },
                { path: '/ecommerce/blocked-phones', label: t('nav.blockedPhones', 'Blocked Phones'), icon: '📵' },
                // WhatsApp Integration (Phase 14) - Consolidated Settings Page
                { path: '/ecommerce/whatsapp', label: t('nav.whatsappSettings', 'WhatsApp Settings'), icon: '📱' },
                // Smart Cart Recovery (Abandoned Cart Hardening)
                { path: '/ecommerce/cart-recovery', label: t('nav.cartRecovery', 'Smart Cart Recovery'), icon: '🔄' },
                // Abandoned Checkouts (Lost Orders Phase)
                { path: '/ecommerce/abandoned-checkouts', label: t('nav.abandonedCheckouts', 'Abandoned Checkouts'), icon: '🚫' },
                { path: '/admin/shipping/setup', label: t('shippingSetup.title', 'Shipping'), icon: 'Shipping', requiredRoles: ['Owner', 'Admin', 'Manager'] },
                { path: '/ecommerce/online-customers', label: t('nav.onlineCustomers', 'Online Customers'), icon: '👥' },
                { path: '/ecommerce/auth-providers', label: t('nav.authProviders', 'Auth Providers'), icon: '🔐', capability: 'commercehub.storefront.manage' },
                { path: '/ecommerce/payment-methods', label: t('paymentMethods.title', 'Payment Methods'), icon: '💳', capability: 'commercehub.payments.view' },
                { path: '/ecommerce/manual-payments', label: t('nav.manualPayments', 'Manual Payments'), icon: '💸', capability: 'commercehub.payments.view' },
                { path: '/ecommerce/coupons', label: t('coupons.title'), icon: '🎟️' },
                { path: '/ecommerce/promotions', label: t('promotions.title', 'Offers'), icon: '🏷️' },
                { path: '/ecommerce/smart-offers', label: t('nav.smartOffers', 'Smart Offers'), icon: '💥' },
                { path: '/ecommerce/commerce-ai', label: t('commerceAi.title', 'AI Assistant'), icon: '🤖' },
                { path: '/ecommerce/pixel-tracking', label: t('nav.pixelTracking', 'Signal Hardening'), icon: '📡', capability: 'commercehub.analytics.view' },
                { path: '/analytics/funnels', label: t('nav.conversionFunnels', 'Conversion Funnels'), icon: '📊', capability: 'commercehub.analytics.view' },
                { path: '/analytics/attribution', label: t('nav.marketingAttribution', 'Marketing Attribution'), icon: '🎯', capability: 'commercehub.analytics.view' },
                { path: '/analytics/attribution/costs', label: t('nav.campaignCosts', 'Campaign Costs'), icon: '💰', capability: 'commercehub.analytics.view' },
                { path: '/analytics/attribution/roi', label: t('nav.marketingRoi', 'Marketing ROI'), icon: '📈', capability: 'commercehub.analytics.view' },
                { path: '/settings/analytics', label: t('nav.analyticsSettings', 'Analytics Providers'), icon: '⚙️', capability: 'commercehub.analytics.view' }
            ]
        },
        // 11) Services Marketplace (Phase P2 - Tab-Based Navigation)
        {
            key: 'marketplace',
            label: t('nav.marketplace', 'Services Marketplace'),
            icon: '🤝',
            path: '/marketplace',
            requiresMarketplaceActor: true
        },
        // CommerceHub Admin (Global Marketplace Publishing)
        {
            key: 'commercehub',
            label: t('nav.commerceHub', 'CommerceHub'),
            icon: '🏪',
            children: [
                { path: '/admin/commercehub', label: t('nav.commerceHubSettings', 'Publishing Settings'), icon: '⚙️', capability: 'commercehub.catalog.manage' },
                { path: '/ecommerce/orders', label: t('orders.title', 'Orders'), icon: '📦', capability: 'commercehub.orders.view' },
                { path: '/commercehub/finance', label: t('nav.commerceHubFinance', 'Finance Dashboard'), icon: '💼', capability: 'commercehub.finance.view' },
                { path: '/commercehub/earnings', label: t('nav.commerceHubEarnings', 'Vendor Earnings'), icon: '📈', capability: 'commercehub.finance.view' },
                { path: '/commercehub/payments', label: t('nav.commerceHubDebtRepayment', 'Debt Repayment'), icon: '💸', capability: 'commercehub.payments.view' },
            ],
        },

        // 12) Admin Affiliate Management (AF-4B) — Owner/Admin only
        {
            key: 'admin-affiliates',
            label: t('nav.affiliateManagement', 'Affiliate Management'),
            icon: '👥',
            path: '/admin/affiliates',
            requiredRoles: ['Owner', 'Admin'],
        },
        // Storefront administration information architecture (Owner/Admin only)
        localizeStorefrontNode(STOREFRONT_NAVIGATION),
        // 10) Reports
        {
            key: 'reports',
            label: t('nav.reports'),
            icon: '📈',
            children: [
                // ═══════════════════════════════════════════════════════════════
                // OPERATIONAL ANALYTICS REPORTS
                // These reports read from business tables for operational insights.
                // For financial reports, use Finance Control Center.
                // ═══════════════════════════════════════════════════════════════
                { path: '/sales-history', label: t('nav.salesReport'), icon: '📊' },
                { path: '/reports/sales-summary', label: t('nav.salesSummary'), icon: '📑' },
                { path: '/reports/sales-return-report', label: t('salesReturnReport.title'), icon: '↩️' },
                { path: '/reports/purchase-return-report', label: t('purchaseReturnReport.title'), icon: '↩️' },
                { path: '/reports/sales-by-category', label: t('nav.salesByCategoryReport'), icon: '📂' },
                { path: '/reports/sales-by-payment-method', label: t('nav.salesByPaymentMethod'), icon: '💳' },
                { path: '/reports/sales-analysis', label: t('nav.salesAnalysis'), icon: '📈' },
                { path: '/reports/purchase-register', label: t('purchaseRegister.title'), icon: '📦' },
                { path: '/reports/purchases-by-supplier', label: t('reports.purchasesBySupplier'), icon: '🏬' },
                { path: '/reports/cost-analysis', label: t('costAnalysis.title'), icon: '📊' },
                { path: '/reports/inventory-valuation', label: t('nav.inventoryValuation'), icon: '🏭' },
                { path: '/reports/product-movement', label: t('productMovement.title'), icon: '📋' },
                { path: '/reports/ar-aging', label: t('arAging.title'), icon: '📅' },
                { path: '/reports/ap-aging', label: t('apAging.title'), icon: '📆' },
                { path: '/sales-rep-reports', label: t('nav.salesRepReports'), icon: '👥' }
                // ═══════════════════════════════════════════════════════════════
                // FINANCIAL REPORTS MOVED TO FINANCE CONTROL CENTER
                // Trial Balance, P&L, Balance Sheet → /treasury/*
                // ═══════════════════════════════════════════════════════════════
            ]
        },
        // Finance Control Center (Phase 5) - Restricted to finance roles
        ...(user?.role === 'Admin' || user?.role === 'SuperAdmin' || user?.role === 'Owner' || user?.role === 'Accountant' ? [{
            key: 'finance_control',
            label: t('nav.financeControlCenter', 'Finance Control Center'),
            icon: '🏦',
            children: [
                { path: '/treasury', label: t('treasury.dashboard', 'Treasury Dashboard'), icon: '📊' },
                { path: '/treasury/trial-balance', label: t('treasury.trialBalance', 'Trial Balance'), icon: '⚖️' },
                { path: '/treasury/profit-loss', label: t('treasury.profitLoss', 'Profit & Loss'), icon: '📈' },
                { path: '/treasury/balance-sheet', label: t('treasury.balanceSheet', 'Balance Sheet'), icon: '📋' },
                { path: '/treasury/cash-flow', label: t('treasury.cashFlow', 'Cash Flow'), icon: '💧' },
                { path: '/treasury/general-ledger', label: t('treasury.generalLedger', 'General Ledger'), icon: '📖' },
                { path: '/treasury/clearing-aging', label: t('treasury.clearingAging', 'Clearing Aging'), icon: '⚠️' },
                { path: '/treasury/transfers', label: t('treasury.transfers', 'Treasury Transfers'), icon: '💸' }
            ]
        }] : []),
        // 11) Users
        ...(user?.role === 'Admin' || user?.role === 'SuperAdmin' ? [{
            key: 'users',
            label: t('nav.users'),
            icon: '👤',
            children: [
                { path: '/settings/audit-log', label: t('nav.trackingUsers'), icon: '📜' }
            ]
        }] : []),
        // 12) Settings
        {
            key: 'settings',
            label: t('settings.settings'),
            icon: '⚙️',
            path: '/settings'
        },
    ]

    // Filter menu items based on user roles
    // Backend returns single 'role' string (e.g., "Owner", "Admin")
    const userRole = user?.role
    const filterMenuNode = (item) => {
        if (item.requiresMarketplaceActor && !isServiceMarketplaceWorkflowActor(user, true)) {
            return null
        }

        if (item.requiredRoles?.length && !item.requiredRoles.includes(userRole)) {
            return null
        }

        const capability = item.capability || STORE_ADMIN_NAVIGATION_CAPABILITY_BY_PATH.get(item.path)
        if (capability && !canAccessCommerceHubVendorNavigationCapability(user, capability)) {
            return null
        }

        const children = item.children?.map(filterMenuNode).filter(Boolean)
        if (item.children && children.length === 0) {
            return null
        }

        return { ...item, children }
    }

    const filteredMenuItems = menuItems.map(filterMenuNode).filter(Boolean)

    const isMenuNodeActive = (node) => {
        if (node.screenIds) {
            return isStorefrontNodeActive(node, activeStorefrontScreenId)
        }
        if (node.path && isActive(node.path)) {
            return true
        }
        return node.children?.some(isMenuNodeActive) || false
    }

    useEffect(() => {
        try {
            window.localStorage.setItem(STOREFRONT_SIDEBAR_STATE_KEY, JSON.stringify(expandedMenus))
        } catch {
            // Navigation remains functional when browser storage is unavailable.
        }
    }, [expandedMenus])

    useEffect(() => {
        setExpandedMenus((previous) => {
            let hasChanges = false
            const nextValue = { ...previous }

            const expandActiveNodes = (items) => {
                items.forEach((item) => {
                    if (!item.children) return
                    if (isMenuNodeActive(item) && !nextValue[item.key]) {
                        nextValue[item.key] = true
                        hasChanges = true
                    }
                    expandActiveNodes(item.children)
                })
            }

            expandActiveNodes(filteredMenuItems)

            return hasChanges ? nextValue : previous
        })
    }, [location.pathname, location.search, userRole])

    const renderNestedNode = (node, depth = 1) => {
        const active = isMenuNodeActive(node)

        if (node.children) {
            const submenuId = `sidebar-submenu-${node.key}`
            return (
                <div key={node.key}>
                    <button
                        type="button"
                        aria-controls={submenuId}
                        aria-expanded={Boolean(expandedMenus[node.key])}
                        onClick={() => toggleMenu(node.key)}
                        className={`flex w-full items-center justify-between rounded-lg px-4 py-2.5 text-sm transition-all duration-200 ${active ? 'bg-white/15 text-white' : 'text-indigo-50 hover:bg-indigo-500/30'}`}
                    >
                        <span className="flex items-center gap-3">
                            {renderIcon(node.icon, 18)}
                            <span className="font-medium">{node.label}</span>
                        </span>
                        <svg aria-hidden="true" className={`h-4 w-4 transition-transform ${expandedMenus[node.key] ? 'rotate-180' : ''}`} fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 9l-7 7-7-7" />
                        </svg>
                    </button>
                    <div id={submenuId} className={`overflow-hidden transition-all duration-300 ${expandedMenus[node.key] ? 'max-h-[800px] opacity-100' : 'max-h-0 opacity-0'}`}>
                        <div className={`mt-1 space-y-1 ${isRTL ? 'pr-4' : 'pl-4'}`}>
                            {node.children.map((child) => renderNestedNode(child, depth + 1))}
                        </div>
                    </div>
                </div>
            )
        }

        return (
            <Link
                key={node.path}
                to={node.path}
                aria-current={active ? 'page' : undefined}
                onClick={onClose}
                className={`flex items-center gap-3 rounded-lg px-4 py-2.5 text-sm transition-all duration-200 ${active ? 'bg-white font-medium text-indigo-700 shadow-md' : 'text-indigo-50 hover:bg-indigo-500/30'}`}
            >
                {renderIcon(node.icon, depth > 1 ? 16 : 18)}
                <span>{node.label}</span>
            </Link>
        )
    }

    return (
        <>
            {/* Mobile Overlay */}
            {isOpen && (
                <div
                    className="fixed inset-0 bg-black bg-opacity-50 z-40 lg:hidden"
                    onClick={onClose}
                />
            )}

            {/* Sidebar */}
            <aside
                className={`fixed top-0 ${isRTL ? 'right-0' : 'left-0'} h-full bg-gradient-to-b from-indigo-600 to-indigo-800 text-white w-72 transform transition-transform duration-300 ease-in-out z-50 ${isOpen ? 'translate-x-0' : isRTL ? 'translate-x-full' : '-translate-x-full'
                    } lg:translate-x-0 shadow-2xl overflow-y-auto`}
                dir={dir}
            >
                {/* Sidebar Header */}
                <div className="p-6 border-b border-indigo-500/30">
                    <div className="flex items-center justify-between">
                        <div className="flex items-center gap-3">
                            <div className="w-12 h-12 bg-white rounded-xl flex items-center justify-center shadow-lg">
                                <span className="text-2xl">🏪</span>
                            </div>
                            <div>
                                <h2 className="text-xl font-bold">{t('app.name')}</h2>
                                <p className="text-xs text-indigo-200">{t('app.subtitle')}</p>
                            </div>
                        </div>
                        <button
                            type="button"
                            aria-label={t('common.close', 'Close navigation')}
                            onClick={onClose}
                            className="lg:hidden text-white hover:bg-indigo-500/30 rounded-lg p-2 transition-colors"
                        >
                            <svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                            </svg>
                        </button>
                    </div>
                </div>

                {/* Navigation Menu */}
                <nav aria-label={t('nav.mainNavigation', 'Main navigation')} className="p-4 space-y-1">
                    {filteredMenuItems.map((item) => (
                        <div key={item.key}>
                            {item.children ? (
                                // Menu with children (expandable)
                                <div>
                                    <button
                                        type="button"
                                        aria-controls={`sidebar-submenu-${item.key}`}
                                        aria-expanded={Boolean(expandedMenus[item.key])}
                                        onClick={() => toggleMenu(item.key)}
                                        className={`w-full flex items-center justify-between px-4 py-3 rounded-lg transition-all duration-200 group ${isMenuNodeActive(item)
                                            ? 'bg-white/15 text-white'
                                            : 'hover:bg-indigo-500/30'
                                            }`}
                                    >
                                        <div className="flex items-center gap-3">
                                            <span className="text-xl">{renderIcon(item.icon)}</span>
                                            <span className="font-medium">{item.label}</span>
                                        </div>
                                        <svg
                                            className={`w-5 h-5 transition-transform duration-200 ${expandedMenus[item.key] ? 'rotate-180' : ''
                                                }`}
                                            fill="none"
                                            stroke="currentColor"
                                            viewBox="0 0 24 24"
                                        >
                                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 9l-7 7-7-7" />
                                        </svg>
                                    </button>

                                    {/* Submenu */}
                                    <div
                                        id={`sidebar-submenu-${item.key}`}
                                        className={`overflow-hidden transition-all duration-300 ${expandedMenus[item.key] ? 'max-h-[1200px] opacity-100 mt-1' : 'max-h-0 opacity-0'
                                            }`}
                                    >
                                        <div className={`space-y-1 ${isRTL ? 'pr-4' : 'pl-4'}`}>
                                            {item.children.map((child) => renderNestedNode(child))}
                                        </div>
                                    </div>
                                </div>
                            ) : (
                                // Single menu item (no children)
                                <Link
                                    to={item.path}
                                    aria-current={isMenuNodeActive(item) ? 'page' : undefined}
                                    onClick={onClose}
                                    className={`flex items-center gap-3 px-4 py-3 rounded-lg transition-all duration-200 ${isMenuNodeActive(item)
                                        ? 'bg-white text-indigo-700 shadow-md font-medium'
                                        : 'hover:bg-indigo-500/30'
                                        }`}
                                >
                                    <span className="text-xl">{renderIcon(item.icon)}</span>
                                    <span className="font-medium">{item.label}</span>
                                </Link>
                            )}
                        </div>
                    ))}
                </nav>
            </aside>
        </>
    )
}
