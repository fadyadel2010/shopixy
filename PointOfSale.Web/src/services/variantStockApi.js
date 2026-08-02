import api from './api'

function createVariantStockMutationKey(productId, variantId, scope) {
    const randomPart = globalThis.crypto?.randomUUID?.() || `${Date.now()}-${Math.random().toString(36).slice(2)}`
    return `variant-stock:${productId}:${variantId}:${scope}:${randomPart}`
}

export function updateVariantStock(productId, variantId, warehouseId, payload, scope = 'stock') {
    return api.put(`/products/${productId}/variants/${variantId}/stock/${warehouseId}`, payload, {
        idempotencyKey: createVariantStockMutationKey(productId, variantId, scope)
    })
}
