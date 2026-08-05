import { afterEach, describe, expect, it, vi } from 'vitest'

const { apiGet, apiPost, apiPut } = vi.hoisted(() => ({
    apiGet: vi.fn(),
    apiPost: vi.fn(),
    apiPut: vi.fn(),
}))

vi.mock('../core/superAdminApiClient', () => ({
    superAdminApiClient: {
        get: apiGet,
        post: apiPost,
        put: apiPut,
    },
}))

import {
    generateHomepageStarterTemplate,
    getHomepagePreviewStoreConfig,
    getHomepageSections,
    isHomepageWorkspaceConflict,
    publishHomepageSections,
    saveHomepageDraft,
} from './homepageService'

describe('homepageService', () => {
    afterEach(() => {
        vi.restoreAllMocks()
        apiGet.mockReset()
        apiPost.mockReset()
        apiPut.mockReset()
    })

    it('loads preview storefront config without forwarding browser credentials', async () => {
        const fetchMock = vi.fn().mockResolvedValue({
            ok: true,
            json: async () => ({ storeName: 'Preview Store' }),
        })

        vi.stubGlobal('fetch', fetchMock)

        await getHomepagePreviewStoreConfig('tenant-123')

        expect(fetchMock).toHaveBeenCalledWith('/api/storefront/store-config?preview=true', {
            method: 'GET',
            credentials: 'omit',
            headers: {
                'Content-Type': 'application/json',
                'X-Tenant-Id': 'tenant-123',
            },
        })
    })

    it('surfaces the backend error message when preview config loading fails', async () => {
        const fetchMock = vi.fn().mockResolvedValue({
            ok: false,
            json: async () => ({ message: 'Tenant isolation violation' }),
        })

        vi.stubGlobal('fetch', fetchMock)

        await expect(getHomepagePreviewStoreConfig('tenant-123')).rejects.toThrow(
            'Tenant isolation violation',
        )
    })

    it('loads and admits the backend-owned Homepage workspace version', async () => {
        apiGet.mockResolvedValue({
            data: [{ id: 'section-1' }],
            meta: { workspaceVersion: 'version-1' },
        })

        await expect(getHomepageSections(null, 'preview')).resolves.toMatchObject({
            sections: [{ id: 'section-1' }],
            workspaceVersion: 'version-1',
        })
        expect(apiGet).toHaveBeenCalledWith('/homepage/sections?mode=preview')
    })

    it('fails closed when a load or mutation response omits version evidence', async () => {
        apiGet.mockResolvedValue({ data: [], meta: {} })
        apiPut.mockResolvedValue({ data: [], meta: {} })

        await expect(getHomepageSections()).rejects.toThrow('workspace version is unavailable')
        await expect(saveHomepageDraft({ sections: [] }, 'version-1')).rejects.toThrow(
            'workspace version is unavailable',
        )
    })

    it('propagates and adopts versions across repeated save and publish', async () => {
        apiPut
            .mockResolvedValueOnce({ data: [{ id: 'section-1' }], meta: { workspaceVersion: 'version-2' } })
            .mockResolvedValueOnce({ data: [{ id: 'section-1' }], meta: { workspaceVersion: 'version-3' } })
        apiPost.mockResolvedValue({
            data: [{ id: 'section-1' }],
            meta: { workspaceVersion: 'version-4' },
        })
        const payload = { tenantId: null, sections: [] }

        const firstSave = await saveHomepageDraft(payload, 'version-1')
        const secondSave = await saveHomepageDraft(payload, firstSave.workspaceVersion)
        const published = await publishHomepageSections(null, secondSave.workspaceVersion)

        expect(apiPut).toHaveBeenNthCalledWith(
            1,
            '/homepage/sections/draft?expectedVersion=version-1',
            { tenantId: null, sections: [] },
        )
        expect(apiPut).toHaveBeenNthCalledWith(
            2,
            '/homepage/sections/draft?expectedVersion=version-2',
            { tenantId: null, sections: [] },
        )
        expect(apiPost).toHaveBeenCalledWith(
            '/homepage/sections/publish?expectedVersion=version-3',
            { tenantId: null },
        )
        expect(published.workspaceVersion).toBe('version-4')
    })

    it('keeps starter-template writes under the accepted Homepage version', async () => {
        apiPost.mockResolvedValue({ data: [], meta: { workspaceVersion: 'version-2' } })

        await generateHomepageStarterTemplate(
            { tenantId: null, overwriteExisting: true },
            'version-1',
        )

        expect(apiPost).toHaveBeenCalledWith(
            '/homepage/sections/template?expectedVersion=version-1',
            { tenantId: null, overwriteExisting: true },
        )
    })

    it('does not issue mutations without accepted version evidence', async () => {
        await expect(saveHomepageDraft({ sections: [] }, null)).rejects.toThrow(
            'workspace version is unavailable',
        )
        await expect(publishHomepageSections(null, '')).rejects.toThrow(
            'workspace version is unavailable',
        )

        expect(apiPut).not.toHaveBeenCalled()
        expect(apiPost).not.toHaveBeenCalled()
    })

    it('classifies only the exact Homepage workspace conflict', () => {
        expect(isHomepageWorkspaceConflict({ status: 409, code: 'HOMEPAGE_WORKSPACE_CONFLICT' })).toBe(true)
        expect(isHomepageWorkspaceConflict({ status: 409, code: 'HOMEPAGE_TEMPLATE_EXISTS' })).toBe(false)
    })
})
