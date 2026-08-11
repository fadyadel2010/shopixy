import { beforeEach, describe, expect, it, vi } from 'vitest'

const { apiGet } = vi.hoisted(() => ({
    apiGet: vi.fn(),
}))

vi.mock('../../../../services/api', () => ({
    default: {
        get: apiGet,
        put: vi.fn(),
        post: vi.fn(),
        delete: vi.fn(),
    },
}))

import { domainManagementService } from './domainManagementService.js'

describe('domainManagementService', () => {
    beforeEach(() => {
        apiGet.mockReset()
    })

    it('uses no-store headers when calling dashboard summary', async () => {
        apiGet.mockResolvedValue({ data: { totalDomains: 3 } })

        await domainManagementService.getDashboardSummary()

        expect(apiGet).toHaveBeenCalledWith('/admin/storefront/domains/dashboard', {
            headers: {
                'Cache-Control': 'no-store',
                Pragma: 'no-cache',
            },
        })
    })

    it('passes pagination params to domain list endpoint', async () => {
        apiGet.mockResolvedValue({ data: { items: [], page: 1, pageSize: 20, totalCount: 0 } })

        await domainManagementService.getDomainList({ page: 2, pageSize: 50, search: 'shop' })

        expect(apiGet).toHaveBeenCalledWith('/admin/storefront/domains', {
            params: { page: 2, pageSize: 50, search: 'shop' },
            headers: {
                'Cache-Control': 'no-store',
                Pragma: 'no-cache',
            },
        })
    })

    it('passes domain id to detail endpoint', async () => {
        apiGet.mockResolvedValue({ data: { id: 'test-id' } })

        await domainManagementService.getDomainDetail('test-id')

        expect(apiGet).toHaveBeenCalledWith('/admin/storefront/domains/test-id', {
            headers: {
                'Cache-Control': 'no-store',
                Pragma: 'no-cache',
            },
        })
    })

    it('passes domain id to verification endpoint', async () => {
        apiGet.mockResolvedValue({ data: { domainId: 'test-id' } })

        await domainManagementService.getDomainVerification('test-id')

        expect(apiGet).toHaveBeenCalledWith('/admin/storefront/domains/test-id/verification', {
            headers: {
                'Cache-Control': 'no-store',
                Pragma: 'no-cache',
            },
        })
    })

    it('passes domain id to ssl endpoint', async () => {
        apiGet.mockResolvedValue({ data: { domainId: 'test-id' } })

        await domainManagementService.getDomainSsl('test-id')

        expect(apiGet).toHaveBeenCalledWith('/admin/storefront/domains/test-id/ssl', {
            headers: {
                'Cache-Control': 'no-store',
                Pragma: 'no-cache',
            },
        })
    })

    it('passes domain id to canonical ssl certificate endpoint', async () => {
        apiGet.mockResolvedValue({ data: { domainId: 'test-id', certificateId: 'cert-1' } })

        await domainManagementService.getDomainSslCertificate('test-id')

        expect(apiGet).toHaveBeenCalledWith('/admin/storefront/domains/test-id/ssl/certificate', {
            headers: {
                'Cache-Control': 'no-store',
                Pragma: 'no-cache',
            },
        })
    })

    it('passes pagination params to ssl operations endpoint', async () => {
        apiGet.mockResolvedValue({ data: { items: [] } })

        await domainManagementService.getDomainSslOperations('test-id', { page: 2, pageSize: 10 })

        expect(apiGet).toHaveBeenCalledWith('/admin/storefront/domains/test-id/ssl/operations', {
            params: { page: 2, pageSize: 10 },
            headers: {
                'Cache-Control': 'no-store',
                Pragma: 'no-cache',
            },
        })
    })

    it('passes operation id to ssl operation status endpoint', async () => {
        apiGet.mockResolvedValue({ data: { operationId: 'op-1' } })

        await domainManagementService.getDomainSslOperation('test-id', 'op-1')

        expect(apiGet).toHaveBeenCalledWith('/admin/storefront/domains/test-id/ssl/operations/op-1', {
            headers: {
                'Cache-Control': 'no-store',
                Pragma: 'no-cache',
            },
        })
    })

    it('passes domain id to redirects endpoint', async () => {
        apiGet.mockResolvedValue({ data: [] })

        await domainManagementService.getDomainRedirects('test-id')

        expect(apiGet).toHaveBeenCalledWith('/admin/storefront/domains/test-id/redirects', {
            headers: {
                'Cache-Control': 'no-store',
                Pragma: 'no-cache',
            },
        })
    })

    it('passes pagination params to history endpoint', async () => {
        apiGet.mockResolvedValue({ data: { items: [], page: 1, pageSize: 50, totalCount: 0 } })

        await domainManagementService.getDomainHistory('test-id', { page: 1, pageSize: 50 })

        expect(apiGet).toHaveBeenCalledWith('/admin/storefront/domains/test-id/history', {
            params: { page: 1, pageSize: 50 },
            headers: {
                'Cache-Control': 'no-store',
                Pragma: 'no-cache',
            },
        })
    })

    it('passes limit param to activity endpoint', async () => {
        apiGet.mockResolvedValue({ data: { recentActivity: [] } })

        await domainManagementService.getActivitySummary({ limit: 10 })

        expect(apiGet).toHaveBeenCalledWith('/admin/storefront/domains/activity', {
            params: { limit: 10 },
            headers: {
                'Cache-Control': 'no-store',
                Pragma: 'no-cache',
            },
        })
    })

    it('returns response data on success', async () => {
        const mockData = { totalDomains: 5, sslExpiringSoon: 1 }
        apiGet.mockResolvedValue({ data: mockData })

        const result = await domainManagementService.getDashboardSummary()

        expect(result).toEqual(mockData)
    })

    it('propagates API errors', async () => {
        apiGet.mockRejectedValue({
            response: { status: 500, data: { message: 'Internal server error' } },
        })

        await expect(domainManagementService.getDashboardSummary()).rejects.toThrow()
    })
})
