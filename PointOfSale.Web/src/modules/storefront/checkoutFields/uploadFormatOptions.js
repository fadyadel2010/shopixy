const IMAGE_FORMAT_IDS = ['jpg', 'png', 'webp']
const FILE_FORMAT_IDS = ['jpg', 'png', 'webp', 'pdf', 'word_doc']

export const UPLOAD_FORMAT_OPTIONS = Object.freeze({
    jpg: Object.freeze({
        id: 'jpg',
        labelKey: 'checkoutFields.uploadFormats.jpg',
        defaultLabel: 'JPG / JPEG',
        mimeTypes: Object.freeze(['image/jpeg']),
        fieldTypes: Object.freeze(['IMAGE', 'FILE', 'MULTI_IMAGE']),
    }),
    png: Object.freeze({
        id: 'png',
        labelKey: 'checkoutFields.uploadFormats.png',
        defaultLabel: 'PNG',
        mimeTypes: Object.freeze(['image/png']),
        fieldTypes: Object.freeze(['IMAGE', 'FILE', 'MULTI_IMAGE']),
    }),
    webp: Object.freeze({
        id: 'webp',
        labelKey: 'checkoutFields.uploadFormats.webp',
        defaultLabel: 'WEBP',
        mimeTypes: Object.freeze(['image/webp']),
        fieldTypes: Object.freeze(['IMAGE', 'FILE', 'MULTI_IMAGE']),
    }),
    pdf: Object.freeze({
        id: 'pdf',
        labelKey: 'checkoutFields.uploadFormats.pdf',
        defaultLabel: 'PDF',
        mimeTypes: Object.freeze(['application/pdf']),
        fieldTypes: Object.freeze(['FILE']),
    }),
    word_doc: Object.freeze({
        id: 'word_doc',
        labelKey: 'checkoutFields.uploadFormats.word',
        defaultLabel: 'Word',
        mimeTypes: Object.freeze(['application/msword']),
        fieldTypes: Object.freeze(['FILE']),
    }),
})

export const normalizeMimeType = (value) => {
    const normalized = String(value || '').split(';', 1)[0].trim().toLowerCase()
    return normalized === 'image/jpg' ? 'image/jpeg' : normalized
}

export const normalizeUploadMimeTypes = (mimeTypes) => {
    if (!Array.isArray(mimeTypes)) return []
    const unique = new Set()
    const normalized = []
    for (const mimeType of mimeTypes) {
        const next = normalizeMimeType(mimeType)
        if (!next || unique.has(next)) continue
        unique.add(next)
        normalized.push(next)
    }
    return normalized
}

export const getUploadFormatOptions = (fieldType) => {
    const formatIds = fieldType === 'FILE' ? FILE_FORMAT_IDS : IMAGE_FORMAT_IDS
    return formatIds.map((id) => UPLOAD_FORMAT_OPTIONS[id])
}

export const getDefaultUploadMimeTypes = (fieldType) =>
    getUploadFormatOptions(fieldType).flatMap((format) => format.mimeTypes)

export const getKnownUploadMimeTypes = (fieldType) =>
    new Set(getDefaultUploadMimeTypes(fieldType).map(normalizeMimeType))

export const getUploadFormatSelection = (fieldType, configuredMimeTypes) => {
    const hasConfiguredMimeTypes = Array.isArray(configuredMimeTypes)
    const normalized = normalizeUploadMimeTypes(configuredMimeTypes)
    const effectiveMimeTypes = hasConfiguredMimeTypes ? normalized : getDefaultUploadMimeTypes(fieldType)
    const selectedIds = new Set()
    const knownMimeTypes = getKnownUploadMimeTypes(fieldType)

    for (const format of getUploadFormatOptions(fieldType)) {
        if (format.mimeTypes.some((mimeType) => effectiveMimeTypes.includes(normalizeMimeType(mimeType)))) {
            selectedIds.add(format.id)
        }
    }

    return {
        selectedIds,
        legacyMimeTypes: effectiveMimeTypes.filter((mimeType) => !knownMimeTypes.has(normalizeMimeType(mimeType))),
        effectiveMimeTypes,
    }
}

export const buildUploadMimeTypes = (fieldType, selectedIds, legacyMimeTypes = []) => {
    const selected = new Set(selectedIds || [])
    return normalizeUploadMimeTypes([
        ...getUploadFormatOptions(fieldType)
            .filter((format) => selected.has(format.id))
            .flatMap((format) => format.mimeTypes),
        ...legacyMimeTypes,
    ])
}
