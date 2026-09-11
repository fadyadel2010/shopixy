import type {
    MarketplaceGovernanceActionResult,
    MarketplaceGovernanceDashboard,
    MarketplaceGovernanceListing,
} from './marketplaceGovernanceService'

const GUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i
const AUTHORITY_OWNER = 'SQL marketplace governance authority'
const VENDOR_GOVERNANCE_STATUSES = new Set(['Active', 'Suspended', 'Banned', 'FinanciallyLocked', 'Missing'])
const TENANT_STATUSES = new Set(['PendingSetup', 'Active', 'Suspended', 'Deleted', 'Missing'])
const VISIBILITY_STATES = new Set(['published', 'unpublished', 'suppressed'])
const SUPPRESSION_OWNERS = new Set(['MarketplaceGovernance'])
const ACTION_ENTITY_TYPES = new Set(['CommerceHubListing', 'VendorGovernance'])
const ACTIONS = new Set(['suppress', 'restore', 'suspend'])

const isObject = (value: unknown): value is Record<string, unknown> =>
    Boolean(value) && typeof value === 'object' && !Array.isArray(value)

const isFiniteNonNegativeInteger = (value: unknown): value is number =>
    typeof value === 'number' && Number.isInteger(value) && value >= 0

const isOptionalString = (value: unknown): value is string | null | undefined =>
    value === undefined || value === null || typeof value === 'string'

const isBackendTimestamp = (value: unknown): value is string => {
    if (typeof value !== 'string' || !value.trim()) return false
    const parsed = Date.parse(value)
    return !Number.isNaN(parsed)
}

const hasValidDashboardCounts = (value: Record<string, unknown>) => [
    'totalListings',
    'publishedListings',
    'suppressedListings',
    'orphanedListings',
    'activeVendors',
    'suspendedVendors',
    'bannedVendors',
    'activeTenants',
    'suspendedTenants',
    'deletedTenants',
    'recentGovernanceAuditCount',
].every((field) => isFiniteNonNegativeInteger(value[field]))

export function deriveMarketplaceGovernanceDashboardAuthority(
    value: unknown,
): MarketplaceGovernanceDashboard | null {
    if (!isObject(value) || !hasValidDashboardCounts(value)) return null

    if (value.authorityOwner !== AUTHORITY_OWNER || !isBackendTimestamp(value.generatedAtUtc)) {
        return null
    }

    if (
        (value.publishedListings as number) > (value.totalListings as number) ||
        (value.suppressedListings as number) > (value.totalListings as number) ||
        (value.orphanedListings as number) > (value.totalListings as number)
    ) {
        return null
    }

    return value as unknown as MarketplaceGovernanceDashboard
}

