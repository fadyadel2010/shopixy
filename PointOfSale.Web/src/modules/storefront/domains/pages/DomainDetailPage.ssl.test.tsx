import { fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const mockState = vi.hoisted(() => ({
    routeDomainId: '11111111-1111-1111-1111-111111111111',
    navigate: vi.fn(),
    domain: {
        id: '11111111-1111-1111-1111-111111111111',
        hostName: 'shop.example.com',
        normalizedHostName: 'shop.example.com',
        domainType: 'Primary',
        lifecycleState: 'SslActive',
        redirectType: 'None',
        canonicalPolicy: 'Https',
        isPrimary: true,
        isVerified: true,
        sslCertificateId: 'cert-1',
        sslActivatedAtUtc: '2026-08-01T00:00:00Z',
        version: 7,
    },
}))

vi.mock('react-router-dom', () => ({
    useParams: () => ({ id: mockState.routeDomainId }),
}))

vi.mock('react-i18next', () => ({
    useTranslation: () => ({
        t: (_key: string, fallback: string, options?: Record<string, string>) => {
            let value = fallback
            for (const [name, replacement] of Object.entries(options || {})) {
                value = value.replace(`{{${name}}}`, String(replacement))
            }
            return value
        },
    }),
}))

vi.mock('../../authoringPlatform', () => ({
    AuthoringButton: ({ action, defaultLabel }: any) => action ? (
        <button type="button" onClick={action.onClick} disabled={action.disabled || action.busy}>
            {action.label || defaultLabel}
        </button>
    ) : null,
    AuthoringConfirmDialog: () => null,
    AuthoringEmptyState: ({ title }: any) => <section>{title}</section>,
    AuthoringFeedback: ({ title, message }: any) => <section>{title}{message}</section>,
    AuthoringLoadingState: () => <div>Loading</div>,
    AuthoringStatusBadge: ({ children }: any) => <span>{children}</span>,
    useAuthoringGuard: () => ({ requestNavigation: mockState.navigate }),
}))

vi.mock('../hooks', () => ({
    LOAD_STATES: {
        NOT_FOUND: 'not-found',
    },
    useDomainDetail: () => ({
        data: mockState.domain,
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
        error: null,
        errorCode: null,
        isConcurrencyConflict: false,
        serverVersion: null,
        archiveDomain: vi.fn(),
        restoreDomain: vi.fn(),
        deleteDomain: vi.fn(),
        promotePrimaryDomain: vi.fn(),
        startDomainVerification: vi.fn(),
        clearError: vi.fn(),
    }),
}))

import DomainDetailPage from './DomainDetailPage'

describe('DomainDetailPage SSL summary', () => {
    beforeEach(() => {
        vi.clearAllMocks()
    })

    it('keeps Primary Domain distinct and navigates to D06', async () => {
        render(<DomainDetailPage />)
        fireEvent.click(screen.getByRole('tab', { name: 'SSL' }))

        expect(screen.getByText('SSL configured')).toBeInTheDocument()
        expect(screen.getByText('Primary Domain')).toBeInTheDocument()

        fireEvent.click(screen.getByRole('button', { name: 'Open SSL Management' }))

        expect(mockState.navigate).toHaveBeenCalledWith(
            `/storefront/domains/${mockState.domain.id}/ssl`,
        )
    })
})
