const CANONICAL_TYPES = new Set([
    'category',
    'product',
    'cms',
    'external',
    'collection',
    'custom',
])

const ENTITY_TYPES = new Set(['category', 'product', 'cms'])
const GUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const EMPTY_GUID = '00000000-0000-0000-0000-000000000000'
const CONTROL_CHARACTER_PATTERN = /[\u0000-\u001f\u007f]/
const ROW_VERSION_PATTERN = /^[1-9]\d*$/

const isBackendGuid = (value) => GUID_PATTERN.test(value || '') && value !== EMPTY_GUID

export class NavigationAuthoringContractError extends Error {
    constructor(code, message, details = null) {
        super(message)
        this.name = 'NavigationAuthoringContractError'
        this.code = code
        this.details = details
    }
}

const normalizeType = (value) => {
    const type = typeof value === 'string' ? value.trim().toLowerCase() : ''
    return type === 'internal' ? 'custom' : type
}

const requireArray = (value, field) => {
    if (!Array.isArray(value)) {
        throw new NavigationAuthoringContractError(
            'NAVIGATION_RESPONSE_REJECTED',
            `Navigation ${field} must be an array.`
        )
    }
    return value
}

const admitLink = (candidate, snapshot, menu, index, validationWarnings) => {
    if (!candidate || typeof candidate !== 'object') {
        throw new NavigationAuthoringContractError(
            'NAVIGATION_RESPONSE_REJECTED',
            `Navigation ${menu} link ${index + 1} is invalid.`
        )
    }

    const type = normalizeType(candidate.type)
    const label = typeof candidate.label === 'string' ? candidate.label.trim() : ''
    if (!CANONICAL_TYPES.has(type) || !label) {
        throw new NavigationAuthoringContractError(
            'NAVIGATION_RESPONSE_REJECTED',
            `Navigation ${menu} link ${index + 1} has no canonical type or label.`
        )
    }

    const targetId = typeof candidate.targetId === 'string' ? candidate.targetId.trim().toLowerCase() : null
    const validationStatus = typeof candidate.validationStatus === 'string'
        ? candidate.validationStatus.trim().toLowerCase()
        : null
    const validationCode = typeof candidate.validationCode === 'string' ? candidate.validationCode.trim() : ''
    const matchingRepairWarning = validationWarnings.find((warning) => (
        warning.status === 'invalid'
        && warning.snapshot === snapshot
        && warning.menu === menu
        && warning.index === index
        && warning.linkType === type
        && warning.code === validationCode
    ))
    const isBackendRepairCandidate = validationStatus === 'invalid' && Boolean(matchingRepairWarning)

    if (ENTITY_TYPES.has(type) && !isBackendGuid(targetId) && !isBackendRepairCandidate) {
        throw new NavigationAuthoringContractError(
            'NAVIGATION_RESPONSE_REJECTED',
            `Navigation ${menu} link ${index + 1} has no backend-issued target identity.`
        )
    }

    const collectionKey = typeof candidate.collectionKey === 'string'
        ? candidate.collectionKey.trim().toLowerCase()
        : null
    if (type === 'collection' && !collectionKey) {
        throw new NavigationAuthoringContractError(
            'NAVIGATION_RESPONSE_REJECTED',
            `Navigation ${menu} link ${index + 1} has no collection identity.`
        )
    }

    return {
        label,
        labelAr: typeof candidate.labelAr === 'string' && candidate.labelAr.trim()
            ? candidate.labelAr.trim()
            : null,
        type,
        targetId: ENTITY_TYPES.has(type) && isBackendGuid(targetId) ? targetId : null,
        collectionKey: type === 'collection' ? collectionKey : null,
        path: typeof candidate.path === 'string' && candidate.path.trim() ? candidate.path.trim() : null,
        slug: typeof candidate.slug === 'string' && candidate.slug.trim() ? candidate.slug.trim() : null,
        openInNewTab: type === 'external' && candidate.openInNewTab === true,
        order: index,
        enabled: candidate.enabled !== false,
        validationStatus: validationStatus || null,
        validationCode: validationCode || null,
        validationMessage: typeof candidate.validationMessage === 'string' && candidate.validationMessage.trim()
            ? candidate.validationMessage.trim()
            : null,
    }
}

