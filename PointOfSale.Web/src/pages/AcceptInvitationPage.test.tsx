import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import AcceptInvitationPage from './AcceptInvitationPage'
import { useAuthStore } from '../store/authStore'
import { acceptPosUserInvitation } from '../services/userManagementApi'
import api from '../services/api'
import { invalidatePosRuntimeCache } from '../services/posRuntimeCache'
import { broadcastPosVendorLogout } from '../auth/posVendorSessionAuthority'

vi.mock('../services/userManagementApi', () => ({
    acceptPosUserInvitation: vi.fn(),
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

describe('AcceptInvitationPage', () => {
    beforeEach(() => {
        vi.clearAllMocks()
        useAuthStore.setState({
            user: { id: 'owner-1', role: 'Owner' },
            isAuthenticated: true,
            hasHydrated: true,
        })
        vi.mocked(acceptPosUserInvitation).mockResolvedValue({})
        vi.mocked(api.post).mockResolvedValue({})
    })

    afterEach(() => {
        useAuthStore.setState({ user: null, isAuthenticated: false, hasHydrated: true })
    })

    it('revokes an existing session before navigating to login', async () => {
        render(
            <MemoryRouter initialEntries={['/accept-invitation?token=test-invitation-token']}>
                <Routes>
                    <Route path="/accept-invitation" element={<AcceptInvitationPage />} />
                    <Route path="/login" element={<div>Login destination</div>} />
                </Routes>
            </MemoryRouter>,
        )

        fireEvent.change(screen.getByLabelText('userManagement.acceptPage.password'), {
            target: { value: 'StrongPassword1!' },
        })
        fireEvent.click(screen.getByRole('button', { name: 'userManagement.acceptPage.submit' }))

        await screen.findByText('userManagement.acceptPage.existingSession')
        fireEvent.click(screen.getByRole('button', { name: 'userManagement.acceptPage.goToLogin' }))

        await screen.findByText('Login destination')
        expect(acceptPosUserInvitation).toHaveBeenCalledWith('test-invitation-token', 'StrongPassword1!')
        expect(api.post).toHaveBeenCalledWith('/auth/logout')
        expect(invalidatePosRuntimeCache).toHaveBeenCalledWith({
            reason: 'invitation-account-switch',
            broadcast: false,
        })
        expect(broadcastPosVendorLogout).toHaveBeenCalledOnce()
        await waitFor(() => expect(useAuthStore.getState().isAuthenticated).toBe(false))
    })
})
