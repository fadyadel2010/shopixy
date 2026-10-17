import api, { getSafeApiErrorMessage } from './api'
import { requestShipmentCancellation } from '../modules/shipping/cancellationOutcome'
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
const SAFE_ERROR_CODE_PATTERN = /^[A-Z0-9_]{2,80}$/
const SAFE_TRACE_ID_PATTERN = /^[A-Za-z0-9_.:-]{1,160}$/

export function mapAdminOrdersError(error, fallback) {
    const status = Number.isInteger(error?.response?.status) ? error.response.status : null
    const backendError = error?.response?.data?.error
    const backendCode = error?.response?.data?.code
        || (typeof backendError === 'string' && SAFE_ERROR_CODE_PATTERN.test(backendError) ? backendError : null)
    const backendTraceId = error?.response?.data?.traceId
        || error?.response?.headers?.['x-correlation-id']
        || error?.response?.headers?.['x-request-id']
    const isClientAuthorityError = !error?.response && !error?.config && error instanceof Error
    const clientMessage = error?.message === 'Tenant context is unavailable. Sign in again before viewing orders.'
        ? error.message
        : 'Orders response could not be verified. Please retry or sign in again.'
    const mapped = new Error(
        isClientAuthorityError
            ? clientMessage
            : getSafeApiErrorMessage(error, fallback),
    )

    mapped.name = 'AdminOrdersError'
    mapped.status = status
    mapped.code = typeof backendCode === 'string' && SAFE_ERROR_CODE_PATTERN.test(backendCode)
        ? backendCode
        : status
            ? `HTTP_${status}`
            : isClientAuthorityError
                ? 'ORDER_CLIENT_AUTHORITY_REJECTED'
                : 'ORDER_API_UNAVAILABLE'
    mapped.traceId = typeof backendTraceId === 'string' && SAFE_TRACE_ID_PATTERN.test(backendTraceId)
        ? backendTraceId
        : null

    return mapped
}

function resolveTenantId() {
    return useAuthStore.getState().user?.tenantId || ''
}

