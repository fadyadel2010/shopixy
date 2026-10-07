import { act } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import AuthBootstrap from './AuthBootstrap'
import Sidebar from './Sidebar'
import PosPermissionRouteGuard from './PosPermissionRouteGuard'
import { useAuthStore } from '../store/authStore'
import { queryClient } from '../queryClient'
import i18n from '../i18n/config'
import { STOREFRONT_SIDEBAR_STATE_KEY, STOREFRONT_OPERATIONS_SIDEBAR_STATE_KEY } from '../modules/storefront/informationArchitecture'

const apiMock = vi.hoisted(() => ({ get: vi.fn(), requestPosSessionRefresh: vi.fn() }))
vi.mock('../services/api', () => ({
    default: apiMock,
    requestPosSessionRefresh: apiMock.requestPosSessionRefresh,
    isDefinitiveAuthFailure: error => [401, 403].includes(error?.response?.status),
}))

const tenantId = '22222222-2222-4222-8222-222222222222'
const owner = {
    id: '11111111-1111-4111-8111-111111111111', tenantId, role: 'Owner',
    effectivePermissions: ['dashboard.view', 'storefront.orders.view', 'storefront.design.manage', 'storefront.payments.manage', 'shipping.manage'],
    // A new POS storefront is independent from marketplace approval/capabilities.
    navigationCapabilities: [],
    vendorLifecycleAuthority: {
        vendorId: tenantId, status: 'Pending', tenantActive: true,
        marketplaceApproved: false, kycVerified: false, debtStatus: 'ACTIVE', isDebtBlocked: false,
        blocksCatalog: true, blocksOrders: true, blocksStorefront: true, blocksAnalytics: true,
        blocksPayments: false, blocksPayouts: true, blocksLogin: false,
    },
}

const renderSidebar = (route = '/') => render(<MemoryRouter initialEntries={[route]}><Sidebar isOpen onClose={() => {}} /></MemoryRouter>)
const branch = key => document.querySelector(`button[aria-controls="sidebar-submenu-${key}"]`)
const assertDiscoverable = href => {
    const link = document.querySelector(`aside a[href="${href}"]`)
    expect(link).toBeInTheDocument()
    for (let node = link?.parentElement; node; node = node.parentElement) {
        if (!node.id.startsWith('sidebar-submenu-')) continue
        expect(node).not.toHaveAttribute('inert')
        expect(node).toHaveClass('grid-rows-[1fr]', 'opacity-100')
    }
}

