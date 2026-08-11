export const STOREFRONT_SIDEBAR_STATE_KEY = 'pointofsale.storefront.sidebar.expansion.v1'
export const STOREFRONT_OPERATIONS_SIDEBAR_STATE_KEY = 'pointofsale.storefront.operations.sidebar.expansion.v1'

export const STOREFRONT_NAVIGATION = {
    key: 'storefront',
    labelKey: 'storefront.ia.root',
    fallbackLabel: 'Storefront',
    icon: 'Store',
    children: [
        {
            screenIds: ['overview'],
            path: '/storefront',
            labelKey: 'storefront.ia.overview',
            fallbackLabel: 'Storefront Dashboard',
            icon: 'LayoutDashboard',
        },
        {
            screenIds: ['status'],
            path: '/storefront/status',
            labelKey: 'storefront.ia.status',
            fallbackLabel: 'Storefront Settings',
            icon: 'Activity',
        },
        {
            screenIds: ['checkout-fields'],
            path: '/storefront/checkout-fields',
            labelKey: 'checkoutFields.title',
            fallbackLabel: 'Checkout Fields',
            icon: 'Files',
        },
        {
            key: 'storefront-domains',
            labelKey: 'storefront.ia.domains',
            fallbackLabel: 'Domains',
            icon: 'Globe',
            children: [
                {
                    screenIds: ['domains'],
                    path: '/storefront/domains',
                    labelKey: 'storefront.ia.domains',
                    fallbackLabel: 'Domains',
                    icon: 'Globe',
                },
                {
                    screenIds: ['domains-activity'],
                    path: '/storefront/domains/activity',
                    labelKey: 'storefront.ia.domainsActivity',
                    fallbackLabel: 'Activity',
                    icon: 'Activity',
                },
            ],
        },
        {
            screenIds: ['homepage'],
            path: '/storefront/homepage',
            labelKey: 'storefront.ia.homepage',
            fallbackLabel: 'Homepage',
            icon: 'Home',
        },
        {
            key: 'storefront-catalog',
            labelKey: 'storefront.ia.catalog',
            fallbackLabel: 'Catalog',
            icon: 'ShoppingBag',
            children: [
                {
                    screenIds: ['product-template'],
                    path: '/storefront/catalog/product-template',
                    labelKey: 'storefront.ia.productTemplate',
                    fallbackLabel: 'Product Template',
                    icon: 'PanelsTopLeft',
                },
            ],
        },
        {
            key: 'storefront-content',
            labelKey: 'storefront.ia.content',
            fallbackLabel: 'Content',
            icon: 'FileText',
            children: [
                {
                    screenIds: ['pages', 'page-editor'],
                    path: '/storefront/content/pages',
                    labelKey: 'storefront.ia.pages',
                    fallbackLabel: 'Pages',
                    icon: 'Files',
                },
            ],
        },
        {
            key: 'storefront-navigation',
            labelKey: 'storefront.ia.navigation',
            fallbackLabel: 'Navigation',
            icon: 'Navigation',
            children: [
                {
                    screenIds: ['navigation-header'],
                    path: '/storefront/navigation?view=header',
                    labelKey: 'storefront.ia.header',
                    fallbackLabel: 'Header',
                    icon: 'PanelTop',
                },
                {
                    screenIds: ['navigation-footer'],
                    path: '/storefront/navigation?view=footer',
                    labelKey: 'storefront.ia.footerLinks',
                    fallbackLabel: 'Footer Links',
                    icon: 'PanelBottom',
                },
            ],
        },
        {
            screenIds: ['footer-content'],
            path: '/storefront/footer-content',
            labelKey: 'storefront.ia.footerContent',
            fallbackLabel: 'Footer Content',
            icon: 'PanelBottomDashed',
        },
        {
            key: 'storefront-appearance',
            labelKey: 'storefront.ia.appearance',
            fallbackLabel: 'Appearance',
            icon: 'Palette',
            children: [
                {
                    screenIds: ['themes'],
                    path: '/storefront/appearance/themes',
                    labelKey: 'storefront.ia.themes',
                    fallbackLabel: 'Themes',
                    icon: 'LayoutTemplate',
                },
                {
                    screenIds: ['colors-typography'],
                    path: '/storefront/appearance/colors-typography',
                    labelKey: 'storefront.ia.colorsTypography',
                    fallbackLabel: 'Colors & Typography',
                    icon: 'Paintbrush',
                },
            ],
        },
        {
            screenIds: ['storeconfig-history'],
            path: '/storefront/storeconfig-version-history',
            labelKey: 'storefront.ia.storeConfigVersionHistory',
            fallbackLabel: 'StoreConfig Version History',
            icon: 'History',
        },
    ],
}