export function buildTenantHeaders() {
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

export function buildOrderQueryParams(filters) {
    const params = new URLSearchParams()

    params.set('page', String(filters.page || DEFAULT_PAGE))
    params.set('pageSize', String(filters.pageSize || DEFAULT_PAGE_SIZE))

    if (filters.status) {
        params.set('status', filters.status)
    }

    if (filters.paymentStatus) {
        params.set('paymentStatus', filters.paymentStatus)
    }

    if (filters.source) {
        params.set('source', filters.source)
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

export async function getAdminOrders(filters, signal) {
    try {
        const response = await api.get(`/admin/orders?${buildOrderQueryParams(filters).toString()}`, {
            headers: buildTenantHeaders(),
            signal,
        })

        const lifecycleAccepted = requireMarketplaceOrderLifecycleListAuthority(response.data)
        const scoped = requireVendorOrderVisibilityListAuthority(lifecycleAccepted, useAuthStore.getState().user)
        if (scoped.items.some((order) => order.isMarketplaceOrder === true || order.source !== 'Ecommerce')) {
            throw new Error('Storefront orders authority rejected a cross-domain order.')
        }
        return scoped
    } catch (error) {
        throw mapAdminOrdersError(error, 'Unable to load tenant orders right now.')
    }
}

export async function getAdminOrderDetails(orderId, signal) {
    try {
        const response = await api.get(`/admin/orders/${orderId}`, {
            headers: buildTenantHeaders(),
            signal,
        })

        const lifecycleAccepted = requireMarketplaceOrderLifecycleAuthority(response.data)
        const scoped = requireVendorOrderVisibilityAuthority(lifecycleAccepted, useAuthStore.getState().user, orderId)
        if (scoped.isMarketplaceOrder === true || scoped.source !== 'Ecommerce') {
            throw new Error('Storefront order authority rejected a cross-domain order.')
        }
        return scoped
    } catch (error) {
        throw mapAdminOrdersError(error, 'Unable to load order details right now.')
    }
}

function requireOrderScopedResponse(payload, orderId, label) {
    if (!payload || String(payload.orderId || '').toLowerCase() !== String(orderId || '').toLowerCase()) {
        throw new Error(`${label} rejected order-target mismatch.`)
    }

    return payload
}

export async function getAdminOrderCustomerTrust(orderId, signal) {
    try {
        const response = await api.get(`/admin/orders/${orderId}/customer-trust`, {
            headers: buildTenantHeaders(),
            signal,
        })
        return requireOrderScopedResponse(response.data, orderId, 'Customer trust authority')
    } catch (error) {
        throw mapAdminOrdersError(error, 'Unable to load customer trust evidence right now.')
    }
}

export async function getAdminOrderSessionIntelligence(orderId, signal) {
    try {
        const response = await api.get(`/admin/orders/${orderId}/session-intelligence`, {
            headers: buildTenantHeaders(),
            signal,
        })
        return response.status === 204 || !response.data
            ? null
            : requireOrderScopedResponse(response.data, orderId, 'Session intelligence authority')
    } catch (error) {
        throw mapAdminOrdersError(error, 'Unable to load session intelligence right now.')
    }
}

export async function runAdminOrderCustomerPhoneAction(orderId, action, reason) {
    const endpoints = {
        block: `/admin/orders/${orderId}/customer-phone/block`,
        unblock: `/admin/orders/${orderId}/customer-phone/unblock`,
        reportSpam: `/admin/orders/${orderId}/customer-phone/report-spam`,
    }
    const endpoint = endpoints[action]
    if (!endpoint) {
        throw new Error('This customer phone action is not supported.')
    }

    try {
        const payload = action === 'block' ? { reason: reason?.trim() || null } : undefined
        const response = await api.post(endpoint, payload, { headers: buildTenantHeaders() })
        return response.data
    } catch (error) {
        throw mapAdminOrdersError(error, 'Unable to update this customer phone right now.')
    }
}

export async function confirmAdminReturnReceipt(returnId, payload) {
    try {
        const response = await api.post(
            `/pos-admin/returns/${returnId}/confirm-receipt`,
            payload,
            { headers: buildTenantHeaders() },
        )
        return response.data
    } catch (error) {
        throw mapAdminOrdersError(error, 'Unable to confirm this return receipt right now.')
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
        throw mapAdminOrdersError(error, 'Unable to update order status right now.')
    }
}

export async function getAdminOrderFulfillmentActions(orderId, signal) {
    try {
        const response = await api.get(`/orders/${orderId}/fulfillment/actions`, {
            headers: buildTenantHeaders(),
            signal,
        })

        return response.data
    } catch (error) {
        throw mapAdminOrdersError(error, 'Unable to load fulfillment actions right now.')
    }
}

const PAYMENT_ACTION_CODES = new Set(['cod-delivered', 'refund'])

export function requireAdminOrderPaymentActionAuthority(authority, expectedOrderId) {
    if (!authority || String(authority.orderId || '').toLowerCase() !== String(expectedOrderId || '').toLowerCase()) {
        throw new Error('Payment action authority rejected order-target mismatch.')
    }

    if (!Array.isArray(authority.availableActions)) {
        throw new Error('Payment action authority rejected missing backend action evidence.')
    }

    const seen = new Set()
    for (const action of authority.availableActions) {
        if (
            !PAYMENT_ACTION_CODES.has(action?.actionCode) ||
            seen.has(action.actionCode) ||
            typeof action.label !== 'string' ||
            !action.label.trim() ||
            action.method !== 'POST'
        ) {
            throw new Error('Payment action authority rejected malformed backend action evidence.')
        }
        seen.add(action.actionCode)
    }

    return authority
}

export async function getAdminOrderPaymentActions(orderId, signal) {
    try {
        const response = await api.get(`/payments/lifecycle/${orderId}/actions`, {
            headers: buildTenantHeaders(),
            signal,
        })

        return requireAdminOrderPaymentActionAuthority(response.data, orderId)
    } catch (error) {
        throw mapAdminOrdersError(error, 'Unable to load payment actions right now.')
    }
}

export async function runAdminOrderPaymentAction(orderId, actionCode, payload = {}) {
    if (!PAYMENT_ACTION_CODES.has(actionCode)) {
        throw new Error('This payment action is not supported by the Orders control.')
    }

    try {
        const endpoint = actionCode === 'cod-delivered'
            ? `/payments/lifecycle/${orderId}/cod-delivered`
            : `/payments/lifecycle/${orderId}/refund`
        const response = await api.post(endpoint, payload, { headers: buildTenantHeaders() })
        return response.data
    } catch (error) {
        throw mapAdminOrdersError(error, 'Unable to run the selected payment action right now.')
    }
}

export async function runAdminOrderFulfillmentAction(orderId, actionCode, payload = {}) {
    const supportedActionCodes = new Set([
        'confirm',
        'pack',
        'out-for-delivery',
        'delivered',
        'complete',
        'returned',
        'failed-delivery',
        'retry-delivery',
    ])

    if (!supportedActionCodes.has(actionCode)) {
        throw new Error('This lifecycle action needs additional evidence and cannot be submitted from this control.')
    }

    try {
        const response = await api.post(
            `/orders/${orderId}/fulfillment/${actionCode}`,
            payload,
            {
                headers: buildTenantHeaders(),
            },
        )

        return response.data
    } catch (error) {
        throw mapAdminOrdersError(error, 'Unable to run the selected lifecycle action right now.')
    }
}

export async function cancelAdminOrder(orderId, reason) {
    if (!reason?.trim()) {
        throw new Error('Cancellation reason is required.')
    }

    try {
        const response = await api.post(
            `/admin/orders/${orderId}/cancel`,
            {
                reason: reason.trim(),
            },
            {
                headers: buildTenantHeaders(),
            },
        )

        return response.data
    } catch (error) {
        throw mapAdminOrdersError(error, 'Unable to cancel this order right now.')
    }
}

export async function cancelAdminShipment(shipmentId, reason) {
    if (!reason?.trim()) {
        throw new Error('Shipment cancellation reason is required.')
    }

    try {
        return await requestShipmentCancellation(api, shipmentId, reason, { headers: buildTenantHeaders() })
    } catch (error) {
        throw mapAdminOrdersError(error, 'Unable to cancel this shipment right now.')
    }
}

export async function getAdminOrderCheckoutAttachmentBlob(orderId, attachmentId, signal) {
    try {
        const response = await api.get(`/admin/orders/${orderId}/checkout-attachments/${attachmentId}/download`, {
            headers: buildTenantHeaders(),
            responseType: 'blob',
            signal,
        })

        return response.data
    } catch (error) {
        throw mapAdminOrdersError(error, 'Unable to load this attachment right now.')
    }
}

export async function downloadAdminOrderCheckoutAttachment(orderId, attachmentId, fileName) {
    const blob = await getAdminOrderCheckoutAttachmentBlob(orderId, attachmentId)

    const blobUrl = window.URL.createObjectURL(blob)
    try {
        const link = document.createElement('a')
        link.href = blobUrl
        link.download = fileName || 'checkout-attachment'
        document.body.appendChild(link)
        link.click()
        link.remove()
    } finally {
        window.URL.revokeObjectURL(blobUrl)
    }
}
