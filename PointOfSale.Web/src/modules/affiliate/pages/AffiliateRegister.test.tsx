import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { MemoryRouter } from 'react-router-dom'
import AffiliateRegister from './AffiliateRegister'

const mocks = vi.hoisted(() => ({ register: vi.fn(), navigate: vi.fn() }))
vi.mock('react-router-dom', async () => ({ ...await vi.importActual('react-router-dom'), useNavigate: () => mocks.navigate }))
vi.mock('../../../services/affiliateAuthApi', () => ({ affiliateAuthApi: {
    register: mocks.register, restoreSession: () => Promise.reject(new Error('No session')),
} }))
vi.mock('../i18n/LanguageContext', () => ({ useLanguage: () => ({ t: (key: string) => key, dir: 'ltr', isRTL: false }) }))
vi.mock('../components/LanguageSwitcher', () => ({ default: () => null }))
vi.mock('../components/ThemeSwitcher', () => ({ default: () => null }))

function submit() {
    const view = render(<MemoryRouter><AffiliateRegister /></MemoryRouter>)
    for (const [id, value] of Object.entries({ 'aff-reg-name': ' QA Global ', 'aff-reg-email': 'qa@example.test', 'aff-reg-password': 'QaAffiliate2026!', 'aff-reg-confirm': 'QaAffiliate2026!' })) {
        fireEvent.change(view.container.querySelector(`#${id}`)!, { target: { value } })
    }
    fireEvent.submit(view.container.querySelector('form')!)
    return view
}

describe('Global enrollment recovery presentation', () => {
    beforeEach(() => { mocks.register.mockReset(); mocks.navigate.mockReset() })
    it('directs committed enrollment delivery failure to resend, never another registration', async () => {
        mocks.register.mockRejectedValue({ response: { status: 503, data: { code: 'AFFILIATE_VERIFICATION_DELIVERY_UNAVAILABLE', accountCreated: true } } })
        submit()
        await waitFor(() => expect(mocks.navigate).toHaveBeenCalledWith('/affiliate/login?registered=1&deliveryFailed=1&email=qa%40example.test', { replace: true }))
        expect(mocks.register).toHaveBeenCalledTimes(1)
    })
    it('shows global duplicate conflicts without promoting a session', async () => {
        mocks.register.mockRejectedValue({ response: { status: 409 } })
        submit()
        expect(await screen.findByText('register.duplicateEmail')).toBeInTheDocument()
        expect(mocks.navigate).not.toHaveBeenCalled()
    })
    it('submits a merchant-independent identity and mirrors persistence field limits', async () => {
        mocks.register.mockResolvedValue({ emailVerified: false })
        const view = submit()
        await waitFor(() => expect(mocks.register).toHaveBeenCalledWith({ email: 'qa@example.test', displayName: 'QA Global', password: 'QaAffiliate2026!' }))
        expect(view.container.querySelector('#aff-reg-name')).toHaveAttribute('maxlength', '200')
        expect(view.container.querySelector('#aff-reg-email')).toHaveAttribute('maxlength', '255')
    })
})
