export const PORTAL_SORT_OPTIONS = [
    { value: 'ranking', label: 'Best Match' },
    { value: 'commission_desc', label: 'Highest Commission' },
    { value: 'commission_asc', label: 'Lowest Commission' },
    { value: 'newest', label: 'Newest First' },
    { value: 'price_desc', label: 'Price: High to Low' },
    { value: 'price_asc', label: 'Price: Low to High' },
]
const VALID_SORTS = new Set(PORTAL_SORT_OPTIONS.map(option => option.value))
export const DEFAULT_SORT = 'commission_desc'
export function isValidSort(sort) {
    return VALID_SORTS.has(sort)
}
