import { afterEach, describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import PosRoleRouteGuard from './PosRoleRouteGuard'
import { useAuthStore } from '../store/authStore'

const baseUser = {
    id: '11111111-1111-4111-8111-111111111111',
    tenantId: '22222222-2222-4222-8222-222222222222',
    navigationCapabilities: [],
    vendorLifecycleAuthority: {
        vendorId: '22222222-2222-4222-8222-222222222222',
        status: 'Active',
    },
}

const renderGuardedAffiliateRoute = () => render(
    <MemoryRouter initialEntries={['/admin/affiliates']}>
        <Routes>
            <Route path="/" element={<div>Dashboard</div>} />
            <Route element={<PosRoleRouteGuard roles={['Owner', 'Admin']} />}>
                <Route path="/admin/affiliates" element={<div>Affiliate Configuration</div>} />
            </Route>
        </Routes>
    </MemoryRouter>,
)

describe('PosRoleRouteGuard', () => {
    afterEach(() => {
        useAuthStore.setState({
            user: null,
            isAuthenticated: false,
            hasHydrated: true,
        })
    })

    it('renders affiliate configuration for backend-hydrated Owner/Admin presentation roles', () => {
        useAuthStore.setState({
            user: { ...baseUser, role: 'Admin' },
            isAuthenticated: true,
            hasHydrated: true,
        })

        renderGuardedAffiliateRoute()

        expect(screen.getByText('Affiliate Configuration')).toBeInTheDocument()
    })

    it('does not render affiliate configuration for a bookmarked or direct route without the presentation role', () => {
        useAuthStore.setState({
            user: { ...baseUser, role: 'Cashier' },
            isAuthenticated: true,
            hasHydrated: true,
        })

        renderGuardedAffiliateRoute()

        expect(screen.getByText('Dashboard')).toBeInTheDocument()
        expect(screen.queryByText('Affiliate Configuration')).not.toBeInTheDocument()
    })
})
