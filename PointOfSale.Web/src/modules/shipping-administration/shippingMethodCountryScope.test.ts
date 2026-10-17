import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'
import {
    SHIPPING_METHOD_COUNTRY_CODES,
    getShippingMethodCountryOptions,
    parseShippingMethodCountryScope,
    serializeShippingMethodCountryScope,
} from './shippingMethodCountryScope'

describe('shared shipping method country scope', () => {
    it('uses exactly the assigned ISO codes from the existing Core country authority', () => {
        const coreSource = readFileSync(resolve(process.cwd(), '../PointOfSale.Core/Helpers/CountryCodeHelper.cs'), 'utf8')
        const isoDeclaration = coreSource.split('private static readonly HashSet<string> ValidIsoCodes')[1].split('};')[0]
        const canonicalCodes = Array.from(isoDeclaration.matchAll(/"([A-Z]{2})"/g), (match) => match[1])
        expect(canonicalCodes).toHaveLength(249)
        expect(SHIPPING_METHOD_COUNTRY_CODES).toEqual(canonicalCodes)
        expect(new Set(SHIPPING_METHOD_COUNTRY_CODES).size).toBe(249)
        for (const code of canonicalCodes) {
            expect(parseShippingMethodCountryScope(JSON.stringify([code]))).toEqual([code])
        }
    })

    it('round-trips selected countries as the existing JSON string, and only null/omitted as all', () => {
        expect(parseShippingMethodCountryScope('[ "EG", "SA", "AE" ]')).toEqual(['EG', 'SA', 'AE'])
        expect(serializeShippingMethodCountryScope(['EG', 'SA'])).toBe('["EG","SA"]')
        expect(parseShippingMethodCountryScope(null)).toBeNull()
        expect(parseShippingMethodCountryScope(undefined)).toBeNull()
        expect(serializeShippingMethodCountryScope(null)).toBeNull()
        expect(() => serializeShippingMethodCountryScope([])).toThrow()
        expect(() => serializeShippingMethodCountryScope(['ZZ'])).toThrow()
    })

    it.each([
        '', ' ', 'not-json', 'null', '{}', 'true', '"EG"', '[]', '[null]', '[1]', '[[]]',
        '["EG",null]', '["EG","EG"]', '["ZZ"]', '["XX"]', '["UK"]', '["EU"]', '["XK"]',
        '["Egypt"]', '["eg"]', '[" EG "]', '["EG",]',
    ])('rejects malformed or non-ISO persisted scope %j instead of interpreting it as all', (scope) => {
        expect(() => parseShippingMethodCountryScope(scope)).toThrow()
    })

    it.each([[], ['EG'], {}, 1, false].map((scope) => ({ scope })))('rejects non-string DTO evidence $scope', ({ scope }) => {
        expect(() => parseShippingMethodCountryScope(scope)).toThrow()
    })

    it('localizes labels without using Intl region labels as an ISO validator', () => {
        const en = getShippingMethodCountryOptions('en')
        const ar = getShippingMethodCountryOptions('ar')
        expect(en.find(({ code }) => code === 'EG')?.label).toBe('Egypt (EG)')
        expect(ar.find(({ code }) => code === 'EG')?.label).toBe('مصر (EG)')
        expect(en.map(({ code }) => code)).toEqual(SHIPPING_METHOD_COUNTRY_CODES)
        expect(ar.map(({ code }) => code)).toEqual(SHIPPING_METHOD_COUNTRY_CODES)
    })
})
