import { render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { MemoryRouter } from 'react-router-dom'
import AffiliatePortal from './AffiliatePortal'

const mocks = vi.hoisted(() => ({ restore: vi.fn(), navigate: vi.fn() }))
vi.mock('react-router-dom', async () => ({ ...await vi.importActual('react-router-dom'), useNavigate: () => mocks.navigate }))
vi.mock('../../../services/affiliateAuthApi', () => ({ AFFILIATE_AUTH_SESSION_EVENT_KEY: 'closure-auth-event', affiliateAuthApi: {
    restoreSession: mocks.restore, logout: vi.fn(),
} }))
vi.mock('../i18n/LanguageContext', () => ({ useLanguage: () => ({ t: (key: string) => key, dir: 'ltr', isRTL: false }) }))
vi.mock('../components/LanguageSwitcher', () => ({ default: () => null }))
vi.mock('../components/ThemeSwitcher', () => ({ default: () => null }))
vi.mock('./portal/DashboardTab', () => ({ default: () => <div>Admitted dashboard</div> }))

describe('Closure: portal does not use presentation cache as session authority', () => {
    beforeEach(() => {
        mocks.restore.mockReset()
        mocks.navigate.mockReset()
        window.history.replaceState({}, '', '/affiliate/portal?tab=orders')
        localStorage.setItem('affiliate_account_id', 'forged-cache-profile')
        localStorage.setItem('affiliate_display_name', 'Cached impostor')
    })

    it.each([401, 403])('redirects terminal HTTP %s to login with bounded local return URL', async status => {
        mocks.restore.mockRejectedValue({ response: { status } })
        render(<MemoryRouter><AffiliatePortal /></MemoryRouter>)
        await waitFor(() => expect(mocks.navigate).toHaveBeenCalledWith(
            '/affiliate/login?returnUrl=%2Faffiliate%2Fportal%3Ftab%3Dorders', { replace: true }))
        expect(screen.queryByText('Cached impostor')).not.toBeInTheDocument()
        expect(screen.queryByText('Admitted dashboard')).not.toBeInTheDocument()
    })

    it('keeps an infrastructure failure distinct from an unauthenticated session', async () => {
        mocks.restore.mockRejectedValue({ response: { status: 503 } })
        render(<MemoryRouter><AffiliatePortal /></MemoryRouter>)
        expect(await screen.findByText('portal.sessionRestoreError')).toBeVisible()
        expect(mocks.navigate).not.toHaveBeenCalled()
        expect(screen.queryByText('Admitted dashboard')).not.toBeInTheDocument()
    })
})
