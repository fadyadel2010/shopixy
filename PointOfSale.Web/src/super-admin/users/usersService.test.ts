import { beforeEach, describe, expect, it, vi } from 'vitest'

const { apiPost } = vi.hoisted(() => ({
    apiPost: vi.fn(),
}))

vi.mock('../core/superAdminApiClient', () => ({
    superAdminApiClient: {
        post: apiPost,
    },
}))

import { applyVendorCertificationAction } from './usersService'

describe('vendor certification service', () => {
    beforeEach(() => {
        apiPost.mockReset()
    })

    it.each([
        ['vendor-verification', true],
        ['marketplace-approval', false],
    ] as const)('uses the governed vendor certification endpoint for %s', async (action, enabled) => {
        const vendor = { id: '4b4f5312-f90c-4b20-9bdb-74b7f0ce1002', role: 'Vendor' }
        apiPost.mockResolvedValue({ data: vendor })

        await expect(applyVendorCertificationAction(vendor.id, action, enabled, 'Certification review')).resolves.toBe(vendor)
        expect(apiPost).toHaveBeenCalledWith(`/users/${vendor.id}/${action}`, {
            enabled,
            reason: 'Certification review',
        })
    })
})
