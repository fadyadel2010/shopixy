import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useLocation, useNavigate } from 'react-router-dom'
import i18n from '../../../i18n/config'
import Sidebar from '../../../components/Sidebar'
import { useAuthStore } from '../../../store/authStore'
import { StorefrontBreadcrumbs } from './StorefrontBreadcrumbs'
import {
    STOREFRONT_NAVIGATION,
    STOREFRONT_ROUTE_COMPATIBILITY,
    STOREFRONT_SIDEBAR_STATE_KEY,
    resolveStorefrontScreen,
} from './storefrontInformationArchitecture'

const flattenNodes = (nodes: any[]): any[] => nodes.flatMap((node) => [
    node,
    ...(node.children ? flattenNodes(node.children) : []),
])

const storefrontUser = {
    id: '11111111-1111-4111-8111-111111111111',
    tenantId: '22222222-2222-4222-8222-222222222222',
    role: 'Owner',
    effectivePermissions: [
        'dashboard.view',
        'storefront.orders.view',
        'storefront.design.manage',
    ],
    navigationCapabilities: [],
}

function RouteHistoryProbe() {
    const location = useLocation()
    const navigate = useNavigate()
    const screenId = resolveStorefrontScreen(location.pathname, location.search)?.id || 'unresolved'

    return (
        <>
            <output aria-label="current route">{`${location.pathname}${location.search}|${screenId}`}</output>
            <button type="button" onClick={() => navigate('/storefront/catalog/product-template?templateId=summer')}>Canonical</button>
            <button type="button" onClick={() => navigate(-1)}>Back</button>
            <button type="button" onClick={() => navigate(1)}>Forward</button>
        </>
    )
}

