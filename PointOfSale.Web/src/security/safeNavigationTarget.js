import { containsAsciiControl } from './textCharacterPolicy'

const SAFE_NAVIGATION_FALLBACK = '/'

export const resolveSafeNavigationTarget = (value, fallback = SAFE_NAVIGATION_FALLBACK) => {
    if (typeof value !== 'string') return fallback

    const candidate = value.trim()
    if (!candidate || containsAsciiControl(candidate) || candidate.includes('\\') || candidate.startsWith('//')) {
        return fallback
    }

    if (candidate.startsWith('/') || candidate.startsWith('#') || candidate.startsWith('?')) {
        return candidate
    }

    try {
        const parsed = new URL(candidate)
        if (
            (parsed.protocol === 'http:' || parsed.protocol === 'https:') &&
            !parsed.username &&
            !parsed.password
        ) {
            return parsed.href
        }
    } catch {
        // Relative non-root targets are intentionally rejected at this public boundary.
    }

    return fallback
}
