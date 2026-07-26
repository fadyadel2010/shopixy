import api from './api'

const toUniqueProductIds = (productIds) => (
    [...new Set(
        (Array.isArray(productIds) ? productIds : [])
            .filter(Boolean),
    )]
)

export async function loadProductUnitsMap(productIds, { signal } = {}) {
    const uniqueProductIds = toUniqueProductIds(productIds)
    if (uniqueProductIds.length === 0) {
        return {}
    }

    const params = new URLSearchParams()
    uniqueProductIds.forEach(productId => params.append('productIds', productId))

    const response = await api.get(`/products/units/batch?${params.toString()}`, { signal })
    return response.data ?? {}
}

export function getProductUnitsForProduct(unitsByProductId, productId) {
    const productUnits = unitsByProductId?.[productId]
    return Array.isArray(productUnits) ? productUnits : []
}
