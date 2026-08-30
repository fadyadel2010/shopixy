import { beforeEach, describe, expect, it, vi } from 'vitest'

const apiGetMock = vi.hoisted(() => vi.fn())
const apiPostMock = vi.hoisted(() => vi.fn())
const currentUser = vi.hoisted(() => ({
    id: '11111111-1111-4111-8111-111111111111',
    tenantId: '83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5',
}))

vi.mock('./api', async (importOriginal) => {
    const actual = await importOriginal<typeof import('./api')>()
    return {
        ...actual,
        default: {
            ...actual.default,
            get: apiGetMock,
            post: apiPostMock,
        },
    }
})

vi.mock('../store/authStore', () => ({
    useAuthStore: {
        getState: () => ({ user: currentUser }),
    },
}))

import {
    getAdminOrderFulfillmentActions,
    getAdminOrderPaymentActions,
    getAdminOrders,
    mapAdminOrdersError,
    requireAdminOrderPaymentActionAuthority,
    runAdminOrderFulfillmentAction,
    runAdminOrderPaymentAction,
} from './adminOrdersApi'

const makeStorefrontOrder = (index: number) => ({
    tenantId: currentUser.tenantId,
    orderId: `00000000-0000-4000-8000-${String(index).padStart(12, '0')}`,
    orderNumber: `ORD-83B3-2026-${String(1000 + index).padStart(5, '0')}`,
    source: 'Ecommerce',
    sourceLabel: 'Ecommerce',
    status: 'Processing',
    paymentStatus: 'Pending',
    isMarketplaceOrder: false,
})

const realAffiliateOrder = {
    ...makeStorefrontOrder(19),
    orderId: 'fc456d07-0cfe-4cc0-af0c-23862534d45a',
    orderNumber: 'ORD-83B3-2026-01019',
    affiliateAttribution: {
        tenantId: currentUser.tenantId,
        orderId: 'fc456d07-0cfe-4cc0-af0c-23862534d45a',
        attributionId: '01a03efa-bb23-7dce-8c4c-b899d545f6f5',
        affiliateAccountId: '01a03e11-e234-7d40-b77e-99f9463c7a40',
        referralCode: 'DMFOQ21IP3',
        promotionLinkId: '01a03e6b-3ff1-7c1b-9cf2-a340244c0f25',
        source: 'ReferralLink',
        attributedAtUtc: '2026-08-26T16:50:14.431971Z',
    },
}

const mixedFifteenOrderResponse = {
    items: [
        realAffiliateOrder,
        {
            ...makeStorefrontOrder(18),
            orderNumber: 'ORD-FIXTURE-AFFILIATE-2',
            affiliateAttribution: {
                tenantId: currentUser.tenantId,
                orderId: makeStorefrontOrder(18).orderId,
                attributionId: '0198ef41-fd7c-7a34-8c12-111111111111',
                affiliateAccountId: '0198ef41-fd7c-7a34-8c12-222222222222',
                referralCode: 'PREVIOUS1',
                promotionLinkId: '0198ef41-fd7c-7a34-8c12-333333333333',
                source: 'ReferralLink',
                attributedAtUtc: '2026-08-26T16:40:00Z',
            },
        },
        ...Array.from({ length: 13 }, (_, index) => makeStorefrontOrder(index + 1)),
    ],
    page: 1,
    pageSize: 20,
    totalCount: 15,
    totalPages: 1,
    hasPreviousPage: false,
    hasNextPage: false,
}

