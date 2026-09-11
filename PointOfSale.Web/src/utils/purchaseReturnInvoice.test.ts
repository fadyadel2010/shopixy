import { describe, expect, it } from 'vitest'
import { mapPurchaseReturnInvoiceItem } from './purchaseReturnInvoice'

describe('mapPurchaseReturnInvoiceItem', () => {
    it('uses the return-specific scalar contract and remaining quantity', () => {
        const mapped = mapPurchaseReturnInvoiceItem({
            originalInvoiceItemIds: ['line-1'],
            productId: 'product-1',
            productName: 'Coffee',
            productVariantId: 'variant-1',
            variantSku: 'COFFEE-DARK-1KG',
            variantAttributes: [
                { name: 'Roast', value: 'Dark' },
                { name: 'Size', value: '1 kg' },
            ],
            unitId: 'unit-1',
            unitName: 'Carton',
            purchasedQuantity: 3,
            returnedQuantity: 1,
            remainingQuantity: 2,
            unitPrice: 100,
            taxRate: 10,
        }, 'Piece')

        expect(mapped.name).toBe('Coffee')
        expect(mapped.variantDisplayName).toBe('Coffee - Dark / 1 kg')
        expect(mapped.originalQuantity).toBe(2)
        expect(mapped.quantity).toBe(0)
        expect(mapped.unitName).toBe('Carton')
    })

    it('falls back to SKU and the default unit label', () => {
        const mapped = mapPurchaseReturnInvoiceItem({
            originalInvoiceItemIds: ['line-2'],
            productId: 'product-2',
            productName: 'Tea',
            productVariantId: 'variant-2',
            variantSku: 'TEA-DEFAULT',
            variantAttributes: null,
            unitId: null,
            unitName: null,
            purchasedQuantity: 5,
            returnedQuantity: 0,
            remainingQuantity: 5,
            unitPrice: 20,
            taxRate: 0,
        }, 'Piece')

        expect(mapped.variantDisplayName).toBe('Tea (TEA-DEFAULT)')
        expect(mapped.unitName).toBe('Piece')
    })
})
