import { describe, expect, it } from 'vitest'
import { fallbackVariantSku, normalizeSkuPart, productSkuBase } from './productSku'

describe('product SKU namespace', () => {
    it('preserves English and mixed-name ASCII normalization', () => {
        expect(normalizeSkuPart('T Shirt')).toBe('T-Shirt')
        expect(normalizeSkuPart('حاسوب Pro 16')).toBe('Pro-16')
        expect(normalizeSkuPart('حاسوب')).toBe('')
        expect(normalizeSkuPart('!!!')).toBe('')
    })

    it('uses the full unique product identity and supports more than 99 variants', () => {
        const base = productSkuBase('aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', crypto.randomUUID())
        expect([0, 1, 2, 99].map(index => fallbackVariantSku(base, index))).toEqual([
            `${base}-V01`, `${base}-V02`, `${base}-V03`, `${base}-V100`,
        ])
        expect(productSkuBase('bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb', crypto.randomUUID())).not.toBe(base)
    })
})
