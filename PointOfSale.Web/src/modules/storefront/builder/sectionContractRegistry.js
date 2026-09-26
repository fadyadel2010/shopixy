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
    showBadge: 'Show Badge',
    badgeText: 'Badge Text',
    videoUrl: 'Video URL',
    showControls: 'Show Controls',
    productIds: 'Product IDs',
    categoryIds: 'Category IDs',
}

const GUID_PATTERN = /^(?!00000000-0000-0000-0000-000000000000$)[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i
const HEX_COLOR_PATTERN = /^#(?:[0-9a-f]{3}|[0-9a-f]{6})$/i
const DEFAULT_TEXT_MAX_LENGTH = 500
const DEFAULT_LONG_TEXT_MAX_LENGTH = 5000
const DEFAULT_URL_MAX_LENGTH = 2048

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

    if (typeof field.maxItems === 'number') {
        builderField.maxItems = field.maxItems
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

    if (field.itemSchema && Array.isArray(field.itemSchema.fields)) {
        builderField.itemLabel = field.itemLabel || 'Item'
        builderField.defaultItem = JSON.parse(JSON.stringify(field.itemSchema.defaultValue || {}))
        builderField.fields = field.itemSchema.fields.map(toBuilderField)
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
            if (sectionType === 'hero-banner' && field.key === 'highlights') return
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

    if (['text', 'textarea', 'url', 'image', 'video', 'color', 'select'].includes(field.type)) {
        if (typeof value !== 'string') {
            throwTypeError(sectionType, field, 'string')
        }

        const maxLength = field.type === 'textarea'
            ? DEFAULT_LONG_TEXT_MAX_LENGTH
            : ['url', 'image', 'video'].includes(field.type)
                ? DEFAULT_URL_MAX_LENGTH
                : DEFAULT_TEXT_MAX_LENGTH
        if (value.length > maxLength) {
            throw new Error(`Section "${sectionType}" setting "${field.key}" exceeds maximum length ${maxLength}.`)
        }

        if (field.type === 'color' && !HEX_COLOR_PATTERN.test(value)) {
            throw new Error(`Section "${sectionType}" setting "${field.key}" must be a hex color.`)
        }

        if (['url', 'image', 'video'].includes(field.type) && !isSafeAuthoredUrl(value, field.type)) {
            throw new Error(`Section "${sectionType}" setting "${field.key}" must be a safe URL or relative path.`)
        }
    }

    if (field.type === 'guid-list') {
        if (!Array.isArray(value) || value.some((item) => typeof item !== 'string')) {
            throwTypeError(sectionType, field, 'string array')
        }

        validateListSize(sectionType, field, value)
        if (value.some((item) => !GUID_PATTERN.test(item))) {
            throw new Error(`Section "${sectionType}" setting "${field.key}" must contain valid GUID values.`)
        }

        if (new Set(value.map((item) => item.toLowerCase())).size !== value.length) {
            throw new Error(`Section "${sectionType}" setting "${field.key}" must not contain duplicate GUID values.`)
        }
    }

    if (field.type === 'image-list' || field.type === 'list') {
        if (!Array.isArray(value)) {
            throwTypeError(sectionType, field, 'array')
        }

        validateListSize(sectionType, field, value)
        if (field.type === 'image-list' && value.some((item) => typeof item !== 'string' || !isSafeAuthoredUrl(item, 'image'))) {
            throw new Error(`Section "${sectionType}" setting "${field.key}" must contain safe image URLs.`)
        }

        if (field.type === 'list') {
            validateNestedList(sectionType, field, value)
        }
    }

    if (Array.isArray(field.options) && field.options.length > 0 && typeof value === 'string' && !field.options.includes(value)) {
        throw new Error(`Section "${sectionType}" setting "${field.key}" has invalid value "${value}".`)
    }
}

function validateListSize(sectionType, field, value) {
    if (typeof field.maxItems === 'number' && value.length > field.maxItems) {
        throw new Error(`Section "${sectionType}" setting "${field.key}" exceeds maximum item count ${field.maxItems}.`)
    }
}

function validateNestedList(sectionType, field, value) {
    const itemFields = field.itemSchema?.fields
    if (!Array.isArray(itemFields)) {
        throw new Error(`Section "${sectionType}" setting "${field.key}" has no semantic item schema.`)
    }

    const fieldsByKey = new Map(itemFields.map((itemField) => [itemField.key, itemField]))
    value.forEach((item, index) => {
        if (!item || typeof item !== 'object' || Array.isArray(item)) {
            throw new Error(`Section "${sectionType}" setting "${field.key}" item ${index} must be an object.`)
        }

        Object.keys(item).forEach((key) => {
            if (!fieldsByKey.has(key)) {
                throw new Error(`Section "${sectionType}" setting "${field.key}" item ${index} contains unsupported property "${key}".`)
            }
        })

        itemFields.forEach((itemField) => {
            const itemValue = item[itemField.key]
            if (typeof itemValue !== 'string') {
                throw new Error(`Section "${sectionType}" setting "${field.key}" item ${index} property "${itemField.key}" must be string.`)
            }

            if (itemField.allowEmpty === false && itemValue.trim().length === 0) {
                throw new Error(`Section "${sectionType}" setting "${field.key}" item ${index} property "${itemField.key}" must not be empty.`)
            }

            if (typeof itemField.maxLength === 'number' && itemValue.length > itemField.maxLength) {
                throw new Error(`Section "${sectionType}" setting "${field.key}" item ${index} property "${itemField.key}" exceeds maximum length ${itemField.maxLength}.`)
            }

            if (['url', 'image', 'video'].includes(itemField.type) && !isSafeAuthoredUrl(itemValue, itemField.type)) {
                throw new Error(`Section "${sectionType}" setting "${field.key}" item ${index} property "${itemField.key}" must be a safe URL or relative path.`)
            }
        })
    })
}

function isSafeAuthoredUrl(value, fieldType) {
    if (!value.trim()) {
        return true
    }

    if (value.length > DEFAULT_URL_MAX_LENGTH || /[\s\u0000-\u001f\u007f\\]/.test(value) || value.startsWith('//')) {
        return false
    }

    if (value.startsWith('/') || value.startsWith('#') || value.startsWith('?')) {
        return true
    }

    try {
        const parsed = new URL(value)
        if (parsed.protocol === 'http:' || parsed.protocol === 'https:') {
            return !parsed.username && !parsed.password
        }

        return fieldType === 'url' && (parsed.protocol === 'mailto:' || parsed.protocol === 'tel:')
    } catch {
        return !value.includes(':')
    }
}

function throwTypeError(sectionType, field, expectedType) {
    throw new Error(`Section "${sectionType}" setting "${field.key}" must be ${expectedType}.`)
}
