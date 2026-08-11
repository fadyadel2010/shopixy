import { beforeEach, describe, expect, it, vi } from 'vitest'

const { apiPost, apiPut, apiPatch, apiDelete } = vi.hoisted(() => ({
    apiPost: vi.fn(),
    apiPut: vi.fn(),
    apiPatch: vi.fn(),
    apiDelete: vi.fn(),
}))

vi.mock('../../../../services/api', () => ({
    default: {
        post: apiPost,
        put: apiPut,
        patch: apiPatch,
        delete: apiDelete,
    },
}))

import { domainManagementService } from './domainManagementService.js'

describe('domainManagementService mutations', () => {
    beforeEach(() => {
        vi.clearAllMocks()
    })

    it('creates a domain', async () => {
        apiPost.mockResolvedValue({ data: { domainId: 'new-id' } })

        const result = await domainManagementService.createDomain({
            hostName: 'shop.example.com',
            domainType: 1,
        })

        expect(apiPost).toHaveBeenCalledWith('/admin/storefront/domains', {
            hostName: 'shop.example.com',
            domainType: 1,
        }, {})
        expect(result).toEqual({ domainId: 'new-id' })
    })

    it('updates a domain with PATCH', async () => {
        apiPatch.mockResolvedValue({ data: { success: true } })

        await domainManagementService.updateDomain('domain-123', {
            hostName: 'newhost.example.com',
            expectedVersion: 5,
        })

        expect(apiPatch).toHaveBeenCalledWith('/admin/storefront/domains/domain-123', {
            hostName: 'newhost.example.com',
            expectedVersion: 5,
        }, {})
    })

    it('starts verification with PUT', async () => {
        apiPut.mockResolvedValue({ data: { operation: 'StartDomainVerification' } })

        await domainManagementService.startDomainVerification('domain-123', { verificationMethod: 0 })

        expect(apiPut).toHaveBeenCalledWith(
            '/admin/storefront/domains/domain-123/verification/start',
            { verificationMethod: 0 },
            {},
        )
    })

    it('executes VerifyNow with PUT', async () => {
        apiPut.mockResolvedValue({ data: { operation: 'VerifyNow' } })

        await domainManagementService.verifyDomainNow('domain-123')

        expect(apiPut).toHaveBeenCalledWith(
            '/admin/storefront/domains/domain-123/verification/verify',
            {},
            {},
        )
    })

    it('executes RetryVerification with PUT', async () => {
        apiPut.mockResolvedValue({ data: { operation: 'RetryVerification' } })

        await domainManagementService.retryDomainVerification('domain-123')

        expect(apiPut).toHaveBeenCalledWith(
            '/admin/storefront/domains/domain-123/verification/retry',
            {},
            {},
        )
    })

    it('refreshes verification token with PUT', async () => {
        apiPut.mockResolvedValue({ data: { operation: 'RefreshVerificationToken' } })

        await domainManagementService.refreshVerificationToken('domain-123', { verificationMethod: 1 })

        expect(apiPut).toHaveBeenCalledWith(
            '/admin/storefront/domains/domain-123/verification/token',
            { verificationMethod: 1 },
            {},
        )
    })

    it('executes SSL certificate commands with POST', async () => {
        apiPost.mockResolvedValue({ data: { operationId: 'operation-1' } })

        await domainManagementService.provisionSslCertificate('domain-123', { idempotencyKey: 'provision-key' })
        await domainManagementService.activateSslCertificate('domain-123', { certificateId: 'cert-1', idempotencyKey: 'activate-key' })
        await domainManagementService.renewSslCertificate('domain-123', { certificateId: 'cert-1', idempotencyKey: 'renew-key' })
        await domainManagementService.revokeSslCertificate('domain-123', { certificateId: 'cert-1', reason: 'ADMIN_REQUESTED', idempotencyKey: 'revoke-key' })
        await domainManagementService.retrySslOperation('domain-123', { operationId: 'operation-1', idempotencyKey: 'retry-key' })
        await domainManagementService.refreshSslCertificate('domain-123', { certificateId: 'cert-1', idempotencyKey: 'refresh-key' })

        expect(apiPost).toHaveBeenCalledWith(
            '/admin/storefront/domains/domain-123/ssl/provision',
            { idempotencyKey: 'provision-key' },
            {},
        )
        expect(apiPost).toHaveBeenCalledWith(
            '/admin/storefront/domains/domain-123/ssl/activate',
            { certificateId: 'cert-1', idempotencyKey: 'activate-key' },
            {},
        )
        expect(apiPost).toHaveBeenCalledWith(
            '/admin/storefront/domains/domain-123/ssl/renew',
            { certificateId: 'cert-1', idempotencyKey: 'renew-key' },
            {},
        )
        expect(apiPost).toHaveBeenCalledWith(
            '/admin/storefront/domains/domain-123/ssl/revoke',
            { certificateId: 'cert-1', reason: 'ADMIN_REQUESTED', idempotencyKey: 'revoke-key' },
            {},
        )
        expect(apiPost).toHaveBeenCalledWith(
            '/admin/storefront/domains/domain-123/ssl/retry',
            { operationId: 'operation-1', idempotencyKey: 'retry-key' },
            {},
        )
        expect(apiPost).toHaveBeenCalledWith(
            '/admin/storefront/domains/domain-123/ssl/refresh',
            { certificateId: 'cert-1', idempotencyKey: 'refresh-key' },
            {},
        )
    })

    it('archives, restores, promotes, and deletes with canonical methods', async () => {
        apiPut.mockResolvedValue({ data: { success: true } })
        apiDelete.mockResolvedValue({ data: { success: true } })

        await domainManagementService.archiveDomain('domain-123', { reason: 'migrated' })
        await domainManagementService.restoreDomain('domain-123', { reason: 'reactivation' })
        await domainManagementService.promotePrimaryDomain('domain-123', { expectedVersion: 3 })
        await domainManagementService.deleteDomain('domain-123', { reason: 'done' })

        expect(apiPut).toHaveBeenCalledWith('/admin/storefront/domains/domain-123/archive', { reason: 'migrated' }, {})
        expect(apiPut).toHaveBeenCalledWith('/admin/storefront/domains/domain-123/restore', { reason: 'reactivation' }, {})
        expect(apiPut).toHaveBeenCalledWith('/admin/storefront/domains/domain-123/primary', { expectedVersion: 3 }, {})
        expect(apiDelete).toHaveBeenCalledWith(
            '/admin/storefront/domains/domain-123',
            expect.objectContaining({ data: { reason: 'done' } }),
        )
    })
})
