const PREVIEWABLE_IMAGE_TYPES = new Set(['image/jpeg', 'image/png', 'image/webp'])

export function isPreviewableCheckoutImage(attachment: unknown): boolean {
    if (typeof attachment !== 'object' || attachment === null) return false
    const contentType = String('contentType' in attachment ? attachment.contentType || '' : '').split(';', 1)[0].trim().toLowerCase()
    return 'mediaType' in attachment && String(attachment.mediaType || '').toLowerCase() === 'image'
        && PREVIEWABLE_IMAGE_TYPES.has(contentType)
}
