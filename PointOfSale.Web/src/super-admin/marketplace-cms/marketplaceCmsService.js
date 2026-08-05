import { superAdminApiClient } from '../core/superAdminApiClient'

function requireExpectedVersion(expectedVersion) {
    if (!Number.isInteger(expectedVersion) || expectedVersion < 1) {
        throw new Error('The current marketplace CMS page version is unavailable. Refresh and try again.')
    }

    return expectedVersion
}

export const marketplaceCmsService = {
    async listPages() {
        const response = await superAdminApiClient.get('/marketplace/pages')
        return response.data
    },

    async getPage(pageId) {
        const response = await superAdminApiClient.get(`/marketplace/pages/${pageId}`)
        return response.data
    },

    async createPage(pageData) {
        const response = await superAdminApiClient.post('/marketplace/pages', pageData)
        return response.data
    },

    async updatePage(pageId, pageData, expectedVersion) {
        const response = await superAdminApiClient.put(`/marketplace/pages/${pageId}`, {
            ...pageData,
            expectedVersion: requireExpectedVersion(expectedVersion),
        })
        return response.data
    },

    async publishPage(pageId, expectedVersion) {
        const version = requireExpectedVersion(expectedVersion)
        const response = await superAdminApiClient.post(`/marketplace/pages/${pageId}/publish?expectedVersion=${version}`)
        return response.data
    },

    async previewPage(pageId) {
        const response = await superAdminApiClient.get(`/marketplace/pages/${pageId}/preview`)
        return response.data
    },

    async deletePage(pageId, expectedVersion) {
        const version = requireExpectedVersion(expectedVersion)
        const response = await superAdminApiClient.delete(`/marketplace/pages/${pageId}?expectedVersion=${version}`)
        return response.data
    },

    getPageVersion(page) {
        const version = page?.draft?.version ?? page?.published?.version
        return Number.isInteger(version) && version > 0 ? version : null
    },

    getListItemVersion(page) {
        const version = page?.draftVersion ?? page?.publishedVersion
        return Number.isInteger(version) && version > 0 ? version : null
    },

    isConcurrencyConflict(error) {
        return error?.status === 409 && error?.code === 'MARKETPLACE_CMS_CONCURRENCY_CONFLICT'
    },

    async translateContent(content, from = 'en', to = 'ar') {
        const response = await superAdminApiClient.post('/marketplace/translate', {
            content,
            from,
            to,
        })

        return response.data.content
    },
}

export default marketplaceCmsService
