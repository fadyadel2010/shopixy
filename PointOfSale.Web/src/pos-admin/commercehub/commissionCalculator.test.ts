import { describe, expect, it } from 'vitest'
import {
    calculateCommissionBreakdown,
    calculateRequiredPriceForTargetEarnings,
    roundCurrencyAmount,
} from './commissionCalculator'

describe('commissionCalculator', () => {
    it('calculates the financial breakdown with backend-aligned rounding', () => {
        expect(calculateCommissionBreakdown(6000, 0.05)).toEqual({
            price: 6000,
            rate: 0.05,
            commissionAmount: 300,
            vendorReceives: 5700,
        })
    })

    it('rounds currency away from zero at the half-cent boundary', () => {
        expect(roundCurrencyAmount(9.995)).toBe(10)
        expect(roundCurrencyAmount(-9.995)).toBe(-10)
    })

    it('calculates the required price for a target payout', () => {
        expect(calculateRequiredPriceForTargetEarnings(5700, 0.05)).toEqual({
            desiredEarnings: 5700,
            rate: 0.05,
            requiredPrice: 6000,
        })
    })

    it('returns null when the rate would make target pricing impossible', () => {
        expect(calculateRequiredPriceForTargetEarnings(100, 1)).toBeNull()
    })
})
