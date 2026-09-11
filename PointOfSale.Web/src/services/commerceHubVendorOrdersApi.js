import api from './api'
import { useAuthStore } from '../store/authStore'
import {
    buildTenantHeaders,
    confirmAdminReturnReceipt,
    getAdminOrderDetails,
    getAdminOrders,
    mapAdminOrdersError,
} from './adminOrdersApi'
import {
    requireBackendMarketplaceLineageAuthority,
    requireCurrentVendorOrderVisibilityAuthority,
    requireCurrentVendorOrderVisibilityListAuthority,
} from '../auth/commerceHubVendorIdentityAuthority'

const ACTION_CODES = new Set(['process', 'ship', 'deliver', 'cancel'])
const RETURN_STATUSES = new Set(['Pending', 'PickupScheduled', 'PickedUp', 'InTransit', 'DeliveredToVendor', 'Confirmed', 'Failed'])
const RETURN_ACTIONS = {
    schedulePickup: { endpoint: 'schedule-pickup', grant: 'canSchedulePickup', label: 'Schedule pickup' },
    markPickedUp: { endpoint: 'mark-picked-up', grant: 'canMarkPickedUp', label: 'Mark picked up' },
    markInTransit: { endpoint: 'mark-in-transit', grant: 'canMarkInTransit', label: 'Mark in transit' },
    markDelivered: { endpoint: 'mark-delivered', grant: 'canMarkDelivered', label: 'Mark delivered to store' },
    markFailed: { endpoint: 'mark-failed', grant: 'canMarkFailed', label: 'Report return failure' },
}
const RETURN_GRANTS = [...new Set(Object.values(RETURN_ACTIONS).map((action) => action.grant))]
const FORWARD_RETURN_GRANTS = ['canSchedulePickup', 'canMarkPickedUp', 'canMarkInTransit', 'canMarkDelivered']
const FORWARD_RETURN_GRANT_BY_STATUS = {
    Pending: 'canSchedulePickup',
    PickupScheduled: 'canMarkPickedUp',
    PickedUp: 'canMarkInTransit',
    InTransit: 'canMarkDelivered',
}
const GUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i

export function requireCommerceHubVendorOrderListAuthority(response, user) {
    const scoped = requireCurrentVendorOrderVisibilityListAuthority(response, user)
    if (scoped.items.some((order) => order.isMarketplaceOrder !== true || order.source !== 'Marketplace')) {
        throw new Error('CommerceHub vendor orders authority rejected a non-marketplace order.')
    }
    scoped.items.forEach(requireBackendMarketplaceLineageAuthority)
    return scoped
}

export function requireCommerceHubVendorOrderAuthority(order, user, subOrderId) {
    const scoped = requireCurrentVendorOrderVisibilityAuthority(order, user, subOrderId)
    if (scoped.isMarketplaceOrder !== true || scoped.source !== 'Marketplace') {
        throw new Error('CommerceHub vendor order authority rejected a non-marketplace order.')
    }
    requireBackendMarketplaceLineageAuthority(scoped)
    for (const item of scoped.items || []) {
        for (const refund of item.refunds || []) {
            if (refund.returnRequest) {
                requireCommerceHubVendorReturnAuthority(refund.returnRequest, user, refund.returnRequest.returnId)
            }
        }
    }
    return scoped
}

export function requireCommerceHubVendorReturnAuthority(returnRequest, user, expectedReturnId) {
    const tenantId = String(user?.tenantId || '').toLowerCase()
    const returnId = String(returnRequest?.returnId || '').toLowerCase()
    if (!returnRequest ||
        !GUID_PATTERN.test(returnId) ||
        returnId !== String(expectedReturnId || '').toLowerCase() ||
        !GUID_PATTERN.test(String(returnRequest.refundId || '')) ||
        !GUID_PATTERN.test(String(returnRequest.orderItemId || '')) ||
        !GUID_PATTERN.test(String(returnRequest.customerId || '')) ||
        String(returnRequest.vendorId || '').toLowerCase() !== tenantId ||
        !RETURN_STATUSES.has(returnRequest.status) ||
        RETURN_GRANTS.some((grant) => typeof returnRequest[grant] !== 'boolean') ||
        typeof returnRequest.canConfirmReceipt !== 'boolean') {
        throw new Error('CommerceHub vendor return authority rejected malformed or cross-scope evidence.')
    }

    const expectedForwardGrant = FORWARD_RETURN_GRANT_BY_STATUS[returnRequest.status]
    const hasContradictoryForwardGrant = FORWARD_RETURN_GRANTS.some((grant) =>
        returnRequest[grant] !== (grant === expectedForwardGrant))
    if (hasContradictoryForwardGrant ||
        returnRequest.canConfirmReceipt !== (returnRequest.status === 'DeliveredToVendor') ||
        (['Confirmed', 'Failed'].includes(returnRequest.status) && returnRequest.canMarkFailed)) {
        throw new Error('CommerceHub vendor return authority rejected contradictory lifecycle grants.')
    }

    return returnRequest
}

export function getCommerceHubVendorReturnActions(returnRequest) {
    return Object.entries(RETURN_ACTIONS)
        .filter(([, action]) => returnRequest?.[action.grant] === true)
        .map(([code, action]) => ({ code, label: action.label, isFailure: code === 'markFailed' }))
}

