import { describe, expect, it } from 'vitest'
import { isStoreSlugFormatValid, normalizeStoreSlug } from './storeSlug'

describe('registration store slug contract', () => {
    it.each([
        'shopixy',
        'shopixy2',
        'shopixy-store',
        '123',
    ])('accepts host-safe value %s', (value) => {
        expect(isStoreSlugFormatValid(value)).toBe(true)
    })

    it.each([
        'متجر',
        'my store',
        'my_store',
        'my.store',
        'my/store',
        'my\\store',
        'my@store',
        'my:store',
        'my?store',
        'my#store',
        '-store',
        'store-',
        'my--store',
        'ab',
    ])('rejects host-unsafe value %s', (value) => {
        expect(isStoreSlugFormatValid(value)).toBe(false)
    })

    it('normalizes case and surrounding whitespace', () => {
        expect(normalizeStoreSlug('  Shopixy-Store  ')).toBe('shopixy-store')
    })
})
