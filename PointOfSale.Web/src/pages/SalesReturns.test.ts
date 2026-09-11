import { describe, expect, it } from 'vitest'
import { calculateReturnTotals, roundQuantity } from './SalesReturns'

describe('Sales Returns financial calculations', () => {
    it('prorates invoice discount using subtotal after line discounts', () => {
        const result = calculateReturnTotals([
            {
                quantity: 1,
                originalQuantity: 1,
                previouslyReturnedQuantity: 0,
                originalUnitPrice: 100,
                originalDiscountAmount: 50,
                originalTaxAmount: 0,
            },
            {
                quantity: 0,
                originalQuantity: 1,
                previouslyReturnedQuantity: 0,
                originalUnitPrice: 100,
                originalDiscountAmount: 0,
                originalTaxAmount: 0,
            },
        ], {
            subtotal: 200,
            discountAmount: 30,
        })

        expect(result.lines[0].invoiceDiscount).toBe(10)
        expect(result.subtotal).toBe(40)
        expect(result.total).toBe(40)
    })

    it('rounds return quantities to six decimal places', () => {
        expect(roundQuantity(1.1234567)).toBe(1.123457)
        expect(roundQuantity(0.0000004)).toBe(0)
    })
})
