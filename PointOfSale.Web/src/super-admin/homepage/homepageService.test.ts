import { afterEach, describe, expect, it, vi } from 'vitest'

import { getHomepagePreviewStoreConfig } from './homepageService'

describe('homepageService', () => {
    afterEach(() => {
        vi.restoreAllMocks()
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
})
