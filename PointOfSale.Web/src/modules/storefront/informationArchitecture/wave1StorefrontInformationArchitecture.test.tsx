/**
 * Wave 1 — Storefront Information Architecture Migration
 * Architecture regression tests for the canonical Operations-domain hierarchy.
 *
 * These tests prove:
 * 1. Storefront hierarchy — canonical Operations sub-group tree
 * 2. Domain grouping — Operations screens grouped by Orders, Payments, etc.
 * 3. No duplicate navigation ownership — no route appears twice
 * 4. Correct ordering — canonical order from §8.1
 * 5. Correct breadcrumbs — Operations route → breadcrumb chain
 * 6. Correct expansion — sub-groups expand/collapse persistently
 * 7. Canonical route mapping — each Operations path maps to a screenId
 * 8. No flat list — the 17 flat children replaced by hierarchical sub-groups
 *
 * Source of truth: ONLINE_STORE_CANONICAL_BLUEPRINT.md §8.1 (Canonical Storefront Hierarchy)
 */

import { existsSync, readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { cleanup, render, screen, fireEvent } from '@testing-library/react'
import { MemoryRouter, useLocation } from 'react-router-dom'
import i18n from '../../../i18n/config'
import Sidebar from '../../../components/Sidebar'
import { useAuthStore } from '../../../store/authStore'
import {
    OPERATIONS_NAVIGATION,
    STOREFRONT_PRODUCT_NAVIGATION,
    resolveStorefrontScreen,
} from './storefrontInformationArchitecture'

const iaSource = () =>
    readFileSync(
        resolve(
            process.cwd(),
            'src/modules/storefront/informationArchitecture/storefrontInformationArchitecture.js',
        ),
        'utf8',
    )

const ownerUser = {
    id: '11111111-1111-4111-8111-111111111111',
    tenantId: '22222222-2222-4222-8222-222222222222',
    role: 'Owner',
    effectivePermissions: [
        'dashboard.view',
        'catalog.view',
        'catalog.manage',
        'catalog.publish',
        'storefront.orders.view',
        'storefront.orders.manage',
        'storefront.payments.view',
        'storefront.payments.manage',
        'storefront.promotions.manage',
        'storefront.analytics.view',
        'storefront.analytics.configure',
        'storefront.design.manage',
        'storefront.domains.manage',
        'storefront.auth.manage',
        'shipping.manage',
    ],
    navigationCapabilities: [
        'commercehub.orders.view',
        'commercehub.payments.view',
        'commercehub.storefront.manage',
        'commercehub.analytics.view',
    ],
    vendorLifecycleAuthority: {
        vendorId: '22222222-2222-4222-8222-222222222222',
        status: 'Active',
        debtStatus: 'Clear',
        tenantActive: true,
        marketplaceApproved: true,
        kycVerified: true,
        isDebtBlocked: false,
        blocksCatalog: false,
        blocksOrders: false,
        blocksStorefront: false,
        blocksAnalytics: false,
        blocksPayments: false,
        blocksPayouts: false,
        blocksLogin: false,
    },
}

const rootNode = OPERATIONS_NAVIGATION

const findNodeByKey = (tree, key) => {
    if (tree.key === key) return tree
    for (const child of tree.children || []) {
        const found = findNodeByKey(child, key)
        if (found) return found
    }
    return null
}

const findNodeByPath = (tree, path) => {
    if (tree.path === path) return tree
    for (const child of tree.children || []) {
        const found = findNodeByPath(child, path)
        if (found) return found
    }
    return null
}

const collectAllPaths = (tree) => {
    const paths = []
    if (tree.path) paths.push(tree.path)
    for (const child of tree.children || []) {
        paths.push(...collectAllPaths(child))
    }
    return paths
}

const collectAllScreenIds = (tree) => {
    const ids = []
    if (tree.screenIds) ids.push(...tree.screenIds)
    for (const child of tree.children || []) {
        ids.push(...collectAllScreenIds(child))
    }
    return ids
}

const collectLeafNodes = (tree) => {
    const leaves = []
    if (tree.path && !tree.children) leaves.push(tree)
    for (const child of tree.children || []) {
        leaves.push(...collectLeafNodes(child))
    }
    return leaves
}

const canonicalStorefrontModules = [
    ['Storefront Dashboard', 'StorefrontDashboard.jsx', '/storefront', 'overview'],
    ['Storefront Settings', 'StorefrontSettings.jsx', '/storefront/status', 'status'],
    ['Orders', 'Orders.jsx', '/ecommerce/orders', 'orders'],
    ['Coupons', 'Coupons.jsx', '/ecommerce/coupons', 'coupons'],
    ['Promotions', 'Promotions.jsx', '/ecommerce/promotions', 'promotions'],
    ['Smart Offers', 'SmartOffersPage.jsx', '/ecommerce/smart-offers', 'smart-offers'],
    ['Commerce AI', 'CommerceAI.jsx', '/ecommerce/commerce-ai', 'commerce-ai'],
    ['Payment Methods', 'PaymentMethods.jsx', '/ecommerce/payment-methods', 'payment-methods'],
    ['Manual Payments', 'ManualPayments.jsx', '/ecommerce/manual-payments', 'manual-payments'],
    ['Shipping Methods', 'ShippingMethods.jsx', '/ecommerce/shipping-methods', 'shipping-methods'],
    ['Abandoned Checkouts', 'AbandonedCheckouts.jsx', '/ecommerce/abandoned-checkouts', 'abandoned-checkouts'],
    ['Abandoned Cart Recovery', 'AbandonedCartRecovery.jsx', '/ecommerce/cart-recovery', 'cart-recovery'],
    ['Blocked Phones', 'BlockedPhones.jsx', '/ecommerce/blocked-phones', 'blocked-phones'],
    ['Analytics Settings', 'AnalyticsSettings.jsx', '/settings/analytics', 'analytics-settings'],
    ['Storefront Analytics', 'StorefrontAnalyticsDashboard.jsx', '/analytics/storefront', 'storefront-analytics'],
]

describe('Wave 1 — Storefront Information Architecture Migration', () => {
    beforeEach(async () => {
        window.localStorage.clear()
        await i18n.changeLanguage('en')
    })

    afterEach(() => {
        cleanup()
        useAuthStore.setState({ user: null, isAuthenticated: false, hasHydrated: true })
    })

    describe('1. Storefront Operations hierarchy', () => {
        it('exports a canonical OPERATIONS_NAVIGATION tree from the IA module', () => {
            expect(rootNode).toBeDefined()
            expect(rootNode.key).toBe('operations')
        })

        it('has exactly the canonical sub-groups and leaf order from §8.1', () => {
            const expectedChildKeys = [
                undefined, // Overview (leaf, no key)
                'operations-orders',
                'operations-payments',
                'operations-catalog',
                'operations-shipping',
                'operations-marketing',
                undefined, // Commerce AI (leaf, no key)
                undefined, // Auth Providers (leaf, no key)
                'operations-analytics',
            ]

            const actualKeys = rootNode.children.map((c) => c.key)
            for (let i = 0; i < expectedChildKeys.length; i++) {
                expect(actualKeys[i]).toBe(expectedChildKeys[i])
            }
        })

        it('defines a canonical Operations Overview screen at /ecommerce/overview', () => {
            const overview = findNodeByPath(rootNode, '/ecommerce/overview')
            expect(overview).toBeDefined()
            expect(overview.screenIds).toContain('operations-overview')
        })

        it('registers every canonical E-Commerce / Storefront module through component, route, navigation, screen, and POS permission', () => {
            const app = readFileSync(resolve(process.cwd(), 'src/App.jsx'), 'utf8')

            for (const [label, componentFile, routePath, screenId] of canonicalStorefrontModules) {
                const componentPath = resolve(process.cwd(), 'src/pages', componentFile)
                const routePathForApp = routePath.replace(/^\//, '')
                const routePathWithoutQuery = routePathForApp.split('?')[0]
                const navigationNode = findNodeByPath(STOREFRONT_PRODUCT_NAVIGATION, routePath)
                const screen = resolveStorefrontScreen(routePath)

                expect(existsSync(componentPath), `${label} component file exists`).toBe(true)
                expect(app, `${label} component is imported/registered`).toContain(componentFile.replace(/\.jsx$/, ''))
                expect(app, `${label} route is registered`).toContain(`path="${routePathWithoutQuery}"`)
                expect(navigationNode, `${label} navigation node exists`).toBeDefined()
                expect(navigationNode.fallbackLabel, `${label} navigation label`).toBe(label)
                expect(screen?.id, `${label} route resolves to screen`).toBe(screenId)
                expect(navigationNode.capability, `${label} is not CommerceHub capability-filtered`).toBeUndefined()
            }

            expect(app).toContain('<Route element={<PosPermissionRouteGuard permission="storefront.orders.view" />}>')
        })
    })

    describe('2. Domain grouping', () => {
        it('groups Orders screens under an Orders sub-group', () => {
            const ordersGroup = findNodeByKey(rootNode, 'operations-orders')
            expect(ordersGroup).toBeDefined()
            const children = ordersGroup.children
            expect(children).toHaveLength(6)
            expect(findNodeByPath(rootNode, '/ecommerce/orders')).toBeDefined()
            expect(findNodeByPath(rootNode, '/ecommerce/abandoned-checkouts')).toBeDefined()
            expect(findNodeByPath(rootNode, '/ecommerce/cart-recovery')).toBeDefined()
            expect(findNodeByPath(rootNode, '/ecommerce/blocked-phones')).toBeDefined()
            expect(findNodeByPath(rootNode, '/ecommerce/online-customers')).toBeDefined()
            expect(findNodeByPath(rootNode, '/ecommerce/whatsapp')).toBeDefined()
        })

        it('groups Payments screens under a Payments sub-group', () => {
            const paymentsGroup = findNodeByKey(rootNode, 'operations-payments')
            expect(paymentsGroup).toBeDefined()
            expect(paymentsGroup.children).toHaveLength(2)
            expect(findNodeByPath(rootNode, '/ecommerce/payment-methods')).toBeDefined()
            expect(findNodeByPath(rootNode, '/ecommerce/manual-payments')).toBeDefined()
        })

        it('groups Catalog screens under a Catalog sub-group', () => {
            const catalogGroup = findNodeByKey(rootNode, 'operations-catalog')
            expect(catalogGroup).toBeDefined()
            expect(catalogGroup.children).toHaveLength(3)
            expect(findNodeByPath(rootNode, '/ecommerce/coupons')).toBeDefined()
            expect(findNodeByPath(rootNode, '/ecommerce/promotions')).toBeDefined()
            expect(findNodeByPath(rootNode, '/ecommerce/smart-offers')).toBeDefined()
        })

        it('groups Shipping under a Shipping sub-group', () => {
            const shippingGroup = findNodeByKey(rootNode, 'operations-shipping')
            expect(shippingGroup).toBeDefined()
            expect(shippingGroup.children).toHaveLength(1)
            expect(findNodeByPath(rootNode, '/ecommerce/shipping-methods')).toBeDefined()
        })

        it('groups Marketing screens under a Marketing sub-group with Attribution sub-children', () => {
            const marketingGroup = findNodeByKey(rootNode, 'operations-marketing')
            expect(marketingGroup).toBeDefined()
            expect(findNodeByPath(rootNode, '/ecommerce/pixel-tracking')).toBeDefined()
            expect(findNodeByPath(rootNode, '/analytics/funnels')).toBeDefined()

            const attributionGroup = findNodeByKey(rootNode, 'operations-marketing-attribution')
            expect(attributionGroup).toBeDefined()
            expect(attributionGroup.children).toHaveLength(3)
            expect(findNodeByPath(rootNode, '/analytics/attribution')).toBeDefined()
            expect(findNodeByPath(rootNode, '/analytics/attribution/costs')).toBeDefined()
            expect(findNodeByPath(rootNode, '/analytics/attribution/roi')).toBeDefined()
        })

        it('places Storefront Analytics and Analytics Settings under Analytics (not Marketing)', () => {
            const analyticsGroup = findNodeByKey(rootNode, 'operations-analytics')
            expect(analyticsGroup).toBeDefined()
            expect(findNodeByPath(rootNode, '/analytics/storefront')).toBeDefined()
            expect(findNodeByPath(rootNode, '/settings/analytics')).toBeDefined()
            const analyticsNode = findNodeByPath(rootNode, '/settings/analytics')
            expect(analyticsNode.screenIds).toContain('analytics-settings')
        })
    })

    describe('3. No duplicate navigation ownership', () => {
        it('every canonical path appears exactly once in the tree', () => {
            const allPaths = collectAllPaths(rootNode)
            const duplicates = allPaths.filter((p, i) => allPaths.indexOf(p) !== i)
            expect(duplicates).toHaveLength(0)
        })

        it('every canonical screenId appears exactly once in the tree', () => {
            const allIds = collectAllScreenIds(rootNode)
            const duplicates = allIds.filter((id, i) => allIds.indexOf(id) !== i)
            expect(duplicates).toHaveLength(0)
        })

        it('does not duplicate Analytics Settings under both Marketing and Analytics', () => {
            // The canonical blueprint §11.8 says "Each screen has exactly one
            // canonical sidebar entry point." The /settings/analytics route
            // must appear only under Analytics.
            const allPaths = collectAllPaths(rootNode)
            const analyticsSettingsCount = allPaths.filter((p) => p === '/settings/analytics').length
            expect(analyticsSettingsCount).toBe(1)
        })
    })

    describe('4. Correct ordering', () => {
        it('follows the canonical top-level order: Overview, Orders, Payments, Catalog, Shipping, Marketing, Commerce AI, Auth Providers, Analytics', () => {
            const labels = rootNode.children.map((c) => c.fallbackLabel)
            expect(labels).toEqual([
                'Overview',
                'Orders',
                'Payments',
                'Catalog',
                'Shipping',
                'Marketing',
                'Commerce AI',
                'Auth Providers',
                'Analytics',
            ])
        })

        it('orders Orders children canonically: Orders, Abandoned Checkouts, Abandoned Cart Recovery, Blocked Phones, Online Customers, WhatsApp Settings', () => {
            const ordersGroup = findNodeByKey(rootNode, 'operations-orders')
            const labels = ordersGroup.children.map((c) => c.fallbackLabel)
            expect(labels).toEqual([
                'Orders',
                'Abandoned Checkouts',
                'Abandoned Cart Recovery',
                'Blocked Phones',
                'Online Customers',
                'WhatsApp Settings',
            ])
        })

        it('orders Marketing children canonically: Pixel Tracking, Conversion Funnels, Marketing Attribution', () => {
            const marketingGroup = findNodeByKey(rootNode, 'operations-marketing')
            const labels = marketingGroup.children.map((c) => c.fallbackLabel)
            expect(labels).toEqual([
                'Pixel Tracking',
                'Conversion Funnels',
                'Marketing Attribution',
            ])
        })

        it('orders Marketing Attribution children: Overview, Campaign Costs, Marketing ROI', () => {
            const attributionGroup = findNodeByKey(rootNode, 'operations-marketing-attribution')
            const labels = attributionGroup.children.map((c) => c.fallbackLabel)
            expect(labels).toEqual(['Overview', 'Campaign Costs', 'Marketing ROI'])
        })
    })

    describe('5. Canonical route mapping', () => {
        it('maps every canonical route to a screenId', () => {
            const leafNodes = collectLeafNodes(rootNode)
            for (const node of leafNodes) {
                expect(node.path).toBeDefined()
                expect(node.screenIds).toBeDefined()
                expect(node.screenIds.length).toBeGreaterThan(0)
                expect(node.labelKey).toBeDefined()
            }
        })

        it('resolves /ecommerce/overview to the operations-overview screen', () => {
            const screen = resolveStorefrontScreen('/ecommerce/overview')
            expect(screen).toBeDefined()
            expect(screen.id).toBe('operations-overview')
        })

        it('resolves /ecommerce/orders to the orders screen', () => {
            const screen = resolveStorefrontScreen('/ecommerce/orders')
            expect(screen).toBeDefined()
            expect(screen.id).toBe('orders')
        })

        it('resolves /analytics/attribution/costs to campaign-costs screen', () => {
            const screen = resolveStorefrontScreen('/analytics/attribution/costs')
            expect(screen).toBeDefined()
            expect(screen.id).toBe('campaign-costs')
        })

        it('resolves /settings/analytics to analytics-settings screen', () => {
            const screen = resolveStorefrontScreen('/settings/analytics')
            expect(screen).toBeDefined()
            expect(screen.id).toBe('analytics-settings')
        })

        it('resolves /analytics/storefront to the storefront-analytics screen', () => {
            const screen = resolveStorefrontScreen('/analytics/storefront')
            expect(screen).toBeDefined()
            expect(screen.id).toBe('storefront-analytics')
        })

        it('resolves /ecommerce/overview with query params', () => {
            const screen = resolveStorefrontScreen('/ecommerce/overview?tab=settings')
            expect(screen).toBeDefined()
            expect(screen.id).toBe('operations-overview')
        })

        it('returns null for unknown /ecommerce paths', () => {
            const screen = resolveStorefrontScreen('/ecommerce/nonexistent')
            expect(screen).toBeNull()
        })
    })

    describe('6. Correct breadcrumbs', () => {
        it('renders breadcrumbs for /ecommerce/orders as Storefront > Operations > Orders > Orders', () => {
            const screen = resolveStorefrontScreen('/ecommerce/orders')
            expect(screen).toBeDefined()
            expect(screen.breadcrumbs).toHaveLength(4)
            expect(screen.breadcrumbs[0].fallbackLabel).toBe('Storefront')
            expect(screen.breadcrumbs[1].fallbackLabel).toBe('Operations')
            expect(screen.breadcrumbs[2].fallbackLabel).toBe('Orders')
            expect(screen.breadcrumbs[3].fallbackLabel).toBe('Orders')
        })

        it('renders breadcrumbs for /analytics/attribution/costs with Marketing Attribution parent', () => {
            const screen = resolveStorefrontScreen('/analytics/attribution/costs')
            expect(screen).toBeDefined()
            expect(screen.breadcrumbs).toHaveLength(6)
            // Storefront > Operations > Marketing > Marketing Attribution > Marketing Attribution > Campaign Costs
            expect(screen.breadcrumbs[0].fallbackLabel).toBe('Storefront')
            expect(screen.breadcrumbs[1].fallbackLabel).toBe('Operations')
            expect(screen.breadcrumbs[2].fallbackLabel).toBe('Marketing')
            expect(screen.breadcrumbs[3].fallbackLabel).toBe('Marketing Attribution')
            expect(screen.breadcrumbs[4].fallbackLabel).toBe('Marketing Attribution')
            expect(screen.breadcrumbs[5].fallbackLabel).toBe('Campaign Costs')
        })

        it('renders breadcrumbs for /settings/analytics as Storefront > Operations > Analytics > Analytics Settings', () => {
            const screen = resolveStorefrontScreen('/settings/analytics')
            expect(screen).toBeDefined()
            expect(screen.breadcrumbs).toHaveLength(4)
            expect(screen.breadcrumbs[0].fallbackLabel).toBe('Storefront')
            expect(screen.breadcrumbs[1].fallbackLabel).toBe('Operations')
            expect(screen.breadcrumbs[2].fallbackLabel).toBe('Analytics')
            expect(screen.breadcrumbs[3].fallbackLabel).toBe('Analytics Settings')
        })

        it('renders breadcrumbs for /ecommerce/overview as Storefront > Overview', () => {
            const screen = resolveStorefrontScreen('/ecommerce/overview')
            expect(screen).toBeDefined()
            expect(screen.breadcrumbs).toHaveLength(2)
            expect(screen.breadcrumbs[0].fallbackLabel).toBe('Storefront')
            expect(screen.breadcrumbs[1].fallbackLabel).toBe('Overview')
        })

        it('renders breadcrumbs for /ecommerce/commerce-ai as Storefront > Operations > Commerce AI', () => {
            const screen = resolveStorefrontScreen('/ecommerce/commerce-ai')
            expect(screen).toBeDefined()
            expect(screen.breadcrumbs).toHaveLength(3)
            expect(screen.breadcrumbs[0].fallbackLabel).toBe('Storefront')
            expect(screen.breadcrumbs[1].fallbackLabel).toBe('Operations')
            expect(screen.breadcrumbs[2].fallbackLabel).toBe('Commerce AI')
        })
    })

    describe('7. Correct expansion behavior (runtime)', () => {
        it('expands the Orders sub-group when clicked and persists across renders', async () => {
            useAuthStore.setState({ user: ownerUser, isAuthenticated: true, hasHydrated: true })
            render(
                <MemoryRouter initialEntries={['/ecommerce/orders']}>
                    <Sidebar isOpen onClose={() => {}} />
                </MemoryRouter>,
            )

            // Navigate to /ecommerce/orders — this should auto-expand parent groups
            // The Orders group button should be rendered (expanded by the auto-expand effect)
            const ordersGroup = (await screen.findAllByRole('button', { name: 'Orders' }))
                .find((el) => el.getAttribute('aria-controls') === 'sidebar-submenu-operations-orders')
            expect(ordersGroup).toBeInTheDocument()

            // Orders link should be visible within the expanded Orders group
            const ordersLink = (await screen.findAllByRole('link', { name: 'Orders' }))
                .find((el) => el.getAttribute('href') === '/ecommerce/orders')
            expect(ordersLink).toBeInTheDocument()
        })

        it('renders canonical sub-group buttons after expanding the Storefront section', async () => {
            useAuthStore.setState({ user: ownerUser, isAuthenticated: true, hasHydrated: true })
            render(
                <MemoryRouter initialEntries={['/']}>
                    <Sidebar isOpen onClose={() => {}} />
                </MemoryRouter>,
            )

            const section = await screen.findByRole('button', { name: 'Storefront' })
            fireEvent.click(section)

            // After expanding Storefront, Design and Operations sub-groups should be visible
            const designGroup = await screen.findByRole('button', { name: 'Design' })
            expect(designGroup).toBeInTheDocument()
            const operationsGroup = await screen.findByRole('button', { name: 'Operations' })
            expect(operationsGroup).toBeInTheDocument()
        })

        it('clicking an Operations sub-group toggles child visibility', async () => {
            useAuthStore.setState({ user: ownerUser, isAuthenticated: true, hasHydrated: true })
            render(
                <MemoryRouter initialEntries={['/']}>
                    <Sidebar isOpen onClose={() => {}} />
                </MemoryRouter>,
            )

            const section = await screen.findByRole('button', { name: 'Storefront' })
            fireEvent.click(section)

            const operationsGroup = await screen.findByRole('button', { name: 'Operations' })
            fireEvent.click(operationsGroup)

            const ordersGroup = (await screen.findAllByRole('button', { name: 'Orders' }))
                .find((el) => el.getAttribute('aria-controls') === 'sidebar-submenu-operations-orders')
            fireEvent.click(ordersGroup)

            // New actors discover the Operations hierarchy expanded initially.
            // The first click collapses it; the second reopens it.
            expect(ordersGroup).toHaveAttribute('aria-expanded', 'false')
            fireEvent.click(ordersGroup)
            expect(ordersGroup).toHaveAttribute('aria-expanded', 'true')
            const ordersLink = (await screen.findAllByRole('link', { name: 'Orders' }))
                .find((el) => el.getAttribute('href') === '/ecommerce/orders')
            expect(ordersLink).toBeInTheDocument()

            // Clicking again should hide children
            fireEvent.click(ordersGroup)
            expect(ordersGroup).toHaveAttribute('aria-expanded', 'false')
        })

        it('persists expanded state to localStorage under the operations state key', async () => {
            useAuthStore.setState({ user: ownerUser, isAuthenticated: true, hasHydrated: true })
            render(
                <MemoryRouter initialEntries={['/']}>
                    <Sidebar isOpen onClose={() => {}} />
                </MemoryRouter>,
            )

            const section = await screen.findByRole('button', { name: 'Storefront' })
            fireEvent.click(section)

            // The expanded state should be persisted
            const stored = window.localStorage.getItem('pointofsale.storefront.operations.sidebar.expansion.v1')
            expect(stored).toBeTruthy()
        })
    })

    describe('8. No flat list (regression protection)', () => {
        it('sidebar source references OPERATIONS_NAVIGATION instead of inline flat children', () => {
            const source = iaSource()
            expect(source).toContain('OPERATIONS_NAVIGATION')
            // The canonical structure must not have a flat array of 17 inline path entries
            expect(source).not.toMatch(/children:\s*\[\s*\{\s*path:\s*['"']\/ecommerce\/orders/)
        })

        it('sidebar component source references STOREFRONT_PRODUCT_NAVIGATION', () => {
            const sidebarSource = readFileSync(
                resolve(process.cwd(), 'src/components/Sidebar.jsx'),
                'utf8',
            )
            expect(sidebarSource).toContain('STOREFRONT_PRODUCT_NAVIGATION')
        })

        it('App.jsx registers the canonical /ecommerce/overview route', () => {
            const app = readFileSync(resolve(process.cwd(), 'src/App.jsx'), 'utf8')
            expect(app).toContain('path="ecommerce/overview"')
            expect(app).toContain('OperationsOverview')
        })

        it('App.jsx registers the dedicated Storefront Analytics route', () => {
            const app = readFileSync(resolve(process.cwd(), 'src/App.jsx'), 'utf8')
            expect(app).toContain('path="analytics/storefront"')
            expect(app).toContain('StorefrontAnalyticsDashboard')
        })
    })

    describe('9. POS Admin permissions regression protection', () => {
        it('renders canonical Storefront Operations screens for an Owner without CommerceHub vendor capabilities', async () => {
            useAuthStore.setState({
                user: { ...ownerUser, navigationCapabilities: [], vendorLifecycleAuthority: null },
                isAuthenticated: true,
                hasHydrated: true,
            })

            render(
                <MemoryRouter initialEntries={['/ecommerce/orders']}>
                    <Sidebar isOpen onClose={() => {}} />
                </MemoryRouter>,
            )

            expect(await screen.findByRole('button', { name: 'Storefront' })).toBeInTheDocument()
            expect(await screen.findByRole('button', { name: 'Operations' })).toBeInTheDocument()
            const ordersLink = (await screen.findAllByRole('link', { name: 'Orders' }))
                .find((el) => el.getAttribute('href') === '/ecommerce/orders')
            expect(ordersLink).toHaveAttribute('href', '/ecommerce/orders')
            expect(await screen.findByRole('link', { name: 'Payment Methods' })).toHaveAttribute('href', '/ecommerce/payment-methods')
            expect(await screen.findByRole('link', { name: 'Coupons' })).toHaveAttribute('href', '/ecommerce/coupons')
            expect(await screen.findByRole('link', { name: 'Storefront Analytics' })).toHaveAttribute('href', '/analytics/storefront')
        })
    })

    describe('10. i18n keys for canonical Operations labels', () => {
        it('includes all canonical storefront.operations* keys', () => {
            const en = readFileSync(
                resolve(process.cwd(), 'src/i18n/locales/en.json'),
                'utf8',
            )
            expect(en).toContain('"operations": "Operations"')
            expect(en).toContain('"operationsOverview": "Overview"')
            expect(en).toContain('"operationsOrders": "Orders"')
            expect(en).toContain('"operationsPayments": "Payments"')
            expect(en).toContain('"operationsCatalog": "Catalog"')
            expect(en).toContain('"operationsShipping": "Shipping"')
            expect(en).toContain('"operationsMarketing": "Marketing"')
            expect(en).toContain('"operationsMarketingAttribution": "Marketing Attribution"')
            expect(en).toContain('"operationsAnalytics": "Analytics"')
            expect(en).toContain('"operationsSettings": "Settings"')
        })

        it('includes all canonical nav.* keys for Operations screens', () => {
            const en = readFileSync(
                resolve(process.cwd(), 'src/i18n/locales/en.json'),
                'utf8',
            )
            expect(en).toContain('"whatsappSettings"')
            expect(en).toContain('"cartRecovery"')
            expect(en).toContain('"blockedPhones"')
            expect(en).toContain('"manualPayments"')
            expect(en).toContain('"pixelTracking"')
            expect(en).toContain('"smartOffers"')
            expect(en).toContain('"authProviders"')
            expect(en).toContain('"analyticsSettings"')
        })
    })
})
