// Canonical publication-status normalization shared by the Products listing
// and the Product Editor. The .NET JSON serializer (JsonStringEnumConverter)
// emits PublishStatus as a string ("Draft"/"Published"/"Archived"), while some
// clients still use numeric values (0/1/2). Unknown, missing, malformed, or
// unsupported values fail closed to Draft so they can never silently surface
// as Published.
export const PUBLISH_STATUS_DRAFT = 0
export const PUBLISH_STATUS_PUBLISHED = 1
export const PUBLISH_STATUS_ARCHIVED = 2

export function normalizePublishStatus(value) {
    if (value === PUBLISH_STATUS_DRAFT
        || value === PUBLISH_STATUS_PUBLISHED
        || value === PUBLISH_STATUS_ARCHIVED) {
        return value
    }

    const normalizedValue = typeof value === 'string' ? value.trim().toLowerCase() : ''

    if (normalizedValue === 'draft' || normalizedValue === '0') {
        return PUBLISH_STATUS_DRAFT
    }

    if (normalizedValue === 'published' || normalizedValue === '1') {
        return PUBLISH_STATUS_PUBLISHED
    }

    if (normalizedValue === 'archived' || normalizedValue === '2') {
        return PUBLISH_STATUS_ARCHIVED
    }

    return PUBLISH_STATUS_DRAFT
}
