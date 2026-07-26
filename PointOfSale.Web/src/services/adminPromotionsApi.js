import api from './api'
import {
    requireAdminPromotionListReadAuthority,
    requireAdminPromotionReadAuthority,
    requireAdminPromotionStatsReadAuthority,
} from '../auth/adminPromotionReadAuthority'

const noStoreOptions = {
    headers: {
        'Cache-Control': 'no-store',
        Pragma: 'no-cache',
    },
}

const normalizeRowVersion = (rowVersion) => (typeof rowVersion === 'string' ? rowVersion.trim() : '')

const mutationOptionsFromPromotion = (promotion) => {
    const rowVersion = normalizeRowVersion(promotion?.rowVersion ?? promotion?.RowVersion)
    if (!rowVersion) {
        throw new Error('Promotion lifecycle mutation requires accepted backend RowVersion evidence.')
    }

    return {
        headers: {
            'If-Match': `"${rowVersion}"`,
        },
    }
}

export const listAdminPromotions = async (filters = {}, currentUser) => {
    const params = new URLSearchParams()
    if (filters.activeOnly) params.set('activeOnly', 'true')
    if (filters.search) params.set('search', filters.search)
    if (filters.scope) params.set('scope', filters.scope)
    if (filters.sortBy) params.set('sortBy', filters.sortBy)
    if (filters.sortDirection) params.set('sortDirection', filters.sortDirection)
    if (filters.page) params.set('page', String(filters.page))
    if (filters.pageSize) params.set('pageSize', String(filters.pageSize))

    const query = params.toString()
    const response = await api.get(`/promotions${query ? `?${query}` : ''}`, noStoreOptions)
    return requireAdminPromotionListReadAuthority(response.data, currentUser)
}

export const getAdminPromotion = async (promotionId, currentUser) => {
    const response = await api.get(`/promotions/${promotionId}`, noStoreOptions)
    return requireAdminPromotionReadAuthority(response.data, currentUser, promotionId)
}

export const getAdminPromotionStats = async (currentUser) => {
    const response = await api.get('/promotions/stats', noStoreOptions)
    return requireAdminPromotionStatsReadAuthority(response.data, currentUser)
}

export const createAdminPromotion = async (data, currentUser) => {
    const response = await api.post('/promotions', data)
    return requireAdminPromotionReadAuthority(response.data, currentUser)
}

export const updateAdminPromotion = async (promotion, data, currentUser) => {
    const promotionId = promotion?.id ?? promotion?.Id
    const response = await api.put(`/promotions/${promotionId}`, data, mutationOptionsFromPromotion(promotion))
    return requireAdminPromotionReadAuthority(response.data, currentUser, promotionId)
}

export const toggleAdminPromotion = async (promotion, currentUser) => {
    const promotionId = promotion?.id ?? promotion?.Id
    const response = await api.patch(`/promotions/${promotionId}/toggle`, null, mutationOptionsFromPromotion(promotion))
    return requireAdminPromotionReadAuthority(response.data, currentUser, promotionId)
}

export const archiveAdminPromotion = async (promotion) => {
    const promotionId = promotion?.id ?? promotion?.Id
    await api.delete(`/promotions/${promotionId}`, mutationOptionsFromPromotion(promotion))
}
