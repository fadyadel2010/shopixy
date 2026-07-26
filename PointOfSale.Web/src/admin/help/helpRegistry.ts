export const helpRegistry: Record<string, string> = {
    '/': 'dashboard',
    '/customers': 'customers',
    '/suppliers': 'suppliers',
    '/sales-representatives': 'sales-representatives',
    '/categories': 'categories',
    '/units': 'units',
    '/warehouses': 'warehouses',
    '/banks': 'banks',
    '/products': 'products',
    '/products/new': 'create-product',
    '/products/edit': 'edit-product',
    '/products/variants': 'product-variants',
    '/products/media': 'product-media',
    '/products/templates': 'product-templates',
    '/financial-accounts?tab=main': 'financial-main-accounts',
    '/financial-accounts?tab=sub': 'financial-sub-accounts',
    '/balance-transfer': 'balance-transfer',
    '/opening-balance/cashbox': 'opening-treasury',
    '/opening-balance/customers': 'opening-customers',
    '/opening-balance/suppliers': 'opening-suppliers',
    '/opening-balance/warehouse-stock': 'opening-warehouse-stock',
    '/pos': 'pos-sales',
    '/sales-returns': 'sales-returns',
    '/purchase-invoice': 'purchase-invoice',
    '/purchase-returns': 'purchase-returns',
    '/treasury': 'treasury-dashboard',
    '/treasury/hub': 'treasury-hub',
    '/treasury/account/1010': 'cash-account',
    '/treasury/account/1030': 'bank-account',
    '/treasury/account/1110': 'visa-clearing',
    '/payments': 'customer-supplier-payments',
    '/adjustments': 'customer-supplier-settlement',
    '/outstanding-balances': 'outstanding-balances',
    '/inventory': 'inventory-balance',
    '/stock-transfers': 'stock-transfers',
    '/barcode-printing': 'barcode-printing',
    '/expense-categories': 'expense-categories',
    '/expenses': 'expenses-movement',
    '/reports/expenses': 'expenses-report',
    '/statement-of-account': 'customer-supplier-statement',
    '/reports/bank-statement': 'bank-statement',
    '/reports/visa-statement': 'visa-statement',
    '/ecommerce/orders': 'ecommerce-orders',
    '/ecommerce/blocked-phones': 'blocked-phones',
    '/ecommerce/whatsapp': 'whatsapp-settings',
    '/ecommerce/cart-recovery': 'cart-recovery',
    '/ecommerce/abandoned-checkouts': 'abandoned-checkouts',
    '/admin/shipping/setup': 'shipping-setup',
    '/ecommerce/online-customers': 'online-customers',
    '/ecommerce/auth-providers': 'auth-providers',
    '/ecommerce/payment-methods': 'payment-methods',
    '/ecommerce/manual-payments': 'manual-payments',
    '/ecommerce/coupons': 'coupons',
    '/ecommerce/promotions': 'promotions',
    '/ecommerce/smart-offers': 'smart-offers',
    '/ecommerce/commerce-ai': 'commerce-ai',
    '/ecommerce/pixel-tracking': 'pixel-tracking',
    '/analytics/funnels': 'conversion-funnels',
    '/analytics/attribution': 'marketing-attribution',
    '/analytics/attribution/costs': 'campaign-costs',
    '/analytics/attribution/roi': 'marketing-roi',
    '/settings/analytics': 'analytics-settings',
}

const helpRouteMatchers = [
    {
        articleKey: 'create-product',
        match: (pathname: string) => pathname === '/products/new/edit',
    },
    {
        articleKey: 'edit-product',
        match: (pathname: string) => /^\/products\/[^/]+\/edit$/.test(pathname),
    },
    {
        articleKey: 'ecommerce-orders',
        match: (pathname: string) => /^\/ecommerce\/orders\/[^/]+$/.test(pathname),
    },
]

function normalizePath(pathname: string) {
    if (!pathname || pathname === '/') {
        return '/'
    }

    return pathname.replace(/\/+$/, '')
}

function normalizeSearch(search = '') {
    if (!search) {
        return ''
    }

    return search.startsWith('?') ? search : `?${search}`
}

function buildRouteKey(pathname: string, search = '') {
    return `${normalizePath(pathname)}${normalizeSearch(search)}`
}

export function getHelpArticleKey(pathname: string, search = '') {
    const normalizedPath = normalizePath(pathname)
    const routeKey = buildRouteKey(pathname, search)

    if (helpRegistry[routeKey]) {
        return helpRegistry[routeKey]
    }

    if (helpRegistry[normalizedPath]) {
        return helpRegistry[normalizedPath]
    }

    const matchedRoute = helpRouteMatchers.find((route) => route.match(normalizedPath))
    return matchedRoute?.articleKey ?? null
}
