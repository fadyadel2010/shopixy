import { beforeEach, describe, expect, it, vi } from 'vitest'

const { apiDelete, apiGet, apiPost } = vi.hoisted(() => ({
    apiDelete: vi.fn(),
    apiGet: vi.fn(),
    apiPost: vi.fn(),
}))

vi.mock('../../../../services/api', () => ({
    default: {
        delete: apiDelete,
        get: apiGet,
        post: apiPost,
        put: vi.fn(),
    },
}))

import { cmsService } from './cmsService'

describe('cmsService', () => {
    beforeEach(() => {
        apiDelete.mockReset()
        apiGet.mockReset()
        apiPost.mockReset()
    })

    it('reads CMS page lists with no-store headers', async () => {
        apiGet.mockResolvedValue({ data: [] })

        await cmsService.listPages()

        expect(apiGet).toHaveBeenCalledWith('/admin/pages', {
            headers: {
                'Cache-Control': 'no-store',
                Pragma: 'no-cache',
            },
        })
    })

    it('reads CMS page details with no-store headers', async () => {
        apiGet.mockResolvedValue({ data: { id: 'page-id' } })

        await cmsService.getPage('page-id')

        expect(apiGet).toHaveBeenCalledWith('/admin/pages/page-id', {
            headers: {
                'Cache-Control': 'no-store',
                Pragma: 'no-cache',
            },
        })
    })

    it('creates CMS preview links with no-store headers and admits backend access evidence', async () => {
        const expiresAtUtc = new Date(Date.now() + 60_000).toISOString()
        apiPost.mockResolvedValue({
            data: {
                slug: 'about-us',
                previewToken: 'backend-token',
                expiresAtUtc,
            },
        })

        const access = await cmsService.createPreviewLink('page-id')

        expect(apiPost).toHaveBeenCalledWith('/admin/pages/page-id/preview-link', undefined, {
            headers: {
                'Cache-Control': 'no-store',
                Pragma: 'no-cache',
            },
        })
        expect(access).toEqual({
            slug: 'about-us',
            previewToken: 'backend-token',
            expiresAtUtc,
        })
    })

    it('rejects malformed or expired CMS preview access responses', async () => {
        apiPost.mockResolvedValue({
            data: {
                slug: 'about-us',
                previewToken: '',
                expiresAtUtc: new Date(Date.now() + 60_000).toISOString(),
            },
        })

        await expect(cmsService.createPreviewLink('page-id')).rejects.toThrow(
            'CMS preview access was rejected by backend preview admission.'
        )
    })

    it('loads CMS version history with no-store headers', async () => {
        apiGet.mockResolvedValue({ data: { versions: [] } })

        await cmsService.getVersionHistory('page-id')

        expect(apiGet).toHaveBeenCalledWith('/admin/pages/page-id/versions', {
            headers: {
                'Cache-Control': 'no-store',
                Pragma: 'no-cache',
            },
        })
    })

    it('submits rollback intent only with If-Match version evidence', async () => {
        apiPost.mockResolvedValue({ data: { id: 'page-id' } })

        await cmsService.rollbackPage('page-id', 2, 5, 'operator reason')

        expect(apiPost).toHaveBeenCalledWith('/admin/pages/page-id/rollback', {
            version: 2,
            reason: 'operator reason',
        }, {
            headers: { 'If-Match': '"5"' },
        })
    })
})
