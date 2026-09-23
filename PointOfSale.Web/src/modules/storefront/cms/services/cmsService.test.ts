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

import { cmsService, extractCmsConflict } from './cmsService'

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
                previewUrl: 'https://shop.example.test/about-us?preview=true&previewToken=backend-token',
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
            previewUrl: 'https://shop.example.test/about-us?preview=true&previewToken=backend-token',
            expiresAtUtc,
        })
    })

    it('rejects malformed or expired CMS preview access responses', async () => {
        apiPost.mockResolvedValue({
            data: {
                slug: 'about-us',
                previewToken: '',
                previewUrl: 'https://shop.example.test/about-us?preview=true',
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

    it('submits CMS publish with backend version evidence', async () => {
        apiPost.mockResolvedValue({ data: { id: 'page-id', published: { version: 3 } } })

        await cmsService.publishPage('page-id', 3)

        expect(apiPost).toHaveBeenCalledWith('/admin/pages/page-id/publish', undefined, {
            headers: { 'If-Match': '"3"' },
        })
    })

    it('does not replace structured CMS publish errors with a generic service error', async () => {
        const structuredConflict = {
            name: 'AxiosError',
            code: 'ERR_BAD_REQUEST',
            message: 'Request failed with status code 409',
            response: {
                status: 409,
                data: {
                    code: 'CONCURRENCY_CONFLICT',
                    message: 'This page changed before it could be published.',
                },
            },
        }
        apiPost.mockRejectedValue(structuredConflict)

        await expect(cmsService.publishPage('page-id', 3)).rejects.toBe(structuredConflict)
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

    it('classifies only the exact CMS concurrency contract as retryable', () => {
        expect(extractCmsConflict({ response: { status: 409, data: { code: 'CONCURRENCY_CONFLICT', message: 'stale' } } })).toMatchObject({
            code: 'CONCURRENCY_CONFLICT',
            message: 'stale',
        })
        expect(extractCmsConflict({ response: { status: 409, data: { code: 'SLUG_CONFLICT' } } })).toBeNull()
        expect(extractCmsConflict({ response: { status: 422, data: { code: 'CONCURRENCY_CONFLICT' } } })).toBeNull()
    })

    it('adopts CMS conflict LatestVersion from the backend contract casing', () => {
        const latestVersion = { id: 'page-id', published: { version: 2 } }

        expect(extractCmsConflict({
            response: {
                status: 409,
                data: {
                    code: 'CONCURRENCY_CONFLICT',
                    message: 'Already published by the backend.',
                    LatestVersion: latestVersion,
                },
            },
        })).toMatchObject({
            code: 'CONCURRENCY_CONFLICT',
            latestVersion,
        })
    })
})
