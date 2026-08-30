import { StrictMode } from 'react'
import { render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { MemoryRouter } from 'react-router-dom'
import AffiliateVerifyEmail from './AffiliateVerifyEmail'

const verifyEmailMock = vi.hoisted(() => vi.fn())

vi.mock('../../../services/affiliateAuthApi', () => ({
    affiliateAuthApi: {
        verifyEmail: verifyEmailMock,
    },
}))

vi.mock('../i18n/LanguageContext', () => ({
    useLanguage: () => ({
        t: (key: string) => {
            const labels: Record<string, string> = {
                'identity.verify.title': 'Verify email',
                'identity.verify.subtitle': 'Confirming your affiliate address',
                'identity.verify.missingToken': 'MISSING_TOKEN_MESSAGE',
                'identity.verify.verifying': 'VERIFYING',
                'identity.verify.success': 'VERIFIED',
                'identity.verify.failed': 'GENERIC_FAILURE',
                'identity.signIn': 'Sign in',
            }

            return labels[key] ?? key
        },
        dir: 'ltr',
        isRTL: false,
    }),
}))

vi.mock('../components/LanguageSwitcher', () => ({ default: () => null }))
vi.mock('../components/ThemeSwitcher', () => ({ default: () => null }))

const rawToken = () => 'A'.repeat(32) + 'B'.repeat(32)

const renderPage = (token?: string) =>
    render(
        <StrictMode>
            <MemoryRouter
                initialEntries={[
                    token === undefined ? '/affiliate/verify-email' : `/affiliate/verify-email?token=${token}`,
                ]}
            >
                <AffiliateVerifyEmail />
            </MemoryRouter>
        </StrictMode>,
    )

describe('AffiliateVerifyEmail page', () => {
    beforeEach(() => {
        verifyEmailMock.mockReset()
    })

    it('issues exactly one verification request under React StrictMode and confirms success', async () => {
        const token = rawToken()
        verifyEmailMock.mockResolvedValueOnce({ message: 'ok' })

        renderPage(token)

        await waitFor(() => expect(screen.getByText('VERIFIED')).toBeDefined())
        expect(verifyEmailMock).toHaveBeenCalledTimes(1)
        expect(verifyEmailMock).toHaveBeenCalledWith(token)
    })

    it('keeps the presented token untouched when forwarding the request', async () => {
        const token = '0123456789ABCDEF'.repeat(4)
        verifyEmailMock.mockResolvedValue({ message: 'ok' })

        renderPage(`  ${token}  `)

        await waitFor(() => expect(verifyEmailMock).toHaveBeenCalledTimes(1))
        expect(verifyEmailMock).toHaveBeenCalledWith(token.trim())
    })

    it('renders the generic failure state without navigating away or retrying', async () => {
        verifyEmailMock.mockRejectedValueOnce(new Error('terminal'))

        renderPage(rawToken())

        await waitFor(() => expect(screen.getByText('GENERIC_FAILURE')).toBeDefined())
        expect(verifyEmailMock).toHaveBeenCalledTimes(1)
    })

    it('shows the missing-token error without issuing any request', () => {
        renderPage()

        expect(screen.getByText('MISSING_TOKEN_MESSAGE')).toBeDefined()
        expect(verifyEmailMock).not.toHaveBeenCalled()
    })
})
