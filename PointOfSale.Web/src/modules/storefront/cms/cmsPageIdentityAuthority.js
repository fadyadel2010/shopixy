const GUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i
const SLUG_PATTERN = /^[a-z0-9]+(?:-[a-z0-9]+)*$/
const CMS_PAGE_STATUSES = new Set(['Draft', 'Published', 'UnpublishedChanges'])
const CMS_CONTENT_TYPES = new Set(['RichText', 'Blocks'])
const SEO_TEXT_LIMITS = {
    metaTitle: 200,
    metaDescription: 320,
    metaKeywords: 500,
}

function normalizeGuid(value) {
    return typeof value === 'string' && GUID_PATTERN.test(value)
        ? value.toLowerCase()
        : ''
}

function normalizeSlug(value) {
    if (typeof value !== 'string') {
        return ''
    }

    const slug = value.trim().toLowerCase()
    return SLUG_PATTERN.test(slug) && slug.length <= 160 ? slug : ''
}

function getCurrentTenantId(currentUser) {
    return normalizeGuid(currentUser?.tenantId || currentUser?.TenantId || '')
}

function isPositiveVersion(value) {
    return Number.isInteger(value) && value > 0
}

function isSupportedContentType(value) {
    return CMS_CONTENT_TYPES.has(value)
}

function normalizeOptionalSeoString(value, maxLength) {
    if (value == null) {
        return null
    }

    if (typeof value !== 'string') {
        return undefined
    }

    const trimmed = value.trim()
    if (!trimmed) {
        return null
    }

    if (trimmed.length > maxLength || /[\u0000-\u001f\u007f]/.test(trimmed)) {
        return undefined
    }

    return trimmed
}

function normalizeOptionalSeoUrl(value) {
    if (value == null) {
        return null
    }

    if (typeof value !== 'string') {
        return undefined
    }

    const trimmed = value.trim()
    if (!trimmed) {
        return null
    }

    if (trimmed.length > 1000 || /[\u0000-\u001f\u007f]/.test(trimmed)) {
        return undefined
    }

    if (trimmed.startsWith('/')) {
        return trimmed.startsWith('//') || trimmed.includes('\\') ? undefined : trimmed
    }

    try {
        const url = new URL(trimmed)
        return (url.protocol === 'http:' || url.protocol === 'https:') && !url.username && !url.password
            ? trimmed
            : undefined
    } catch {
        return undefined
    }
}

function admitCmsPageSeo(seo) {
    if (!seo || typeof seo !== 'object' || Array.isArray(seo)) {
        return null
    }

    const metaTitle = normalizeOptionalSeoString(seo.metaTitle, SEO_TEXT_LIMITS.metaTitle)
    const metaDescription = normalizeOptionalSeoString(seo.metaDescription, SEO_TEXT_LIMITS.metaDescription)
    const metaKeywords = normalizeOptionalSeoString(seo.metaKeywords, SEO_TEXT_LIMITS.metaKeywords)
    const openGraphImageUrl = normalizeOptionalSeoUrl(seo.openGraphImageUrl)

    if (
        metaTitle === undefined
        || metaDescription === undefined
        || metaKeywords === undefined
        || openGraphImageUrl === undefined
    ) {
        return null
    }

    return {
        metaTitle,
        metaDescription,
        metaKeywords,
        openGraphImageUrl,
    }
}

function admitCmsPageTranslation(translation) {
    if (!translation || typeof translation !== 'object' || Array.isArray(translation)) {
        return null
    }

    const title = typeof translation.title === 'string' ? translation.title.trim() : ''
    const seo = admitCmsPageSeo(translation.seo)
    const contentIsValid = translation.contentType === 'RichText'
        ? typeof translation.content === 'string' && translation.content.trim().length > 0
        : translation.contentType === 'Blocks'
            ? Array.isArray(translation.content) && translation.content.length > 0
            : false

    if (!title || title.length > 200 || !contentIsValid || !isPositiveVersion(translation.schemaVersion) || !seo) {
        return null
    }

    return {
        ...translation,
        title,
        seo,
    }
}

function admitCmsPageVersion(version) {
    if (!version || typeof version !== 'object') {
        return null
    }

    const recordId = normalizeGuid(version.recordId)
    const slug = normalizeSlug(version.slug)
    const seo = admitCmsPageSeo(version.seo)
    if (!recordId || !slug || !isPositiveVersion(version.version) || !isSupportedContentType(version.contentType) || !seo) {
        return null
    }

    let translations = version.translations
    if (translations != null) {
        if (!translations || typeof translations !== 'object' || Array.isArray(translations)) {
            return null
        }

        const locales = Object.keys(translations)
        if (!locales.includes('en') || locales.some((locale) => locale !== 'en' && locale !== 'ar')) {
            return null
        }

        const english = admitCmsPageTranslation(translations.en)
        const arabic = translations.ar == null ? null : admitCmsPageTranslation(translations.ar)
        if (!english || (translations.ar != null && !arabic)) {
            return null
        }

        translations = arabic ? { en: english, ar: arabic } : { en: english }
    }

    return {
        ...version,
        recordId,
        slug,
        seo,
        ...(translations == null ? {} : { translations }),
    }
}

