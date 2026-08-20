import { beforeEach, describe, expect, it, vi } from 'vitest'

const apiGetMock = vi.hoisted(() => vi.fn())
const apiPostMock = vi.hoisted(() => vi.fn())

vi.mock('./api', () => ({
    default: {
        get: apiGetMock,
        post: apiPostMock,
    },
}))

describe('adminProductFeedApi', () => {
    beforeEach(() => {
        vi.clearAllMocks()
        apiGetMock.mockResolvedValue({ data: [] })
        apiPostMock.mockResolvedValue({ data: { tokenId: 'token-1', channel: 'meta', feedUrl: 'https://secret', createdAtUtc: '2026-08-20T00:00:00Z' } })
    })

    it('uses the channel-isolated admin feed token endpoints', async () => {
        const { default: adminProductFeedApi, PRODUCT_FEED_CHANNELS } = await import('./adminProductFeedApi')

        for (const channel of PRODUCT_FEED_CHANNELS.map((item) => item.id)) {
            await adminProductFeedApi.listTokens(channel)
            await adminProductFeedApi.createToken(channel)
            await adminProductFeedApi.rotateToken(channel, `${channel}-token`)
            await adminProductFeedApi.revokeToken(channel, `${channel}-token`)
        }

        expect(apiGetMock.mock.calls.map((call) => call[0])).toEqual([
            '/admin/feeds/meta/tokens',
            '/admin/feeds/google/tokens',
            '/admin/feeds/tiktok/tokens',
            '/admin/feeds/snapchat/tokens',
        ])
        expect(apiPostMock.mock.calls.map((call) => call[0])).toEqual([
            '/admin/feeds/meta/tokens',
            '/admin/feeds/meta/tokens/meta-token/rotate',
            '/admin/feeds/meta/tokens/meta-token/revoke',
            '/admin/feeds/google/tokens',
            '/admin/feeds/google/tokens/google-token/rotate',
            '/admin/feeds/google/tokens/google-token/revoke',
            '/admin/feeds/tiktok/tokens',
            '/admin/feeds/tiktok/tokens/tiktok-token/rotate',
            '/admin/feeds/tiktok/tokens/tiktok-token/revoke',
            '/admin/feeds/snapchat/tokens',
            '/admin/feeds/snapchat/tokens/snapchat-token/rotate',
            '/admin/feeds/snapchat/tokens/snapchat-token/revoke',
        ])
    })

    it('rejects unsupported channel values before calling the API', async () => {
        const { default: adminProductFeedApi } = await import('./adminProductFeedApi')

        await expect(adminProductFeedApi.listTokens('Meta')).rejects.toThrow('Unsupported product feed channel')
        expect(apiGetMock).not.toHaveBeenCalled()
    })
})
