const PRODUCT_CONTEXT_SECTION_TYPES = new Set([
    'product-hero',
    'product-gallery',
    'product-specs',
    'product-video',
    'related-products',
])

const PRODUCT_PAGE_KEYS = new Set([
    'product',
    'product-template',
])

export function isSectionAvailableForPage(sectionType, pageKey = 'home') {
    if (!sectionType) {
        return false
    }

    if (PRODUCT_CONTEXT_SECTION_TYPES.has(sectionType)) {
        return PRODUCT_PAGE_KEYS.has(pageKey)
    }

    return true
}

export function filterSectionsForPage(sections = [], pageKey = 'home') {
    if (!Array.isArray(sections)) {
        return []
    }

    return sections
        .filter((section) => isSectionAvailableForPage(section?.type, pageKey))
        .map((section, index) => ({ ...section, order: index }))
}

export function filterSectionDefinitionsForPage(definitions = [], pageKey = 'home') {
    if (!Array.isArray(definitions)) {
        return []
    }

    return definitions.filter((definition) => isSectionAvailableForPage(definition?.type, pageKey))
}
