import sectionAuthority from '@theme-authority/shopixy-theme-authority.json'

const FIELD_LABEL_OVERRIDES = {
    ctaText: 'CTA Text',
    ctaLink: 'CTA Link',
    backgroundImage: 'Background Image',
    backgroundColor: 'Background Color',
    overlayOpacity: 'Overlay Opacity',
    textColor: 'Text Color',
    maxWidth: 'Max Width',
    aspectRatio: 'Aspect Ratio',
    showProductCount: 'Show Product Count',
    showAddToCart: 'Add to Cart Button',
    showPrice: 'Show Price',
    showRating: 'Show Rating',
    showBadge: 'Show Badge',
    badgeText: 'Badge Text',
    showName: 'Show Name',
    videoUrl: 'Video URL',
    showControls: 'Show Controls',
    showDescription: 'Show Description',
    showVariantSelector: 'Show Variant Selector',
    showSku: 'Show SKU',
    showStockStatus: 'Show Stock Status',
    showThumbnails: 'Show Thumbnails',
    maxImages: 'Max Images',
    useDescription: 'Use Product Description',
    productIds: 'Product IDs',
    categoryIds: 'Category IDs',
}

function toTitleCase(value) {
    return value
        .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
        .replace(/[-_]+/g, ' ')
        .replace(/\b\w/g, (character) => character.toUpperCase())
}

function createOptionLabel(value) {
    return toTitleCase(String(value))
}

function toBuilderField(field) {
    const label = FIELD_LABEL_OVERRIDES[field.key] || toTitleCase(field.key)
    const type = field.type === 'url' ? 'text' : field.type
    const builderField = {
        key: field.key,
        type,
        label,
        defaultValue: field.defaultValue,
    }

    if (typeof field.min === 'number') {
        builderField.min = field.min
    }

    if (typeof field.max === 'number') {
        builderField.max = field.max
    }

    if (typeof field.rows === 'number') {
        builderField.rows = field.rows
    }

    if (Array.isArray(field.options)) {
        builderField.options = field.options.map((option) => ({
            value: option,
            label: createOptionLabel(option),
        }))
    }

    return builderField
}

export const sectionContracts = sectionAuthority.sectionContracts

export function getSectionContract(sectionType) {
    return sectionContracts.find((contract) => contract.type === sectionType)
}

export function getContractDefaultSettings(sectionType) {
    const contract = getSectionContract(sectionType)
    return contract ? JSON.parse(JSON.stringify(contract.defaultSettings)) : {}
}

export function getContractSettingsSchema(sectionType) {
    const contract = getSectionContract(sectionType)
    return contract ? contract.fields.map(toBuilderField) : []
}

export function materializeContractSettings(sectionType, settings = {}) {
    return {
        ...getContractDefaultSettings(sectionType),
        ...settings,
    }
}

export function validateContractSettingsStrict(sectionType, settings = {}) {
    const contract = getSectionContract(sectionType)
    if (!contract) {
        throw new Error(`Unknown section type "${sectionType}".`)
    }

    const fieldsByKey = new Map(contract.fields.map((field) => [field.key, field]))

    Object.keys(settings).forEach((key) => {
        if (!fieldsByKey.has(key)) {
            throw new Error(`Section "${sectionType}" has unsupported setting "${key}".`)
        }
    })

    contract.fields.forEach((field) => {
        if (!Object.prototype.hasOwnProperty.call(settings, field.key)) {
            throw new Error(`Section "${sectionType}" is missing required setting "${field.key}".`)
        }

        validateFieldValue(sectionType, field, settings[field.key])
    })
}

export function validateSectionsAgainstContract(sections = []) {
    sections.forEach((section, index) => {
        const sectionLabel = section?.id || `index ${index}`
        if (!section || typeof section !== 'object') {
            throw new Error(`Section ${sectionLabel} must be an object.`)
        }

        if (!section.type) {
            throw new Error(`Section ${sectionLabel} is missing type.`)
        }

        validateContractSettingsStrict(section.type, section.settings || {})
    })
}

function validateFieldValue(sectionType, field, value) {
    if (value === null || value === undefined) {
        throw new Error(`Section "${sectionType}" setting "${field.key}" is required.`)
    }

    if (field.type === 'toggle' && typeof value !== 'boolean') {
        throwTypeError(sectionType, field, 'boolean')
    }

    if (field.type === 'number') {
        if (typeof value !== 'number' || !Number.isFinite(value)) {
            throwTypeError(sectionType, field, 'number')
        }

        if (typeof field.min === 'number' && value < field.min) {
            throw new Error(`Section "${sectionType}" setting "${field.key}" is below minimum ${field.min}.`)
        }

        if (typeof field.max === 'number' && value > field.max) {
            throw new Error(`Section "${sectionType}" setting "${field.key}" is above maximum ${field.max}.`)
        }
    }

    if (['text', 'textarea', 'url', 'image', 'video', 'color', 'select'].includes(field.type) && typeof value !== 'string') {
        throwTypeError(sectionType, field, 'string')
    }

    if (field.type === 'guid-list' && (!Array.isArray(value) || value.some((item) => typeof item !== 'string'))) {
        throwTypeError(sectionType, field, 'string array')
    }

    if ((field.type === 'image-list' || field.type === 'list') && !Array.isArray(value)) {
        throwTypeError(sectionType, field, 'array')
    }

    if (Array.isArray(field.options) && field.options.length > 0 && typeof value === 'string' && !field.options.includes(value)) {
        throw new Error(`Section "${sectionType}" setting "${field.key}" has invalid value "${value}".`)
    }
}

function throwTypeError(sectionType, field, expectedType) {
    throw new Error(`Section "${sectionType}" setting "${field.key}" must be ${expectedType}.`)
}
