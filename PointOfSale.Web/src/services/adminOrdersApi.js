import api, { getSafeApiErrorMessage } from './api'
import { useAuthStore } from '../store/authStore'
import {
    requireMarketplaceOrderLifecycleAuthority,
    requireMarketplaceOrderLifecycleListAuthority,
} from '../auth/marketplaceOrderLifecycleAuthority'
import {
    requireCurrentVendorOrderVisibilityAuthority as requireVendorOrderVisibilityAuthority,
    requireCurrentVendorOrderVisibilityListAuthority as requireVendorOrderVisibilityListAuthority,
} from '../auth/commerceHubVendorIdentityAuthority'

const DEFAULT_PAGE = 1
const DEFAULT_PAGE_SIZE = 20

function resolveTenantId() {
    return useAuthStore.getState().user?.tenantId || ''
}

function buildTenantHeaders() {
    const tenantId = resolveTenantId()

    if (!tenantId) {
        throw new Error('Tenant context is unavailable. Sign in again before viewing orders.')
    }

    return {
        'X-Tenant-Id': tenantId,
        'Cache-Control': 'no-store',
        Pragma: 'no-cache',
    }
}

function buildOrderQueryParams(filters) {
    const params = new URLSearchParams()

    params.set('page', String(filters.page || DEFAULT_PAGE))
    params.set('pageSize', String(filters.pageSize || DEFAULT_PAGE_SIZE))

    if (filters.status) {
        params.set('status', filters.status)
    }

    if (filters.paymentStatus) {
        params.set('paymentStatus', filters.paymentStatus)
    }

    if (filters.fromDate) {
        params.set('fromDate', filters.fromDate)
    }

    if (filters.toDate) {
        params.set('toDate', filters.toDate)
    }

    if (filters.customerId) {
        params.set('customerId', filters.customerId)
    }

    if (filters.search) {
        params.set('search', filters.search)
    }

    if (filters.sortBy) {
        params.set('sortBy', filters.sortBy)
    }

    if (filters.sortDirection) {
        params.set('sortDirection', filters.sortDirection)
    }

    return params
}

export async function getAdminOrders(filters) {
    try {
        const response = await api.get(`/admin/orders?${buildOrderQueryParams(filters).toString()}`, {
            headers: buildTenantHeaders(),
        })

        const lifecycleAccepted = requireMarketplaceOrderLifecycleListAuthority(response.data)
        return requireVendorOrderVisibilityListAuthority(lifecycleAccepted, useAuthStore.getState().user)
    } catch (error) {
        throw new Error(getSafeApiErrorMessage(error, 'Unable to load tenant orders right now.'))
    }
}

export async function getAdminOrderDetails(orderId) {
    try {
        const response = await api.get(`/admin/orders/${orderId}`, {
            headers: buildTenantHeaders(),
        })

        const lifecycleAccepted = requireMarketplaceOrderLifecycleAuthority(response.data)
        return requireVendorOrderVisibilityAuthority(lifecycleAccepted, useAuthStore.getState().user, orderId)
    } catch (error) {
        throw new Error(getSafeApiErrorMessage(error, 'Unable to load order details right now.'))
    }
}

export async function updateAdminOrderStatus(orderId, { newStatus, reason, rowVersion }) {
    if (!rowVersion) {
        throw new Error('Current order RowVersion is required before changing lifecycle status.')
    }

    try {
        const response = await api.put(
            `/admin/orders/${orderId}/status`,
            {
                newStatus,
                reason: reason?.trim() || null,
            },
            {
                headers: {
                    ...buildTenantHeaders(),
                    'If-Match': rowVersion,
                },
            },
        )

        return response.data
    } catch (error) {
        throw new Error(getSafeApiErrorMessage(error, 'Unable to update order status right now.'))
    }
}
