import { describe, expect, it } from 'vitest'
import {
    requireMarketplaceOrderLifecycleAuthority,
    requireMarketplaceOrderLifecycleListAuthority,
} from './marketplaceOrderLifecycleAuthority'

const order = {
    orderId: 'order-1',
    tenantId: '11111111-1111-4111-8111-111111111111',
    isMarketplaceOrder: true,
    status: 'Processing',
    paymentStatus: 'Paid',
    subOrders: [
        {
            subOrderId: 'sub-1',
            status: 'Processing',
            paymentStatus: 'Paid',
        },
    ],
}

describe('Marketplace order lifecycle authority', () => {
    it('admits the exact synthetic Storefront enum only with backend audit provenance', () => {
        const synthetic = { orderId: 'qa', source: 'Ecommerce', isMarketplaceOrder: false,
            isSyntheticQa: true, status: 'Delivered', paymentStatus: 'SyntheticQaCollected', rowVersion: '123' }
        expect(requireMarketplaceOrderLifecycleAuthority(synthetic)).toBe(synthetic)
        expect(() => requireMarketplaceOrderLifecycleAuthority({ ...synthetic, isSyntheticQa: false })).toThrow(/malformed payment/)
        expect(() => requireMarketplaceOrderLifecycleAuthority({ ...synthetic, isMarketplaceOrder: true })).toThrow(/malformed payment/)
        expect(() => requireMarketplaceOrderLifecycleAuthority({ ...synthetic, source: 'Marketplace' })).toThrow(/malformed payment/)
        expect(() => requireMarketplaceOrderLifecycleAuthority({ ...synthetic, paymentStatus: 'UnknownFuturePaid' })).toThrow(/malformed payment/)
    })
    it('accepts current backend lifecycle and split lifecycle evidence', () => {
        expect(requireMarketplaceOrderLifecycleAuthority(order)).toBe(order)
        expect(requireMarketplaceOrderLifecycleListAuthority({ items: [order] })).toEqual({ items: [order] })
    })

    it('rejects fabricated lifecycle values from raw vendor order DTOs', () => {
        expect(() => requireMarketplaceOrderLifecycleAuthority({
            ...order,
            status: 'LocallyAdvanced',
        })).toThrow(/lifecycle authority/)
    })

    it('rejects payment, refund, and failed-state lifecycle contradictions', () => {
        expect(() => requireMarketplaceOrderLifecycleAuthority({
            ...order,
            status: 'Completed',
            paymentStatus: 'Failed',
            subOrders: [{ ...order.subOrders[0], status: 'Completed', paymentStatus: 'Failed' }],
        })).toThrow(/failed-payment fulfillment/)

        expect(() => requireMarketplaceOrderLifecycleAuthority({
            ...order,
            status: 'Refunded',
            paymentStatus: 'Paid',
            subOrders: [{ ...order.subOrders[0], status: 'Refunded', paymentStatus: 'Paid' }],
        })).toThrow(/refund lifecycle/)
    })

    it('rejects parent/child split lifecycle contradictions', () => {
        expect(() => requireMarketplaceOrderLifecycleAuthority({
            ...order,
            status: 'Cancelled',
            paymentStatus: 'Pending',
            subOrders: [{ ...order.subOrders[0], status: 'Delivered', paymentStatus: 'Paid' }],
        })).toThrow(/parent\/child lifecycle/)
    })

    it('does not apply Marketplace payment coupling to a canonical Ecommerce lifecycle', () => {
        const ecommerceOrder = {
            orderId: 'order-2',
            tenantId: '11111111-1111-4111-8111-111111111111',
            isMarketplaceOrder: false,
            status: 'Delivered',
            paymentStatus: 'Pending',
        }

        expect(requireMarketplaceOrderLifecycleAuthority(ecommerceOrder)).toBe(ecommerceOrder)
    })
})
