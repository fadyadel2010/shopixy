const LINK_TYPES = new Set(['Internal', 'Cms', 'External'])
const REVISION_PATTERN = /^[a-f0-9]{64}$/
const MAX_URL_LENGTH = 1000

const createId = () =>
    typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function'
        ? crypto.randomUUID()
        : `footer-${Date.now()}-${Math.random().toString(16).slice(2)}`

const localized = (value, fallback = '') => ({
    en: typeof value?.en === 'string' ? value.en : fallback,
    ar: typeof value?.ar === 'string' ? value.ar : '',
})

export const createFooterLink = (isLocalized) => ({
    _key: createId(),
    label: '',
    labelLocalized: isLocalized ? localized(null) : undefined,
    type: 'Internal',
    url: '',
    cmsPageSlug: '',
    openInNewTab: false,
    isActive: true,
})

export const createFooterSection = (isLocalized) => ({
    _key: createId(),
    title: '',
    titleLocalized: isLocalized ? localized(null) : undefined,
    isActive: true,
    links: [createFooterLink(isLocalized)],
})

export const createSocialLink = () => ({
    _key: createId(),
    platform: '',
    url: '',
    icon: '',
    isActive: true,
})

function requireOrderedArray(value, label) {
    if (!Array.isArray(value)) {
        throw new Error(`${label} response is invalid.`)
    }

    const sorted = value.slice().sort((left, right) => left.order - right.order)
    if (sorted.some((item, index) => !Number.isInteger(item?.order) || item.order !== index)) {
        throw new Error(`${label} response contains invalid ordering.`)
    }
    return sorted
}

export function admitFooterConfiguration(configuration, expectedScope) {
    if (!configuration || configuration.scope !== expectedScope || !REVISION_PATTERN.test(configuration.revision || '')) {
        throw new Error('Footer response is missing authoritative scope or revision evidence.')
    }

    const sections = requireOrderedArray(configuration.sections, 'Footer sections').map((section) => ({
        _key: createId(),
        title: typeof section.title === 'string' ? section.title : '',
        titleLocalized: localized(section.titleLocalized, section.title || ''),
        isActive: section.isActive !== false,
        links: requireOrderedArray(section.links, 'Footer links').map((link) => ({
            _key: createId(),
            label: typeof link.label === 'string' ? link.label : '',
            labelLocalized: localized(link.labelLocalized, link.label || ''),
            type: LINK_TYPES.has(link.type) ? link.type : 'Internal',
            url: typeof link.url === 'string' ? link.url : '',
            cmsPageSlug: typeof link.cmsPageSlug === 'string' ? link.cmsPageSlug : '',
            openInNewTab: Boolean(link.openInNewTab),
            isActive: link.isActive !== false,
        })),
    }))
    const socialLinks = requireOrderedArray(configuration.socialLinks, 'Social links').map((link) => ({
        _key: createId(),
        platform: typeof link.platform === 'string' ? link.platform : '',
        url: typeof link.url === 'string' ? link.url : '',
        icon: typeof link.icon === 'string' ? link.icon : '',
        isActive: link.isActive !== false,
    }))

    return {
        scope: configuration.scope,
        revision: configuration.revision,
        updatedAtUtc: configuration.updatedAtUtc,
        sections,
        socialLinks,
    }
}

function trimLocalizedField(field) {
    const value = {
        en: (field?.en || '').trim(),
        ar: (field?.ar || '').trim(),
    }
    return value.en || value.ar ? value : null
}

