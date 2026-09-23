import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import ResetPasswordPage from './ResetPasswordPage'
import { useAuthStore } from '../store/authStore'
import { completePosPasswordReset } from '../services/userManagementApi'
import api from '../services/api'

vi.mock('../services/userManagementApi', () => ({
    completePosPasswordReset: vi.fn(),
}))
vi.mock('../services/api', () => ({
    default: { post: vi.fn() },
}))
vi.mock('../services/posRuntimeCache', () => ({
    invalidatePosRuntimeCache: vi.fn(),
}))
vi.mock('../auth/posVendorSessionAuthority', async (importOriginal) => ({
    ...await importOriginal<typeof import('../auth/posVendorSessionAuthority')>(),
    broadcastPosVendorLogout: vi.fn(),
}))
vi.mock('../hooks/useRTL', () => ({
    useRTL: () => ({ dir: 'ltr' }),
}))
vi.mock('react-i18next', () => ({
    useTranslation: () => ({ t: (key: string) => key }),
}))

describe('ResetPasswordPage', () => {
    beforeEach(() => {
        vi.clearAllMocks()
        useAuthStore.setState({
            user: { id: 'owner-1', role: 'Owner' },
            isAuthenticated: true,
            hasHydrated: true,
        })
        vi.mocked(completePosPasswordReset).mockResolvedValue({})
        vi.mocked(api.post).mockResolvedValue({})
    })

    afterEach(() => {
        useAuthStore.setState({ user: null, isAuthenticated: false, hasHydrated: true })
    })

    it('revokes an existing session before navigating to login', async () => {
        render(
            <MemoryRouter initialEntries={['/reset-password?token=test-reset-token']}>
                <Routes>
                    <Route path="/reset-password" element={<ResetPasswordPage />} />
                    <Route path="/login" element={<div>Login destination</div>} />
                </Routes>
            </MemoryRouter>,
        )

        fireEvent.change(screen.getByLabelText('userManagement.resetPage.password'), {
            target: { value: 'NewStrongPassword1!' },
        })
        fireEvent.click(screen.getByRole('button', { name: 'userManagement.resetPage.submit' }))

        await screen.findByText('userManagement.resetPage.existingSession')
        fireEvent.click(screen.getByRole('button', { name: 'userManagement.resetPage.goToLogin' }))

        await screen.findByText('Login destination')
        expect(completePosPasswordReset).toHaveBeenCalledWith('test-reset-token', 'NewStrongPassword1!')
        expect(api.post).toHaveBeenCalledWith('/auth/logout')
        await waitFor(() => expect(useAuthStore.getState().isAuthenticated).toBe(false))
    })
})