export function deriveMarketplaceGovernanceListingAuthority(
    value: unknown,
): MarketplaceGovernanceListing | null {
    if (!isObject(value)) return null

    const listingId = value.listingId
    const tenantId = value.tenantId
    const productId = value.productId
    const variantId = value.variantId
    const categoryId = value.categoryId
    const tenantName = value.tenantName
    const productName = value.productName
    const tenantStatus = value.tenantStatus
    const vendorGovernanceStatus = value.vendorGovernanceStatus
    const visibilityState = value.visibilityState

    if (
        typeof listingId !== 'string' || !GUID_PATTERN.test(listingId) ||
        typeof tenantId !== 'string' || !GUID_PATTERN.test(tenantId) ||
        typeof productId !== 'string' || !GUID_PATTERN.test(productId) ||
        typeof variantId !== 'string' || !GUID_PATTERN.test(variantId) ||
        typeof categoryId !== 'string' || !GUID_PATTERN.test(categoryId) ||
        typeof tenantName !== 'string' || !tenantName.trim() ||
        typeof productName !== 'string' || !productName.trim() ||
        typeof tenantStatus !== 'string' || !TENANT_STATUSES.has(tenantStatus) ||
        typeof vendorGovernanceStatus !== 'string' || !VENDOR_GOVERNANCE_STATUSES.has(vendorGovernanceStatus) ||
        typeof visibilityState !== 'string' || !VISIBILITY_STATES.has(visibilityState) ||
        typeof value.isPublished !== 'boolean' ||
        typeof value.isSuppressed !== 'boolean' ||
        typeof value.isOrphaned !== 'boolean' ||
        typeof value.overridePresent !== 'boolean' ||
        !isFiniteNonNegativeInteger(value.priceOverrideMutationVersion) ||
        !isBackendTimestamp(value.createdAtUtc) ||
        (value.updatedAtUtc !== undefined && value.updatedAtUtc !== null && !isBackendTimestamp(value.updatedAtUtc)) ||
        !isOptionalString(value.tenantSlug) ||
        !isOptionalString(value.variantName) ||
        !isOptionalString(value.sku) ||
        !isOptionalString(value.categoryName) ||
        !isOptionalString(value.suppressionReason) ||
        !isOptionalString(value.suppressionTimestampUtc) ||
        !isOptionalString(value.suppressionOwner) ||
        !isOptionalString(value.orphanReason)
    ) {
        return null
    }

    if (value.suppressionTimestampUtc && !isBackendTimestamp(value.suppressionTimestampUtc)) return null
    if (value.suppressionOwner && !SUPPRESSION_OWNERS.has(value.suppressionOwner)) return null
    if (value.isSuppressed && visibilityState !== 'suppressed') return null
    if (!value.isSuppressed && (value.suppressionReason || value.suppressionTimestampUtc || value.suppressionOwner)) return null
    if (value.isOrphaned && !value.orphanReason) return null
    if (!value.isOrphaned && value.orphanReason) return null
    if (visibilityState === 'published' && (!value.isPublished || value.isSuppressed || value.isOrphaned)) return null
    if ((vendorGovernanceStatus === 'Suspended' || vendorGovernanceStatus === 'Banned') && visibilityState !== 'suppressed') {
        return null
    }
    if ((tenantStatus === 'Suspended' || tenantStatus === 'Deleted' || tenantStatus === 'Missing') && visibilityState !== 'suppressed') {
        return null
    }

    return value as unknown as MarketplaceGovernanceListing
}

export function deriveMarketplaceGovernanceListingsAuthority(
    value: unknown,
): MarketplaceGovernanceListing[] | null {
    if (!Array.isArray(value)) return null

    const rows = value.map(deriveMarketplaceGovernanceListingAuthority)
    return rows.some((row) => row === null) ? null : rows as MarketplaceGovernanceListing[]
}

export function deriveMarketplaceGovernanceActionAuthority(
    value: unknown,
    expected: { entityId: string; action: 'suppress' | 'restore' | 'suspend' },
): MarketplaceGovernanceActionResult | null {
    if (!isObject(value)) return null

    if (
        value.entityId !== expected.entityId ||
        typeof value.entityId !== 'string' || !GUID_PATTERN.test(value.entityId) ||
        typeof value.entityType !== 'string' || !ACTION_ENTITY_TYPES.has(value.entityType) ||
        value.action !== expected.action ||
        typeof value.action !== 'string' || !ACTIONS.has(value.action) ||
        typeof value.changed !== 'boolean' ||
        typeof value.visibilityState !== 'string' || !VISIBILITY_STATES.has(value.visibilityState) && value.visibilityState !== 'active' && value.visibilityState !== 'suspended' ||
        typeof value.message !== 'string' || !value.message.trim() ||
        !isBackendTimestamp(value.appliedAtUtc)
    ) {
        return null
    }

    if (value.entityType === 'CommerceHubListing' && !VISIBILITY_STATES.has(value.visibilityState)) return null
    if (value.entityType === 'VendorGovernance' && value.visibilityState !== 'active' && value.visibilityState !== 'suspended') return null

    return value as unknown as MarketplaceGovernanceActionResult
}