export function mapFooterDraftToRequest(draft, expectedRevision, isLocalized) {
    return {
        expectedRevision,
        sections: draft.sections.map((section, sectionIndex) => ({
            title: isLocalized ? (section.titleLocalized?.en || '').trim() : section.title.trim(),
            ...(isLocalized ? { titleLocalized: trimLocalizedField(section.titleLocalized) } : {}),
            order: sectionIndex,
            isActive: section.isActive !== false,
            links: section.links.map((link, linkIndex) => ({
                label: isLocalized ? (link.labelLocalized?.en || '').trim() : link.label.trim(),
                ...(isLocalized ? { labelLocalized: trimLocalizedField(link.labelLocalized) } : {}),
                type: link.type,
                url: link.type === 'Cms' ? null : (link.url || '').trim(),
                cmsPageSlug: link.type === 'Cms' ? (link.cmsPageSlug || '').trim() : null,
                openInNewTab: Boolean(link.openInNewTab),
                order: linkIndex,
                isActive: link.isActive !== false,
            })),
        })),
        socialLinks: draft.socialLinks.map((link, index) => ({
            platform: link.platform.trim(),
            url: link.url.trim(),
            icon: (link.icon || '').trim() || null,
            order: index,
            isActive: link.isActive !== false,
        })),
    }
}

export function getFooterDraftFingerprint(draft, isLocalized) {
    return JSON.stringify(mapFooterDraftToRequest(draft, 'revision', isLocalized))
}

function isExternalUrl(value) {
    if (!value || value.length > MAX_URL_LENGTH || /[\u0000-\u001f\u007f]/.test(value)) {
        return false
    }
    try {
        const parsed = new URL(value)
        return (parsed.protocol === 'http:' || parsed.protocol === 'https:') && !parsed.username && !parsed.password
    } catch {
        return false
    }
}

function isInternalUrl(value) {
    return Boolean(value) &&
        value.length <= MAX_URL_LENGTH &&
        value.startsWith('/') &&
        !value.startsWith('//') &&
        !value.includes('\\') &&
        !/[\u0000-\u001f\u007f]/.test(value) &&
        !value.toLowerCase().startsWith('/api/') &&
        !value.toLowerCase().startsWith('/admin') &&
        !value.toLowerCase().includes('preview=true') &&
        !value.toLowerCase().includes('previewtoken=')
}

const issue = (code, message, field) => ({ code, message, field })

