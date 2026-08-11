import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const mockState = vi.hoisted(() => ({
    routeDomainId: '11111111-1111-1111-1111-111111111111',
    navigate: vi.fn(),
    toast: {
        showInfo: vi.fn(),
        showWarning: vi.fn(),
    },
    queries: {
        detail: {
            data: {
                id: '11111111-1111-1111-1111-111111111111',
                hostName: 'shop.example.com',
                normalizedHostName: 'shop.example.com',
                isPrimary: true,
                isVerified: true,
            },
            state: 'success',
            error: null,
            isLoading: false,
            isError: false,
            isUnauthorized: false,
            refetch: vi.fn(),
            refresh: vi.fn(),
        },
        ssl: {
            data: {
                certificateId: 'cert-1',
                provider: 'LetsEncrypt',
                status: 'Active',
                thumbprint: 'safe-thumbprint',
                notAfterUtc: '2026-12-01T00:00:00Z',
            },
            state: 'success',
            error: null,
            isLoading: false,
            isError: false,
            isUnauthorized: false,
            refetch: vi.fn(),
            refresh: vi.fn(),
        },
        certificate: {
            data: {
                domainId: '11111111-1111-1111-1111-111111111111',
                certificateId: 'cert-1',
                generation: 3,
                provider: 'LetsEncrypt',
                status: 'Active',
                operationPhase: 'None',
                issuer: 'R3',
                serialNumber: 'serial-123',
                fingerprint: 'safe-fingerprint',
                notBeforeUtc: '2026-01-01T00:00:00Z',
                notAfterUtc: '2026-12-01T00:00:00Z',
                nextRenewalUtc: '2026-10-01T00:00:00Z',
                renewalEligible: false,
                retryable: false,
                failureCode: null,
                lastOperationId: 'op-success',
                lastOperationStatus: 'Succeeded',
                lastOperationPhase: 'MetadataSync',
                lastOperationUpdatedAtUtc: '2026-08-01T00:00:00Z',
                degraded: false,
                secretMaterial: 'SHOULD_NOT_RENDER',
            },
            state: 'success',
            error: null,
            isLoading: false,
            isError: false,
            isUnauthorized: false,
            refetch: vi.fn(),
            refresh: vi.fn(),
        },
        operations: {
            data: {
                items: [
                    {
                        operationId: 'op-success',
                        certificateId: 'cert-1',
                        operationType: 'Activation',
                        status: 'Succeeded',
                        phase: 'ActivationProbe',
                        retryable: false,
                        failureCode: null,
                        attemptCount: 1,
                        createdAtUtc: '2026-08-01T00:00:00Z',
                        updatedAtUtc: '2026-08-01T00:02:00Z',
                        completedAtUtc: '2026-08-01T00:02:00Z',
                    },
                ],
                page: 1,
                pageSize: 20,
                totalCount: 1,
                totalPages: 1,
                hasNextPage: false,
                hasPreviousPage: false,
            },
            state: 'success',
            error: null,
            isLoading: false,
            isError: false,
            isUnauthorized: false,
            isRefreshing: false,
            refetch: vi.fn(),
            refresh: vi.fn(),
            nextPage: vi.fn(),
            previousPage: vi.fn(),
        },
    },
    mutations: {
        isMutating: false,
        mutationStatus: 'idle',
        error: null,
        errorCode: null,
        provisionSslCertificate: vi.fn(),
        activateSslCertificate: vi.fn(),
        renewSslCertificate: vi.fn(),
        revokeSslCertificate: vi.fn(),
        retrySslOperation: vi.fn(),
        refreshSslCertificate: vi.fn(),
        clearError: vi.fn(),
    },
}))

vi.mock('react-router-dom', () => ({
    useParams: () => ({ id: mockState.routeDomainId }),
}))

vi.mock('react-i18next', () => ({
    useTranslation: () => ({
        i18n: { language: 'en' },
        t: (_key: string, fallback: string, options?: Record<string, string>) => {
            let value = fallback
            for (const [name, replacement] of Object.entries(options || {})) {
                value = value.replace(`{{${name}}}`, String(replacement))
            }
            return value
        },
    }),
}))

vi.mock('../../../../components/Toast', () => ({
    useToast: () => mockState.toast,
}))