export function requireCommerceHubVendorActionAuthority(authority, user, subOrderId) {
    const tenantId = String(user?.tenantId || '').toLowerCase()
    const expectedSubOrderId = String(subOrderId || '').toLowerCase()
    if (!authority ||
        String(authority.tenantId || '').toLowerCase() !== tenantId ||
        String(authority.storeId || '').toLowerCase() !== tenantId ||
        String(authority.subOrderId || '').toLowerCase() !== expectedSubOrderId ||
        !Number.isInteger(authority.lifecycleVersion) || authority.lifecycleVersion < 1 ||
        typeof authority.paymentReady !== 'boolean' ||
        typeof authority.inventoryReady !== 'boolean' ||
        !Array.isArray(authority.availableActions)) {
        throw new Error('CommerceHub vendor action authority rejected malformed or cross-scope evidence.')
    }

    const seen = new Set()
    for (const action of authority.availableActions) {
        if (!ACTION_CODES.has(action?.code) || seen.has(action.code) || !action.targetStatus || typeof action.requiresReason !== 'boolean') {
            throw new Error('CommerceHub vendor action authority rejected malformed action evidence.')
        }
        seen.add(action.code)
    }
    return authority
}

export async function getCommerceHubVendorOrders(filters, signal) {
    try {
        const response = await getAdminOrders({ ...filters, source: 'Marketplace' }, signal)
        return requireCommerceHubVendorOrderListAuthority(response, useAuthStore.getState().user)
    } catch (error) {
        throw mapAdminOrdersError(error, 'Unable to load CommerceHub vendor orders right now.')
    }
}

export async function getCommerceHubVendorOrder(subOrderId, signal) {
    try {
        const order = await getAdminOrderDetails(subOrderId, signal)
        return requireCommerceHubVendorOrderAuthority(order, useAuthStore.getState().user, subOrderId)
    } catch (error) {
        throw mapAdminOrdersError(error, 'Unable to load this CommerceHub vendor order right now.')
    }
}

export async function getCommerceHubVendorOrderActions(subOrderId, signal) {
    try {
        const response = await api.get(`/commercehub/suborders/${subOrderId}/actions`, {
            headers: buildTenantHeaders(),
            signal,
        })
        return requireCommerceHubVendorActionAuthority(response.data, useAuthStore.getState().user, subOrderId)
    } catch (error) {
        throw mapAdminOrdersError(error, 'Unable to load CommerceHub fulfillment authority right now.')
    }
}

export async function runCommerceHubVendorOrderAction(subOrderId, actionCode, reason = '') {
    const authority = await getCommerceHubVendorOrderActions(subOrderId)
    const action = authority.availableActions.find((candidate) => candidate.code === actionCode)
    if (!action) {
        throw new Error('The selected CommerceHub action is no longer authorized by the backend.')
    }
    if (action.requiresReason && !reason.trim()) {
        throw new Error('A reason is required for this CommerceHub action.')
    }

    try {
        const response = await api.patch(
            `/commercehub/suborders/${subOrderId}/status`,
            {
                status: action.targetStatus,
                expectedVersion: authority.lifecycleVersion,
                reason: reason.trim() || null,
            },
            { headers: buildTenantHeaders() },
        )
        return response.data
    } catch (error) {
        throw mapAdminOrdersError(error, 'Unable to run this CommerceHub fulfillment action right now.')
    }
}

export async function runCommerceHubVendorReturnAction(subOrderId, returnId, actionCode, payload = {}) {
    if (!Object.hasOwn(RETURN_ACTIONS, actionCode)) {
        throw new Error('The selected return action is not recognized.')
    }
    const action = RETURN_ACTIONS[actionCode]

    const currentOrder = await getCommerceHubVendorOrder(subOrderId)
    const currentReturn = (currentOrder.items || [])
        .flatMap((item) => item.refunds || [])
        .map((refund) => refund.returnRequest)
        .find((candidate) => String(candidate?.returnId || '').toLowerCase() === String(returnId || '').toLowerCase())
    const acceptedReturn = requireCommerceHubVendorReturnAuthority(
        currentReturn,
        useAuthStore.getState().user,
        returnId,
    )
    if (!acceptedReturn[action.grant]) {
        throw new Error('The selected return action is no longer authorized by the backend.')
    }
    if (actionCode === 'markFailed' && !payload.notes?.trim()) {
        throw new Error('A failure reason is required before reporting a failed return.')
    }

    try {
        const response = await api.post(
            `/pos-admin/returns/${returnId}/${action.endpoint}`,
            {
                expectedStatus: acceptedReturn.status,
                timestampUtc: payload.timestampUtc || null,
                trackingNumber: payload.trackingNumber?.trim() || null,
                courierName: payload.courierName?.trim() || null,
                notes: payload.notes?.trim() || null,
            },
            { headers: buildTenantHeaders() },
        )
        return requireCommerceHubVendorReturnAuthority(response.data, useAuthStore.getState().user, returnId)
    } catch (error) {
        throw mapAdminOrdersError(error, 'Unable to update this CommerceHub return right now.')
    }
}

export const confirmCommerceHubVendorReturnReceipt = confirmAdminReturnReceipt
