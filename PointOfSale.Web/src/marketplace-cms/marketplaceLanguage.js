export const MARKETPLACE_LANGUAGE_STORAGE_KEY = 'shopixy-marketplace-language'
export const MARKETPLACE_LANGUAGE_COOKIE = 'shopixy_marketplace_lang'
export const SUPPORTED_MARKETPLACE_LANGUAGES = ['en', 'ar']

export function normalizeMarketplaceLanguage(value) {
    if (!value) {
        return 'en'
    }

    return String(value).toLowerCase().startsWith('ar') ? 'ar' : 'en'
}

export function resolveMarketplaceLanguageFromPath(pathname) {
    const [, firstSegment] = normalizePathname(pathname).split('/')
    return SUPPORTED_MARKETPLACE_LANGUAGES.includes(firstSegment) ? firstSegment : null
}

export function getSavedMarketplaceLanguage() {
    if (typeof window === 'undefined') {
        return null
    }

    const fromStorage = window.localStorage.getItem(MARKETPLACE_LANGUAGE_STORAGE_KEY)
    if (fromStorage) {
        return normalizeMarketplaceLanguage(fromStorage)
    }

    return getCookieLanguage()
}

export function resolveInitialLanguage(pathname = typeof window !== 'undefined' ? window.location.pathname : '/') {
    const savedLanguage = getSavedMarketplaceLanguage()
    if (savedLanguage) {
        return savedLanguage
    }

    const pathLanguage = resolveMarketplaceLanguageFromPath(pathname)
    if (pathLanguage) {
        return pathLanguage
    }

    if (typeof window !== 'undefined') {
        return normalizeMarketplaceLanguage(window.navigator.language)
    }

    return 'en'
}

export function getCurrentLanguage(pathname = typeof window !== 'undefined' ? window.location.pathname : '/') {
    return resolveMarketplaceLanguageFromPath(pathname) || resolveInitialLanguage(pathname)
}

export function setLanguage(language) {
    if (typeof window === 'undefined') {
        return normalizeMarketplaceLanguage(language)
    }

    const normalizedLanguage = normalizeMarketplaceLanguage(language)
    window.localStorage.setItem(MARKETPLACE_LANGUAGE_STORAGE_KEY, normalizedLanguage)
    document.cookie = `${MARKETPLACE_LANGUAGE_COOKIE}=${normalizedLanguage}; path=/; max-age=31536000; SameSite=Lax`
    return normalizedLanguage
}

export function getStoredMarketplaceLanguage() {
    return resolveInitialLanguage()
}

export function persistMarketplaceLanguage(language) {
    return setLanguage(language)
}

export function buildMarketplacePagePath(language, slug) {
    return `/${normalizeMarketplaceLanguage(language)}/${slug}`
}

export function buildMarketplaceProductPath(language, productId) {
    return `/${normalizeMarketplaceLanguage(language)}/product/${productId}`
}

export function replaceMarketplaceLanguagePrefix(pathname, language) {
    const normalizedLanguage = normalizeMarketplaceLanguage(language)
    const normalizedPathname = normalizePathname(pathname)
    const segments = normalizedPathname.split('/').filter(Boolean)

    if (segments.length > 0 && SUPPORTED_MARKETPLACE_LANGUAGES.includes(segments[0])) {
        segments[0] = normalizedLanguage
        return `/${segments.join('/')}`
    }

    if (segments.length === 0) {
        return `/${normalizedLanguage}`
    }

    return `/${normalizedLanguage}/${segments.join('/')}`
}

export function buildMarketplaceLanguageUrl(pathname, search, language) {
    const targetPath = replaceMarketplaceLanguagePrefix(pathname, language)
    const normalizedSearch = search || ''
    return `${targetPath}${normalizedSearch}`
}

export function prefixMarketplacePath(language, url) {
    if (!url || /^https?:\/\//i.test(url)) {
        return url || ''
    }

    const normalizedLanguage = normalizeMarketplaceLanguage(language)
    if (url.startsWith(`/${normalizedLanguage}/`) || url === `/${normalizedLanguage}`) {
        return url
    }

    return `/${normalizedLanguage}${url.startsWith('/') ? url : `/${url}`}`
}

export function applyMarketplaceDocumentLanguage(language) {
    if (typeof document === 'undefined') {
        return
    }

    const normalizedLanguage = normalizeMarketplaceLanguage(language)
    document.documentElement.lang = normalizedLanguage
    document.documentElement.dir = normalizedLanguage === 'ar' ? 'rtl' : 'ltr'
    document.body?.setAttribute('dir', document.documentElement.dir)
}

function getCookieLanguage() {
    if (typeof document === 'undefined') {
        return null
    }

    const cookiePrefix = `${MARKETPLACE_LANGUAGE_COOKIE}=`
    const rawCookie = document.cookie
        .split(';')
        .map((item) => item.trim())
        .find((item) => item.startsWith(cookiePrefix))

    if (!rawCookie) {
        return null
    }

    return normalizeMarketplaceLanguage(rawCookie.slice(cookiePrefix.length))
}

function normalizePathname(pathname) {
    if (!pathname) {
        return '/'
    }

    return pathname.startsWith('/') ? pathname : `/${pathname}`
}
