const DEFAULT_LANGUAGE = 'en'

export function normalizeDocumentLanguage(language) {
    if (!language) {
        return DEFAULT_LANGUAGE
    }

    return String(language).toLowerCase().startsWith('ar') ? 'ar' : DEFAULT_LANGUAGE
}

export function getDocumentDirection(language) {
    return normalizeDocumentLanguage(language) === 'ar' ? 'rtl' : 'ltr'
}

export function syncDocumentLanguage(language) {
    const normalizedLanguage = normalizeDocumentLanguage(language)
    const direction = getDocumentDirection(normalizedLanguage)

    if (typeof document === 'undefined') {
        return { language: normalizedLanguage, direction }
    }

    document.documentElement.lang = normalizedLanguage
    document.documentElement.dir = direction
    document.body?.setAttribute('lang', normalizedLanguage)
    document.body?.setAttribute('dir', direction)

    return { language: normalizedLanguage, direction }
}