export const OPERATIONS_NAVIGATION = {
    key: 'operations',
    labelKey: 'storefront.operations',
    fallbackLabel: 'Operations',
    icon: 'ShoppingBasket',
    requiredRoles: ['Owner', 'Admin'],
    children: [
        {
            screenIds: ['operations-overview'],
            path: '/ecommerce/overview',
            labelKey: 'storefront.operationsOverview',
            fallbackLabel: 'Overview',
            icon: 'LayoutDashboard',
        },
        {
            key: 'operations-orders',
            labelKey: 'storefront.operationsOrders',
            fallbackLabel: 'Orders',
            icon: 'Package',
            children: [
                {
                    screenIds: ['orders'],
                    path: '/ecommerce/orders',
                    labelKey: 'storefront.operationsOrders',
                    fallbackLabel: 'Orders',
                    icon: 'Package',
                },
                {
                    screenIds: ['abandoned-checkouts'],
                    path: '/ecommerce/abandoned-checkouts',
                    labelKey: 'storefront.abandonedCheckouts',
                    fallbackLabel: 'Abandoned Checkouts',
                    icon: 'ShoppingBasket',
                },
                {
                    screenIds: ['cart-recovery'],
                    path: '/ecommerce/cart-recovery',
                    labelKey: 'nav.cartRecovery',
                    fallbackLabel: 'Abandoned Cart Recovery',
                    icon: 'ShoppingBasket',
                },
                {
                    screenIds: ['blocked-phones'],
                    path: '/ecommerce/blocked-phones',
                    labelKey: 'nav.blockedPhones',
                    fallbackLabel: 'Blocked Phones',
                    icon: 'Shield',
                },
                {
                    screenIds: ['online-customers'],
                    path: '/ecommerce/online-customers',
                    labelKey: 'storefront.onlineCustomers',
                    fallbackLabel: 'Online Customers',
                    icon: 'Store',
                },
                {
                    screenIds: ['whatsapp'],
                    path: '/ecommerce/whatsapp',
                    labelKey: 'nav.whatsappSettings',
                    fallbackLabel: 'WhatsApp Settings',
                    icon: 'Bot',
                },
            ],
        },
        {
            key: 'operations-payments',
            labelKey: 'storefront.operationsPayments',
            fallbackLabel: 'Payments',
            icon: 'CreditCard',
            children: [
                {
                    screenIds: ['payment-methods'],
                    path: '/ecommerce/payment-methods',
                    labelKey: 'nav.paymentMethods',
                    fallbackLabel: 'Payment Methods',
                    icon: 'CreditCard',
                },
                {
                    screenIds: ['manual-payments'],
                    path: '/ecommerce/manual-payments',
                    labelKey: 'nav.manualPayments',
                    fallbackLabel: 'Manual Payments',
                    icon: 'CreditCard',
                },
            ],
        },
        {
            key: 'operations-catalog',
            labelKey: 'storefront.operationsCatalog',
            fallbackLabel: 'Catalog',
            icon: 'Tag',
            children: [
                {
                    screenIds: ['coupons'],
                    path: '/ecommerce/coupons',
                    labelKey: 'coupons.title',
                    fallbackLabel: 'Coupons',
                    icon: 'Tag',
                },
                {
                    screenIds: ['promotions'],
                    path: '/ecommerce/promotions',
                    labelKey: 'promotions.title',
                    fallbackLabel: 'Promotions',
                    icon: 'Tag',
                },
                {
                    screenIds: ['smart-offers'],
                    path: '/ecommerce/smart-offers',
                    labelKey: 'nav.smartOffers',
                    fallbackLabel: 'Smart Offers',
                    icon: 'Target',
                },
            ],
        },
        {
            key: 'operations-shipping',
            labelKey: 'storefront.operationsShipping',
            fallbackLabel: 'Shipping',
            icon: 'Truck',
            requiredRoles: ['Owner', 'Admin'],
            children: [
                {
                    screenIds: ['shipping-methods'],
                    path: '/ecommerce/shipping-methods',
                    labelKey: 'storefront.shippingSetup',
                    fallbackLabel: 'Shipping Methods',
                    icon: 'Truck',
                    requiredRoles: ['Owner', 'Admin'],
                },
            ],
        },
        {
            key: 'operations-marketing',
            labelKey: 'storefront.operationsMarketing',
            fallbackLabel: 'Marketing',
            icon: 'BarChart3',
            children: [
                {
                    screenIds: ['pixel-tracking'],
                    path: '/ecommerce/pixel-tracking',
                    labelKey: 'nav.pixelTracking',
                    fallbackLabel: 'Pixel Tracking',
                    icon: 'Target',
                },
                {
                    screenIds: ['conversion-funnels'],
                    path: '/analytics/funnels',
                    labelKey: 'analytics.conversionFunnels',
                    fallbackLabel: 'Conversion Funnels',
                    icon: 'BarChart3',
                },
                {
                    key: 'operations-marketing-attribution',
                    labelKey: 'storefront.operationsMarketingAttribution',
                    fallbackLabel: 'Marketing Attribution',
                    icon: 'BarChart3',
                    children: [
                        {
                            screenIds: ['marketing-attribution'],
                            path: '/analytics/attribution',
                            labelKey: 'storefront.operationsOverview',
                            fallbackLabel: 'Overview',
                            icon: 'LayoutDashboard',
                        },
                        {
                            screenIds: ['campaign-costs'],
                            path: '/analytics/attribution/costs',
                            labelKey: 'analytics.campaignCosts',
                            fallbackLabel: 'Campaign Costs',
                            icon: 'CreditCard',
                        },
                        {
                            screenIds: ['marketing-roi'],
                            path: '/analytics/attribution/roi',
                            labelKey: 'analytics.marketingRoi',
                            fallbackLabel: 'Marketing ROI',
                            icon: 'BarChart3',
                        },
                    ],
                },
            ],
        },
        {
            screenIds: ['commerce-ai'],
            path: '/ecommerce/commerce-ai',
            labelKey: 'commerceAi.title',
            fallbackLabel: 'Commerce AI',
            icon: 'Bot',
        },
        {
            screenIds: ['auth-providers'],
            path: '/ecommerce/auth-providers',
            labelKey: 'nav.authProviders',
            fallbackLabel: 'Auth Providers',
            icon: 'Shield',
        },
        {
            key: 'operations-analytics',
            labelKey: 'storefront.operationsAnalytics',
            fallbackLabel: 'Analytics',
            icon: 'BarChart3',
            children: [
                {
                    screenIds: ['storefront-analytics'],
                    path: '/analytics/storefront',
                    labelKey: 'storefrontAnalytics.title',
                    fallbackLabel: 'Storefront Analytics',
                    icon: 'BarChart3',
                },
                {
                    screenIds: ['analytics-settings'],
                    path: '/settings/analytics',
                    labelKey: 'nav.analyticsSettings',
                    fallbackLabel: 'Analytics Settings',
                    icon: 'Settings',
                },
            ],
        },
    ],
}

