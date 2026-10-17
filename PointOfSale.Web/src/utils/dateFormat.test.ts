import { describe, expect, it } from 'vitest'
import {
    formatCompactCurrency,
    formatCurrency,
    formatDateInputValue,
    formatSignedCurrency,
    parseDateInput,
} from './dateFormat'

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
        const options = { currencyDisplay: 'code' } satisfies Intl.NumberFormatOptions

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

describe('date input helpers', () => {
    it('round-trips a calendar date without UTC conversion', () => {
        const date = parseDateInput('2026-09-16')

        expect(date).not.toBeNull()
        expect(date?.getFullYear()).toBe(2026)
        expect(date?.getMonth()).toBe(8)
        expect(date?.getDate()).toBe(16)
        expect(formatDateInputValue(date)).toBe('2026-09-16')
    })

    it('rejects blank and impossible calendar dates', () => {
        expect(parseDateInput('')).toBeNull()
        expect(parseDateInput('2026-02-30')).toBeNull()
        expect(formatDateInputValue(new Date('invalid'))).toBe('')
    })
})
