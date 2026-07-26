import { describe, expect, it } from 'vitest'
import {
    requireCustomerExistingRefundVisibilityAuthority,
    requireCustomerRefundVisibilityAuthority,
} from '../../../PointOfSale.CommerceHub/src/features/customer/paymentVisibilityAuthority'
import { requireSuperAdminPaymentMethodPresentation } from '../super-admin/payments/paymentOwnershipAuthority'

const refund = {
    refundId: '11111111-1111-4111-8111-111111111111',
    orderId: '22222222-2222-4222-8222-222222222222',
    orderNumber: 'ORD-1',
    orderItemId: '33333333-3333-4333-8333-333333333333',
    vendorId: '44444444-4444-4444-8444-444444444444',
    productName: 'Product',
    quantity: 1,
    unitPriceSnapshot: 10,
    amount: 10,
    currency: 'EGP',
    status: 'Requested' as const,
    reason: 'Damaged',
    reasonCode: 'DamagedItem' as const,
    mediaUrls: [],
    media: [],
    createdAt: '2030-01-01T00:00:00Z',
}

const method = {
    id: '55555555-5555-4555-8555-555555555555',
    name: 'Bank',
    destinationValue: 'Account',
    instructions: 'Use reference',
    isActive: true,
    createdAtUtc: '2030-01-01T00:00:00Z',
    createdBy: '66666666-6666-4666-8666-666666666666',
}

describe('payment visibility authority', () => {
    it('rejects customer refund rows that lack backend visibility identity', () => {
        expect(() => requireCustomerRefundVisibilityAuthority({ ...refund, orderId: 'route-value' })).toThrow(/visibility authority rejected/i)
    })

    it('rejects embedded refund rows that exceed the current visible order item total', () => {
        expect(() => requireCustomerExistingRefundVisibilityAuthority({ ...refund, amount: 20 }, 10)).toThrow(/visibility contradiction/i)
    })

    it('validates Super Admin payment-method mutation evidence as a method row', () => {
        expect(requireSuperAdminPaymentMethodPresentation(method)).toEqual(method)
        expect(() => requireSuperAdminPaymentMethodPresentation({ ...method, createdBy: 'frontend' })).toThrow(/payment-method evidence/i)
    })
})
