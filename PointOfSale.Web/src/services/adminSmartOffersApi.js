import api from './api'
import {
    requireAdminSmartOfferListReadAuthority,
    requireAdminSmartOfferReadAuthority,
    requireAdminSmartOfferStatsReadAuthority,
} from '../auth/adminSmartOfferReadAuthority'

const noStoreOptions = {
    headers: {
        'Cache-Control': 'no-store',
        Pragma: 'no-cache',
    },
}

const normalizeRowVersion = (rowVersion) => (typeof rowVersion === 'string' ? rowVersion.trim() : '')

const mutationOptionsFromSmartOffer = (smartOffer) => {
    const rowVersion = normalizeRowVersion(smartOffer?.rowVersion ?? smartOffer?.RowVersion)
    if (!rowVersion) {
        throw new Error('Smart Offer lifecycle mutation requires accepted backend RowVersion evidence.')
    }

    return {
        headers: {
            'If-Match': `"${rowVersion}"`,
        },
    }
}

export const listAdminSmartOffers = async (filters = {}, currentUser) => {
    const params = new URLSearchParams()
    if (filters.search) params.set('search', filters.search)
    if (filters.status && filters.status !== 'All') params.set('status', filters.status)
    if (filters.sortBy) params.set('sortBy', filters.sortBy)
    if (filters.sortDirection) params.set('sortDirection', filters.sortDirection)
    if (filters.page) params.set('page', String(filters.page))
    if (filters.pageSize) params.set('pageSize', String(filters.pageSize))

    const query = params.toString()
    const response = await api.get(`/admin/smart-offers${query ? `?${query}` : ''}`, noStoreOptions)
    return requireAdminSmartOfferListReadAuthority(response.data, currentUser)
}

export const getAdminSmartOffer = async (offerId, currentUser) => {
    const response = await api.get(`/admin/smart-offers/${offerId}`, noStoreOptions)
    return requireAdminSmartOfferReadAuthority(response.data, currentUser, offerId)
}

export const getAdminSmartOfferStats = async (currentUser) => {
    const response = await api.get('/admin/smart-offers/stats', noStoreOptions)
    return requireAdminSmartOfferStatsReadAuthority(response.data, currentUser)
}

export const createAdminSmartOffer = async (data, currentUser) => {
    const response = await api.post('/admin/smart-offers', data)
    return requireAdminSmartOfferReadAuthority(response.data, currentUser)
}

export const updateAdminSmartOffer = async (smartOffer, data, currentUser) => {
    const smartOfferId = smartOffer?.id ?? smartOffer?.Id
    const response = await api.put(`/admin/smart-offers/${smartOfferId}`, data, mutationOptionsFromSmartOffer(smartOffer))
    return requireAdminSmartOfferReadAuthority(response.data, currentUser, smartOfferId)
}

export const archiveAdminSmartOffer = async (smartOffer) => {
    const smartOfferId = smartOffer?.id ?? smartOffer?.Id
    await api.delete(`/admin/smart-offers/${smartOfferId}`, mutationOptionsFromSmartOffer(smartOffer))
}