export const STOREFRONT_PRODUCT_NAVIGATION = {
    key: 'storefront',
    labelKey: 'storefront.ia.root',
    fallbackLabel: 'Storefront',
    icon: 'Store',
    requiredRoles: ['Owner', 'Admin'],
    children: [
        {
            ...STOREFRONT_NAVIGATION,
            key: 'storefront-design',
            labelKey: 'storefront.design',
            fallbackLabel: 'Design',
            icon: 'Palette',
            requiredRoles: ['Owner', 'Admin'],
        },
        OPERATIONS_NAVIGATION,
    ],
}

export const STOREFRONT_ROUTE_COMPATIBILITY = [
    { screenId: 'overview', canonical: '/storefront', legacy: [] },
    { screenId: 'status', canonical: '/storefront/status', legacy: ['/settings?tab=storefront'] },
    { screenId: 'checkout-fields', canonical: '/storefront/checkout-fields', legacy: [] },
    { screenId: 'homepage', canonical: '/storefront/homepage', legacy: ['/storefront/builder?page=home'] },
    {
        screenId: 'product-template',
        canonical: '/storefront/catalog/product-template',
        legacy: ['/storefront/builder?page=product-template&templateId={templateId}'],
    },
    { screenId: 'pages', canonical: '/storefront/content/pages', legacy: ['/storefront/cms/pages'] },
    {
        screenId: 'page-editor',
        canonical: '/storefront/content/pages/{pageId}',
        legacy: ['/storefront/cms/pages/{pageId}'],
    },
    {
        screenId: 'navigation-header',
        canonical: '/storefront/navigation?view=header',
        legacy: ['/storefront/cms/navigation', '/storefront/cms/navigation?view=header'],
    },
    {
        screenId: 'navigation-footer',
        canonical: '/storefront/navigation?view=footer',
        legacy: ['/storefront/cms/navigation?view=footer'],
    },
    { screenId: 'footer-content', canonical: '/storefront/footer-content', legacy: ['/storefront/cms/footer'] },
    { screenId: 'themes', canonical: '/storefront/appearance/themes', legacy: ['/storefront/themes'] },
    {
        screenId: 'colors-typography',
        canonical: '/storefront/appearance/colors-typography',
        legacy: ['/storefront/theme'],
    },
    {
        screenId: 'storeconfig-history',
        canonical: '/storefront/storeconfig-version-history',
        legacy: ['/storefront/versions'],
    },
    {
        screenId: 'domains',
        canonical: '/storefront/domains',
        legacy: [],
    },
    {
        screenId: 'domain-detail',
        canonical: '/storefront/domains/:id',
        legacy: [],
    },
    {
        screenId: 'domain-add',
        canonical: '/storefront/domains/add',
        legacy: [],
    },
    {
        screenId: 'domain-list',
        canonical: '/storefront/domains/list',
        legacy: [],
    },
    {
        screenId: 'dns-verification',
        canonical: '/storefront/domains/:id/verification',
        legacy: [],
    },
    {
        screenId: 'ssl-management',
        canonical: '/storefront/domains/:id/ssl',
        legacy: [],
    },
    {
        screenId: 'redirects',
        canonical: '/storefront/domains/:id/redirects',
        legacy: [],
    },
    {
        screenId: 'domain-history',
        canonical: '/storefront/domains/:id/history',
        legacy: [],
    },
    {
        screenId: 'domains-activity',
        canonical: '/storefront/domains/activity',
        legacy: [],
    },
]

