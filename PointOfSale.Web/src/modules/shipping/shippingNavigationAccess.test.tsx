import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import Sidebar from '../../components/Sidebar'
import PosPermissionRouteGuard from '../../components/PosPermissionRouteGuard'
import CommerceHubVendorRouteGuard from '../../components/CommerceHubVendorRouteGuard'
import ShippingAccessDenied from './components/ShippingAccessDenied'
import { useAuthStore } from '../../store/authStore'
import type { PosUser } from '../../store/authStore'
import i18n from '../../i18n/config'
import { resolveStorefrontScreen, isStorefrontNodeActive, STOREFRONT_PRODUCT_NAVIGATION } from '../storefront/informationArchitecture'

const path = '/admin/shipping/shipments'
const tenantId = '22222222-2222-4222-8222-222222222222'
const viewer = {
    id: '11111111-1111-4111-8111-111111111111', tenantId, role: 'Admin',
    effectivePermissions: ['dashboard.view', 'shipping.view'],
    navigationCapabilities: ['commercehub.storefront.manage'],
    vendorLifecycleAuthority: {
        vendorId: tenantId, status: 'Active', governanceStatus: 'Active', tenantActive: true,
        marketplaceApproved: true, kycVerified: true, debtStatus: 'ACTIVE', isDebtBlocked: false,
        blocksCatalog: false, blocksOrders: false, blocksStorefront: false, blocksAnalytics: false,
        blocksPayments: false, blocksPayouts: false, blocksLogin: false,
    },
}

function LocationProbe() {
    return <output data-testid="current-path">{useLocation().pathname}</output>
}

function renderShipping(user: PosUser, initialPath = path) {
    useAuthStore.setState({ user, isAuthenticated: true, hasHydrated: true })
    return render(
        <MemoryRouter initialEntries={[initialPath]}>
            <Sidebar isOpen onClose={() => {}} />
            <LocationProbe />
            <Routes>
                <Route path="/" element={<p>Dashboard redirect</p>} />
                <Route element={<PosPermissionRouteGuard permission="shipping.view" deniedElement={<ShippingAccessDenied reason="permission" />} />}>
                    <Route element={<CommerceHubVendorRouteGuard capability="commercehub.storefront.manage" deniedElement={<ShippingAccessDenied reason="storefront" />} />}>
                        <Route path={path} element={<p>Authorized shipment data</p>} />
                        <Route path={`${path}/:id`} element={<p>Authorized shipment data</p>} />
                    </Route>
                </Route>
            </Routes>
        </MemoryRouter>,
    )
}

describe('shipping navigation and explicit access denial', () => {
    beforeEach(async () => { localStorage.clear(); await i18n.changeLanguage('en') })
    afterEach(() => { cleanup(); useAuthStore.setState({ user: null, isAuthenticated: false, hasHydrated: true }) })

    it.each([
        ['authorized view-only account', viewer, true],
        ['Admin without shipping permission', { ...viewer, effectivePermissions: ['dashboard.view'] }, false],
        ['missing backend capability', { ...viewer, navigationCapabilities: [] }, false],
        ['blocked Storefront lifecycle', { ...viewer, vendorLifecycleAuthority: { ...viewer.vendorLifecycleAuthority, blocksStorefront: true } }, false],
        ['foreign vendor identity', { ...viewer, vendorLifecycleAuthority: { ...viewer.vendorLifecycleAuthority, vendorId: '33333333-3333-4333-8333-333333333333' } }, false],
    ])('keeps the sidebar and route admission aligned for %s', (_name, user, allowed) => {
        renderShipping(user)
        expect(Boolean(document.querySelector(`aside a[href="${path}"]`))).toBe(allowed)
        // Viewing shipments must not require or grant method-management permission.
        expect(document.querySelector('aside a[href="/ecommerce/shipping-methods"]')).toBeNull()
        expect(Boolean(screen.queryByText('Authorized shipment data'))).toBe(allowed)
        expect(screen.queryByText('Dashboard redirect')).not.toBeInTheDocument()
        expect(screen.getByTestId('current-path')).toHaveTextContent(path)
        if (!allowed) expect(screen.getByRole('alert')).toHaveTextContent('Shipping access required')
    })

    it('explains the specific missing permission on bookmarked shipment details', () => {
        renderShipping({ ...viewer, effectivePermissions: ['dashboard.view'] }, `${path}/synthetic-id`)
        expect(screen.getByRole('alert')).toHaveTextContent('shipping.view')
        expect(screen.getByRole('link', { name: 'Return to dashboard' })).toHaveAttribute('href', '/')
        expect(screen.queryByText('Authorized shipment data')).not.toBeInTheDocument()
    })

    it.each(['en', 'ar'])('localizes denial and the Shipments sidebar link in %s', async language => {
        await i18n.changeLanguage(language)
        const view = renderShipping(viewer)
        const link = document.querySelector(`aside a[href="${path}"]`)
        expect(link).toHaveTextContent(i18n.t('shipping.shipments'))
        expect(document.querySelector('aside')).toHaveAttribute('dir', language === 'ar' ? 'rtl' : 'ltr')
        view.unmount()
        renderShipping({ ...viewer, navigationCapabilities: [] })
        expect(screen.getByRole('alert')).toHaveTextContent(i18n.t('shippingAccess.storefront'))
        expect(screen.getByRole('alert')).toHaveTextContent(i18n.t('shippingAccess.help'))
    })

    it('resolves lists and details to the same active sidebar destination', () => {
        for (const destination of [path, `${path}/synthetic-id`]) {
            expect(resolveStorefrontScreen(destination)?.id).toBe('shipments')
            expect(isStorefrontNodeActive(STOREFRONT_PRODUCT_NAVIGATION, 'shipments')).toBe(true)
        }
    })
})
