import api from './api'
import {
    requireAdminCouponListReadAuthority,
    requireAdminCouponReadAuthority,
} from '../auth/adminCouponReadAuthority'

const noStoreOptions = {
    headers: {
        'Cache-Control': 'no-store',
        Pragma: 'no-cache',
    },
}

const normalizeRowVersion = (rowVersion) => (typeof rowVersion === 'string' ? rowVersion.trim() : '')

const mutationOptionsFromCoupon = (coupon) => {
    const rowVersion = normalizeRowVersion(coupon?.rowVersion ?? coupon?.RowVersion)
    if (!rowVersion) {
        throw new Error('Coupon lifecycle mutation requires accepted backend RowVersion evidence.')
    }

    return {
        headers: {
            'If-Match': `"${rowVersion}"`,
        },
    }
}

export const listAdminCoupons = async (filters = {}, currentUser) => {
    const params = new URLSearchParams()
    if (filters.activeOnly) params.set('activeOnly', 'true')
    if (filters.search) params.set('search', filters.search)
    if (filters.sortBy) params.set('sortBy', filters.sortBy)
    if (filters.sortDirection) params.set('sortDirection', filters.sortDirection)
    if (filters.page) params.set('page', String(filters.page))
    if (filters.pageSize) params.set('pageSize', String(filters.pageSize))

    const query = params.toString()
    const response = await api.get(`/coupons${query ? `?${query}` : ''}`, noStoreOptions)
    return requireAdminCouponListReadAuthority(response.data, currentUser)
}

export const getAdminCoupon = async (couponId, currentUser) => {
    const response = await api.get(`/coupons/${couponId}`, noStoreOptions)
    return requireAdminCouponReadAuthority(response.data, currentUser, couponId)
}

export const createAdminCoupon = async (data, currentUser) => {
    const response = await api.post('/coupons', data)
    return requireAdminCouponReadAuthority(response.data, currentUser)
}

export const updateAdminCoupon = async (coupon, data, currentUser) => {
    const couponId = coupon?.id ?? coupon?.Id
    const response = await api.put(`/coupons/${couponId}`, data, mutationOptionsFromCoupon(coupon))
    return requireAdminCouponReadAuthority(response.data, currentUser, couponId)
}

export const toggleAdminCoupon = async (coupon, currentUser) => {
    const couponId = coupon?.id ?? coupon?.Id
    const response = await api.patch(`/coupons/${couponId}/toggle`, null, mutationOptionsFromCoupon(coupon))
    return requireAdminCouponReadAuthority(response.data, currentUser, couponId)
}

export const archiveAdminCoupon = async (coupon) => {
    const couponId = coupon?.id ?? coupon?.Id
    await api.delete(`/coupons/${couponId}`, mutationOptionsFromCoupon(coupon))
}