describe('Admin Orders API error contract', () => {
    beforeEach(() => {
        vi.clearAllMocks()
    })

    it('submits manual delivery evidence to the backend-advertised lifecycle endpoint', async () => {
        apiPostMock.mockResolvedValue({ data: { success: true, newStatus: 'Delivered' } })
        const payload = {
            deliveryReference: 'POD-001',
            proofOfDeliveryUrl: 'https://evidence.example.test/pod/001',
            recipientName: 'Recipient',
        }

        await expect(runAdminOrderFulfillmentAction(realAffiliateOrder.orderId, 'delivered', payload))
            .resolves.toEqual({ success: true, newStatus: 'Delivered' })
        expect(apiPostMock).toHaveBeenCalledWith(
            `/orders/${realAffiliateOrder.orderId}/fulfillment/delivered`,
            payload,
            expect.objectContaining({ headers: expect.objectContaining({ 'X-Tenant-Id': currentUser.tenantId }) }),
        )
    })

    it('forwards AbortSignal to fulfillment and payment capability reads', async () => {
        const controller = new AbortController()
        apiGetMock
            .mockResolvedValueOnce({ data: { orderId: realAffiliateOrder.orderId, availableActions: [] } })
            .mockResolvedValueOnce({
                data: {
                    orderId: realAffiliateOrder.orderId,
                    currentStatus: 'Pending',
                    paymentMethodCode: 'COD',
                    statusMessage: 'Waiting for delivery',
                    availableActions: [],
                },
            })

        await getAdminOrderFulfillmentActions(realAffiliateOrder.orderId, controller.signal)
        await getAdminOrderPaymentActions(realAffiliateOrder.orderId, controller.signal)

        expect(apiGetMock).toHaveBeenNthCalledWith(1, `/orders/${realAffiliateOrder.orderId}/fulfillment/actions`, expect.objectContaining({ signal: controller.signal }))
        expect(apiGetMock).toHaveBeenNthCalledWith(2, `/payments/lifecycle/${realAffiliateOrder.orderId}/actions`, expect.objectContaining({ signal: controller.signal }))
    })

    it('submits only fixed backend payment lifecycle endpoints', async () => {
        apiPostMock.mockResolvedValue({ data: { success: true, newStatus: 'Collected' } })

        await runAdminOrderPaymentAction(realAffiliateOrder.orderId, 'cod-delivered', { deliveryReference: 'COD-1' })
        await runAdminOrderPaymentAction(realAffiliateOrder.orderId, 'refund', { amount: 10, refundId: 'refund-1' })

        expect(apiPostMock).toHaveBeenNthCalledWith(1, `/payments/lifecycle/${realAffiliateOrder.orderId}/cod-delivered`, { deliveryReference: 'COD-1' }, expect.any(Object))
        expect(apiPostMock).toHaveBeenNthCalledWith(2, `/payments/lifecycle/${realAffiliateOrder.orderId}/refund`, { amount: 10, refundId: 'refund-1' }, expect.any(Object))
        await expect(runAdminOrderPaymentAction(realAffiliateOrder.orderId, 'provider-admin', {}))
            .rejects.toThrow('not supported')
    })

    it('rejects malformed, duplicate, or cross-order payment capability evidence', () => {
        expect(() => requireAdminOrderPaymentActionAuthority({
            orderId: '99999999-9999-4999-8999-999999999999',
            availableActions: [],
        }, realAffiliateOrder.orderId)).toThrow('order-target mismatch')

        expect(() => requireAdminOrderPaymentActionAuthority({
            orderId: realAffiliateOrder.orderId,
            availableActions: [
                { actionCode: 'refund', label: 'Refund', method: 'POST' },
                { actionCode: 'refund', label: 'Refund again', method: 'POST' },
            ],
        }, realAffiliateOrder.orderId)).toThrow('malformed backend action evidence')
    })

    it('accepts a mixed 15-order HTTP 200 list containing Affiliate UUIDv7 evidence', async () => {
        apiGetMock.mockResolvedValue({ data: mixedFifteenOrderResponse })

        const result = await getAdminOrders({ page: 1, pageSize: 20 })

        expect(result).toEqual(mixedFifteenOrderResponse)
        expect(result.items).toHaveLength(15)
        expect(result.items.filter((order) => order.affiliateAttribution)).toHaveLength(2)
        expect(result.items.every((order) => order.source === 'Ecommerce')).toBe(true)
        expect(result.items.every((order) => !order.affiliateAttributionWarning)).toBe(true)
        expect(apiGetMock).toHaveBeenCalledOnce()
    })

    it('accepts an empty Admin Orders page without inventing Affiliate evidence', async () => {
        const emptyResponse = {
            items: [],
            page: 1,
            pageSize: 20,
            totalCount: 0,
            totalPages: 0,
            hasPreviousPage: false,
            hasNextPage: false,
        }
        apiGetMock.mockResolvedValue({ data: emptyResponse })

        await expect(getAdminOrders({ page: 1, pageSize: 20 })).resolves.toEqual(emptyResponse)
    })

    it('keeps multiple malformed optional Affiliate rows readable with row-level warnings', async () => {
        const response = {
            ...mixedFifteenOrderResponse,
            items: [
                {
                    ...realAffiliateOrder,
                    affiliateAttribution: {
                        ...realAffiliateOrder.affiliateAttribution,
                        attributionId: 'not-a-guid',
                    },
                },
                {
                    ...makeStorefrontOrder(18),
                    affiliateAttribution: {
                        ...realAffiliateOrder.affiliateAttribution,
                        orderId: makeStorefrontOrder(18).orderId,
                        referralCode: '',
                    },
                },
                ...Array.from({ length: 13 }, (_, index) => makeStorefrontOrder(index + 1)),
            ],
        }
        apiGetMock.mockResolvedValue({ data: response })

        const result = await getAdminOrders({ page: 1, pageSize: 20 })

        expect(result.items).toHaveLength(15)
        expect(result.items.slice(0, 2)).toEqual([
            expect.objectContaining({
                orderId: realAffiliateOrder.orderId,
                affiliateAttribution: null,
                affiliateAttributionWarning: expect.objectContaining({ code: 'AFFILIATE_ATTRIBUTION_UNAVAILABLE' }),
            }),
            expect.objectContaining({
                orderId: makeStorefrontOrder(18).orderId,
                affiliateAttribution: null,
                affiliateAttributionWarning: expect.objectContaining({ code: 'AFFILIATE_ATTRIBUTION_UNAVAILABLE' }),
            }),
        ])
    })

    it.each([500, 503])('maps an API %i without exposing its raw response', async (status) => {
        apiGetMock.mockRejectedValue({
            response: {
                status,
                data: { message: 'SQL exception: SELECT * FROM Orders' },
                headers: {},
            },
            config: { method: 'get' },
        })

        await expect(getAdminOrders({ page: 1, pageSize: 20 })).rejects.toMatchObject({
            message: 'Service is temporarily unavailable. Please try again.',
            status,
            code: `HTTP_${status}`,
        })
    })

    it('distinguishes a network failure from a client authority rejection', async () => {
        apiGetMock.mockRejectedValue(Object.assign(new Error('connect ECONNREFUSED with secret endpoint'), {
            config: { method: 'get', url: '/admin/orders' },
        }))

        await expect(getAdminOrders({ page: 1, pageSize: 20 })).rejects.toMatchObject({
            message: 'Unable to load tenant orders right now.',
            status: null,
            code: 'ORDER_API_UNAVAILABLE',
        })
    })

    it('rejects stale cross-tenant response data and accepts a current response on retry', async () => {
        apiGetMock
            .mockResolvedValueOnce({
                data: {
                    ...mixedFifteenOrderResponse,
                    items: [{
                        ...makeStorefrontOrder(1),
                        tenantId: '99999999-9999-4999-8999-999999999999',
                    }],
                },
            })
            .mockResolvedValueOnce({ data: mixedFifteenOrderResponse })

        await expect(getAdminOrders({ page: 1, pageSize: 20 })).rejects.toMatchObject({
            message: 'Orders response could not be verified. Please retry or sign in again.',
            code: 'ORDER_CLIENT_AUTHORITY_REJECTED',
        })
        await expect(getAdminOrders({ page: 1, pageSize: 20 })).resolves.toEqual(mixedFifteenOrderResponse)
        expect(apiGetMock).toHaveBeenCalledTimes(2)
    })

    it('preserves a safe backend machine code, status, and trace id', () => {
        const error = mapAdminOrdersError({
            response: {
                status: 403,
                data: {
                    code: 'ORDER_ACCESS_DENIED',
                    message: 'Internal policy detail must not be exposed.',
                    traceId: 'trace-403',
                },
                headers: {},
            },
        }, 'Unable to load tenant orders right now.')

        expect(error.message).toBe('You do not have permission to perform this action.')
        expect(error.status).toBe(403)
        expect(error.code).toBe('ORDER_ACCESS_DENIED')
        expect(error.traceId).toBe('trace-403')
    })

    it('classifies an unavailable API without leaking a backend response body', () => {
        const error = mapAdminOrdersError({
            response: {
                status: 503,
                data: { message: 'SQL exception: SELECT * FROM Orders' },
                headers: { 'x-correlation-id': 'corr-503' },
            },
        }, 'Unable to load tenant orders right now.')

        expect(error.message).toBe('Service is temporarily unavailable. Please try again.')
        expect(error.status).toBe(503)
        expect(error.code).toBe('HTTP_503')
        expect(error.traceId).toBe('corr-503')
    })

    it('retains deterministic client authority failures as a distinct machine code', () => {
        const error = mapAdminOrdersError(
            new Error('Orders response failed tenant authority validation.'),
            'Unable to load tenant orders right now.',
        )

        expect(error.message).toBe('Orders response could not be verified. Please retry or sign in again.')
        expect(error.code).toBe('ORDER_CLIENT_AUTHORITY_REJECTED')
        expect(error.status).toBeNull()
        expect(error.traceId).toBeNull()
    })
})
