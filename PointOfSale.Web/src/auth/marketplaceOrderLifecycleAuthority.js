import {
    isMarketplaceOrderLifecycleStatus,
    isMarketplacePaymentStatus,
    MARKETPLACE_ACTIVE_FULFILLMENT_LIFECYCLES,
    MARKETPLACE_FAILED_PAYMENT_STATUSES,
    MARKETPLACE_FULFILLED_LIFECYCLES,
    MARKETPLACE_PAID_PAYMENT_STATUSES,
    MARKETPLACE_PAYMENT_PENDING_LIFECYCLES,
    MARKETPLACE_PAYMENT_PENDING_PAYMENT_STATUSES,
    MARKETPLACE_REFUND_PAYMENT_STATUSES,
} from './marketplaceOrderAuthorityDefinitions'

const normalizeString = (value) => (typeof value === 'string' ? value.trim() : '')

function assertLifecyclePaymentConsistency(status, paymentStatus) {
    if (MARKETPLACE_PAYMENT_PENDING_LIFECYCLES.has(status) && !MARKETPLACE_PAYMENT_PENDING_PAYMENT_STATUSES.has(paymentStatus)) {
        throw new Error('Marketplace order lifecycle authority rejected payment-pending contradiction.')
    }

    if (status === 'Failed' && !MARKETPLACE_FAILED_PAYMENT_STATUSES.has(paymentStatus)) {
        throw new Error('Marketplace order lifecycle authority rejected failed lifecycle contradiction.')
    }

    if (MARKETPLACE_FAILED_PAYMENT_STATUSES.has(paymentStatus) && MARKETPLACE_ACTIVE_FULFILLMENT_LIFECYCLES.has(status)) {
        throw new Error('Marketplace order lifecycle authority rejected failed-payment fulfillment contradiction.')
    }

    if (status === 'Refunded' && !MARKETPLACE_REFUND_PAYMENT_STATUSES.has(paymentStatus)) {
        throw new Error('Marketplace order lifecycle authority rejected refund lifecycle contradiction.')
    }

    if (MARKETPLACE_FULFILLED_LIFECYCLES.has(status) && !MARKETPLACE_PAID_PAYMENT_STATUSES.has(paymentStatus) && !MARKETPLACE_REFUND_PAYMENT_STATUSES.has(paymentStatus)) {
        throw new Error('Marketplace order lifecycle authority rejected fulfilled unpaid lifecycle contradiction.')
    }
}

function assertChildLifecycleConsistency(parentStatus, childStatus, childPaymentStatus) {
    assertLifecyclePaymentConsistency(childStatus, childPaymentStatus)

    if ((parentStatus === 'Cancelled' || parentStatus === 'Failed') && MARKETPLACE_ACTIVE_FULFILLMENT_LIFECYCLES.has(childStatus)) {
        throw new Error('Marketplace order lifecycle authority rejected parent/child lifecycle contradiction.')
    }

    if (parentStatus === 'Refunded' && childStatus !== 'Refunded' && childStatus !== 'Cancelled') {
        throw new Error('Marketplace order lifecycle authority rejected parent/child refund contradiction.')
    }
}

export function requireMarketplaceOrderLifecycleAuthority(order) {
    const status = normalizeString(order?.status)
    const paymentStatus = normalizeString(order?.paymentStatus)
    const isMarketplaceOrder = order?.isMarketplaceOrder === true
    const rowVersion = normalizeString(order?.rowVersion)

    if (!isMarketplaceOrderLifecycleStatus(status)) {
        throw new Error('Marketplace order lifecycle authority rejected malformed lifecycle evidence.')
    }

    if (paymentStatus) {
        // A known, explicitly synthetic Storefront state is readable for audit.
        // It is NEVER admitted as Marketplace paid evidence or a general fallback
        // for unknown future enum values. Durable SQL provenance is required.
        const isSyntheticStorefront = !isMarketplaceOrder && order?.source === 'Ecommerce' &&
            order?.isSyntheticQa === true && paymentStatus === 'SyntheticQaCollected'
        if (!isMarketplacePaymentStatus(paymentStatus) && !isSyntheticStorefront) {
            throw new Error('Marketplace order lifecycle authority rejected malformed payment lifecycle evidence.')
        }

        if (isMarketplaceOrder) {
            assertLifecyclePaymentConsistency(status, paymentStatus)
        }
    }

    if (isMarketplaceOrder && Array.isArray(order?.subOrders)) {
        for (const subOrder of order.subOrders) {
            const childStatus = normalizeString(subOrder?.status)
            const childPaymentStatus = normalizeString(subOrder?.paymentStatus)

            if (!isMarketplaceOrderLifecycleStatus(childStatus) || !isMarketplacePaymentStatus(childPaymentStatus)) {
                throw new Error('Marketplace order lifecycle authority rejected malformed split lifecycle evidence.')
            }

            assertChildLifecycleConsistency(status, childStatus, childPaymentStatus)
        }
    }

    if (!paymentStatus && (!Array.isArray(order?.subOrders) || order.subOrders.length === 0)) {
        throw new Error('Marketplace order lifecycle authority rejected missing payment lifecycle evidence.')
    }

    if (!isMarketplaceOrder && Object.prototype.hasOwnProperty.call(order ?? {}, 'rowVersion') && !rowVersion) {
        throw new Error('Marketplace order lifecycle authority rejected missing status concurrency evidence.')
    }

    return order
}

export function requireMarketplaceOrderLifecycleListAuthority(response) {
    const items = Array.isArray(response?.items) ? response.items : []
    for (const item of items) {
        requireMarketplaceOrderLifecycleAuthority(item)
    }

    return response
}
