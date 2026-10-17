import { render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { AuthoringButton, AuthoringEmptyState, AuthoringFeedback, AuthoringStatusBadge } from '../../authoringPlatform'

const mockState = vi.hoisted(() => ({
    routeDomainId: '11111111-1111-1111-1111-111111111111',
    verificationData: {
        domainId: '11111111-1111-1111-1111-111111111111',
        hostName: 'shop.example.com',
        normalizedHostName: 'shop.example.com',
        status: 'Pending',
        method: 'DnsTxt',
        verificationToken: 'shopixy-domain-verify-test',
        dnsRecordType: 'TXT',
        dnsRecordName: '_shopixy-verification.shop.example.com',
        dnsRecordValue: 'shopixy-domain-verify-test',
        httpVerificationUrl: null,
    },
}))

vi.mock('react-router-dom', () => ({
    useParams: () => ({ id: mockState.routeDomainId }),
}))

vi.mock('react-i18next', () => ({
    useTranslation: () => ({
        t: (_key: string, fallback: string) => fallback,
    }),
}))

vi.mock('../../authoringPlatform', () => ({
    AuthoringButton: ({ action, defaultLabel }: Parameters<typeof AuthoringButton>[0]) => (
        <button type="button" onClick={action?.onClick}>
            {action?.label || defaultLabel}
        </button>
    ),
    AuthoringEmptyState: ({ title, description }: Parameters<typeof AuthoringEmptyState>[0]) => (
        <section>
            <h2>{title}</h2>
            <p>{description}</p>
        </section>
    ),
    AuthoringFeedback: ({ title, message }: Parameters<typeof AuthoringFeedback>[0]) => (
        <section>
            <h2>{title}</h2>
            <p>{message}</p>
        </section>
    ),
    AuthoringLoadingState: () => <div>Loading</div>,
    AuthoringStatusBadge: ({ children }: Parameters<typeof AuthoringStatusBadge>[0]) => <span>{children}</span>,
    useAuthoringGuard: () => ({ requestNavigation: vi.fn() }),
}))

vi.mock('../hooks', () => ({
    LOAD_STATES: {
        NOT_FOUND: 'not-found',
    },
    useDomainVerification: () => ({
        data: mockState.verificationData,
        state: 'success',
        error: null,
        isLoading: false,
        isError: false,
        isUnauthorized: false,
        isSuccess: true,
        refetch: vi.fn(),
    }),
    useDomainMutations: () => ({
        isMutating: false,
        mutationStatus: 'idle',
        error: null,
        verifyDomainNow: vi.fn(),
        retryDomainVerification: vi.fn(),
        refreshVerificationToken: vi.fn(),
    }),
}))

import DnsVerificationPage from './DnsVerificationPage'

describe('DnsVerificationPage', () => {
    beforeEach(() => {
        mockState.verificationData = {
            domainId: mockState.routeDomainId,
            hostName: 'shop.example.com',
            normalizedHostName: 'shop.example.com',
            status: 'Pending',
            method: 'DnsTxt',
            verificationToken: 'shopixy-domain-verify-test',
            dnsRecordType: 'TXT',
            dnsRecordName: '_shopixy-verification.shop.example.com',
            dnsRecordValue: 'shopixy-domain-verify-test',
            httpVerificationUrl: null,
        }
    })

    it('displays the API hostname instead of the route domain id', () => {
        render(<DnsVerificationPage />)

        expect(screen.getByText('shop.example.com')).toBeInTheDocument()
        expect(screen.queryByText(mockState.routeDomainId)).not.toBeInTheDocument()
    })

    it('displays CNAME instructions when the API verification method is CNAME', () => {
        mockState.verificationData = {
            ...mockState.verificationData,
            method: 'DnsCname',
            dnsRecordType: 'CNAME',
            dnsRecordName: '_shopixy-verification.shop.example.com',
            dnsRecordValue: 'verify-cname-token.domains.shopixy.com',
        }

        render(<DnsVerificationPage />)

        expect(screen.getByText('CNAME')).toBeInTheDocument()
        expect(screen.getByText('verify-cname-token.domains.shopixy.com')).toBeInTheDocument()
        expect(screen.queryByText('TXT')).not.toBeInTheDocument()
    })
})