describe('Storefront constitutional information architecture', () => {
    beforeEach(async () => {
        window.localStorage.clear()
        await i18n.changeLanguage('en')
        useAuthStore.setState({ user: storefrontUser, isAuthenticated: true, hasHydrated: true })
    })

    afterEach(() => {
        cleanup()
        useAuthStore.setState({ user: null, isAuthenticated: false, hasHydrated: true })
        document.title = ''
    })

    it('defines the exact canonical hierarchy, order, labels, and semantic icons', () => {
        expect(STOREFRONT_NAVIGATION.icon).toBe('Store')
        expect(STOREFRONT_NAVIGATION.children.map((node) => node.fallbackLabel)).toEqual([
            'Storefront Dashboard',
            'Storefront Settings',
            'Checkout Fields',
            'Domains',
            'Homepage',
            'Catalog',
            'Content',
            'Navigation',
            'Footer Content',
            'Appearance',
            'StoreConfig Version History',
        ])

        const groups = Object.fromEntries(
            STOREFRONT_NAVIGATION.children
                .filter((node) => node.children)
                .map((node) => [node.fallbackLabel, node.children.map((child) => child.fallbackLabel)]),
        )
        expect(groups).toEqual({
            Catalog: ['Product Template'],
            Content: ['Pages'],
            Domains: ['Domains', 'Activity'],
            Navigation: ['Header', 'Footer Links'],
            Appearance: ['Themes', 'Colors & Typography'],
        })

        const nodes = flattenNodes(STOREFRONT_NAVIGATION.children)
        expect(nodes.map((node) => node.icon)).toEqual([
            'LayoutDashboard',
            'Activity',
            'Files',
            'Globe',
            'Globe',
            'Activity',
            'Home',
            'ShoppingBag',
            'PanelsTopLeft',
            'FileText',
            'Files',
            'Navigation',
            'PanelTop',
            'PanelBottom',
            'PanelBottomDashed',
            'Palette',
            'LayoutTemplate',
            'Paintbrush',
            'History',
        ])
        expect(nodes.some((node) => /category template/i.test(node.fallbackLabel))).toBe(false)
    })

    it.each([
        ['/storefront', '', 'overview'],
        ['/storefront/overview', '', 'overview'],
        ['/settings', '?tab=storefront', 'status'],
        ['/storefront/status', '', 'status'],
        ['/storefront/checkout-fields', '', 'checkout-fields'],
        ['/storefront/builder', '?page=home', 'homepage'],
        ['/storefront/homepage', '', 'homepage'],
        ['/storefront/builder', '?page=product-template&templateId=summer', 'product-template'],
        ['/storefront/catalog/product-template', '?templateId=summer', 'product-template'],
        ['/storefront/cms/pages', '', 'pages'],
        ['/storefront/content/pages', '', 'pages'],
        ['/storefront/cms/pages/page-id', '', 'page-editor'],
        ['/storefront/content/pages/page-id', '', 'page-editor'],
        ['/storefront/cms/navigation', '', 'navigation-header'],
        ['/storefront/cms/navigation', '?view=footer', 'navigation-footer'],
        ['/storefront/navigation', '?view=header', 'navigation-header'],
        ['/storefront/navigation', '?view=footer', 'navigation-footer'],
        ['/storefront/cms/footer', '', 'footer-content'],
        ['/storefront/footer-content', '', 'footer-content'],
        ['/storefront/themes', '', 'themes'],
        ['/storefront/appearance/themes', '', 'themes'],
        ['/storefront/theme', '', 'colors-typography'],
        ['/storefront/appearance/colors-typography', '', 'colors-typography'],
        ['/storefront/versions', '', 'storeconfig-history'],
        ['/storefront/storeconfig-version-history', '', 'storeconfig-history'],
        ['/storefront/domains', '', 'domains'],
        ['/storefront/domains/list', '', 'domain-list'],
        ['/storefront/domains/add', '', 'domain-add'],
        ['/storefront/domains/activity', '', 'domains-activity'],
        ['/storefront/domains/example.com', '', 'domain-detail'],
        ['/storefront/domains/example.com/verification', '', 'dns-verification'],
        ['/storefront/domains/example.com/ssl', '', 'ssl-management'],
        ['/storefront/domains/example.com/redirects', '', 'redirects'],
        ['/storefront/domains/example.com/history', '', 'domain-history'],
    ])('resolves %s%s to %s', (pathname, search, screenId) => {
        expect(resolveStorefrontScreen(pathname, search)?.id).toBe(screenId)
    })

    it('keeps every canonical and legacy route registered under the existing Owner/Admin guard', () => {
        const appSource = readFileSync(resolve(process.cwd(), 'src/App.jsx'), 'utf8')
        const registeredPaths = [
            'storefront',
            'storefront/overview',
            'storefront/status',
            'storefront/checkout-fields',
            'storefront/homepage',
            'storefront/catalog/product-template',
            'storefront/content/pages',
            'storefront/content/pages/:pageId',
            'storefront/navigation',
            'storefront/footer-content',
            'storefront/appearance/themes',
            'storefront/appearance/colors-typography',
            'storefront/storeconfig-version-history',
            'storefront/builder',
            'storefront/cms/pages',
            'storefront/cms/pages/:pageId',
            'storefront/cms/navigation',
            'storefront/cms/footer',
            'storefront/themes',
            'storefront/theme',
            'storefront/versions',
            'storefront/domains',
            'storefront/domains/:id',
            'storefront/domains/add',
            'storefront/domains/list',
            'storefront/domains/activity',
            'storefront/domains/:id/verification',
            'storefront/domains/:id/ssl',
            'storefront/domains/:id/redirects',
            'storefront/domains/:id/history',
        ]

        for (const path of registeredPaths) {
            expect(appSource).toContain(`path="${path}"`)
        }
        expect(appSource).toContain('path="settings" element={<Settings />}')
        expect(appSource).toContain('<Route element={<PosPermissionRouteGuard permission="storefront.design.manage" />}>')
        expect(STOREFRONT_ROUTE_COMPATIBILITY).toHaveLength(22)

        const headerRoute = new URL(STOREFRONT_NAVIGATION.children[7].children[0].path, 'http://localhost')
        const footerLinksRoute = new URL(STOREFRONT_NAVIGATION.children[7].children[1].path, 'http://localhost')
        expect(headerRoute.pathname).toBe('/storefront/navigation')
        expect(footerLinksRoute.pathname).toBe(headerRoute.pathname)
        expect(footerLinksRoute.search).not.toBe(headerRoute.search)
    })

    it('renders canonical linked breadcrumbs and updates the browser page title', async () => {
        render(
            <MemoryRouter initialEntries={['/storefront/appearance/colors-typography']}>
                <Routes>
                    <Route path="*" element={<StorefrontBreadcrumbs />} />
                </Routes>
            </MemoryRouter>,
        )

        const breadcrumb = screen.getByRole('navigation', { name: 'Storefront breadcrumb' })
        expect(breadcrumb).toHaveTextContent('StorefrontAppearanceColors & Typography')
        expect(screen.getByRole('link', { name: 'Storefront' })).toHaveAttribute('href', '/storefront')
        expect(screen.getByRole('link', { name: 'Appearance' })).toHaveAttribute('href', '/storefront/appearance/themes')
        expect(screen.getByText('Colors & Typography')).toHaveAttribute('aria-current', 'page')
        await waitFor(() => expect(document.title).toBe('Colors & Typography | Storefront Administration'))
    })

    it('includes selected authoring identity and guards breadcrumb navigation', async () => {
        const user = userEvent.setup()
        const navigationGuard = vi.fn(() => false)
        render(
            <MemoryRouter initialEntries={['/storefront/catalog/product-template?templateId=summer']}>
                <StorefrontBreadcrumbs
                    appendCurrentLabel
                    currentLabel="Summer"
                    onNavigate={navigationGuard}
                />
            </MemoryRouter>,
        )

        const breadcrumb = screen.getByRole('navigation', { name: 'Storefront breadcrumb' })
        expect(breadcrumb).toHaveTextContent('StorefrontCatalogProduct TemplateSummer')
        expect(screen.getByRole('link', { name: 'Product Template' })).toHaveAttribute(
            'href',
            '/storefront/catalog/product-template?templateId=summer',
        )
        expect(screen.getByText('Summer')).toHaveAttribute('aria-current', 'page')

        await user.click(screen.getByRole('link', { name: 'Catalog' }))
        expect(navigationGuard).toHaveBeenCalledWith('/storefront/catalog/product-template')
        expect(screen.getByText('Summer')).toBeInTheDocument()
        await waitFor(() => expect(document.title).toBe('Summer | Storefront Administration'))
    })

    it('preserves template identity through canonical navigation, back, and forward history', async () => {
        const user = userEvent.setup()
        render(
            <MemoryRouter initialEntries={['/storefront/builder?page=product-template&templateId=summer']}>
                <RouteHistoryProbe />
            </MemoryRouter>,
        )

        expect(screen.getByLabelText('current route')).toHaveTextContent(
            '/storefront/builder?page=product-template&templateId=summer|product-template',
        )
        await user.click(screen.getByRole('button', { name: 'Canonical' }))
        expect(screen.getByLabelText('current route')).toHaveTextContent(
            '/storefront/catalog/product-template?templateId=summer|product-template',
        )
        await user.click(screen.getByRole('button', { name: 'Back' }))
        expect(screen.getByLabelText('current route')).toHaveTextContent(
            '/storefront/builder?page=product-template&templateId=summer|product-template',
        )
        await user.click(screen.getByRole('button', { name: 'Forward' }))
        expect(screen.getByLabelText('current route')).toHaveTextContent(
            '/storefront/catalog/product-template?templateId=summer|product-template',
        )
    })

    it('supports keyboard expansion, active-page semantics, and persistent group state', async () => {
        const user = userEvent.setup()
        const renderSidebar = () => render(
            <MemoryRouter initialEntries={['/storefront']}>
                <Sidebar isOpen onClose={() => {}} />
            </MemoryRouter>,
        )

        const firstRender = renderSidebar()
        const storefrontGroup = await screen.findByRole('button', { name: 'Storefront' })
        await waitFor(() => expect(storefrontGroup).toHaveAttribute('aria-expanded', 'true'))
        const overviewLinks = screen.getAllByRole('link', { name: 'Storefront Dashboard' })
        const designOverview = overviewLinks.find((el) => el.getAttribute('href') === '/storefront')
        expect(designOverview).toHaveAttribute('aria-current', 'page')
        expect(screen.queryByText('Category Template')).not.toBeInTheDocument()

        const catalogButtons = screen.getAllByRole('button', { name: 'Catalog' })
        const catalogGroup = catalogButtons.find((el) => el.getAttribute('aria-controls') === 'sidebar-submenu-storefront-catalog')
        catalogGroup.focus()
        await user.keyboard('{Enter}')
        expect(catalogGroup).toHaveAttribute('aria-expanded', 'true')
        expect(screen.getByRole('link', { name: 'Product Template' })).toHaveAttribute(
            'href',
            '/storefront/catalog/product-template',
        )

        await waitFor(() => {
            const stored = JSON.parse(window.localStorage.getItem(STOREFRONT_SIDEBAR_STATE_KEY) || '{}')
            expect(stored['storefront-catalog']).toBe(true)
        })

        firstRender.unmount()
        renderSidebar()
        const catalogAfterRemount = (await screen.findAllByRole('button', { name: 'Catalog' }))
            .find((el) => el.getAttribute('aria-controls') === 'sidebar-submenu-storefront-catalog')
        expect(catalogAfterRemount).toHaveAttribute('aria-expanded', 'true')
    }, 15000)
})
