import api from './api'

export const PRODUCT_FEED_CHANNELS = Object.freeze([
    { id: 'meta', name: 'Meta' },
    { id: 'google', name: 'Google' },
    { id: 'tiktok', name: 'TikTok' },
    { id: 'snapchat', name: 'Snapchat' },
])

const SUPPORTED_CHANNELS = new Set(PRODUCT_FEED_CHANNELS.map((channel) => channel.id))

export function assertProductFeedChannel(channel) {
    if (!SUPPORTED_CHANNELS.has(channel)) {
        throw new Error('Unsupported product feed channel')
    }
}

const tokenPath = (channel) => {
    assertProductFeedChannel(channel)
    return `/admin/feeds/${channel}/tokens`
}

const tokenActionPath = (channel, tokenId, action) => {
    assertProductFeedChannel(channel)
    return `/admin/feeds/${channel}/tokens/${tokenId}/${action}`
}

const adminProductFeedApi = {
    async listTokens(channel) {
        const response = await api.get(tokenPath(channel))
        return Array.isArray(response.data) ? response.data : []
    },

    async createToken(channel) {
        const response = await api.post(tokenPath(channel))
        return response.data
    },

    async rotateToken(channel, tokenId) {
        const response = await api.post(tokenActionPath(channel, tokenId, 'rotate'))
        return response.data
    },

    async revokeToken(channel, tokenId) {
        await api.post(tokenActionPath(channel, tokenId, 'revoke'))
    },
}

export default adminProductFeedApi
