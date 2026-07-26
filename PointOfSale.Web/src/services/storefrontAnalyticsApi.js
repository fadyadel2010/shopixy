import api from './api'

export const getStorefrontAnalyticsDashboard = async (range = '7d') => {
    const response = await api.get('/admin/analytics/storefront-dashboard', {
        params: { range }
    })

    return response.data
}

export const getStorefrontLiveMonitoring = async () => {
    const response = await api.get('/admin/analytics/storefront-live')
    return response.data
}

export default {
    getStorefrontAnalyticsDashboard,
    getStorefrontLiveMonitoring
}
