import { describe, expect, it } from 'vitest'
import {
    buildPosCartSnapshotFingerprint,
    shouldApplyExternalPosCartSnapshot,
} from './posCartStorage'

const buildSnapshot = (cartId: string, quantity: number) => ({
    cartId,
    items: [
        {
            productId: 'product-1',
            variantId: null,
            unitId: null,
            name: 'Test Product',
            quantity,
            unitPrice: 10,
            discountAmount: 0,
            taxRate: 0,
        },
    ],
    customer: null,
    discountType: 'fixed',
    discountValue: 0,
    warehouseId: 'warehouse-1',
    editingSaleId: null,
    editingInvoiceNumber: null,
    editingOriginalItems: [],
})

describe('POS cart external snapshot safety', () => {
    it('applies an external cart when the local tab has no active cart', () => {
        const externalFingerprint = buildPosCartSnapshotFingerprint(buildSnapshot('cart-1', 1))

        expect(shouldApplyExternalPosCartSnapshot({
            localHasActiveCart: false,
            localFingerprint: buildPosCartSnapshotFingerprint(null),
            externalFingerprint,
        })).toBe(true)
    })

    it('allows identical external snapshots for an active local cart', () => {
        const fingerprint = buildPosCartSnapshotFingerprint(buildSnapshot('cart-1', 1))

        expect(shouldApplyExternalPosCartSnapshot({
            localHasActiveCart: true,
            localFingerprint: fingerprint,
            externalFingerprint: fingerprint,
        })).toBe(true)
    })

    it('blocks silent overwrite when another tab changes a different active cart', () => {
        const localFingerprint = buildPosCartSnapshotFingerprint(buildSnapshot('cart-1', 1))
        const externalFingerprint = buildPosCartSnapshotFingerprint(buildSnapshot('cart-2', 2))

        expect(shouldApplyExternalPosCartSnapshot({
            localHasActiveCart: true,
            localFingerprint,
            externalFingerprint,
        })).toBe(false)
    })
})