vi.mock('../../authoringPlatform', () => ({
    AuthoringButton: ({ action, defaultLabel }: any) => action ? (
        <button
            type="button"
            onClick={action.onClick}
            disabled={action.disabled || action.busy}
            aria-label={action.ariaLabel || action.label || defaultLabel}
        >
            {action.busy ? action.busyLabel || defaultLabel : action.label || defaultLabel}
        </button>
    ) : null,
    AuthoringDialog: ({ open, title, description, children }: any) => open ? (
        <section role="dialog" aria-label={title}>
            <h2>{title}</h2>
            <p>{description}</p>
            {children}
        </section>
    ) : null,
    AuthoringEmptyState: ({ title, description, action }: any) => (
        <section>
            <h2>{title}</h2>
            <p>{description}</p>
            {action ? <button type="button" onClick={action.onClick}>{action.label}</button> : null}
        </section>
    ),
    AuthoringFeedback: ({ title, message, action }: any) => (
        <section>
            <h2>{title}</h2>
            <p>{message}</p>
            {action ? <button type="button" onClick={action.onClick}>{action.label}</button> : null}
        </section>
    ),
    AuthoringLoadingState: ({ label }: any) => <div>{label || 'Loading'}</div>,
    AuthoringStatusBadge: ({ children }: any) => <span>{children}</span>,
    useAuthoringGuard: () => ({ requestNavigation: mockState.navigate }),
}))

vi.mock('../hooks', () => ({
    LOAD_STATES: {
        NOT_FOUND: 'not-found',
    },
    useDomainDetail: () => mockState.queries.detail,
    useDomainSsl: () => mockState.queries.ssl,
    useDomainSslCertificate: () => mockState.queries.certificate,
    useDomainSslOperations: () => mockState.queries.operations,
    useDomainMutations: () => mockState.mutations,
}))

import SslManagementPage from './SslManagementPage'

function resetState() {
    vi.clearAllMocks()
    mockState.queries.detail = {
        ...mockState.queries.detail,
        data: {
            id: mockState.routeDomainId,
            hostName: 'shop.example.com',
            normalizedHostName: 'shop.example.com',
            isPrimary: true,
            isVerified: true,
        },
        state: 'success',
        error: null,
        isLoading: false,
        isError: false,
        isUnauthorized: false,
    }
    mockState.queries.ssl = {
        ...mockState.queries.ssl,
        data: {
            certificateId: 'cert-1',
            provider: 'LetsEncrypt',
            status: 'Active',
            thumbprint: 'safe-thumbprint',
            notAfterUtc: '2026-12-01T00:00:00Z',
        },
        state: 'success',
        error: null,
        isLoading: false,
        isError: false,
        isUnauthorized: false,
    }
    mockState.queries.certificate = {
        ...mockState.queries.certificate,
        data: {
            domainId: mockState.routeDomainId,
            certificateId: 'cert-1',
            generation: 3,
            provider: 'LetsEncrypt',
            status: 'Active',
            operationPhase: 'None',
            issuer: 'R3',
            serialNumber: 'serial-123',
            fingerprint: 'safe-fingerprint',
            notBeforeUtc: '2026-01-01T00:00:00Z',
            notAfterUtc: '2026-12-01T00:00:00Z',
            nextRenewalUtc: '2026-10-01T00:00:00Z',
            renewalEligible: false,
            retryable: false,
            failureCode: null,
            lastOperationId: 'op-success',
            lastOperationStatus: 'Succeeded',
            lastOperationPhase: 'MetadataSync',
            lastOperationUpdatedAtUtc: '2026-08-01T00:00:00Z',
            degraded: false,
            secretMaterial: 'SHOULD_NOT_RENDER',
        },
        state: 'success',
        error: null,
        isLoading: false,
        isError: false,
        isUnauthorized: false,
    }
    mockState.queries.operations = {
        ...mockState.queries.operations,
        data: {
            items: [
                {
                    operationId: 'op-success',
                    certificateId: 'cert-1',
                    operationType: 'Activation',
                    status: 'Succeeded',
                    phase: 'ActivationProbe',
                    retryable: false,
                    failureCode: null,
                    attemptCount: 1,
                    createdAtUtc: '2026-08-01T00:00:00Z',
                    updatedAtUtc: '2026-08-01T00:02:00Z',
                    completedAtUtc: '2026-08-01T00:02:00Z',
                },
            ],
            page: 1,
            pageSize: 20,
            totalCount: 1,
            totalPages: 1,
            hasNextPage: false,
            hasPreviousPage: false,
        },
        state: 'success',
        error: null,
        isLoading: false,
        isError: false,
        isUnauthorized: false,
        isRefreshing: false,
    }
    mockState.mutations = {
        ...mockState.mutations,
        isMutating: false,
        mutationStatus: 'idle',
        error: null,
        errorCode: null,
        provisionSslCertificate: vi.fn().mockResolvedValue({
            operationId: 'op-provision',
            operationStatus: 'Running',
            operationPhase: 'ProviderRequest',
        }),
        activateSslCertificate: vi.fn().mockResolvedValue({
            operationId: 'op-activate',
            operationStatus: 'Running',
            operationPhase: 'ActivationProbe',
        }),
        renewSslCertificate: vi.fn().mockResolvedValue({
            operationId: 'op-renew',
            operationStatus: 'Running',
            operationPhase: 'RenewalWindow',
        }),
        revokeSslCertificate: vi.fn().mockResolvedValue({
            operationId: 'op-revoke',
            operationStatus: 'Running',
            operationPhase: 'Revocation',
        }),
        retrySslOperation: vi.fn().mockResolvedValue({
            operationId: 'op-retry',
            operationStatus: 'Running',
            operationPhase: 'RetryBackoff',
        }),
        refreshSslCertificate: vi.fn().mockResolvedValue({
            operationId: 'op-refresh',
            operationStatus: 'Succeeded',
            operationPhase: 'MetadataSync',
        }),
        clearError: vi.fn(),
    }
}