export function validateFooterDraft(draft, { isLocalized, cmsPages = [], allowCmsSlugEntry = false } = {}) {
    const issues = []
    if (!draft || !Array.isArray(draft.sections) || !Array.isArray(draft.socialLinks)) {
        return [issue('FOOTER_COLLECTION_REQUIRED', 'Footer sections and social links are required.', 'footer')]
    }
    if (draft.sections.length > 20) {
        issues.push(issue('FOOTER_SECTION_LIMIT_EXCEEDED', 'A footer can contain at most 20 sections.', 'sections'))
    }
    if (draft.socialLinks.length > 20) {
        issues.push(issue('FOOTER_SOCIAL_LIMIT_EXCEEDED', 'A footer can contain at most 20 social links.', 'socialLinks'))
    }

    const knownCmsSlugs = new Set(cmsPages.map((page) => page.slug?.trim().toLowerCase()).filter(Boolean))
    const targets = new Set()
    draft.sections.forEach((section, sectionIndex) => {
        const title = isLocalized ? section.titleLocalized?.en : section.title
        if (!title?.trim()) {
            issues.push(issue('FOOTER_REQUIRED_FIELD', `Section ${sectionIndex + 1} needs an English title.`, `sections[${sectionIndex}].title`))
        } else if (title.trim().length > 120) {
            issues.push(issue('FOOTER_FIELD_TOO_LONG', `Section ${sectionIndex + 1} title must be 120 characters or fewer.`, `sections[${sectionIndex}].title`))
        }
        if ((section.titleLocalized?.ar || '').trim().length > 120) {
            issues.push(issue('FOOTER_FIELD_TOO_LONG', `Section ${sectionIndex + 1} Arabic title must be 120 characters or fewer.`, `sections[${sectionIndex}].titleLocalized.ar`))
        }
        if (!Array.isArray(section.links) || section.links.length > 30) {
            issues.push(issue('FOOTER_LINK_LIMIT_EXCEEDED', `Section ${sectionIndex + 1} can contain at most 30 links.`, `sections[${sectionIndex}].links`))
            return
        }

        section.links.forEach((link, linkIndex) => {
            const path = `sections[${sectionIndex}].links[${linkIndex}]`
            const label = isLocalized ? link.labelLocalized?.en : link.label
            if (!label?.trim()) {
                issues.push(issue('FOOTER_REQUIRED_FIELD', `Link ${linkIndex + 1} in section ${sectionIndex + 1} needs an English label.`, `${path}.label`))
            } else if (label.trim().length > 160) {
                issues.push(issue('FOOTER_FIELD_TOO_LONG', `Link ${linkIndex + 1} label must be 160 characters or fewer.`, `${path}.label`))
            }
            if (!LINK_TYPES.has(link.type)) {
                issues.push(issue('FOOTER_LINK_TYPE_INVALID', `Link ${linkIndex + 1} has an invalid type.`, `${path}.type`))
                return
            }

            let target = ''
            if (link.type === 'Cms') {
                const slug = link.cmsPageSlug?.trim().toLowerCase()
                if (!slug) {
                    issues.push(issue('FOOTER_REQUIRED_FIELD', `Link ${linkIndex + 1} must target a CMS page.`, `${path}.cmsPageSlug`))
                    return
                }
                if (!allowCmsSlugEntry && !knownCmsSlugs.has(slug)) {
                    issues.push(issue('FOOTER_CMS_TARGET_NOT_PUBLISHED', `Link ${linkIndex + 1} must target an available published CMS page.`, `${path}.cmsPageSlug`))
                }
                target = `Cms:${slug}`
            } else {
                const url = link.url?.trim()
                if (url?.toLowerCase().startsWith('mailto:')) {
                    issues.push(issue('FOOTER_EMAIL_INVALID', 'Email links are not supported by the current footer runtime. Use a storefront contact path.', `${path}.url`))
                } else if (url?.toLowerCase().startsWith('tel:')) {
                    issues.push(issue('FOOTER_PHONE_INVALID', 'Phone links are not supported by the current footer runtime. Use a storefront contact path.', `${path}.url`))
                } else if (link.type === 'Internal' && !isInternalUrl(url)) {
                    issues.push(issue('FOOTER_INTERNAL_URL_INVALID', `Internal link ${linkIndex + 1} must use a safe path beginning with '/'.`, `${path}.url`))
                } else if (link.type === 'External' && !isExternalUrl(url)) {
                    issues.push(issue('FOOTER_EXTERNAL_URL_INVALID', `External link ${linkIndex + 1} must include http:// or https:// and cannot contain credentials.`, `${path}.url`))
                }
                target = `${link.type}:${url || ''}`
            }

            if (target && targets.has(target.toLowerCase())) {
                issues.push(issue('FOOTER_DUPLICATE_LINK', 'The same footer target cannot be used more than once.', path))
            }
            targets.add(target.toLowerCase())
        })
    })

    const platforms = new Set()
    const socialTargets = new Set()
    draft.socialLinks.forEach((link, index) => {
        const path = `socialLinks[${index}]`
        const platform = link.platform?.trim()
        const url = link.url?.trim()
        if (!platform) {
            issues.push(issue('FOOTER_REQUIRED_FIELD', `Social link ${index + 1} needs a platform.`, `${path}.platform`))
        } else if (platform.length > 40) {
            issues.push(issue('FOOTER_FIELD_TOO_LONG', `Social platform ${index + 1} must be 40 characters or fewer.`, `${path}.platform`))
        }
        if (!isExternalUrl(url)) {
            issues.push(issue('FOOTER_EXTERNAL_URL_INVALID', `Social link ${index + 1} must include http:// or https:// and cannot contain credentials.`, `${path}.url`))
        }
        if (platforms.has(platform?.toLowerCase()) || socialTargets.has(url?.toLowerCase())) {
            issues.push(issue('FOOTER_DUPLICATE_SOCIAL_LINK', 'Social platforms and social URLs must be unique.', path))
        }
        if (platform) platforms.add(platform.toLowerCase())
        if (url) socialTargets.add(url.toLowerCase())
    })

    return issues
}

export { getAuthoringErrorFeedback } from '../../modules/storefront/authoringPlatform'
