import { describe, it, expect, vi, afterEach } from 'vitest'
import { renderHook, act, waitFor } from '@testing-library/react'

const mockCreateDomain = vi.hoisted(() => vi.fn())
const mockUpdateDomain = vi.hoisted(() => vi.fn())
const mockArchiveDomain = vi.hoisted(() => vi.fn())
const mockRestoreDomain = vi.hoisted(() => vi.fn())
const mockDeleteDomain = vi.hoisted(() => vi.fn())
const mockPromotePrimaryDomain = vi.hoisted(() => vi.fn())
const mockStartDomainVerification = vi.hoisted(() => vi.fn())
const mockVerifyDomainNow = vi.hoisted(() => vi.fn())
const mockRetryDomainVerification = vi.hoisted(() => vi.fn())
const mockRefreshVerificationToken = vi.hoisted(() => vi.fn())
const mockProvisionSslCertificate = vi.hoisted(() => vi.fn())
const mockActivateSslCertificate = vi.hoisted(() => vi.fn())
const mockRenewSslCertificate = vi.hoisted(() => vi.fn())
const mockRevokeSslCertificate = vi.hoisted(() => vi.fn())
const mockRetrySslOperation = vi.hoisted(() => vi.fn())
const mockRefreshSslCertificate = vi.hoisted(() => vi.fn())

vi.mock('../services/domainManagementService.js', () => ({
    default: {
        createDomain: mockCreateDomain,
        updateDomain: mockUpdateDomain,
        archiveDomain: mockArchiveDomain,
        restoreDomain: mockRestoreDomain,
        deleteDomain: mockDeleteDomain,
        promotePrimaryDomain: mockPromotePrimaryDomain,
        startDomainVerification: mockStartDomainVerification,
        verifyDomainNow: mockVerifyDomainNow,
        retryDomainVerification: mockRetryDomainVerification,
        refreshVerificationToken: mockRefreshVerificationToken,
        provisionSslCertificate: mockProvisionSslCertificate,
        activateSslCertificate: mockActivateSslCertificate,
        renewSslCertificate: mockRenewSslCertificate,
        revokeSslCertificate: mockRevokeSslCertificate,
        retrySslOperation: mockRetrySslOperation,
        refreshSslCertificate: mockRefreshSslCertificate,
    },
}))

vi.mock('../../../../services/api', () => ({
    getSafeApiErrorMessage: (err) => err?.response?.data?.message || err?.message || 'Request failed',
}))

import { useDomainMutations } from './useDomainMutations.js'