describe('SslManagementPage', () => {
    beforeEach(resetState)

    it('renders safe certificate metadata and operation history without secret fields', () => {
        render(<SslManagementPage />)

        expect(screen.getAllByText('shop.example.com').length).toBeGreaterThan(0)
        expect(screen.getByText('safe-fingerprint')).toBeInTheDocument()
        expect(screen.getByText('Certificate operations')).toBeInTheDocument()
        expect(screen.getByText('Activation')).toBeInTheDocument()
        expect(screen.queryByText('SHOULD_NOT_RENDER')).not.toBeInTheDocument()
    })

    it('renders empty state and sends provision with request-scoped idempotency', async () => {
        mockState.queries.certificate.data = {
            domainId: mockState.routeDomainId,
            certificateId: null,
            status: 'Pending',
            operationPhase: 'None',
        }
        mockState.queries.ssl.data = { certificateId: null, status: 'Pending', provider: 'LetsEncrypt' }
        mockState.queries.operations.data = { items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 }

        render(<SslManagementPage />)
        fireEvent.click(screen.getAllByRole('button', { name: 'Provision certificate' })[0])

        expect(mockState.mutations.provisionSslCertificate).toHaveBeenCalledWith(
            mockState.routeDomainId,
            expect.objectContaining({ idempotencyKey: expect.stringMatching(/^ssl-provision-/) }),
        )
        expect(localStorage.getItem('ssl-provision')).toBeNull()
        expect(sessionStorage.getItem('ssl-provision')).toBeNull()
    })

    it('allows a verified domain to provision a new generation after a terminal certificate', async () => {
        mockState.queries.certificate.data = {
            ...mockState.queries.certificate.data,
            status: 'Revoked',
        }
        mockState.queries.ssl.data = {
            ...mockState.queries.ssl.data,
            status: 'Revoked',
        }
        mockState.queries.operations.data = { items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 }

        render(<SslManagementPage />)
        fireEvent.click(screen.getByRole('button', { name: 'Provision certificate' }))

        expect(mockState.mutations.provisionSslCertificate).toHaveBeenCalledTimes(1)
    })

    it('uses an activation confirmation and preserves accepted async wording', async () => {
        mockState.queries.certificate.data = {
            ...mockState.queries.certificate.data,
            status: 'Issued',
        }

        render(<SslManagementPage />)
        fireEvent.click(screen.getByRole('button', { name: 'Activate certificate' }))

        expect(screen.getByRole('dialog', { name: 'Activate certificate' })).toBeInTheDocument()
        expect(screen.getByText(/requires SNI proof/)).toBeInTheDocument()

        fireEvent.click(screen.getAllByRole('button', { name: 'Activate certificate' })[1])

        await waitFor(() => {
            expect(mockState.mutations.activateSslCertificate).toHaveBeenCalledWith(
                mockState.routeDomainId,
                expect.objectContaining({ certificateId: 'cert-1', idempotencyKey: expect.stringMatching(/^ssl-activate-/) }),
            )
        })
        expect(await screen.findByText('Operation accepted')).toBeInTheDocument()
        expect(screen.queryByText('Certificate activated successfully')).not.toBeInTheDocument()
    })

    it('requires revocation confirmation and canonical reason code', async () => {
        render(<SslManagementPage />)
        fireEvent.click(screen.getByRole('button', { name: 'Revoke certificate' }))
        fireEvent.change(screen.getByLabelText('Reason'), { target: { value: 'SECURITY_COMPROMISE' } })
        fireEvent.click(screen.getByRole('button', { name: 'Request revocation' }))

        expect(mockState.mutations.revokeSslCertificate).toHaveBeenCalledWith(
            mockState.routeDomainId,
            expect.objectContaining({
                certificateId: 'cert-1',
                reason: 'SECURITY_COMPROMISE',
                idempotencyKey: expect.stringMatching(/^ssl-revoke-/),
            }),
        )
    })

    it('shows retry only for failed retryable operations after retryAfter has elapsed', () => {
        mockState.queries.operations.data = {
            ...mockState.queries.operations.data,
            items: [
                {
                    operationId: 'op-ready',
                    operationType: 'Renewal',
                    status: 'Failed',
                    phase: 'RetryBackoff',
                    retryable: true,
                    retryAfterUtc: '2026-01-01T00:00:00Z',
                    failureCode: 'SNI_PROOF_FAILURE',
                    attemptCount: 2,
                    createdAtUtc: '2026-01-01T00:00:00Z',
                },
                {
                    operationId: 'op-wait',
                    operationType: 'Activation',
                    status: 'Failed',
                    phase: 'RetryBackoff',
                    retryable: true,
                    retryAfterUtc: '2999-01-01T00:00:00Z',
                    failureCode: 'RATE_LIMITED',
                    attemptCount: 1,
                    createdAtUtc: '2026-01-01T00:00:00Z',
                },
            ],
        }

        render(<SslManagementPage />)

        expect(screen.getAllByRole('button', { name: 'Retry' })).toHaveLength(1)
        expect(screen.getAllByText(/after/).length).toBeGreaterThan(0)
    })

    it('prevents duplicate command submission within the same attempt', async () => {
        mockState.queries.certificate.data = {
            domainId: mockState.routeDomainId,
            certificateId: null,
            status: 'Pending',
            operationPhase: 'None',
        }
        mockState.queries.ssl.data = { certificateId: null, status: 'Pending', provider: 'LetsEncrypt' }
        mockState.mutations.provisionSslCertificate = vi.fn(() => new Promise(() => {}))

        render(<SslManagementPage />)
        const button = screen.getAllByRole('button', { name: 'Provision certificate' })[0]
        fireEvent.click(button)
        fireEvent.click(button)

        expect(mockState.mutations.provisionSslCertificate).toHaveBeenCalledTimes(1)
    })

    it('reuses the idempotency key after an uncertain transport failure', async () => {
        mockState.queries.certificate.data = {
            domainId: mockState.routeDomainId,
            certificateId: null,
            status: 'Pending',
            operationPhase: 'None',
        }
        mockState.queries.ssl.data = { certificateId: null, status: 'Pending', provider: 'LetsEncrypt' }
        mockState.queries.operations.data = { items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 }
        mockState.mutations.provisionSslCertificate = vi.fn()
            .mockRejectedValueOnce(new Error('transport lost after send'))
            .mockResolvedValueOnce({
                operationId: 'op-provision',
                operationStatus: 'Running',
                operationPhase: 'ProviderRequest',
            })

        render(<SslManagementPage />)
        const button = screen.getAllByRole('button', { name: 'Provision certificate' })[0]

        fireEvent.click(button)
        await waitFor(() => expect(mockState.mutations.provisionSslCertificate).toHaveBeenCalledTimes(1))
        fireEvent.click(button)
        await waitFor(() => expect(mockState.mutations.provisionSslCertificate).toHaveBeenCalledTimes(2))

        const firstKey = mockState.mutations.provisionSslCertificate.mock.calls[0][1].idempotencyKey
        const secondKey = mockState.mutations.provisionSslCertificate.mock.calls[1][1].idempotencyKey
        expect(secondKey).toBe(firstKey)
    })

    it('does not show a success-style toast when the accepted operation is already failed', async () => {
        mockState.mutations.refreshSslCertificate = vi.fn().mockResolvedValue({
            operationId: 'op-refresh-failed',
            operationStatus: 'Failed',
            operationPhase: 'MetadataSync',
        })

        render(<SslManagementPage />)
        fireEvent.click(screen.getByRole('button', { name: 'Refresh state' }))

        await waitFor(() => {
            expect(mockState.toast.showWarning).toHaveBeenCalledWith('Operation accepted but failed')
        })
        expect(mockState.toast.showInfo).not.toHaveBeenCalled()
    })

    it('renders forbidden, error, and rate limit states with safe messages', () => {
        mockState.queries.certificate.isUnauthorized = true
        const { rerender } = render(<SslManagementPage />)
        expect(screen.getByText('Authentication required')).toBeInTheDocument()

        mockState.queries.certificate.isUnauthorized = false
        mockState.queries.certificate.isError = true
        mockState.queries.certificate.error = 'Unable to load data.'
        rerender(<SslManagementPage />)
        expect(screen.getByText('Unable to load SSL management')).toBeInTheDocument()

        mockState.queries.certificate.isError = false
        mockState.mutations.error = 'HTTP 429'
        mockState.mutations.errorCode = 'RATE_LIMITED'
        rerender(<SslManagementPage />)
        expect(screen.getByText('Rate limited')).toBeInTheDocument()
        expect(screen.getByText(/cannot be retried yet/)).toBeInTheDocument()
    })
})
