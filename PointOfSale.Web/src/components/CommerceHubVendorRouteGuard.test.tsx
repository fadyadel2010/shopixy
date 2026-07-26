import { afterEach, describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import CommerceHubVendorRouteGuard from './CommerceHubVendorRouteGuard'
import { useAuthStore } from '../store/authStore'

const tenantId = '22222222-2222-4222-8222-222222222222'

const baseUser = {
    id: '11111111-1111-4111-8111-111111111111',
    tenantId,
    role: 'Admin',
    navigationCapabilities: [
        'commercehub.storefront.manage',
        'commercehub.storefront.cms.manage',
        'commercehub.storefront.preview',
    ],
    vendorLifecycleAuthority: {
        vendorId: tenantId,
        status: 'Active',
        governanceStatus: 'Active',
        tenantActive: true,
        marketplaceApproved: true,
        kycVerified: true,
        debtStatus: 'ACTIVE',
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

const renderGuardedStorefrontRoute = () => render(
    <MemoryRouter initialEntries={['/storefront']}>
        <Routes>
            <Route path="/" element={<div>Dashboard</div>} />
            <Route element={<CommerceHubVendorRouteGuard capability="commercehub.storefront.manage" />}>
                <Route path="/storefront" element={<div>Storefront Administration</div>} />
            </Route>
        </Routes>
    </MemoryRouter>,
)

describe('CommerceHubVendorRouteGuard', () => {
    afterEach(() => {
        useAuthStore.setState({
            user: null,
            isAuthenticated: false,
            hasHydrated: true,
        })
    })

    it('renders Storefront Administration after backend-admitted Storefront navigation authority', () => {
        useAuthStore.setState({
            user: baseUser,
            isAuthenticated: true,
            hasHydrated: true,
        })

        renderGuardedStorefrontRoute()

        expect(screen.getByText('Storefront Administration')).toBeInTheDocument()
    })

    it('does not render bookmarked Storefront routes when backend lifecycle blocks Storefront', () => {
        useAuthStore.setState({
            user: {
                ...baseUser,
                vendorLifecycleAuthority: {
                    ...baseUser.vendorLifecycleAuthority,
                    blocksStorefront: true,
                },
            },
            isAuthenticated: true,
            hasHydrated: true,
        })

        renderGuardedStorefrontRoute()

        expect(screen.getByText('Dashboard')).toBeInTheDocument()
        expect(screen.queryByText('Storefront Administration')).not.toBeInTheDocument()
    })
})
