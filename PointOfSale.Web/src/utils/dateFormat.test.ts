import { describe, expect, it } from 'vitest'
import { formatCompactCurrency, formatCurrency, formatSignedCurrency } from './dateFormat'

describe('dateFormat currency helpers', () => {
    it('throws for unsupported currencies instead of silently relabeling them as EGP', () => {
        expect(() => formatCurrency(10, 'JOD', 'en-US')).toThrow('Unsupported currency "JOD"')
    })

    it('throws for unknown currencies across compact currency formatting too', () => {
        expect(() => formatCompactCurrency(10, 'INVALID', 'en-US')).toThrow('Unknown currency "INVALID"')
    })

    it('still uses the default currency when no currency is provided', () => {
        expect(formatCurrency(10, undefined, 'en-US')).toContain('10.00')
    })

    it('delegates signed currency rendering to Intl so bidi sign placement stays locale-aware', () => {
        const options = { currencyDisplay: 'code' }

        expect(formatSignedCurrency(10, 'USD', 'ar-EG', options)).toBe(
            formatCurrency(10, 'USD', 'ar-EG', {
                ...options,
                signDisplay: 'always',
            }),
        )
        expect(formatSignedCurrency(-10, 'USD', 'ar-EG', options)).toBe(
            formatCurrency(-10, 'USD', 'ar-EG', {
                ...options,
                signDisplay: 'always',
            }),
        )
    })
})
