import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import Sidebar from './Sidebar'
import { useAuthStore } from '../store/authStore'
import i18n from '../i18n/config'

const ownerUser = {
    id: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
    tenantId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
    role: 'Owner',
    effectivePermissions: ['users.view', 'users.invite', 'audit.view', 'settings.view'],
    navigationCapabilities: [],
    vendorLifecycleAuthority: {
        vendorId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
        status: 'Active',
        debtStatus: 'Current',
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

beforeEach(async () => {
    await i18n.changeLanguage('en')
})

afterEach(() => {
    useAuthStore.setState({ user: null, isAuthenticated: false, hasHydrated: true })
})

const renderSidebar = () => render(
    <MemoryRouter initialEntries={['/']}>
        <Sidebar isOpen onClose={() => {}} />
    </MemoryRouter>,
)

describe('Sidebar users navigation', () => {
    it('shows Users Management and audit tracking to Owner/Admin permission holders', async () => {
        useAuthStore.setState({ user: ownerUser, isAuthenticated: true, hasHydrated: true })
        renderSidebar()
        // Group label and child link share the nav label.
        expect(await screen.findAllByText('Users Management')).not.toHaveLength(0)
    })

    it('hides the Users group from roles without users or audit permissions', async () => {
        useAuthStore.setState({
            user: { ...ownerUser, role: 'Cashier', effectivePermissions: ['dashboard.view', 'sales.view'] },
            isAuthenticated: true,
            hasHydrated: true,
        })
        renderSidebar()
        await screen.findByText('Dashboard')
        expect(screen.queryByText('Users Management')).not.toBeInTheDocument()
    })
})
