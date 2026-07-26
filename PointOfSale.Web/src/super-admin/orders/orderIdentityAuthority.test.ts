import { describe, expect, it } from 'vitest'
import { requireSuperAdminOrderDetailAuthority, requireSuperAdminOrderListAuthority } from './orderIdentityAuthority'
import type { SuperAdminOrderDetails, SuperAdminOrderListItem } from './ordersService'

const listOrder: SuperAdminOrderListItem = {
    orderId: 'order-1',
    orderNumber: 'CH-1001',
    customerName: 'Customer',
    totalAmount: 100,
    currency: 'EGP',
    status: 'Pending',
    paymentStatus: 'Pending',
    tenantName: 'Vendor A',
    vendorCount: 1,
    createdAt: '2026-06-17T10:00:00Z',
    subOrders: [
        {
            subOrderId: 'sub-1',
            vendorId: 'vendor-1',
            vendorName: 'Vendor A',
            amount: 100,
            currency: 'EGP',
            status: 'Pending',
            paymentStatus: 'Pending',
        },
    ],
}

const detailOrder: SuperAdminOrderDetails = {
    ...listOrder,
    addressLine: 'Street',
    paymentMethodCode: 'Card',
    paymentMethodName: 'Card',
    subtotal: 90,
    shippingTotal: 10,
    platformCommissionTotal: 5,
    vendorEarningsTotal: 95,
    payments: [],
    subOrders: [
        {
            subOrderId: 'sub-1',
            orderNumber: 'CH-1001-A',
            vendorId: 'vendor-1',
            vendorName: 'Vendor A',
            amount: 100,
            currency: 'EGP',
            status: 'Pending',
            paymentStatus: 'Pending',
            platformCommissionAmount: 5,
            platformCommissionRate: 0.05,
            vendorEarningAmount: 95,
            paymentMethodName: 'Card',
            itemCount: 1,
        },
    ],
    orderItems: [],
}

describe('Super Admin order identity authority', () => {
    it('accepts current backend list and detail identity evidence', () => {
        expect(requireSuperAdminOrderListAuthority([listOrder])).toEqual([listOrder])
        expect(requireSuperAdminOrderDetailAuthority(detailOrder, 'order-1')).toEqual(detailOrder)
    })

    it('rejects duplicate or malformed list identities instead of preserving cached rows', () => {
        expect(() => requireSuperAdminOrderListAuthority([listOrder, listOrder])).toThrow(/duplicate order identity/)
        expect(() => requireSuperAdminOrderListAuthority([{ ...listOrder, orderNumber: '' }])).toThrow(/malformed list order/)
    })

    it('rejects detail identity that does not match the selected order route', () => {
        expect(() => requireSuperAdminOrderDetailAuthority(detailOrder, 'order-2')).toThrow(/selected order/)
    })

    it('rejects fabricated split order evidence', () => {
        expect(() => requireSuperAdminOrderDetailAuthority({
            ...detailOrder,
            subOrders: [{ ...detailOrder.subOrders[0], subOrderId: '' }],
        }, 'order-1')).toThrow(/split order/)
    })

    it('rejects lifecycle/payment contradictions instead of rendering admin status presentation', () => {
        expect(() => requireSuperAdminOrderListAuthority([{
            ...listOrder,
            status: 'Completed',
            paymentStatus: 'Failed',
            subOrders: [{ ...listOrder.subOrders[0], status: 'Completed', paymentStatus: 'Failed' }],
        }])).toThrow(/lifecycle authority/)

        expect(() => requireSuperAdminOrderDetailAuthority({
            ...detailOrder,
            status: 'Refunded',
            paymentStatus: 'Paid',
            subOrders: [{ ...detailOrder.subOrders[0], status: 'Refunded', paymentStatus: 'Paid' }],
        }, 'order-1')).toThrow(/lifecycle authority/)
    })

    it('rejects child lifecycle contradictions instead of reconciling split lifecycle locally', () => {
        expect(() => requireSuperAdminOrderListAuthority([{
            ...listOrder,
            status: 'Cancelled',
            paymentStatus: 'Pending',
            subOrders: [{ ...listOrder.subOrders[0], status: 'Shipped', paymentStatus: 'Paid' }],
        }])).toThrow(/parent\/child lifecycle/)
    })

    it('rejects split item evidence that is not bound to a backend sub-order', () => {
        expect(() => requireSuperAdminOrderDetailAuthority({
            ...detailOrder,
            orderItems: [
                {
                    itemId: 'item-1',
                    subOrderId: 'forged-sub-order',
                    vendorName: 'Vendor A',
                    productName: 'Product',
                    quantity: 1,
                    unitPrice: 100,
                    totalPrice: 100,
                },
            ],
        }, 'order-1')).toThrow(/split item/)
    })

    it('rejects fabricated totals, commissions, and vendor earnings instead of repairing them locally', () => {
        expect(() => requireSuperAdminOrderDetailAuthority({
            ...detailOrder,
            totalAmount: 101,
        }, 'order-1')).toThrow(/financial authority/)

        expect(() => requireSuperAdminOrderDetailAuthority({
            ...detailOrder,
            platformCommissionTotal: 6,
        }, 'order-1')).toThrow(/financial authority/)
    })
})
