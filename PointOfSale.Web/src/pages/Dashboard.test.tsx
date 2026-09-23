import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import Dashboard from './Dashboard'
import { useAuthStore } from '../store/authStore'

vi.mock('../components/PosDashboardContent', () => ({
    default: () => <div>POS dashboard content</div>,
}))
vi.mock('./StorefrontAnalyticsDashboard', () => ({
    default: () => <div>Storefront analytics content</div>,
}))
vi.mock('../hooks/useRTL', () => ({
    useRTL: () => ({ isRTL: false, dir: 'ltr' }),
}))
vi.mock('react-i18next', () => ({
    useTranslation: () => ({ t: (key: string) => key }),
}))

const cashierUser = {
    id: 'cashier-1',
    tenantId: 'tenant-1',
    role: 'Cashier',
    effectivePermissions: [
        'dashboard.view',
        'catalog.view',
        'customers.view',
        'customers.manage',
        'sales.view',
        'sales.operate',
    ],
}

const ownerUser = {
    id: 'owner-1',
    tenantId: 'tenant-1',
    role: 'Owner',
    effectivePermissions: ['dashboard.view', 'storefront.analytics.view'],
}

describe('Dashboard permission gating', () => {
    beforeEach(() => {
        window.localStorage.clear()
    })

    afterEach(() => {
        useAuthStore.setState({ user: null, isAuthenticated: false, hasHydrated: true })
        window.localStorage.clear()
    })

    it('falls back to POS content for Cashier with a stale storefront tab stored', () => {
        window.localStorage.setItem('admin.dashboard.activeTab', 'storefront')
        useAuthStore.setState({ user: cashierUser, isAuthenticated: true, hasHydrated: true })

        render(
            <MemoryRouter>
                <Dashboard />
            </MemoryRouter>,
        )

        expect(screen.getByText('POS dashboard content')).toBeInTheDocument()
        expect(screen.queryByText('Storefront analytics content')).not.toBeInTheDocument()
        expect(screen.queryByText('dashboard.tabs.storefront')).not.toBeInTheDocument()
        expect(window.localStorage.getItem('admin.dashboard.activeTab')).toBe('pos')
    })

    it('keeps the storefront tab for permission holders', () => {
        window.localStorage.setItem('admin.dashboard.activeTab', 'storefront')
        useAuthStore.setState({ user: ownerUser, isAuthenticated: true, hasHydrated: true })

        render(
            <MemoryRouter>
                <Dashboard />
            </MemoryRouter>,
        )

        expect(screen.getByText('Storefront analytics content')).toBeInTheDocument()
        expect(screen.getByText('dashboard.tabs.storefront')).toBeInTheDocument()
        expect(screen.getByText('dashboard.tabs.pos')).toBeInTheDocument()
    })
})