const admitConfig = (candidate, field, validationWarnings) => {
    if (!candidate || typeof candidate !== 'object') {
        throw new NavigationAuthoringContractError(
            'NAVIGATION_RESPONSE_REJECTED',
            `Navigation ${field} configuration is required.`
        )
    }

    return {
        header: requireArray(candidate.header, `${field}.header`).map((link, index) => admitLink(link, field, 'header', index, validationWarnings)),
        footer: requireArray(candidate.footer, `${field}.footer`).map((link, index) => admitLink(link, field, 'footer', index, validationWarnings)),
    }
}

const admitValidationWarnings = (candidate) => {
    if (candidate == null) {
        return []
    }

    return requireArray(candidate, 'validationWarnings').map((warning, index) => {
        const code = typeof warning?.code === 'string' ? warning.code.trim() : ''
        const message = typeof warning?.message === 'string' ? warning.message.trim() : ''
        const snapshot = typeof warning?.snapshot === 'string' ? warning.snapshot.trim().toLowerCase() : ''
        const menu = typeof warning?.menu === 'string' ? warning.menu.trim().toLowerCase() : ''
        const linkIndex = Number.isInteger(warning?.index) ? warning.index : null
        const targetId = typeof warning?.targetId === 'string' ? warning.targetId.trim().toLowerCase() : null

        if (!code || !message || !['draft', 'live'].includes(snapshot) || !['header', 'footer'].includes(menu)) {
            throw new NavigationAuthoringContractError(
                'NAVIGATION_RESPONSE_REJECTED',
                `Navigation validation warning ${index + 1} is invalid.`
            )
        }

        return {
            code,
            message,
            snapshot,
            menu,
            index: linkIndex,
            field: typeof warning?.field === 'string' && warning.field.trim() ? warning.field.trim() : null,
            linkType: typeof warning?.linkType === 'string' && warning.linkType.trim()
                ? warning.linkType.trim().toLowerCase()
                : '',
            targetId: isBackendGuid(targetId) ? targetId : null,
            status: typeof warning?.status === 'string' && warning.status.trim()
                ? warning.status.trim().toLowerCase()
                : 'invalid',
        }
    })
}

export function admitNavigationSnapshot(candidate) {
    const rowVersion = typeof candidate?.rowVersion === 'string'
        ? candidate.rowVersion.trim()
        : String(candidate?.rowVersion ?? '')
    if (!ROW_VERSION_PATTERN.test(rowVersion)) {
        throw new NavigationAuthoringContractError(
            'NAVIGATION_RESPONSE_REJECTED',
            'Navigation response did not include a usable backend RowVersion.'
        )
    }

    const validationWarnings = admitValidationWarnings(candidate.validationWarnings)

    return {
        rowVersion,
        draft: admitConfig(candidate.draft, 'draft', validationWarnings),
        live: candidate.live == null ? null : admitConfig(candidate.live, 'live', validationWarnings),
        hasPendingChanges: candidate.hasPendingChanges === true,
        validationWarnings,
        brokenLinks: admitValidationWarnings(candidate.brokenLinks ?? validationWarnings),
    }
}

const assertObviousClientValidity = (link, menu, index) => {
    const context = `${menu} link ${index + 1}`
    if (!link.label || link.label.length > 120) {
        throw new NavigationAuthoringContractError(
            'NAVIGATION_CLIENT_INVALID',
            `${context} requires a label of 120 characters or fewer.`
        )
    }

    if (ENTITY_TYPES.has(link.type) && !isBackendGuid(link.targetId)) {
        throw new NavigationAuthoringContractError(
            'NAVIGATION_CLIENT_INVALID',
            `${context} requires a selected ${link.type} target.`,
            { menu, index, field: 'targetId', linkType: link.type }
        )
    }

    if (link.type === 'collection' && !link.collectionKey) {
        throw new NavigationAuthoringContractError(
            'NAVIGATION_CLIENT_INVALID',
            `${context} requires a selected collection.`,
            { menu, index, field: 'collectionKey', linkType: link.type }
        )
    }

    if (link.type === 'custom' && (
        !link.path ||
        link.path.length > 2048 ||
        CONTROL_CHARACTER_PATTERN.test(link.path) ||
        !link.path.startsWith('/') ||
        link.path.startsWith('//') ||
        link.path.includes('\\')
    )) {
        throw new NavigationAuthoringContractError(
            'NAVIGATION_CLIENT_INVALID',
            `${context} requires a storefront-relative path.`
        )
    }

    if (link.type === 'external') {
        if (!link.path || link.path.length > 2048 || CONTROL_CHARACTER_PATTERN.test(link.path)) {
            throw new NavigationAuthoringContractError(
                'NAVIGATION_CLIENT_INVALID',
                `${context} requires a valid http(s) URL.`
            )
        }

        try {
            const url = new URL(link.path)
            if (!['http:', 'https:'].includes(url.protocol) || url.username || url.password) {
                throw new Error('unsupported URL')
            }
        } catch {
            throw new NavigationAuthoringContractError(
                'NAVIGATION_CLIENT_INVALID',
                `${context} requires a valid http(s) URL.`
            )
        }
    }
}

