/**
 * AF-39: Anonymous public discovery clients for the Affiliate Hub.
 *
 * CONTRACT:
 * - Targets the explicitly [AllowAnonymous] /api/affiliate/discover/public-products routes.
 * - Uses the bare axios package (no auth interceptors) so an Affiliate
 *   Authorization header is never attached, even when a portal session exists.
 * - Sort values are restricted to the backend public allowlist; commission
 *   ordering is impossible through this boundary.
 */
import axios from 'axios'

export const PUBLIC_PRODUCT_LIST_ROUTE = '/api/affiliate/discover/public-products'

export const PUBLIC_SORT_OPTIONS = [
    { value: 'newest', label: 'Newest First' },
    { value: 'price_desc', label: 'Price: High to Low' },
    { value: 'price_asc', label: 'Price: Low to High' },
    { value: 'name_asc', label: 'Name: A to Z' },
]

export const PUBLIC_SORT_VALUES = new Set(PUBLIC_SORT_OPTIONS.map(o => o.value))
export const DEFAULT_PUBLIC_SORT = 'newest'
const PUBLIC_DISCOVERY_TIMEOUT_MS = 15_000

export function isValidPublicSort(sort) {
    return typeof sort === 'string' && PUBLIC_SORT_VALUES.has(sort)
}

export function discoverPublicProducts({ page = 1, pageSize = 20, search, categorySlug, sort, signal } = {}) {
    return axios.get(PUBLIC_PRODUCT_LIST_ROUTE, {
        timeout: PUBLIC_DISCOVERY_TIMEOUT_MS,
        params: {
            page,
            pageSize,
            ...(search && { search }),
            ...(categorySlug && { categorySlug }),
            sort: isValidPublicSort(sort) ? sort : DEFAULT_PUBLIC_SORT,
        },
        ...(signal && { signal }),
    }).then(r => r.data)
}

export function discoverPublicProductDetail(productId, { signal } = {}) {
    return axios.get(`${PUBLIC_PRODUCT_LIST_ROUTE}/${encodeURIComponent(productId)}`, {
        timeout: PUBLIC_DISCOVERY_TIMEOUT_MS,
        ...(signal && { signal }),
    }).then(r => r.data)
}
