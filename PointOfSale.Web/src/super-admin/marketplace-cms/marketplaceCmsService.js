import { superAdminApiClient } from '../core/superAdminApiClient'

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

    async updatePage(pageId, pageData) {
        const response = await superAdminApiClient.put(`/marketplace/pages/${pageId}`, pageData)
        return response.data
    },

    async publishPage(pageId) {
        const response = await superAdminApiClient.post(`/marketplace/pages/${pageId}/publish`)
        return response.data
    },

    async previewPage(pageId) {
        const response = await superAdminApiClient.get(`/marketplace/pages/${pageId}/preview`)
        return response.data
    },

    async deletePage(pageId) {
        await superAdminApiClient.delete(`/marketplace/pages/${pageId}`)
    },

    async getFooterConfiguration() {
        const response = await superAdminApiClient.get('/marketplace/footer')
        return response.data
    },

    async updateFooterConfiguration(configuration) {
        const response = await superAdminApiClient.put('/marketplace/footer', configuration)
        return response.data
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
