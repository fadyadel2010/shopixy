import { superAdminApiClient } from '../core/superAdminApiClient'

export const storefrontFooterService = {
    async getConfiguration() {
        const response = await superAdminApiClient.get('/storefront/footer')
        return response.data
    },

    async updateConfiguration(configuration) {
        const response = await superAdminApiClient.put('/storefront/footer', configuration)
        return response.data
    },
}

export default storefrontFooterService
