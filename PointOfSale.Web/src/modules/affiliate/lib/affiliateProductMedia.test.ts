import { describe, expect, it } from 'vitest'
import { affiliateProductMediaUrl } from './affiliateProductMedia'

describe('Affiliate product media merchant authority', () => {
    it('resolves store-relative media against each authoritative merchant origin', () => {
        const path = '/api/upload/product-media/product/image.png'
        expect(affiliateProductMediaUrl(path, 'https://shop-a.example.test/products/a')).toBe('https://shop-a.example.test' + path)
        expect(affiliateProductMediaUrl(path, 'https://shop-b.example.test/products/b')).toBe('https://shop-b.example.test' + path)
    })
    it('preserves intentional external images and missing image data', () => {
        expect(affiliateProductMediaUrl('https://cdn.example.test/qa-screenshot.png', 'https://shop.example.test')).toBe('https://cdn.example.test/qa-screenshot.png')
        expect(affiliateProductMediaUrl('', 'https://shop.example.test')).toBe('')
        expect(affiliateProductMediaUrl(null, null)).toBe('')
    })
    it('never falls back to the portal origin if merchant URL authority is missing', () => {
        expect(affiliateProductMediaUrl('/api/upload/product-media/product/image.png', null)).toBe('')
    })
})
