import type { SuperAdminOrderDetails, SuperAdminOrderListItem } from './ordersService'
import { isMarketplaceOrderLifecycleStatus, isMarketplacePaymentStatus } from '../../auth/marketplaceOrderAuthorityDefinitions'
import { requireMarketplaceOrderLifecycleAuthority } from './orderLifecycleAuthority'

function isNonEmptyString(value: unknown): value is string {
    return typeof value === 'string' && value.trim().length > 0
}

function isFiniteNumber(value: unknown): value is number {
    return typeof value === 'number' && Number.isFinite(value)
}

function isNonNegativeMoney(value: unknown): value is number {
    return isFiniteNumber(value) && value >= 0
}

function sameMoney(left: number, right: number): boolean {
    return Math.abs(left - right) < 0.005
}

function isValidOrderStatus(value: unknown): boolean {
    return isNonEmptyString(value) && isMarketplaceOrderLifecycleStatus(value)
}

function isValidPaymentStatus(value: unknown): boolean {
    return isNonEmptyString(value) && isMarketplacePaymentStatus(value)
}

function assertSubOrders(order: { subOrders?: unknown[]; vendorCount?: unknown }) {
    if (!Array.isArray(order.subOrders) || order.subOrders.length === 0) {
        throw new Error('Super Admin order identity authority rejected missing split order evidence.')
    }

    if (isFiniteNumber(order.vendorCount) && order.vendorCount !== order.subOrders.length) {
        throw new Error('Super Admin order identity authority rejected contradictory split order evidence.')
    }

    const seenSubOrderIds = new Set<string>()
    let splitTotal = 0
    for (const subOrder of order.subOrders as Array<Record<string, unknown>>) {
        if (
            !isNonEmptyString(subOrder.subOrderId) ||
            seenSubOrderIds.has(subOrder.subOrderId) ||
            !isNonEmptyString(subOrder.vendorId) ||
            !isNonEmptyString(subOrder.vendorName) ||
            !isNonNegativeMoney(subOrder.amount) ||
            !isNonEmptyString(subOrder.currency) ||
            !isValidOrderStatus(subOrder.status) ||
            !isValidPaymentStatus(subOrder.paymentStatus)
        ) {
            throw new Error('Super Admin order identity authority rejected malformed split order evidence.')
        }

        seenSubOrderIds.add(subOrder.subOrderId)
        splitTotal += subOrder.amount
    }

    const parentTotal = (order as { totalAmount?: unknown }).totalAmount
    if (parentTotal !== undefined && (!isNonNegativeMoney(parentTotal) || !sameMoney(parentTotal, splitTotal))) {
        throw new Error('Super Admin order financial authority rejected contradictory parent and split totals.')
    }
}

function assertDetailSplitItems(order: SuperAdminOrderDetails) {
    if (!Array.isArray(order.orderItems)) {
        throw new Error('Super Admin order identity authority rejected missing split item evidence.')
    }

    const subOrderIds = new Set(order.subOrders.map((subOrder) => subOrder.subOrderId))
    const seenItemIds = new Set<string>()
    for (const item of order.orderItems) {
        if (
            !isNonEmptyString(item.itemId) ||
            seenItemIds.has(item.itemId) ||
            !isNonEmptyString(item.subOrderId) ||
            !subOrderIds.has(item.subOrderId) ||
            !isNonEmptyString(item.vendorName) ||
            !isNonEmptyString(item.productName) ||
            !isFiniteNumber(item.quantity) ||
            item.quantity <= 0 ||
            !isFiniteNumber(item.unitPrice) ||
            !isFiniteNumber(item.totalPrice)
        ) {
            throw new Error('Super Admin order identity authority rejected malformed split item evidence.')
        }

        seenItemIds.add(item.itemId)
    }
}

function assertDetailFinancialAuthority(order: SuperAdminOrderDetails) {
    if (
        !isNonNegativeMoney(order.subtotal) ||
        !isNonNegativeMoney(order.shippingTotal) ||
        !isNonNegativeMoney(order.totalAmount) ||
        !isNonNegativeMoney(order.platformCommissionTotal) ||
        !isNonNegativeMoney(order.vendorEarningsTotal) ||
        !sameMoney(order.totalAmount, order.subtotal + order.shippingTotal)
    ) {
        throw new Error('Super Admin order financial authority rejected contradictory order totals.')
    }

    let commissionTotal = 0
    let earningsTotal = 0
    for (const subOrder of order.subOrders) {
        if (
            !isNonNegativeMoney(subOrder.platformCommissionAmount) ||
            !isNonNegativeMoney(subOrder.platformCommissionRate) ||
            !isNonNegativeMoney(subOrder.vendorEarningAmount) ||
            !sameMoney(subOrder.amount, subOrder.platformCommissionAmount + subOrder.vendorEarningAmount)
        ) {
            throw new Error('Super Admin order financial authority rejected contradictory vendor financial evidence.')
        }
        commissionTotal += subOrder.platformCommissionAmount
        earningsTotal += subOrder.vendorEarningAmount
    }

    if (!sameMoney(order.platformCommissionTotal, commissionTotal) || !sameMoney(order.vendorEarningsTotal, earningsTotal)) {
        throw new Error('Super Admin order financial authority rejected contradictory aggregate financial evidence.')
    }
}

function assertListOrder(order: SuperAdminOrderListItem) {
    requireMarketplaceOrderLifecycleAuthority(order)

    if (
        !isNonEmptyString(order.orderId) ||
        !isNonEmptyString(order.orderNumber) ||
        !isNonEmptyString(order.tenantName) ||
        !isNonEmptyString(order.createdAt) ||
        !isFiniteNumber(order.totalAmount) ||
        !isFiniteNumber(order.vendorCount) ||
        !isValidOrderStatus(order.status) ||
        !isValidPaymentStatus(order.paymentStatus)
    ) {
        throw new Error('Super Admin order identity authority rejected malformed list order evidence.')
    }

    assertSubOrders(order)
}

export function requireSuperAdminOrderListAuthority(orders: SuperAdminOrderListItem[]): SuperAdminOrderListItem[] {
    if (!Array.isArray(orders)) {
        throw new Error('Super Admin order identity authority rejected malformed list evidence.')
    }

    const seenOrderIds = new Set<string>()
    for (const order of orders) {
        assertListOrder(order)
        if (seenOrderIds.has(order.orderId)) {
            throw new Error('Super Admin order identity authority rejected duplicate order identity evidence.')
        }

        seenOrderIds.add(order.orderId)
    }

    return orders
}

export function requireSuperAdminOrderDetailAuthority(order: SuperAdminOrderDetails, expectedOrderId: string): SuperAdminOrderDetails {
    requireMarketplaceOrderLifecycleAuthority(order)

    if (
        !isNonEmptyString(expectedOrderId) ||
        !isNonEmptyString(order?.orderId) ||
        order.orderId !== expectedOrderId ||
        !isNonEmptyString(order.orderNumber) ||
        !isNonEmptyString(order.tenantName) ||
        !isNonEmptyString(order.createdAt) ||
        !isFiniteNumber(order.totalAmount) ||
        !isFiniteNumber(order.vendorCount) ||
        !isValidOrderStatus(order.status) ||
        !isValidPaymentStatus(order.paymentStatus)
    ) {
        throw new Error('Super Admin order identity authority rejected detail evidence for the selected order.')
    }

    assertSubOrders(order)
    assertDetailFinancialAuthority(order)
    assertDetailSplitItems(order)
    return order
}