const screen = (id, titleKey, fallbackTitle, breadcrumbs) => ({
    id,
    titleKey,
    fallbackTitle,
    breadcrumbs,
})

const breadcrumb = (labelKey, fallbackLabel, path = null) => ({ labelKey, fallbackLabel, path })

const ROOT = breadcrumb('storefront.ia.root', 'Storefront', '/storefront')
const OPERATIONS = breadcrumb('storefront.operations', 'Operations', '/ecommerce/overview')
const OPERATION_ORDERS = breadcrumb('storefront.operationsOrders', 'Orders', '/ecommerce/orders')
const OPERATION_PAYMENTS = breadcrumb('storefront.operationsPayments', 'Payments', '/ecommerce/payment-methods')
const OPERATION_CATALOG = breadcrumb('storefront.operationsCatalog', 'Catalog', '/ecommerce/coupons')
const OPERATION_SHIPPING = breadcrumb('storefront.operationsShipping', 'Shipping', '/ecommerce/shipping-methods')
const OPERATION_MARKETING = breadcrumb('storefront.operationsMarketing', 'Marketing', '/ecommerce/pixel-tracking')
const OPERATION_ATTRIBUTION = breadcrumb(
    'storefront.operationsMarketingAttribution',
    'Marketing Attribution',
    '/analytics/attribution',
)
const OPERATION_ANALYTICS = breadcrumb('storefront.operationsAnalytics', 'Analytics', '/analytics/storefront')
const CATALOG = breadcrumb('storefront.ia.catalog', 'Catalog', '/storefront/catalog/product-template')
const CONTENT = breadcrumb('storefront.ia.content', 'Content', '/storefront/content/pages')
const PAGES = breadcrumb('storefront.ia.pages', 'Pages', '/storefront/content/pages')
const NAVIGATION = breadcrumb('storefront.ia.navigation', 'Navigation', '/storefront/navigation?view=header')
const APPEARANCE = breadcrumb('storefront.ia.appearance', 'Appearance', '/storefront/appearance/themes')
const DOMAINS = breadcrumb('storefront.ia.domains', 'Domains', '/storefront/domains')

