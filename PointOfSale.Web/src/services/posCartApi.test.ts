import { beforeEach, describe, expect, it, vi } from 'vitest'

const apiGetMock = vi.hoisted(() => vi.fn())
const apiPostMock = vi.hoisted(() => vi.fn())
const apiPutMock = vi.hoisted(() => vi.fn())
const apiDeleteMock = vi.hoisted(() => vi.fn())

vi.mock('./api', () => ({
    default: {
        get: apiGetMock,
        post: apiPostMock,
        put: apiPutMock,
        delete: apiDeleteMock,
    },
}))

import { getOpenPosCart, isPosCartId, synchronizePosCart } from './posCartApi'

const cartId = '11111111-1111-4111-8111-111111111111'
const warehouseId = '22222222-2222-4222-8222-222222222222'
const productId = '33333333-3333-4333-8333-333333333333'
const variantId = '44444444-4444-4444-8444-444444444444'
const unitId = '55555555-5555-4555-8555-555555555555'

const makeCart = (overrides: Record<string, unknown> = {}) => ({
    cartId,
    warehouseId,
    customerId: null,
    status: 0,
    version: 1,
    expiresAtUtc: '2099-01-01T00:00:00Z',
    items: [],
    ...overrides,
})

const desiredItem = {
    productId,
    variantId,
    unitId,
    quantity: 2,
    discountAmount: 3,
}

describe('posCartApi', () => {
    beforeEach(() => {
        vi.clearAllMocks()
    })

    it('accepts only backend-compatible GUID cart identifiers', () => {
        expect(isPosCartId(cartId)).toBe(true)
        expect(isPosCartId('pos-cart-1788986721814-yvc8po6q')).toBe(false)
        expect(isPosCartId('')).toBe(false)
    })

    it('treats the successful empty open-cart response as no active cart', async () => {
        apiGetMock.mockResolvedValueOnce({ data: null })

        await expect(getOpenPosCart()).resolves.toBeNull()
        expect(apiGetMock).toHaveBeenCalledWith('/pos-carts/open', { signal: undefined })
    })

    it('adds the exact product, variant, unit, quantity, and discount to a persisted cart', async () => {
        apiGetMock.mockResolvedValueOnce({ data: makeCart() })
        apiPostMock.mockResolvedValueOnce({
            data: makeCart({
                version: 2,
                items: [{
                    itemId: '66666666-6666-4666-8666-666666666666',
                    productId,
                    productVariantId: variantId,
                    unitId,
                    quantity: 2,
                    discountAmount: 3,
                }],
            }),
        })

        const result = await synchronizePosCart({
            cartId,
            warehouseId,
            items: [desiredItem],
        })

        expect(result.version).toBe(2)
        expect(apiPostMock).toHaveBeenCalledWith(
            `/pos-carts/${cartId}/items`,
            {
                productId,
                productVariantId: variantId,
                unitId,
                quantity: 2,
                discountAmount: 3,
                expectedVersion: 1,
            },
            expect.objectContaining({
                headers: expect.objectContaining({ 'Idempotency-Key': expect.any(String) }),
            }),
        )
    })

    it('updates a matching line and removes a server line absent from the browser cart', async () => {
        const keptItem = {
            itemId: '66666666-6666-4666-8666-666666666666',
            productId,
            productVariantId: variantId,
            unitId,
            quantity: 1,
            discountAmount: 0,
        }
        const removedItem = {
            itemId: '77777777-7777-4777-8777-777777777777',
            productId: '88888888-8888-4888-8888-888888888888',
            productVariantId: '99999999-9999-4999-8999-999999999999',
            unitId,
            quantity: 1,
            discountAmount: 0,
        }
        apiGetMock.mockResolvedValueOnce({ data: makeCart({ items: [keptItem, removedItem] }) })
        apiPutMock.mockResolvedValueOnce({
            data: makeCart({ version: 2, items: [{ ...keptItem, quantity: 2, discountAmount: 3 }, removedItem] }),
        })
        apiDeleteMock.mockResolvedValueOnce({
            data: makeCart({ version: 3, items: [{ ...keptItem, quantity: 2, discountAmount: 3 }] }),
        })

        const result = await synchronizePosCart({ cartId, warehouseId, items: [desiredItem] })

        expect(apiPutMock).toHaveBeenCalledWith(
            `/pos-carts/${cartId}/items/${keptItem.itemId}`,
            { quantity: 2, discountAmount: 3, expectedVersion: 1 },
            expect.any(Object),
        )
        expect(apiDeleteMock).toHaveBeenCalledWith(
            `/pos-carts/${cartId}/items/${removedItem.itemId}`,
            expect.objectContaining({ data: expect.objectContaining({ expectedVersion: 2 }) }),
        )
        expect(result.items).toHaveLength(1)
    })

    it('cancels a cart from a different warehouse and creates a replacement in the selected context', async () => {
        const wrongWarehouseCart = makeCart({ warehouseId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa' })
        apiGetMock.mockResolvedValueOnce({ data: wrongWarehouseCart })
        apiPostMock.mockImplementation(async (url: string) => {
            if (url.endsWith('/cancel')) {
                return { data: makeCart({ ...wrongWarehouseCart, status: 3, version: 2 }) }
            }
            if (url === '/pos-carts') {
                return { data: makeCart() }
            }
            return {
                data: makeCart({
                    version: 2,
                    items: [{
                        itemId: '66666666-6666-4666-8666-666666666666',
                        productId,
                        productVariantId: variantId,
                        unitId,
                        quantity: 2,
                        discountAmount: 3,
                    }],
                }),
            }
        })

        await synchronizePosCart({ cartId, warehouseId, items: [desiredItem] })

        expect(apiPostMock).toHaveBeenCalledWith(
            `/pos-carts/${cartId}/cancel`,
            expect.objectContaining({ expectedVersion: 1 }),
            expect.any(Object),
        )
        expect(apiPostMock).toHaveBeenCalledWith(
            '/pos-carts',
            expect.objectContaining({ warehouseId }),
            expect.any(Object),
        )
    })
})