export function deriveCurrentCmsPageIdentityPresentation(page, currentUser, options = {}) {
    const currentTenantId = getCurrentTenantId(currentUser)
    const pageId = normalizeGuid(page?.id)
    const tenantId = normalizeGuid(page?.tenantId)
    const expectedPageId = options.expectedPageId ? normalizeGuid(options.expectedPageId) : ''

    if (!currentTenantId || !pageId || !tenantId || tenantId !== currentTenantId) {
        return null
    }

    if (expectedPageId && pageId !== expectedPageId) {
        return null
    }

    const draft = page.draft == null ? null : admitCmsPageVersion(page.draft)
    const published = page.published == null ? null : admitCmsPageVersion(page.published)
    const currentVersion = draft || published
    const slug = normalizeSlug(page.slug)

    if (!currentVersion || !slug || slug !== currentVersion.slug || !isSupportedContentType(page.contentType)) {
        return null
    }

    return {
        ...page,
        id: pageId,
        tenantId,
        slug,
        draft,
        published,
    }
}

export function deriveCurrentCmsPageListPresentation(pages, currentUser) {
    const currentTenantId = getCurrentTenantId(currentUser)
    if (!currentTenantId || !Array.isArray(pages)) {
        return null
    }

    const seenPageIds = new Set()
    const seenSlugs = new Set()
    const admitted = []

    for (const page of pages) {
        const pageId = normalizeGuid(page?.id)
        const tenantId = normalizeGuid(page?.tenantId)
        const slug = normalizeSlug(page?.slug)
        const hasDraft = page?.hasDraft === true
        const hasPublished = page?.hasPublished === true
        const draftVersion = page?.draftVersion ?? null
        const publishedVersion = page?.publishedVersion ?? null

        if (
            !pageId
            || !tenantId
            || tenantId !== currentTenantId
            || !slug
            || !isSupportedContentType(page?.contentType)
            || !CMS_PAGE_STATUSES.has(page?.status)
            || (!hasDraft && !hasPublished)
            || (hasDraft && !isPositiveVersion(draftVersion))
            || (hasPublished && !isPositiveVersion(publishedVersion))
            || (!hasDraft && draftVersion != null)
            || (!hasPublished && publishedVersion != null)
            || seenPageIds.has(pageId)
            || seenSlugs.has(slug)
        ) {
            return null
        }

        seenPageIds.add(pageId)
        seenSlugs.add(slug)
        admitted.push({
            ...page,
            id: pageId,
            tenantId,
            slug,
        })
    }

    return admitted
}

export function deriveCurrentCmsPageVersionHistoryPresentation(history, currentUser, options = {}) {
    const currentTenantId = getCurrentTenantId(currentUser)
    const pageId = normalizeGuid(history?.pageId)
    const tenantId = normalizeGuid(history?.tenantId)
    const expectedPageId = options.expectedPageId ? normalizeGuid(options.expectedPageId) : ''

    if (!currentTenantId || !pageId || !tenantId || tenantId !== currentTenantId) {
        return null
    }

    if (expectedPageId && pageId !== expectedPageId) {
        return null
    }

    const currentDraftVersion = history.currentDraftVersion ?? null
    const publishedVersion = history.publishedVersion ?? null
    if (
        (currentDraftVersion != null && !isPositiveVersion(currentDraftVersion))
        || (publishedVersion != null && !isPositiveVersion(publishedVersion))
        || !Array.isArray(history.versions)
    ) {
        return null
    }

    const seenVersions = new Set()
    const versions = []
    for (const entry of history.versions) {
        const snapshot = admitCmsPageVersion(entry?.snapshot)
        if (
            !isPositiveVersion(entry?.version)
            || seenVersions.has(entry.version)
            || !snapshot
            || snapshot.version !== entry.version
        ) {
            return null
        }

        seenVersions.add(entry.version)
        versions.push({
            ...entry,
            snapshot,
            isCurrentDraft: entry.isCurrentDraft === true,
            isPublished: entry.isPublished === true,
        })
    }

    if (versions.length === 0) {
        return null
    }

    return {
        ...history,
        pageId,
        tenantId,
        currentDraftVersion,
        publishedVersion,
        versions,
    }
}
