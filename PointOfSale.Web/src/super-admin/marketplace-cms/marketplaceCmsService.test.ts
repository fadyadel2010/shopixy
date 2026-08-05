import { beforeEach, describe, expect, it, vi } from 'vitest'

const { apiDelete, apiGet, apiPost, apiPut } = vi.hoisted(() => ({
    apiDelete: vi.fn(),
    apiGet: vi.fn(),
    apiPost: vi.fn(),
    apiPut: vi.fn(),
}))

vi.mock('../core/superAdminApiClient', () => ({
    superAdminApiClient: {
        delete: apiDelete,
        get: apiGet,
        post: apiPost,
        put: apiPut,
    },
}))

import { marketplaceCmsService } from './marketplaceCmsService'

describe('marketplaceCmsService version contract', () => {
    beforeEach(() => {
        apiDelete.mockReset()
        apiGet.mockReset()
        apiPost.mockReset()
        apiPut.mockReset()
    })

    it('creates without client-generated version evidence and adopts the response', async () => {
        const payload = { slug: 'about', title: 'About' }
        const created = { id: 'page-1', draft: { version: 1 } }
        apiPost.mockResolvedValue({ data: created })

        await expect(marketplaceCmsService.createPage(payload)).resolves.toBe(created)
        expect(apiPost).toHaveBeenCalledWith('/marketplace/pages', payload)
        expect(marketplaceCmsService.getPageVersion(created)).toBe(1)
    })

    it('propagates each adopted version through repeated update, publish, and delete', async () => {
        const versionTwo = { id: 'page-1', draft: { version: 2 } }
        const versionThree = { id: 'page-1', draft: { version: 3 } }
        const published = { id: 'page-1', draft: null, published: { version: 3 } }
        const deleted = { id: 'page-1', deleted: true }
        apiPut.mockResolvedValueOnce({ data: versionTwo }).mockResolvedValueOnce({ data: versionThree })
        apiPost.mockResolvedValue({ data: published })
        apiDelete.mockResolvedValue({ data: deleted })

        const firstUpdate = await marketplaceCmsService.updatePage('page-1', { title: 'v2' }, 1)
        const secondUpdate = await marketplaceCmsService.updatePage(
            'page-1',
            { title: 'v3' },
            marketplaceCmsService.getPageVersion(firstUpdate),
        )
        const publishResponse = await marketplaceCmsService.publishPage(
            'page-1',
            marketplaceCmsService.getPageVersion(secondUpdate),
        )
        const deleteResponse = await marketplaceCmsService.deletePage(
            'page-1',
            marketplaceCmsService.getPageVersion(publishResponse),
        )

        expect(apiPut).toHaveBeenNthCalledWith(1, '/marketplace/pages/page-1', {
            title: 'v2',
            expectedVersion: 1,
        })
        expect(apiPut).toHaveBeenNthCalledWith(2, '/marketplace/pages/page-1', {
            title: 'v3',
            expectedVersion: 2,
        })
        expect(apiPost).toHaveBeenCalledWith('/marketplace/pages/page-1/publish?expectedVersion=3')
        expect(apiDelete).toHaveBeenCalledWith('/marketplace/pages/page-1?expectedVersion=3')
        expect(deleteResponse).toBe(deleted)
    })

    it('fails closed instead of issuing a mutation without current version evidence', async () => {
        await expect(marketplaceCmsService.updatePage('page-1', {}, null)).rejects.toThrow(
            'The current marketplace CMS page version is unavailable.',
        )
        await expect(marketplaceCmsService.publishPage('page-1', 0)).rejects.toThrow(
            'The current marketplace CMS page version is unavailable.',
        )
        await expect(marketplaceCmsService.deletePage('page-1', undefined)).rejects.toThrow(
            'The current marketplace CMS page version is unavailable.',
        )

        expect(apiPut).not.toHaveBeenCalled()
        expect(apiPost).not.toHaveBeenCalled()
        expect(apiDelete).not.toHaveBeenCalled()
    })

    it('identifies only the backend concurrency conflict contract as a version conflict', () => {
        expect(marketplaceCmsService.isConcurrencyConflict({
            status: 409,
            code: 'MARKETPLACE_CMS_CONCURRENCY_CONFLICT',
        })).toBe(true)
        expect(marketplaceCmsService.isConcurrencyConflict({
            status: 409,
            code: 'MARKETPLACE_CMS_SLUG_EXISTS',
        })).toBe(false)
    })
})