export const SCREENS = {
    overview: screen('overview', 'storefront.ia.overview', 'Overview', [
        ROOT,
        breadcrumb('storefront.ia.overview', 'Storefront Dashboard'),
    ]),
    status: screen('status', 'storefront.ia.status', 'Storefront Settings', [
        ROOT,
        breadcrumb('storefront.ia.status', 'Storefront Settings'),
    ]),
    'checkout-fields': screen('checkout-fields', 'checkoutFields.title', 'Checkout Fields', [
        ROOT,
        breadcrumb('checkoutFields.title', 'Checkout Fields'),
    ]),
    homepage: screen('homepage', 'storefront.ia.homepage', 'Homepage', [
        ROOT,
        breadcrumb('storefront.ia.homepage', 'Homepage'),
    ]),
    'product-template': screen('product-template', 'storefront.ia.productTemplate', 'Product Template', [
        ROOT,
        CATALOG,
        breadcrumb('storefront.ia.productTemplate', 'Product Template'),
    ]),
    pages: screen('pages', 'storefront.ia.pages', 'Pages', [
        ROOT,
        CONTENT,
        breadcrumb('storefront.ia.pages', 'Pages'),
    ]),
    'page-editor': screen('page-editor', 'storefront.ia.page', 'Page', [
        ROOT,
        CONTENT,
        PAGES,
        breadcrumb('storefront.ia.page', 'Page'),
    ]),
    'navigation-header': screen('navigation-header', 'storefront.ia.header', 'Header', [
        ROOT,
        NAVIGATION,
        breadcrumb('storefront.ia.header', 'Header'),
    ]),
    'navigation-footer': screen('navigation-footer', 'storefront.ia.footerLinks', 'Footer Links', [
        ROOT,
        NAVIGATION,
        breadcrumb('storefront.ia.footerLinks', 'Footer Links'),
    ]),
    'footer-content': screen('footer-content', 'storefront.ia.footerContent', 'Footer Content', [
        ROOT,
        breadcrumb('storefront.ia.footerContent', 'Footer Content'),
    ]),
    themes: screen('themes', 'storefront.ia.themes', 'Themes', [
        ROOT,
        APPEARANCE,
        breadcrumb('storefront.ia.themes', 'Themes'),
    ]),
    'colors-typography': screen('colors-typography', 'storefront.ia.colorsTypography', 'Colors & Typography', [
        ROOT,
        APPEARANCE,
        breadcrumb('storefront.ia.colorsTypography', 'Colors & Typography'),
    ]),
    'storeconfig-history': screen(
        'storeconfig-history',
        'storefront.ia.storeConfigVersionHistory',
        'StoreConfig Version History',
        [ROOT, breadcrumb('storefront.ia.storeConfigVersionHistory', 'StoreConfig Version History')],
    ),
    domains: screen('domains', 'storefront.ia.domains', 'Domains', [
        ROOT,
        breadcrumb('storefront.ia.root', 'Home', '/storefront'),
        breadcrumb('storefront.ia.domains', 'Domains'),
    ]),
    'domain-detail': screen('domain-detail', 'storefront.ia.domainDetail', 'Domain Detail', [
        ROOT,
        breadcrumb('storefront.ia.root', 'Home', '/storefront'),
        breadcrumb('storefront.ia.domains', 'Domains', '/storefront/domains'),
        breadcrumb('storefront.ia.domainDetail', 'Domain'),
    ]),
    'domain-add': screen('domain-add', 'storefront.ia.addDomain', 'Add Domain', [
        ROOT,
        breadcrumb('storefront.ia.root', 'Home', '/storefront'),
        breadcrumb('storefront.ia.domains', 'Domains', '/storefront/domains'),
        breadcrumb('storefront.ia.addDomain', 'Add Domain'),
    ]),
    'domain-list': screen('domain-list', 'storefront.ia.domainList', 'Domain List', [
        ROOT,
        breadcrumb('storefront.ia.root', 'Home', '/storefront'),
        breadcrumb('storefront.ia.domains', 'Domains', '/storefront/domains'),
        breadcrumb('storefront.ia.domainList', 'Domain List'),
    ]),
    'dns-verification': screen('dns-verification', 'storefront.ia.dnsVerification', 'DNS Verification', [
        ROOT,
        breadcrumb('storefront.ia.root', 'Home', '/storefront'),
        breadcrumb('storefront.ia.domains', 'Domains', '/storefront/domains'),
        breadcrumb('storefront.ia.domainDetail', 'Domain'),
        breadcrumb('storefront.ia.dnsVerification', 'DNS Verification'),
    ]),
    'ssl-management': screen('ssl-management', 'storefront.ia.sslManagement', 'SSL Management', [
        ROOT,
        breadcrumb('storefront.ia.root', 'Home', '/storefront'),
        breadcrumb('storefront.ia.domains', 'Domains', '/storefront/domains'),
        breadcrumb('storefront.ia.domainDetail', 'Domain'),
        breadcrumb('storefront.ia.sslManagement', 'SSL Management'),
    ]),
    redirects: screen('redirects', 'storefront.ia.redirects', 'Redirects', [
        ROOT,
        breadcrumb('storefront.ia.root', 'Home', '/storefront'),
        breadcrumb('storefront.ia.domains', 'Domains', '/storefront/domains'),
        breadcrumb('storefront.ia.domainDetail', 'Domain'),
        breadcrumb('storefront.ia.redirects', 'Redirects'),
    ]),
    'domain-history': screen('domain-history', 'storefront.ia.domainHistory', 'History', [
        ROOT,
        breadcrumb('storefront.ia.root', 'Home', '/storefront'),
        breadcrumb('storefront.ia.domains', 'Domains', '/storefront/domains'),
        breadcrumb('storefront.ia.domainDetail', 'Domain'),
        breadcrumb('storefront.ia.domainHistory', 'History'),
    ]),
    'domains-activity': screen('domains-activity', 'storefront.ia.domainsActivity', 'Activity', [
        ROOT,
        breadcrumb('storefront.ia.root', 'Home', '/storefront'),
        breadcrumb('storefront.ia.domains', 'Domains', '/storefront/domains'),
        breadcrumb('storefront.ia.domainsActivity', 'Activity'),
    ]),
    'operations-overview': screen('operations-overview', 'storefront.operationsOverview', 'Overview', [
        ROOT,
        breadcrumb('storefront.operationsOverview', 'Overview'),
    ]),
    orders: screen('orders', 'storefront.operationsOrders', 'Orders', [
        ROOT,
        OPERATIONS,
        OPERATION_ORDERS,
        breadcrumb('storefront.operationsOrders', 'Orders'),
    ]),
    'abandoned-checkouts': screen(
        'abandoned-checkouts',
        'storefront.abandonedCheckouts',
        'Abandoned Checkouts',
        [ROOT, OPERATIONS, OPERATION_ORDERS, breadcrumb('storefront.abandonedCheckouts', 'Abandoned Checkouts')],
    ),
    'cart-recovery': screen('cart-recovery', 'nav.cartRecovery', 'Abandoned Cart Recovery', [
        ROOT,
        OPERATIONS,
        OPERATION_ORDERS,
        breadcrumb('nav.cartRecovery', 'Abandoned Cart Recovery'),
    ]),
    'blocked-phones': screen('blocked-phones', 'nav.blockedPhones', 'Blocked Phones', [
        ROOT,
        OPERATIONS,
        OPERATION_ORDERS,
        breadcrumb('nav.blockedPhones', 'Blocked Phones'),
    ]),
    'online-customers': screen('online-customers', 'storefront.onlineCustomers', 'Online Customers', [
        ROOT,
        OPERATIONS,
        OPERATION_ORDERS,
        breadcrumb('storefront.onlineCustomers', 'Online Customers'),
    ]),
    whatsapp: screen('whatsapp', 'nav.whatsappSettings', 'WhatsApp Settings', [
        ROOT,
        OPERATIONS,
        OPERATION_ORDERS,
        breadcrumb('nav.whatsappSettings', 'WhatsApp Settings'),
    ]),
    'payment-methods': screen('payment-methods', 'nav.paymentMethods', 'Payment Methods', [
        ROOT,
        OPERATIONS,
        OPERATION_PAYMENTS,
        breadcrumb('nav.paymentMethods', 'Payment Methods'),
    ]),
    'manual-payments': screen('manual-payments', 'nav.manualPayments', 'Manual Payments', [
        ROOT,
        OPERATIONS,
        OPERATION_PAYMENTS,
        breadcrumb('nav.manualPayments', 'Manual Payments'),
    ]),
    coupons: screen('coupons', 'coupons.title', 'Coupons', [
        ROOT,
        OPERATIONS,
        OPERATION_CATALOG,
        breadcrumb('coupons.title', 'Coupons'),
    ]),
    promotions: screen('promotions', 'promotions.title', 'Promotions', [
        ROOT,
        OPERATIONS,
        OPERATION_CATALOG,
        breadcrumb('promotions.title', 'Promotions'),
    ]),
    'smart-offers': screen('smart-offers', 'nav.smartOffers', 'Smart Offers', [
        ROOT,
        OPERATIONS,
        OPERATION_CATALOG,
        breadcrumb('nav.smartOffers', 'Smart Offers'),
    ]),
    'shipping-methods': screen('shipping-methods', 'storefront.shippingSetup', 'Shipping Methods', [
        ROOT,
        OPERATIONS,
        OPERATION_SHIPPING,
        breadcrumb('storefront.shippingSetup', 'Shipping Methods'),
    ]),
    'pixel-tracking': screen('pixel-tracking', 'nav.pixelTracking', 'Pixel Tracking', [
        ROOT,
        OPERATIONS,
        OPERATION_MARKETING,
        breadcrumb('nav.pixelTracking', 'Pixel Tracking'),
    ]),
    'conversion-funnels': screen('conversion-funnels', 'analytics.conversionFunnels', 'Conversion Funnels', [
        ROOT,
        OPERATIONS,
        OPERATION_MARKETING,
        breadcrumb('analytics.conversionFunnels', 'Conversion Funnels'),
    ]),
    'marketing-attribution': screen(
        'marketing-attribution',
        'storefront.operationsMarketingAttribution',
        'Marketing Attribution',
        [ROOT, OPERATIONS, OPERATION_MARKETING, breadcrumb('storefront.operationsMarketingAttribution', 'Marketing Attribution')],
    ),
    'campaign-costs': screen('campaign-costs', 'analytics.campaignCosts', 'Campaign Costs', [
        ROOT,
        OPERATIONS,
        OPERATION_MARKETING,
        OPERATION_ATTRIBUTION,
        OPERATION_ATTRIBUTION,
        breadcrumb('analytics.campaignCosts', 'Campaign Costs'),
    ]),
    'marketing-roi': screen('marketing-roi', 'analytics.marketingRoi', 'Marketing ROI', [
        ROOT,
        OPERATIONS,
        OPERATION_MARKETING,
        OPERATION_ATTRIBUTION,
        OPERATION_ATTRIBUTION,
        breadcrumb('analytics.marketingRoi', 'Marketing ROI'),
    ]),
    'commerce-ai': screen('commerce-ai', 'commerceAi.title', 'Commerce AI', [
        ROOT,
        OPERATIONS,
        breadcrumb('commerceAi.title', 'Commerce AI'),
    ]),
    'auth-providers': screen('auth-providers', 'nav.authProviders', 'Auth Providers', [
        ROOT,
        OPERATIONS,
        breadcrumb('nav.authProviders', 'Auth Providers'),
    ]),
    'storefront-analytics': screen('storefront-analytics', 'storefrontAnalytics.title', 'Storefront Analytics', [
        ROOT,
        OPERATIONS,
        OPERATION_ANALYTICS,
        breadcrumb('storefrontAnalytics.title', 'Storefront Analytics'),
    ]),
    'analytics-settings': screen('analytics-settings', 'nav.analyticsSettings', 'Analytics Settings', [
        ROOT,
        OPERATIONS,
        OPERATION_ANALYTICS,
        breadcrumb('nav.analyticsSettings', 'Analytics Settings'),
    ]),
}