describe('useDomainMutations (DOMAIN-3.1-C)', () => {
    afterEach(() => {
        vi.clearAllMocks()
    })

    describe('createDomain', () => {
        it('calls domainManagementService.createDomain and returns DomainResponse', async () => {
            const mockResponse = { domainId: 'new-id', normalizedHostName: 'shop.example.com' }
            mockCreateDomain.mockResolvedValue(mockResponse)

            const { result } = renderHook(() => useDomainMutations())

            const response = await act(async () =>
                result.current.createDomain({ hostName: 'shop.example.com', domainType: 0 }))

            expect(mockCreateDomain).toHaveBeenCalledWith({
                hostName: 'shop.example.com',
                domainType: 0,
            })
            expect(response).toEqual(mockResponse)
            expect(result.current.isMutating).toBe(false)
        })

        it('sets mutationError on API failure', async () => {
            mockCreateDomain.mockRejectedValue({
                response: { status: 409, data: { message: 'Taken', code: 'DUPLICATE_HOSTNAME' } },
            })

            const { result } = renderHook(() => useDomainMutations())

            await act(async () => {
                await expect(result.current.createDomain({ hostName: 'taken.com' })).rejects.toThrow()
            })

            expect(result.current.mutationError).toBe('Taken')
            expect(result.current.mutationStatus).toBe('domain-failure')
        })
    })

    describe('updateDomain', () => {
        it('calls domainManagementService.updateDomain with id and request', async () => {
            mockUpdateDomain.mockResolvedValue({ success: true })

            const { result } = renderHook(() => useDomainMutations())

            const response = await act(async () =>
                result.current.updateDomain('domain-1', { hostName: 'new.com', expectedVersion: 1 }))

            expect(mockUpdateDomain).toHaveBeenCalledWith('domain-1', {
                hostName: 'new.com',
                expectedVersion: 1,
            })
            expect(response).toEqual({ success: true })
        })
    })

    describe('archiveDomain', () => {
        it('calls domainManagementService.archiveDomain with id and options', async () => {
            mockArchiveDomain.mockResolvedValue({ success: true })

            const { result } = renderHook(() => useDomainMutations())

            await act(async () => result.current.archiveDomain('domain-1', { reason: 'archived' }))

            expect(mockArchiveDomain).toHaveBeenCalledWith('domain-1', { reason: 'archived' })
        })
    })

    describe('restoreDomain', () => {
        it('calls domainManagementService.restoreDomain with id and options', async () => {
            mockRestoreDomain.mockResolvedValue({ success: true })

            const { result } = renderHook(() => useDomainMutations())

            await act(async () => result.current.restoreDomain('domain-1', { reason: 'restored' }))

            expect(mockRestoreDomain).toHaveBeenCalledWith('domain-1', { reason: 'restored' })
        })
    })

    describe('deleteDomain', () => {
        it('calls domainManagementService.deleteDomain with id and options', async () => {
            mockDeleteDomain.mockResolvedValue({ success: true })

            const { result } = renderHook(() => useDomainMutations())

            await act(async () => result.current.deleteDomain('domain-1', { reason: 'deleted' }))

            expect(mockDeleteDomain).toHaveBeenCalledWith('domain-1', { reason: 'deleted' })
        })
    })

    describe('promotePrimaryDomain', () => {
        it('calls domainManagementService.promotePrimaryDomain with id and options', async () => {
            mockPromotePrimaryDomain.mockResolvedValue({ success: true })

            const { result } = renderHook(() => useDomainMutations())

            await act(async () => result.current.promotePrimaryDomain('domain-1', { expectedVersion: 2 }))

            expect(mockPromotePrimaryDomain).toHaveBeenCalledWith('domain-1', { expectedVersion: 2 })
        })

        it('sets isConcurrencyConflict on 409 status', async () => {
            mockPromotePrimaryDomain.mockRejectedValue({
                response: { status: 409, data: { message: 'Precondition required' } },
            })

            const { result } = renderHook(() => useDomainMutations())

            await act(async () => {
                await expect(result.current.promotePrimaryDomain('domain-1')).rejects.toThrow()
            })

            expect(result.current.isConcurrencyConflict).toBe(true)
        })
    })

    describe('verification commands', () => {
        it('calls start, verify now, retry, and refresh token mutations', async () => {
            mockStartDomainVerification.mockResolvedValue({ operation: 'StartDomainVerification' })
            mockVerifyDomainNow.mockResolvedValue({ operation: 'VerifyNow' })
            mockRetryDomainVerification.mockResolvedValue({ operation: 'RetryVerification' })
            mockRefreshVerificationToken.mockResolvedValue({ operation: 'RefreshVerificationToken' })

            const { result } = renderHook(() => useDomainMutations())

            await act(async () => result.current.startDomainVerification('domain-1', { expectedVersion: 1 }))
            await act(async () => result.current.verifyDomainNow('domain-1'))
            await act(async () => result.current.retryDomainVerification('domain-1'))
            await act(async () => result.current.refreshVerificationToken('domain-1', { verificationMethod: 0 }))

            expect(mockStartDomainVerification).toHaveBeenCalledWith('domain-1', { expectedVersion: 1 })
            expect(mockVerifyDomainNow).toHaveBeenCalledWith('domain-1', {})
            expect(mockRetryDomainVerification).toHaveBeenCalledWith('domain-1', {})
            expect(mockRefreshVerificationToken).toHaveBeenCalledWith('domain-1', { verificationMethod: 0 })
        })

        it('classifies expected verification failure separately from infrastructure failure', async () => {
            mockVerifyDomainNow.mockRejectedValueOnce({
                response: { status: 422, data: { message: 'Record missing', code: 'RecordMissing' } },
            })

            const { result } = renderHook(() => useDomainMutations())

            await act(async () => {
                await expect(result.current.verifyDomainNow('domain-1')).rejects.toThrow()
            })

            expect(result.current.mutationStatus).toBe('domain-failure')
            expect(result.current.mutationError).toBe('Record missing')

            mockVerifyDomainNow.mockRejectedValueOnce({
                response: { status: 503, data: { message: 'Resolver unavailable', code: 'SERVICE_UNAVAILABLE' } },
            })

            await act(async () => {
                await expect(result.current.verifyDomainNow('domain-1')).rejects.toThrow()
            })

            expect(result.current.mutationStatus).toBe('infrastructure-error')
            expect(result.current.mutationError).toBe('Resolver unavailable')
        })
    })

    describe('SSL certificate commands', () => {
        it('calls certificate lifecycle mutations', async () => {
            mockProvisionSslCertificate.mockResolvedValue({ operationId: 'provision-op' })
            mockActivateSslCertificate.mockResolvedValue({ operationId: 'activate-op' })
            mockRenewSslCertificate.mockResolvedValue({ operationId: 'renew-op' })
            mockRevokeSslCertificate.mockResolvedValue({ operationId: 'revoke-op' })
            mockRetrySslOperation.mockResolvedValue({ operationId: 'retry-op' })
            mockRefreshSslCertificate.mockResolvedValue({ operationId: 'refresh-op' })

            const { result } = renderHook(() => useDomainMutations())

            await act(async () => result.current.provisionSslCertificate('domain-1', { idempotencyKey: 'provision-key' }))
            await act(async () => result.current.activateSslCertificate('domain-1', { certificateId: 'cert-1', idempotencyKey: 'activate-key' }))
            await act(async () => result.current.renewSslCertificate('domain-1', { certificateId: 'cert-1', idempotencyKey: 'renew-key' }))
            await act(async () => result.current.revokeSslCertificate('domain-1', { certificateId: 'cert-1', reason: 'ADMIN_REQUESTED', idempotencyKey: 'revoke-key' }))
            await act(async () => result.current.retrySslOperation('domain-1', { operationId: 'op-1', idempotencyKey: 'retry-key' }))
            await act(async () => result.current.refreshSslCertificate('domain-1', { certificateId: 'cert-1', idempotencyKey: 'refresh-key' }))

            expect(mockProvisionSslCertificate).toHaveBeenCalledWith('domain-1', { idempotencyKey: 'provision-key' })
            expect(mockActivateSslCertificate).toHaveBeenCalledWith('domain-1', { certificateId: 'cert-1', idempotencyKey: 'activate-key' })
            expect(mockRenewSslCertificate).toHaveBeenCalledWith('domain-1', { certificateId: 'cert-1', idempotencyKey: 'renew-key' })
            expect(mockRevokeSslCertificate).toHaveBeenCalledWith('domain-1', { certificateId: 'cert-1', reason: 'ADMIN_REQUESTED', idempotencyKey: 'revoke-key' })
            expect(mockRetrySslOperation).toHaveBeenCalledWith('domain-1', { operationId: 'op-1', idempotencyKey: 'retry-key' })
            expect(mockRefreshSslCertificate).toHaveBeenCalledWith('domain-1', { certificateId: 'cert-1', idempotencyKey: 'refresh-key' })
        })
    })

    describe('clearError', () => {
        it('clears mutationError and concurrency conflict flags', async () => {
            mockCreateDomain.mockRejectedValue({
                response: { status: 500, data: { message: 'Error' } },
            })

            const { result } = renderHook(() => useDomainMutations())

            await act(async () => {
                await expect(result.current.createDomain({ hostName: 'test.com' })).rejects.toThrow()
            })

            expect(result.current.mutationError).toBeDefined()

            act(() => result.current.clearError())

            expect(result.current.mutationError).toBeNull()
            expect(result.current.isConcurrencyConflict).toBe(false)
        })
    })
})
