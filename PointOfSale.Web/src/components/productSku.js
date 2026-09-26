export function normalizeSkuPart(value) {
    return String(value || '').trim()
        .replace(/[^a-z0-9]+/gi, '-')
        .replace(/^-+|-+$/g, '')
}

// A persisted product GUID is the preferred namespace. For an unsaved product,
// keep one cryptographically generated namespace for the lifetime of the draft.
export function productSkuBase(productId, draftId) {
    const id = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(productId || '')
        ? productId : draftId
    return `PX-${id.replace(/-/g, '').toUpperCase()}`
}

export function fallbackVariantSku(base, index) {
    return `${base}-V${String(index + 1).padStart(2, '0')}`
}
