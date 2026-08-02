function normalizeText(value) {
    return `${value ?? ''}`.trim()
}

function normalizeVariantAttributesForAuthoring(attributes) {
    if (Array.isArray(attributes)) {
        return attributes.reduce((acc, attribute) => {
            const name = normalizeText(attribute?.name)
            if (name) {
                acc[name] = normalizeText(attribute?.value)
            }
            return acc
        }, {})
    }

    if (attributes && typeof attributes === 'object') {
        return Object.entries(attributes).reduce((acc, [name, value]) => {
            const normalizedName = normalizeText(name)
            if (normalizedName) {
                acc[normalizedName] = normalizeText(value)
            }
            return acc
        }, {})
    }

    return {}
}

function toNullableNumber(value) {
    if (value == null || value === '') {
        return null
    }

    return Number.parseFloat(value) || 0
}

export function toProductAuthoringVariant(variant, sortOrder = variant?.sortOrder) {
    return {
        variantId: variant?.variantId || variant?.id || null,
        sku: normalizeText(variant?.sku || variant?.SKU) || null,
        barcode: normalizeText(variant?.barcode) || null,
        price: Number.parseFloat(variant?.price) || 0,
        onlinePrice: toNullableNumber(variant?.onlinePrice),
        costPrice: Number.parseFloat(variant?.costPrice) || 0,
        weight: toNullableNumber(variant?.weight),
        mediaId: variant?.mediaId || null,
        imageUrl: variant?.mediaId ? null : normalizeText(variant?.imageUrl) || null,
        isActive: variant?.isActive !== false,
        isDefault: Boolean(variant?.isDefault),
        attributes: normalizeVariantAttributesForAuthoring(variant?.attributes),
        sortOrder
    }
}

export function normalizeProductAuthoringVariants(variants) {
    if (!Array.isArray(variants)) {
        return []
    }

    return variants.map((variant) => toProductAuthoringVariant(variant, variant?.sortOrder))
}