const normalizePath = (pathname) => {
    const [pathOnly] = pathname.split('?')
    const normalized = pathOnly.length > 1 ? pathOnly.replace(/\/+$/, '') : pathOnly
    return normalized.toLowerCase()
}

export function resolveStorefrontScreen(pathname, search = '') {
    const path = normalizePath(pathname)
    const inlineQuery = pathname.includes('?') ? pathname.substring(pathname.indexOf('?')) : ''
    const params = new URLSearchParams(search || inlineQuery)

    if (path === '/settings' && params.get('tab') === 'storefront') return SCREENS.status
    if (path === '/storefront' || path === '/storefront/overview') return SCREENS.overview
    if (path === '/storefront/status') return SCREENS.status
    if (path === '/storefront/checkout-fields') return SCREENS['checkout-fields']
    if (path === '/storefront/homepage') return SCREENS.homepage
    if (path === '/storefront/catalog/product-template') return SCREENS['product-template']
    if (path === '/storefront/builder') {
        return params.get('page') === 'product-template' ? SCREENS['product-template'] : SCREENS.homepage
    }
    if (path === '/storefront/content/pages' || path === '/storefront/cms/pages') return SCREENS.pages
    if (path.startsWith('/storefront/content/pages/') || path.startsWith('/storefront/cms/pages/')) {
        return SCREENS['page-editor']
    }
    if (path === '/storefront/navigation') {
        return params.get('view') === 'footer' ? SCREENS['navigation-footer'] : SCREENS['navigation-header']
    }
    if (path === '/storefront/cms/navigation') {
        return params.get('view') === 'footer' ? SCREENS['navigation-footer'] : SCREENS['navigation-header']
    }
    if (path === '/storefront/footer-content' || path === '/storefront/cms/footer') return SCREENS['footer-content']
    if (path === '/storefront/appearance/themes' || path === '/storefront/themes') return SCREENS.themes
    if (path === '/storefront/appearance/colors-typography' || path === '/storefront/theme') {
        return SCREENS['colors-typography']
    }
    if (path === '/storefront/storeconfig-version-history' || path === '/storefront/versions') {
        return SCREENS['storeconfig-history']
    }
    if (path === '/ecommerce/overview') return SCREENS['operations-overview']
    if (path === '/ecommerce/orders') return SCREENS.orders
    if (path.startsWith('/ecommerce/orders/')) return SCREENS.orders
    if (path === '/ecommerce/abandoned-checkouts') return SCREENS['abandoned-checkouts']
    if (path === '/ecommerce/cart-recovery') return SCREENS['cart-recovery']
    if (path === '/ecommerce/blocked-phones') return SCREENS['blocked-phones']
    if (path === '/ecommerce/online-customers') return SCREENS['online-customers']
    if (path === '/ecommerce/whatsapp') return SCREENS.whatsapp
    if (path === '/ecommerce/payment-methods') return SCREENS['payment-methods']
    if (path === '/ecommerce/manual-payments') return SCREENS['manual-payments']
    if (path === '/ecommerce/coupons') return SCREENS.coupons
    if (path === '/ecommerce/promotions') return SCREENS.promotions
    if (path === '/ecommerce/smart-offers') return SCREENS['smart-offers']
    if (path === '/ecommerce/shipping-methods') return SCREENS['shipping-methods']
    if (path === '/ecommerce/pixel-tracking') return SCREENS['pixel-tracking']
    if (path === '/analytics/funnels') return SCREENS['conversion-funnels']
    if (path === '/analytics/attribution') return SCREENS['marketing-attribution']
    if (path === '/analytics/attribution/costs') return SCREENS['campaign-costs']
    if (path === '/analytics/attribution/roi') return SCREENS['marketing-roi']
    if (path === '/analytics/storefront') return SCREENS['storefront-analytics']
    if (path === '/ecommerce/commerce-ai') return SCREENS['commerce-ai']
    if (path === '/ecommerce/auth-providers') return SCREENS['auth-providers']
    if (path === '/settings/analytics') return SCREENS['analytics-settings']

    // Domain Management routes
    if (path === '/storefront/domains/activity') return SCREENS['domains-activity']
    if (path === '/storefront/domains/list') return SCREENS['domain-list']
    if (path === '/storefront/domains/add') return SCREENS['domain-add']
    if (path === '/storefront/domains' || path === '/storefront/domains/') return SCREENS.domains
    if (path.startsWith('/storefront/domains/')) {
        const rest = path.replace(/^\/storefront\/domains\//, '').replace(/\/$/, '')
        if (/^[^/]+$/.test(rest)) return SCREENS['domain-detail']
        const [domainId, sub] = rest.split('/')
        if (sub === 'verification') return SCREENS['dns-verification']
        if (sub === 'ssl') return SCREENS['ssl-management']
        if (sub === 'redirects') return SCREENS.redirects
        if (sub === 'history') return SCREENS['domain-history']
    }

    return null
}

export function isStorefrontNodeActive(node, activeScreenId) {
    if (!activeScreenId) return false
    if (node.screenIds?.includes(activeScreenId)) return true
    return node.children?.some((child) => isStorefrontNodeActive(child, activeScreenId)) || false
}
