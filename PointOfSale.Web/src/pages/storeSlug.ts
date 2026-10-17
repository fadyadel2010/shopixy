export const normalizeStoreSlug = (value: string) => value.trim().toLowerCase()

export const isStoreSlugFormatValid = (value: string) => (
    value.length >= 3
    && value.length <= 63
    && /^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$/.test(value)
    && !value.includes('--')
)
