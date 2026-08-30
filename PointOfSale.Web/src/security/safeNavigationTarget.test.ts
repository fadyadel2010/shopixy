import { describe, expect, it } from 'vitest'
import { resolveSafeNavigationTarget } from './safeNavigationTarget'

describe('resolveSafeNavigationTarget', () => {
    it('preserves safe internal and HTTPS targets', () => {
        expect(resolveSafeNavigationTarget('/marketplace/products?q=phone')).toBe('/marketplace/products?q=phone')
        expect(resolveSafeNavigationTarget('https://shopixy.example/products')).toBe('https://shopixy.example/products')
    })

    it('rejects backslash and scheme-relative redirect targets', () => {
        expect(resolveSafeNavigationTarget('https:\\\\attacker.example', '#')).toBe('#')
        expect(resolveSafeNavigationTarget('//attacker.example', '#')).toBe('#')
    })

    it('rejects executable and credential-bearing URLs', () => {
        expect(resolveSafeNavigationTarget('javascript:alert(1)', '#')).toBe('#')
        expect(resolveSafeNavigationTarget('https://user:password@example.com', '#')).toBe('#')
    })
})