describe('first-login sidebar navigation hydration', () => {
    beforeEach(async () => {
        localStorage.clear()
        apiMock.get.mockReset()
        queryClient.clear()
        useAuthStore.setState({ user: null, isAuthenticated: false, hasHydrated: false })
        await i18n.changeLanguage('en')
    })
    afterEach(() => { cleanup(); queryClient.clear(); vi.restoreAllMocks() })

    it('discovers both authorized links on the initial session bootstrap without visiting either route or requesting its data', async () => {
        let resolveSession
        apiMock.get.mockReturnValue(new Promise(resolve => { resolveSession = resolve }))
        render(<MemoryRouter initialEntries={['/']}><AuthBootstrap /><Sidebar isOpen onClose={() => {}} /></MemoryRouter>)
        expect(document.querySelector('aside a[href="/ecommerce/payment-methods"]')).toBeNull()
        await act(async () => resolveSession({ data: { user: owner } }))
        assertDiscoverable('/ecommerce/payment-methods')
        assertDiscoverable('/ecommerce/shipping-methods')
        expect(apiMock.get.mock.calls.map(([url]) => url)).toEqual(['/auth/session'])
        expect(useAuthStore.getState().user.navigationCapabilities).toEqual([])
        expect(useAuthStore.getState().user.effectivePermissions).toEqual(owner.effectivePermissions)
    })

    it.each(['{}', 'null', '{broken', JSON.stringify({ 'operations-payments': false, 'operations-shipping': false })])('initializes discoverable navigation from a legacy/empty/corrupt cache: %s', cached => {
        localStorage.setItem(STOREFRONT_SIDEBAR_STATE_KEY, cached)
        localStorage.setItem(STOREFRONT_OPERATIONS_SIDEBAR_STATE_KEY, cached)
        useAuthStore.getState().login(owner)
        renderSidebar()
        assertDiscoverable('/ecommerce/payment-methods')
        assertDiscoverable('/ecommerce/shipping-methods')
    })

    it('keeps explicit same-actor collapse preferences across remount and logout/login', async () => {
        useAuthStore.getState().login(owner)
        const view = renderSidebar()
        fireEvent.click(branch('operations-payments'))
        expect(branch('operations-payments')).toHaveAttribute('aria-expanded', 'false')
        await waitFor(() => expect(JSON.parse(localStorage.getItem(STOREFRONT_SIDEBAR_STATE_KEY)).expanded['operations-payments']).toBe(false))
        view.unmount()
        useAuthStore.getState().logout()
        useAuthStore.getState().login(owner)
        renderSidebar()
        expect(branch('operations-payments')).toHaveAttribute('aria-expanded', 'false')
        assertDiscoverable('/ecommerce/shipping-methods')
    })

    it('does not treat an empty scoped snapshot as disabled navigation or allow cached authority to grant access', () => {
        const cached = JSON.stringify({ version: 2, scope: `${owner.tenantId}:${owner.id}`, expanded: {}, effectivePermissions: ['storefront.payments.manage'] })
        localStorage.setItem(STOREFRONT_SIDEBAR_STATE_KEY, cached)
        useAuthStore.getState().login(owner)
        renderSidebar()
        assertDiscoverable('/ecommerce/payment-methods')
        assertDiscoverable('/ecommerce/shipping-methods')
        act(() => useAuthStore.getState().hydrateSession({ ...owner, effectivePermissions: ['dashboard.view'] }))
        expect(document.querySelector('aside a[href="/ecommerce/payment-methods"]')).toBeNull()
    })

    it('resets in-memory and cached expansion when tenant or actor changes without route navigation', () => {
        useAuthStore.getState().login(owner)
        renderSidebar()
        fireEvent.click(branch('operations-shipping'))
        const otherTenant = '33333333-3333-4333-8333-333333333333'
        act(() => useAuthStore.getState().hydrateSession({ ...owner, tenantId: otherTenant, vendorLifecycleAuthority: { ...owner.vendorLifecycleAuthority, vendorId: otherTenant } }))
        assertDiscoverable('/ecommerce/shipping-methods')
        fireEvent.click(branch('operations-payments'))
        act(() => useAuthStore.getState().hydrateSession({ ...owner, id: '44444444-4444-4444-8444-444444444444', tenantId: otherTenant, vendorLifecycleAuthority: { ...owner.vendorLifecycleAuthority, vendorId: otherTenant } }))
        assertDiscoverable('/ecommerce/payment-methods')
    })

    it.each([
        ['Owner', owner.effectivePermissions, true, true],
        ['Admin', owner.effectivePermissions, true, true],
        ['Manager', ['dashboard.view', 'storefront.orders.view', 'shipping.manage'], false, true],
        ['Cashier', ['dashboard.view'], false, false],
        ['Warehouse', ['dashboard.view', 'shipping.view', 'shipping.fulfill'], false, false],
    ])('projects %s permissions into navigation and preserves direct-route denial', (role, effectivePermissions, paymentAllowed, shippingAllowed) => {
        useAuthStore.getState().login({ ...owner, role, effectivePermissions })
        const view = renderSidebar()
        expect(Boolean(document.querySelector('aside a[href="/ecommerce/payment-methods"]'))).toBe(paymentAllowed)
        expect(Boolean(document.querySelector('aside a[href="/ecommerce/shipping-methods"]'))).toBe(shippingAllowed)
        if (paymentAllowed) assertDiscoverable('/ecommerce/payment-methods')
        if (shippingAllowed) assertDiscoverable('/ecommerce/shipping-methods')
        view.unmount()
        for (const [path, permission, allowed] of [
            ['/ecommerce/payment-methods', 'storefront.payments.manage', paymentAllowed],
            ['/ecommerce/shipping-methods', 'shipping.manage', shippingAllowed],
        ]) {
            const routeView = render(<MemoryRouter initialEntries={[path]}><Routes><Route path="/" element={<p>Denied landing</p>} /><Route element={<PosPermissionRouteGuard permission={permission} />}><Route path={path} element={<p>Protected workspace</p>} /></Route></Routes></MemoryRouter>)
            expect(Boolean(screen.queryByText('Protected workspace'))).toBe(allowed)
            expect(Boolean(screen.queryByText('Denied landing'))).toBe(!allowed)
            routeView.unmount()
        }
    })

    it('removes navigation immediately when refreshed authority revokes permissions', () => {
        useAuthStore.getState().login(owner)
        renderSidebar()
        act(() => useAuthStore.getState().hydrateSession({ ...owner, effectivePermissions: ['dashboard.view'] }))
        expect(document.querySelector('aside a[href="/ecommerce/payment-methods"]')).toBeNull()
        expect(document.querySelector('aside a[href="/ecommerce/shipping-methods"]')).toBeNull()
    })

    it('does not expose unrelated management links through structural parents when a Manager can access Shipping', () => {
        useAuthStore.getState().login({ ...owner, role: 'Manager', effectivePermissions: ['dashboard.view', 'storefront.orders.view', 'storefront.payments.view', 'storefront.analytics.view', 'storefront.promotions.manage', 'shipping.manage'] })
        renderSidebar()
        assertDiscoverable('/ecommerce/shipping-methods')
        for (const path of ['/ecommerce/payment-methods', '/ecommerce/auth-providers', '/ecommerce/commerce-ai', '/ecommerce/pixel-tracking', '/settings/analytics']) {
            expect(document.querySelector(`aside a[href="${path}"]`)).toBeNull()
        }
        expect(document.querySelector('aside a[href="/analytics/storefront"]')).toBeInTheDocument()
    })

    it.each(['en', 'ar'])('discovers initial navigation in %s independently of direction', async language => {
        await i18n.changeLanguage(language)
        useAuthStore.getState().login(owner)
        renderSidebar()
        assertDiscoverable('/ecommerce/payment-methods')
        assertDiscoverable('/ecommerce/shipping-methods')
        expect(document.querySelector('aside')).toHaveAttribute('dir', language === 'ar' ? 'rtl' : 'ltr')
    })

    it('fails closed for missing capability authority rather than interpreting it as a disabled module', () => {
        useAuthStore.getState().hydrateSession({ ...owner, navigationCapabilities: null })
        renderSidebar()
        expect(useAuthStore.getState().isAuthenticated).toBe(false)
        expect(document.querySelector('aside a[href="/ecommerce/payment-methods"]')).toBeNull()
    })
})
