import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { act, useState } from 'react'
import SuperAdminProtectedRoute from './SuperAdminProtectedRoute'
import {
    SUPER_ADMIN_SESSION_LOGOUT_STORAGE_KEY,
    useSuperAdminAuthStore,
} from './superAdminAuthStore'

const coordinatorMock = vi.hoisted(() => ({
    restore: vi.fn(),
}))

vi.mock('./useSuperAdminSessionBootstrap', () => ({
    useSuperAdminSessionBootstrap: () => true,
}))

vi.mock('./superAdminSessionCoordinator', () => ({
    restoreSuperAdminSession: coordinatorMock.restore,
    isDefinitiveSuperAdminAuthFailure: (error: { status?: number }) => (
        error?.status === 401 || error?.status === 403
    ),
}))

const session = {
    superAdminId: '11111111-1111-4111-8111-111111111111',
    email: 'admin@example.test',
    role: 'SuperAdmin' as const,
    tokenType: 'Bearer',
    expiresAtUtc: '2099-01-01T00:00:00.000Z',
    refreshTokenExpiresAtUtc: '2099-01-02T00:00:00.000Z',
    tokenVersion: 1,
    navigationCapabilities: ['super-admin.finance.view'] as const,
}

function createDeferred<T>() {
    let resolve!: (value: T) => void
    let reject!: (reason: unknown) => void
    const promise = new Promise<T>((res, rej) => {
        resolve = res
        reject = rej
    })

    return { promise, resolve, reject }
}

function DraftForm() {
    const [value, setValue] = useState('')
    return <input aria-label="Super Admin draft" value={value} onChange={(event) => setValue(event.target.value)} />
}

function renderProtectedRoute() {
    return render(
        <MemoryRouter initialEntries={['/super-admin/finance']}>
            <Routes>
                <Route element={<SuperAdminProtectedRoute />}>
                    <Route path="/super-admin/finance" element={<DraftForm />} />
                </Route>
                <Route path="/super-admin/login" element={<div>Super Admin login</div>} />
            </Routes>
        </MemoryRouter>,
    )
}

describe('Super Admin lifecycle session stability', () => {
    beforeEach(() => {
        localStorage.clear()
        coordinatorMock.restore.mockReset()
        useSuperAdminAuthStore.setState({
            user: {
                superAdminId: session.superAdminId,
                email: session.email,
                role: session.role,
                authType: 'super_admin',
                navigationCapabilities: [...session.navigationCapabilities],
            },
            isAuthenticated: true,
            hasHydrated: true,
        })
    })

    it('deduplicates focus and visibility checks while preserving unsaved form state', async () => {
        const lifecycleSession = createDeferred<typeof session>()
        coordinatorMock.restore.mockReturnValue(lifecycleSession.promise)
        const user = userEvent.setup()
        renderProtectedRoute()

        const input = screen.getByLabelText('Super Admin draft')
        await user.type(input, 'unsaved-platform-change')
        const userAuthorityBefore = useSuperAdminAuthStore.getState().user

        act(() => {
            window.dispatchEvent(new Event('blur'))
            window.dispatchEvent(new Event('focus'))
            document.dispatchEvent(new Event('visibilitychange'))
        })

        expect(coordinatorMock.restore).toHaveBeenCalledOnce()
        expect(screen.getByLabelText('Super Admin draft')).toHaveValue('unsaved-platform-change')
        expect(screen.queryByText('Checking secure Super Admin session...')).not.toBeInTheDocument()

        await act(async () => {
            lifecycleSession.resolve(session)
            await lifecycleSession.promise
        })

        expect(screen.getByLabelText('Super Admin draft')).toHaveValue('unsaved-platform-change')
        expect(useSuperAdminAuthStore.getState().user).toBe(userAuthorityBefore)
    })

    it('retains a proven session after a temporary background network failure', async () => {
        coordinatorMock.restore.mockRejectedValue(new Error('network unavailable'))
        renderProtectedRoute()

        act(() => window.dispatchEvent(new Event('focus')))

        await waitFor(() => expect(coordinatorMock.restore).toHaveBeenCalledOnce())
        expect(useSuperAdminAuthStore.getState().isAuthenticated).toBe(true)
        expect(screen.getByLabelText('Super Admin draft')).toBeInTheDocument()
    })

    it('fails closed after backend authentication authority is definitively lost', async () => {
        coordinatorMock.restore.mockRejectedValue({ status: 401 })
        renderProtectedRoute()

        act(() => window.dispatchEvent(new Event('focus')))

        expect(await screen.findByText('Super Admin login')).toBeInTheDocument()
        expect(useSuperAdminAuthStore.getState().isAuthenticated).toBe(false)
    })

    it('ignores unrelated storage changes and honors explicit cross-tab logout', () => {
        renderProtectedRoute()

        act(() => {
            window.dispatchEvent(new StorageEvent('storage', {
                key: 'super-admin-auth-storage',
                oldValue: 'old',
                newValue: null,
            }))
        })
        expect(useSuperAdminAuthStore.getState().isAuthenticated).toBe(true)

        act(() => {
            window.dispatchEvent(new StorageEvent('storage', {
                key: SUPER_ADMIN_SESSION_LOGOUT_STORAGE_KEY,
                oldValue: null,
                newValue: 'logout',
            }))
        })
        expect(useSuperAdminAuthStore.getState().isAuthenticated).toBe(false)
    })
})
