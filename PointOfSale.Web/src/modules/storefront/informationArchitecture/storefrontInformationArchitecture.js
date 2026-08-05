export const STOREFRONT_SIDEBAR_STATE_KEY = 'pointofsale.storefront.sidebar.expansion.v1'

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
            fallbackLabel: 'Overview',
            icon: 'LayoutDashboard',
        },
        {
            screenIds: ['status'],
            path: '/storefront/status',
            labelKey: 'storefront.ia.status',
            fallbackLabel: 'Storefront Status',
            icon: 'Activity',
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

export const STOREFRONT_ROUTE_COMPATIBILITY = [
    { screenId: 'overview', canonical: '/storefront', legacy: [] },
    { screenId: 'status', canonical: '/storefront/status', legacy: ['/settings?tab=storefront'] },
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
]

const screen = (id, titleKey, fallbackTitle, breadcrumbs) => ({
    id,
    titleKey,
    fallbackTitle,
    breadcrumbs,
})

const breadcrumb = (labelKey, fallbackLabel, path = null) => ({ labelKey, fallbackLabel, path })

const ROOT = breadcrumb('storefront.ia.root', 'Storefront', '/storefront')
const CATALOG = breadcrumb('storefront.ia.catalog', 'Catalog', '/storefront/catalog/product-template')
const CONTENT = breadcrumb('storefront.ia.content', 'Content', '/storefront/content/pages')
const PAGES = breadcrumb('storefront.ia.pages', 'Pages', '/storefront/content/pages')
const NAVIGATION = breadcrumb('storefront.ia.navigation', 'Navigation', '/storefront/navigation?view=header')
const APPEARANCE = breadcrumb('storefront.ia.appearance', 'Appearance', '/storefront/appearance/themes')

const SCREENS = {
    overview: screen('overview', 'storefront.ia.overview', 'Overview', [
        ROOT,
        breadcrumb('storefront.ia.overview', 'Overview'),
    ]),
    status: screen('status', 'storefront.ia.status', 'Storefront Status', [
        ROOT,
        breadcrumb('storefront.ia.status', 'Storefront Status'),
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
}

const normalizePath = (pathname) => {
    const normalized = pathname.length > 1 ? pathname.replace(/\/+$/, '') : pathname
    return normalized.toLowerCase()
}

export function resolveStorefrontScreen(pathname, search = '') {
    const path = normalizePath(pathname)
    const params = new URLSearchParams(search)

    if (path === '/settings' && params.get('tab') === 'storefront') return SCREENS.status
    if (path === '/storefront' || path === '/storefront/overview') return SCREENS.overview
    if (path === '/storefront/status') return SCREENS.status
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

    return null
}

export function isStorefrontNodeActive(node, activeScreenId) {
    if (!activeScreenId) return false
    if (node.screenIds?.includes(activeScreenId)) return true
    return node.children?.some((child) => isStorefrontNodeActive(child, activeScreenId)) || false
}
