import api from '../../../../services/api'

const BASE_PATH = '/admin/storefront/checkout-fields'

const withNoStoreHeaders = (options = {}) => ({
    ...options,
    headers: {
        ...(options.headers || {}),
        'Cache-Control': 'no-store',
        Pragma: 'no-cache',
    },
})

export const checkoutFieldsService = {
    async getFields(options = {}) {
        const response = await api.get(BASE_PATH, withNoStoreHeaders(options))
        return response.data
    },

    async initializeSystemFields(options = {}) {
        const response = await api.post(`${BASE_PATH}/system-fields/initialize`, {}, withNoStoreHeaders(options))
        return response.data
    },

    async createField(request, options = {}) {
        const response = await api.post(BASE_PATH, request, options)
        return response.data
    },

    async updateField(fieldId, request, options = {}) {
        const response = await api.patch(`${BASE_PATH}/${fieldId}`, request, options)
        return response.data
    },

    async deleteField(fieldId, options = {}) {
        await api.delete(`${BASE_PATH}/${fieldId}`, withNoStoreHeaders(options))
    },

    async reorderFields(fields, options = {}) {
        const response = await api.put(`${BASE_PATH}/order`, { fields }, options)
        return response.data
    },
}

export default checkoutFieldsService
