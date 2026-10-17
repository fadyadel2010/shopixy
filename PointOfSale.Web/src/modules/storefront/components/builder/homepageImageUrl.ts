export const HOMEPAGE_MEDIA_PREFIX = '/api/storefront/homepage-media/'
export function isValidImageUrl(value: unknown): boolean {
    if (!value) return true
    if (typeof value !== 'string') return false
    if (/\s|\\/.test(value) || value.startsWith('//')) return false
    if (value.startsWith('/')) return true
    if (!value.includes(':')) return true // Preserve existing relative image paths.
    try {
        const url = new URL(value)
        return ['http:', 'https:'].includes(url.protocol) && !url.username && !url.password
    } catch { return false }
}
