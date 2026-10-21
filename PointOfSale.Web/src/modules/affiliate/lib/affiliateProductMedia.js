// Store-relative catalog media belongs to the product merchant, not the portal
// origin (which may also have an unrelated POS session). StorefrontUrl is issued
// by the backend URL resolver; never infer an owner from browser tenant state.
export function affiliateProductMediaUrl(image, storefrontUrl) {
    if (typeof image !== 'string') return ''
    if (!image.startsWith('/') || image.startsWith('//')) return image
    try {
        const origin = new URL(storefrontUrl).origin
        return new URL(image, origin).href
    } catch {
        return ''
    }
}
