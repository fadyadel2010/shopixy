import { beforeEach, describe, expect, it, vi } from 'vitest'

const { apiGet, apiPut, apiPost } = vi.hoisted(() => ({
    apiGet: vi.fn(),
    apiPut: vi.fn(),
    apiPost: vi.fn(),
}))

vi.mock('../../../services/api', () => ({
    default: {
        get: apiGet,
        put: apiPut,
        post: apiPost,
        delete: vi.fn(),
    },
}))

import { storeConfigService } from './storeConfigService'

describe('storeConfigService', () => {
    beforeEach(() => {
        apiGet.mockReset()
        apiPut.mockReset()
        apiPost.mockReset()
    })

    it('uses no-store headers when reading admin store configuration authority', async () => {
        apiGet.mockResolvedValue({ data: { storeName: 'Backend Store' } })

        await storeConfigService.getStoreConfig()

        expect(apiGet).toHaveBeenCalledWith('/admin/store-config', {
            headers: {
                'Cache-Control': 'no-store',
                Pragma: 'no-cache',
            },
        })
    })

    it('uses no-store headers when reading public runtime store configuration authority', async () => {
        apiGet.mockResolvedValue({ data: { storeName: 'Backend Store' } })

        await storeConfigService.getRuntimeStoreConfig({ params: { themePreviewToken: 'token' } })

        expect(apiGet).toHaveBeenCalledWith('/storefront/store-config', {
            params: { themePreviewToken: 'token' },
            headers: {
                'Cache-Control': 'no-store',
                Pragma: 'no-cache',
            },
        })
    })

    it('loads draft preview config from the authenticated storefront preview endpoint', async () => {
        const signal = new AbortController().signal
        apiGet.mockResolvedValue({ data: { storeName: 'Preview Store' } })

        const result = await storeConfigService.getPreviewConfig({ signal })

        expect(result).toEqual({ storeName: 'Preview Store' })
        expect(apiGet).toHaveBeenCalledWith('/storefront/config/preview', {
            params: { preview: true },
            signal,
            headers: {
                'Cache-Control': 'no-store',
                Pragma: 'no-cache',
            },
        })
    })

    it('admits the backend-resolved theme preview URL', async () => {
        const expiresAtUtc = new Date(Date.now() + 60_000).toISOString()
        apiPost.mockResolvedValue({
            data: {
                previewToken: 'backend-token',
                previewUrl: 'https://shop.example.test/?themePreviewToken=backend-token',
                expiresAtUtc,
            },
        })

        const result = await storeConfigService.createThemePreviewLink('editorial')

        expect(apiPost).toHaveBeenCalledWith('/admin/store-config/theme/preview-link/editorial', {})
        expect(result).toEqual({
            previewToken: 'backend-token',
            previewUrl: 'https://shop.example.test/?themePreviewToken=backend-token',
            expiresAtUtc,
        })
    })

    it('rejects a malformed backend theme preview URL', async () => {
        apiPost.mockResolvedValue({
            data: {
                previewToken: 'backend-token',
                previewUrl: 'javascript:alert(1)',
                expiresAtUtc: new Date(Date.now() + 60_000).toISOString(),
            },
        })

        await expect(storeConfigService.createThemePreviewLink('editorial')).rejects.toThrow(
            'Theme preview access was rejected by backend URL authority admission.',
        )
    })

    it('submits store configuration mutations with If-Match rowVersion evidence', async () => {
        apiPut.mockResolvedValue({ data: { rowVersion: '43' } })

        const result = await storeConfigService.updateStoreConfig({
            storeName: 'Backend Store',
            rowVersion: '42',
        })

        expect(result).toEqual({ rowVersion: '43' })
        expect(apiPut).toHaveBeenCalledWith('/admin/store-config', {
            storeName: 'Backend Store',
            rowVersion: '42',
        }, {
            headers: {
                'If-Match': '"42"',
            },
        })
    })

    it('submits storefront status mutations to the status-only authority endpoint', async () => {
        apiPut.mockResolvedValue({ data: { rowVersion: '43', storefrontStatus: 'live' } })

        const result = await storeConfigService.updateStorefrontStatus({
            storefrontStatus: 'live',
            maintenanceMessage: '',
            rowVersion: '42',
        })

        expect(result).toEqual({ rowVersion: '43', storefrontStatus: 'live' })
        expect(apiPut).toHaveBeenCalledWith('/admin/store-config/status', {
            storefrontStatus: 'live',
            maintenanceMessage: '',
            rowVersion: '42',
        }, {
            headers: {
                'If-Match': '"42"',
            },
        })
    })

    it('loads real version history from the admin versions endpoint', async () => {
        apiGet.mockResolvedValue({
            data: {
                currentVersion: 3,
                totalVersions: 2,
                versions: [
                    { version: 3, publishedBy: 'admin@example.com' },
                    { version: 2, publishedBy: 'owner@example.com' },
                ],
            },
        })

        const result = await storeConfigService.getVersionHistory()

        expect(result).toMatchObject({ currentVersion: 3, versions: expect.any(Array) })
        if (!result || typeof result !== 'object' || Array.isArray(result)) throw new Error('Version response is not an object')
        expect(result.versions).toHaveLength(2)
        expect(apiGet).toHaveBeenCalledWith('/admin/store-config/versions', {
            headers: {
                'Cache-Control': 'no-store',
                Pragma: 'no-cache',
            },
        })
    })

    it('submits theme draft mutations with caller-supplied If-Match rowVersion evidence', async () => {
        apiPut.mockResolvedValue({ data: { rowVersion: '44' } })

        const result = await storeConfigService.updateThemeOverrides({
            presetKey: 'dark-minimal',
            colors: { primary: '#111111' },
        }, {
            headers: {
                'If-Match': '"43"',
            },
        })

        expect(result).toEqual({ rowVersion: '44' })
        expect(apiPut).toHaveBeenCalledWith('/admin/store-config/theme/overrides', {
            presetKey: 'dark-minimal',
            colors: { primary: '#111111' },
        }, {
            headers: {
                'If-Match': '"43"',
            },
        })
    })

    it('submits theme publish mutations with caller-supplied If-Match rowVersion evidence', async () => {
        apiPost.mockResolvedValue({ data: { rowVersion: '45' } })

        const result = await storeConfigService.publishTheme({
            headers: {
                'If-Match': '"44"',
            },
        })

        expect(result).toEqual({ rowVersion: '45' })
        expect(apiPost).toHaveBeenCalledWith('/admin/store-config/theme/publish', undefined, {
            headers: {
                'If-Match': '"44"',
            },
        })
    })

    it('submits theme activation through the dedicated theme command endpoint', async () => {
        apiPost.mockResolvedValue({ data: { rowVersion: '46', storefrontTheme: 'minimal' } })

        const result = await storeConfigService.activateTheme('minimal', {
            headers: {
                'If-Match': '"45"',
            },
        })

        expect(result).toEqual({ rowVersion: '46', storefrontTheme: 'minimal' })
        expect(apiPost).toHaveBeenCalledWith('/admin/store-config/theme/activate', {
            themeId: 'minimal',
        }, {
            headers: {
                'If-Match': '"45"',
            },
        })
    })
})
