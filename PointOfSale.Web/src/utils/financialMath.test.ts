import { describe, expect, it } from 'vitest'
import {
    assertTwoDecimalCurrency,
    divide,
    normalizeCurrencyCode,
    parseStrictAmountInput,
    roundCurrency,
    toCurrencyMinorUnits,
} from './financialMath'

describe('normalizeCurrencyCode', () => {
    it('maps known currency symbols to ISO currency codes', () => {
        expect(normalizeCurrencyCode('$')).toBe('USD')
        expect(normalizeCurrencyCode('€')).toBe('EUR')
    })
})

describe('assertTwoDecimalCurrency', () => {
    it('returns the normalized code for supported two-decimal currencies', () => {
        expect(assertTwoDecimalCurrency(' usd ')).toBe('USD')
        expect(assertTwoDecimalCurrency('$')).toBe('USD')
    })

    it('rejects non-2-decimal currencies that were previously missed by the blacklist', () => {
        expect(() => assertTwoDecimalCurrency('JOD')).toThrow(
            'Unsupported currency "JOD" (3 decimals). Only 2-decimal currencies are supported in this financial engine.',
        )
    })
})

describe('currency rounding helpers', () => {
    it('rounds currency half values away from zero', () => {
        expect(roundCurrency(9.995)).toBe(10)
        expect(roundCurrency(-9.995)).toBe(-10)
    })

    it('converts currency values to minor units using half-away-from-zero rounding', () => {
        expect(toCurrencyMinorUnits(1.005)).toBe(101)
        expect(toCurrencyMinorUnits(-1.005)).toBe(-101)
    })
})

describe('parseStrictAmountInput', () => {
    it('rejects blank, exponent, comma, negative, and over-precision values', () => {
        expect(parseStrictAmountInput('')).toMatchObject({ isValid: false, reason: 'required' })
        expect(parseStrictAmountInput('1e3')).toMatchObject({ isValid: false, reason: 'invalid_format' })
        expect(parseStrictAmountInput('1,000')).toMatchObject({ isValid: false, reason: 'invalid_format' })
        expect(parseStrictAmountInput('-1')).toMatchObject({ isValid: false, reason: 'negative' })
        expect(parseStrictAmountInput('10.999')).toMatchObject({ isValid: false, reason: 'too_many_decimals' })
    })

    it('accepts zero only when explicitly allowed', () => {
        expect(parseStrictAmountInput('0', { allowZero: true })).toMatchObject({ isValid: true, value: 0 })
        expect(parseStrictAmountInput('0', { allowZero: false })).toMatchObject({ isValid: false, reason: 'zero' })
    })
})

describe('divide', () => {
    it('divides two valid numbers', () => {
        expect(divide(10, 4)).toBe(2.5)
        expect(divide('9', '3')).toBe(3)
    })

    it('returns 0 when the divisor is zero', () => {
        expect(divide(10, 0)).toBe(0)
    })

    it('returns 0 when the divisor is invalid', () => {
        expect(divide(10, 'abc')).toBe(0)
        expect(divide(10, null)).toBe(0)
    })
})
