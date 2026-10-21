import { matchRoutes, Navigate, parsePath } from 'react-router-dom'
import { buildAdminMenu } from '../components/Sidebar'
import PosPermissionRouteGuard from '../components/PosPermissionRouteGuard'
import CommerceHubVendorRouteGuard from '../components/CommerceHubVendorRouteGuard'
import { hasPosPermission } from '../auth/posPermissions'
import { canAccessCommerceHubVendorNavigationCapability } from '../auth/posVendorSessionAuthority'
import { isServiceMarketplaceWorkflowActor } from '../marketplace/authority'

// Search-only enrichment, keyed by existing translation identity, never by guessed URLs/roles.
export const NAVIGATION_ALIASES = {
    'nav.itemCard': ['Products', 'Product catalog', 'SKU', 'الأصناف', 'المنتجات', 'كتالوج المنتجات'],
    'nav.sales': ['POS', 'Point of sale', 'نقطة البيع', 'الكاشير'],
    'nav.customersRegistration': ['Customers', 'العملاء'],
    'nav.suppliersRegistration': ['Suppliers', 'الموردين'],
    'nav.commerceHubSettings': ['Marketplace publishing', 'Merchant catalog', 'نشر المنتجات', 'كتالوج التاجر'],
    'nav.paymentMethods': ['Payment options', 'طرق الدفع', 'وسائل الدفع'],
    'storefront.shippingSetup': ['Shipping Methods', 'Delivery', 'طرق الشحن', 'التوصيل'],
    'treasury.profitLoss': ['P&L', 'Profit and loss', 'الأرباح والخسائر'],
    'treasury.generalLedgerNav': ['GL', 'General ledger', 'دفتر الأستاذ'],
    'storefront.ia.productTemplate': ['Product template', 'قالب المنتجات'],
    'nav.affiliateProducts': ['Products', 'منتجات', 'التسويق بالعمولة'],
}

export const navigationLabel = (i18n, language, key, fallback) => {
    const value = i18n.getResource(language, 'translation', key)
    // Deliberate English fallback for a missing resource; completeness tests fail on missing keys.
    return typeof value === 'string' && value.trim() ? value : fallback || i18n.getFixedT('en')(key)
}

export function buildNavigationRegistry(routes, user, i18n) {
    if (!user?.id || !user?.tenantId) return []
    const destinations = new Map()
    const visit = (nodes, parents = [], application = 'pos', policies = []) => {
        nodes.forEach(node => {
            const descriptor = node.label
            const applicationId = node.application || application
            const branch = [...parents, descriptor]
            const inherited = [...policies, node]
            if (node.children) visit(node.children, branch, applicationId, inherited)
            if (!node.path || node.placeholder || node.children) return
            const to = parsePath(node.path)
            // Only explicit, internal, parameter-free menu contracts are launchable.
            if (!to.pathname?.startsWith('/') || to.pathname.startsWith('//') || to.hash) return
            const matches = matchRoutes(routes, to)
            const leaf = matches?.at(-1)?.route
            if (!leaf || leaf.path === '*' || leaf.path?.includes(':') || leaf.element?.type === Navigate) return
            if (!matches.some(match => match.route.handle?.application === 'pos')) return
            const routePolicies = matches.map(match => match.route.element).flatMap(element => {
                if (element?.type === PosPermissionRouteGuard) return [{ permission: element.props.permission }]
                if (element?.type === CommerceHubVendorRouteGuard) return [{ capability: element.props.capability }]
                return []
            })
            const allPolicies = [...inherited, ...routePolicies]
            if (allPolicies.some(policy =>
                (policy.permission && !hasPosPermission(user, policy.permission))
                || (policy.capability && !canAccessCommerceHubVendorNavigationCapability(user, policy.capability))
                || (policy.requiresMarketplaceActor && !isServiceMarketplaceWorkflowActor(user, true))
            )) return
            // Same actual destination in two Sidebar groups is one search result.
            const contract = JSON.stringify(to)
            if (destinations.has(contract)) return
            const labels = Object.fromEntries(['en', 'ar'].map(language => [language,
                navigationLabel(i18n, language, descriptor.key, descriptor.fallback),
            ]))
            const breadcrumbs = Object.fromEntries(['en', 'ar'].map(language => [language,
                branch.map(label => navigationLabel(i18n, language, label.key, label.fallback)),
            ]))
            const modules = Object.fromEntries(['en', 'ar'].map(language => [language,
                navigationLabel(i18n, language, `smartNavigation.modules.${applicationId}`),
            ]))
            destinations.set(contract, {
                id: `${applicationId}:${node.screenIds?.[0] || node.key || descriptor.key}`,
                to, routeId: leaf.id, application: applicationId, labelKey: descriptor.key,
                labels, breadcrumbs, modules, icon: node.icon,
                aliases: NAVIGATION_ALIASES[descriptor.key] || [],
                permissions: allPolicies.map(policy => policy.permission).filter(Boolean),
                capabilities: allPolicies.map(policy => policy.capability).filter(Boolean),
            })
        })
    }
    visit(buildAdminMenu((key, fallback) => ({ key, fallback }), user))
    return [...destinations.values()]
}

export function resolveNavigationVisit(entries, routes, location) {
    const routeId = matchRoutes(routes, location)?.at(-1)?.route.id
    const candidates = entries.filter(entry => entry.routeId === routeId && entry.to.pathname === location.pathname)
    const actual = new URLSearchParams(location.search)
    // Ignore unregistered query values, never persist them. Match only declared menu selectors.
    return candidates.find(entry => entry.to.search && [...new URLSearchParams(entry.to.search)]
        .every(([key, value]) => actual.get(key) === value))
        || candidates.find(entry => !entry.to.search)
        || null
}

export const normalizeNavigationSearch = value => String(value).normalize('NFKC').toLocaleLowerCase()
    .replace(/[\u064B-\u065F\u0670\u0640]/g, '').replace(/[أإآ]/g, 'ا').replace(/ى/g, 'ي')
    .replace(/[^\p{L}\p{N}&]+/gu, ' ').trim().replace(/\s+/g, ' ')

export function searchNavigation(entries, query) {
    const normalized = normalizeNavigationSearch(query).slice(0, 200)
    if (!normalized) return entries
    const words = normalized.split(' ')
    const rank = entry => {
        const labels = [...Object.values(entry.labels), ...entry.aliases].map(normalizeNavigationSearch)
        if (labels.includes(normalized)) return 0
        if (normalized.length < 2) return Infinity
        if (labels.some(label => label.startsWith(normalized))) return 1
        if (labels.some(label => words.every(word => label.split(' ').some(token => token.startsWith(word))))) return 2
        const context = [...labels, ...Object.values(entry.modules), ...Object.values(entry.breadcrumbs).flat()]
            .map(normalizeNavigationSearch).join(' ').split(' ')
        return words.every(word => word.length > 1 && context.some(token => token.startsWith(word))) ? 3 : Infinity
    }
    return entries.map((entry, index) => ({ entry, index, rank: rank(entry) }))
        .filter(result => Number.isFinite(result.rank))
        .sort((a, b) => a.rank - b.rank || a.index - b.index).map(result => result.entry)
}
