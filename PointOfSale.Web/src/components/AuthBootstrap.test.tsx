import { StrictMode, act } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import AuthBootstrap from './AuthBootstrap'
import ProtectedRoute from './ProtectedRoute'
import { useAuthStore } from '../store/authStore'
import { marketplaceQueryClient, queryClient, superAdminQueryClient } from '../queryClient'
import { POS_VENDOR_SESSION_AUTHORITY_LOST_EVENT } from '../auth/posVendorSessionAuthority'

const apiMock = vi.hoisted(() => ({
    get: vi.fn(),
}))

vi.mock('../services/api', () => ({
    default: apiMock,
}))

function createDeferred() {
    let resolve
    let reject

    const promise = new Promise((res, rej) => {
        resolve = res
        reject = rej
    })

    return { promise, resolve, reject }
}

const authenticatedUser = {
    id: '11111111-1111-4111-8111-111111111111',
    tenantId: '22222222-2222-4222-8222-222222222222',
    username: 'cashier',
    firstName: 'POS',
    lastName: 'User',
    role: 'Cashier',
    effectivePermissions: [
        'dashboard.view',
        'catalog.view',
        'customers.view',
        'customers.manage',
        'sales.view',
        'sales.operate',
    ],
    navigationCapabilities: [],
    vendorLifecycleAuthority: {
        vendorId: '22222222-2222-4222-8222-222222222222',
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

describe('POS auth bootstrap runtime hardening', () => {
    beforeEach(() => {
        localStorage.clear()
        useAuthStore.setState({
            user: null,
            isAuthenticated: false,
            hasHydrated: false,
        })
        apiMock.get.mockReset()
        queryClient.clear()
        marketplaceQueryClient.clear()
        superAdminQueryClient.clear()
    })

    afterEach(() => {
        localStorage.clear()
        vi.clearAllMocks()
    })

    it('does not let a stale anonymous session bootstrap wipe a newer login', async () => {
        const bootstrapRequest = createDeferred()
        apiMock.get.mockReturnValue(bootstrapRequest.promise)

        render(<AuthBootstrap />)

        expect(apiMock.get).toHaveBeenCalledWith('/auth/session')

        act(() => {
            useAuthStore.getState().login(authenticatedUser)
        })

        expect(useAuthStore.getState().isAuthenticated).toBe(true)
        expect(useAuthStore.getState().hasHydrated).toBe(true)

        await act(async () => {
            bootstrapRequest.reject(new Error('Unauthorized'))
            await Promise.resolve()
        })

        await waitFor(() => {
            const state = useAuthStore.getState()
            expect(state.isAuthenticated).toBe(true)
            expect(state.user?.id).toBe(authenticatedUser.id)
            expect(state.hasHydrated).toBe(true)
        })
    })

    it('does not let a stale successful bootstrap replace a newer login actor', async () => {
        const bootstrapRequest = createDeferred()
        apiMock.get.mockReturnValue(bootstrapRequest.promise)

        render(<AuthBootstrap />)

        const replacementUser = {
            ...authenticatedUser,
            id: '33333333-3333-4333-8333-333333333333',
        }

        act(() => {
            useAuthStore.getState().login(replacementUser)
        })

        await act(async () => {
            bootstrapRequest.resolve({ data: { user: authenticatedUser } })
            await Promise.resolve()
        })

        expect(useAuthStore.getState().user?.id).toBe(replacementUser.id)
    })

    it('restarts an in-flight bootstrap after the StrictMode effect replay', async () => {
        const abandonedRequest = createDeferred()
        apiMock.get
            .mockReturnValueOnce(abandonedRequest.promise)
            .mockResolvedValueOnce({ data: { user: authenticatedUser } })

        render(
            <StrictMode>
                <AuthBootstrap />
            </StrictMode>,
        )

        await waitFor(() => {
            expect(apiMock.get).toHaveBeenCalledTimes(2)
            const state = useAuthStore.getState()
            expect(state.isAuthenticated).toBe(true)
            expect(state.hasHydrated).toBe(true)
        })

        await act(async () => {
            abandonedRequest.resolve({ data: { user: { ...authenticatedUser, id: 'stale-user' } } })
            await Promise.resolve()
        })

        expect(useAuthStore.getState().user?.id).toBe(authenticatedUser.id)
    })

    it('does not render protected content for a persisted session before backend bootstrap proves authority', () => {
        useAuthStore.setState({
            user: authenticatedUser,
            isAuthenticated: true,
            hasHydrated: false,
        })

        render(
            <MemoryRouter initialEntries={['/']}>
                <Routes>
                    <Route element={<ProtectedRoute />}>
                        <Route path="/" element={<div>Dashboard</div>} />
                    </Route>
                </Routes>
            </MemoryRouter>,
        )

        expect(screen.getByText('Restoring session...')).toBeInTheDocument()
        expect(screen.queryByText('Dashboard')).not.toBeInTheDocument()
    })

    it('shows a visible session restore state instead of a blank protected route while auth is unresolved', () => {
        render(
            <MemoryRouter initialEntries={['/']}>
                <Routes>
                    <Route element={<ProtectedRoute />}>
                        <Route path="/" element={<div>Dashboard</div>} />
                    </Route>
                </Routes>
            </MemoryRouter>,
        )

        expect(screen.getByText('Restoring session...')).toBeInTheDocument()
    })

    it('rejects malformed backend session DTOs and clears protected presentation', async () => {
        apiMock.get.mockResolvedValue({ data: { user: { id: 'user-1', role: 'Cashier' } } })

        render(<AuthBootstrap />)

        await waitFor(() => {
            const state = useAuthStore.getState()
            expect(state.isAuthenticated).toBe(false)
            expect(state.user).toBeNull()
            expect(state.hasHydrated).toBe(true)
        })
    })

    it('treats cross-tab storage as terminal logout, not session creation authority', async () => {
        apiMock.get.mockResolvedValue({ data: { user: authenticatedUser } })

        render(<AuthBootstrap />)

        await waitFor(() => {
            expect(useAuthStore.getState().isAuthenticated).toBe(true)
        })

        act(() => {
            window.dispatchEvent(new StorageEvent('storage', { key: 'pos_vendor_session_logout_at' }))
        })

        expect(useAuthStore.getState().isAuthenticated).toBe(false)
        expect(useAuthStore.getState().user).toBeNull()
    })

    it('ignores storage activity from foreign runtime domains', async () => {
        apiMock.get.mockResolvedValue({ data: { user: authenticatedUser } })

        render(<AuthBootstrap />)

        await waitFor(() => {
            expect(useAuthStore.getState().isAuthenticated).toBe(true)
        })

        act(() => {
            window.dispatchEvent(new StorageEvent('storage', { key: 'super_admin_session_logout_at' }))
            window.dispatchEvent(new StorageEvent('storage', { key: null }))
        })

        expect(useAuthStore.getState().isAuthenticated).toBe(true)
        expect(useAuthStore.getState().user?.id).toBe(authenticatedUser.id)
    })

    it('revalidates CommerceHub vendor authority loss without converting it into POS logout', async () => {
        apiMock.get.mockResolvedValue({ data: { user: authenticatedUser } })

        render(<AuthBootstrap />)

        await waitFor(() => {
            expect(useAuthStore.getState().isAuthenticated).toBe(true)
        })

        act(() => {
            window.dispatchEvent(new Event(POS_VENDOR_SESSION_AUTHORITY_LOST_EVENT))
        })

        await waitFor(() => {
            expect(apiMock.get).toHaveBeenCalledTimes(2)
            expect(useAuthStore.getState().isAuthenticated).toBe(true)
            expect(useAuthStore.getState().hasHydrated).toBe(true)
        })
    })

    it('purges POS queries on actor replacement without clearing marketplace or Super Admin domains', async () => {
        apiMock.get.mockResolvedValue({ data: { user: authenticatedUser } })
        queryClient.setQueryData(['pos-authority'], { actor: 'old' })
        marketplaceQueryClient.setQueryData(['marketplace', 'scope'], { actor: 'marketplace' })
        superAdminQueryClient.setQueryData(['super-admin', 'finance'], { actor: 'platform' })

        render(<AuthBootstrap />)

        await waitFor(() => {
            expect(useAuthStore.getState().isAuthenticated).toBe(true)
        })

        const replacementUser = {
            ...authenticatedUser,
            id: '33333333-3333-4333-8333-333333333333',
        }

        act(() => {
            useAuthStore.getState().login(replacementUser)
        })

        expect(queryClient.getQueryData(['pos-authority'])).toBeUndefined()
        expect(marketplaceQueryClient.getQueryData(['marketplace', 'scope'])).toEqual({ actor: 'marketplace' })
        expect(superAdminQueryClient.getQueryData(['super-admin', 'finance'])).toEqual({ actor: 'platform' })
    })

    it('withholds protected routes while BFCache restoration revalidates backend authority', async () => {
        const restoredSession = createDeferred()
        apiMock.get
            .mockResolvedValueOnce({ data: { user: authenticatedUser } })
            .mockReturnValueOnce(restoredSession.promise)

        render(
            <>
                <AuthBootstrap />
                <MemoryRouter initialEntries={['/commercehub/finance']}>
                    <Routes>
                        <Route element={<ProtectedRoute />}>
                            <Route path="/commercehub/finance" element={<div>Vendor Finance</div>} />
                        </Route>
                    </Routes>
                </MemoryRouter>
            </>,
        )

        await waitFor(() => {
            expect(screen.getByText('Vendor Finance')).toBeInTheDocument()
        })

        act(() => {
            const event = new Event('pageshow')
            Object.defineProperty(event, 'persisted', { value: true })
            window.dispatchEvent(event)
        })

        expect(screen.getByText('Restoring session...')).toBeInTheDocument()
        expect(screen.queryByText('Vendor Finance')).not.toBeInTheDocument()

        await act(async () => {
            restoredSession.resolve({ data: { user: authenticatedUser } })
            await Promise.resolve()
        })

        await waitFor(() => {
            expect(screen.getByText('Vendor Finance')).toBeInTheDocument()
        })
    })
})
