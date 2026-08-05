const PRODUCT_CONTEXT_SECTION_TYPES = new Set([
    'product-gallery',
    'product-specs',
    'product-video',
    'related-products',
])

export function isRuntimeOwnedSection(sectionType) {
    return sectionType === 'product-hero'
}

export function isSectionAvailableForPage(sectionType, pageKey = 'home') {
    if (!sectionType) {
        return false
    }

    if (isRuntimeOwnedSection(sectionType)) {
        return false
    }

    if (PRODUCT_CONTEXT_SECTION_TYPES.has(sectionType)) {
        return pageKey === 'product-template'
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
