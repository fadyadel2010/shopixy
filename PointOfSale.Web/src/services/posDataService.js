import api from './api'
import { readPosRuntimeCache, POS_RUNTIME_CACHE_TTL_MS } from './posRuntimeCache'

const normalizeSearchQuery = (value) => value.trim().toLowerCase()

export const searchPosProducts = async ({ query, warehouseId, signal, force = false }) => {
    const trimmedQuery = query.trim()
    const cacheKey = `product-search:${warehouseId}:${normalizeSearchQuery(trimmedQuery)}`

    return readPosRuntimeCache(
        cacheKey,
        async () => {
            const response = await api.get('/products/search', {
                params: {
                    q: trimmedQuery,
                    warehouseId,
                },
                signal,
            })

            return Array.isArray(response.data) ? response.data : []
        },
        {
            ttlMs: POS_RUNTIME_CACHE_TTL_MS,
            force,
        },
    )
}

export const getQuickAccessProducts = async ({ warehouseId, limit = 12, signal, force = false }) => {
    const cacheKey = `quick-access:${warehouseId}:${limit}`

    return readPosRuntimeCache(
        cacheKey,
        async () => {
            const response = await api.get('/products/quick-access', {
                params: {
                    warehouseId,
                    limit,
                },
                signal,
            })

            return Array.isArray(response.data) ? response.data : []
        },
        {
            ttlMs: POS_RUNTIME_CACHE_TTL_MS,
            force,
        },
    )
}

export const getWarehouseStockLevels = async ({ warehouseId, signal, force = false }) => {
    const cacheKey = `warehouse-stock:${warehouseId}`

    return readPosRuntimeCache(
        cacheKey,
        async () => {
            const response = await api.get(`/warehouses/${warehouseId}/stock`, { signal })
            return Array.isArray(response.data) ? response.data : []
        },
        {
            ttlMs: POS_RUNTIME_CACHE_TTL_MS,
            force,
        },
    )
}

export const getWarehouseOpeningVariants = async ({ warehouseId, signal, force = false }) => {
    const cacheKey = `warehouse-opening-variants:${warehouseId}`

    return readPosRuntimeCache(
        cacheKey,
        async () => {
            const response = await api.get('/warehouses/opening-stock', { params: { warehouseId }, signal })
            return Array.isArray(response.data) ? response.data : []
        },
        {
            ttlMs: POS_RUNTIME_CACHE_TTL_MS,
            force,
        },
    )
}