const mapLinkToRequest = (link, menu, index) => {
    assertObviousClientValidity(link, menu, index)
    return {
        label: link.label,
        labelAr: link.labelAr,
        type: link.type,
        targetId: ENTITY_TYPES.has(link.type) ? link.targetId : null,
        collectionKey: link.type === 'collection' ? link.collectionKey : null,
        path: link.type === 'external' || link.type === 'custom' ? link.path : null,
        openInNewTab: link.type === 'external' && link.openInNewTab,
        order: index,
        enabled: link.enabled,
    }
}

// The single frontend Navigation DTO mapper.
export function mapNavigationDraftToRequest(snapshot) {
    const admitted = admitNavigationSnapshot(snapshot)
    return {
        rowVersion: admitted.rowVersion,
        header: admitted.draft.header.map((link, index) => mapLinkToRequest(link, 'header', index)),
        footer: admitted.draft.footer.map((link, index) => mapLinkToRequest(link, 'footer', index)),
    }
}

const admitTargetOptions = (value, type) => requireArray(value, `targets.${type}`).map((candidate) => {
    const targetId = typeof candidate?.targetId === 'string' ? candidate.targetId.trim().toLowerCase() : null
    const collectionKey = typeof candidate?.collectionKey === 'string'
        ? candidate.collectionKey.trim().toLowerCase()
        : null
    const label = typeof candidate?.label === 'string' ? candidate.label.trim() : ''
    if (!label || (ENTITY_TYPES.has(type) && !isBackendGuid(targetId)) || (type === 'collection' && !collectionKey)) {
        throw new NavigationAuthoringContractError(
            'NAVIGATION_TARGETS_REJECTED',
            `Navigation ${type} targets did not match the canonical contract.`
        )
    }

    return {
        type,
        targetId,
        collectionKey,
        label,
        resolvedPath: typeof candidate.resolvedPath === 'string' ? candidate.resolvedPath : null,
        state: typeof candidate.state === 'string' ? candidate.state : '',
    }
})

export function admitNavigationTargets(candidate) {
    return {
        categories: admitTargetOptions(candidate?.categories, 'category'),
        products: admitTargetOptions(candidate?.products, 'product'),
        cmsPages: admitTargetOptions(candidate?.cmsPages, 'cms'),
        collections: admitTargetOptions(candidate?.collections, 'collection'),
    }
}

export function extractNavigationAdmissionError(error) {
    const payload = error?.response?.data
    if (error?.response?.status !== 422 || typeof payload?.code !== 'string' || !payload.code.startsWith('NAVIGATION_')) {
        return null
    }

    return {
        code: payload.code,
        message: payload.message || 'Navigation could not be admitted.',
        details: payload.navigation || null,
    }
}

export function extractNavigationConflict(error) {
    const payload = error?.response?.data
    if (error?.response?.status !== 409 || payload?.code !== 'CONCURRENCY_CONFLICT') {
        return null
    }

    let latestVersion = null
    if (payload.latestVersion) {
        try {
            latestVersion = admitNavigationSnapshot(payload.latestVersion)
        } catch {
            latestVersion = null
        }
    }

    return {
        code: payload.code,
        message: payload.message || 'Navigation changed while you were editing it. Reload the latest version.',
        latestVersion,
    }
}
