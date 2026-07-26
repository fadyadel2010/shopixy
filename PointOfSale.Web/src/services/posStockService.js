import api from './api'
import { toSafeNumber } from '../utils/financialMath'
import { readPosRuntimeCache, POS_RUNTIME_CACHE_TTL_MS } from './posRuntimeCache'

const getCartItemKey = (item) => `${item?.productId}::${item?.variantId || item?.productVariantId || 'default'}`

const buildRequestItems = (items) => {
    const requestItems = new Map()

    for (const item of Array.isArray(items) ? items : []) {
        const productId = item?.productId
        if (!productId) {
            continue
        }

        const variantId = item?.variantId || item?.productVariantId || null
        const key = getCartItemKey(item)
        const quantity = Math.max(0, toSafeNumber(item?.quantity))
        const conversionFactor = Math.max(1, toSafeNumber(item?.conversionFactor, 1))
        const requestedBaseQuantity = quantity * conversionFactor

        if (!requestItems.has(key)) {
            requestItems.set(key, {
                key,
                productId,
                variantId,
                requestedBaseQuantity,
            })
            continue
        }

        requestItems.get(key).requestedBaseQuantity += requestedBaseQuantity
    }

    return [...requestItems.values()]
}

const getWarehouseAvailableQuantity = (stockLevels, warehouseId) => {
    const matchingStock = Array.isArray(stockLevels)
        ? stockLevels.find(stockLevel => stockLevel?.warehouseId === warehouseId)
        : null

    return Math.max(0, toSafeNumber(
        matchingStock?.availableQuantity ?? matchingStock?.quantity,
    ))
}

export const loadPosItemStock = async ({ productId, variantId, warehouseId, signal, force = false }) => {
    const endpoint = variantId
        ? `/products/${productId}/variants/${variantId}/stock`
        : `/products/${productId}/stock`
    const cacheKey = `pos-stock:${warehouseId}:${productId}:${variantId || 'default'}`

    return readPosRuntimeCache(
        cacheKey,
        async () => {
            const response = await api.get(endpoint, { signal })

            return {
                productId,
                variantId,
                stockQuantity: getWarehouseAvailableQuantity(response.data, warehouseId),
            }
        },
        {
            ttlMs: POS_RUNTIME_CACHE_TTL_MS,
            force,
        },
    )
}

export const revalidatePosCartStock = async ({ items = [], warehouseId, signal, baselineItems = [] }) => {
    const requestItems = buildRequestItems(items)
    const baselineByKey = new Map(
        buildRequestItems(baselineItems).map(item => [item.key, item.requestedBaseQuantity]),
    )
    const stockLevels = await Promise.all(
        requestItems.map(item => loadPosItemStock({
            productId: item.productId,
            variantId: item.variantId,
            warehouseId,
            signal,
            force: true,
        })),
    )

    const stockByKey = new Map(
        stockLevels.map(stockLevel => [
            `${stockLevel.productId}::${stockLevel.variantId || 'default'}`,
            stockLevel.stockQuantity,
        ]),
    )

    const effectiveStockLevels = stockLevels.map(stockLevel => {
        const itemKey = `${stockLevel.productId}::${stockLevel.variantId || 'default'}`
        return {
            ...stockLevel,
            stockQuantity: Math.max(0, toSafeNumber(stockLevel.stockQuantity)) + Math.max(0, toSafeNumber(baselineByKey.get(itemKey))),
        }
    })

    const mismatches = requestItems.reduce((results, item) => {
        const baselineQuantity = Math.max(0, toSafeNumber(baselineByKey.get(item.key)))
        const availableStock = Math.max(0, toSafeNumber(stockByKey.get(item.key)))
        const requiredAdditionalQuantity = Math.max(0, item.requestedBaseQuantity - baselineQuantity)

        if (requiredAdditionalQuantity > availableStock) {
            results.push({
                ...item,
                availableStock,
                baselineQuantity,
                requiredAdditionalQuantity,
            })
        }

        return results
    }, [])

    return {
        ok: mismatches.length === 0,
        stockLevels,
        effectiveStockLevels,
        mismatches,
    }
}
