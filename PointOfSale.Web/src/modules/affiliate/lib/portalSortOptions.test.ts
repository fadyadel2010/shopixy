import { expect, it } from 'vitest'
import { DEFAULT_SORT, isValidSort, PORTAL_SORT_OPTIONS } from './portalSortOptions'

it('shares the exact portal allowlist without changing the anonymous discovery boundary', () => {
    expect(DEFAULT_SORT).toBe('commission_desc')
    expect(PORTAL_SORT_OPTIONS.map(option => option.value)).toEqual(['ranking', 'commission_desc', 'commission_asc', 'newest', 'price_desc', 'price_asc'])
    for (const option of PORTAL_SORT_OPTIONS) expect(isValidSort(option.value)).toBe(true)
    expect(isValidSort('unknown')).toBe(false)
    expect(isValidSort(undefined)).toBe(false)
})
