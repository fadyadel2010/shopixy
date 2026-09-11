import { superAdminApiClient } from '../core/superAdminApiClient'
import type { SuperAdminApiMeta } from '../auth/superAdminAuth.types'
import {
    deriveMarketplaceGovernanceActionAuthority,
    deriveMarketplaceGovernanceDashboardAuthority,
    deriveMarketplaceGovernanceListingsAuthority,
} from './marketplaceGovernanceAuthority'

export type GovernanceListingStatus =
    | ''
    | 'published'
    | 'unpublished'
    | 'suppressed'
    | 'orphaned'
    | 'vendor_suspended'
    | 'tenant_suspended'

export interface MarketplaceGovernanceDashboard {
    totalListings: number
    publishedListings: number
    suppressedListings: number
    orphanedListings: number
    activeVendors: number
    suspendedVendors: number
    bannedVendors: number
    activeTenants: number
    suspendedTenants: number
    deletedTenants: number
    recentGovernanceAuditCount: number
    authorityOwner: string
    generatedAtUtc: string
}

export interface MarketplaceGovernanceListing {
    listingId: string
    tenantId: string
    tenantName: string
    tenantSlug?: string | null
    tenantStatus: string
    vendorGovernanceStatus: string
    productId: string
    variantId: string
    productName: string
    variantName?: string | null
    sku?: string | null
    isPublished: boolean
    isSuppressed: boolean
    visibilityState: string
    suppressionReason?: string | null
    suppressionTimestampUtc?: string | null
    suppressionOwner?: string | null
    isOrphaned: boolean
    orphanReason?: string | null
    categoryId: string
    categoryName?: string | null
    overridePresent: boolean
    priceOverrideMutationVersion: number
    createdAtUtc: string
    updatedAtUtc?: string | null
}

export interface MarketplaceGovernanceListingFilters {
    page: number
    pageSize: number
    q?: string
    status?: GovernanceListingStatus
    vendorStatus?: '' | 'Active' | 'Suspended' | 'Banned' | 'FinanciallyLocked'
    tenantStatus?: '' | 'PendingSetup' | 'Active' | 'Suspended' | 'Deleted'
    overrideEnabled?: '' | 'true' | 'false'
}

export interface MarketplaceGovernanceListingResult {
    listings: MarketplaceGovernanceListing[]
    meta?: SuperAdminApiMeta & {
        totalCount?: number
        totalPages?: number
    }
}

export interface MarketplaceGovernanceActionResult {
    entityId: string
    entityType: string
    action: string
    changed: boolean
    visibilityState: string
    message: string
    appliedAtUtc: string
}

function buildListingParams(filters: MarketplaceGovernanceListingFilters): URLSearchParams {
    const params = new URLSearchParams()
    params.set('page', String(filters.page))
    params.set('pageSize', String(filters.pageSize))

    if (filters.q?.trim()) params.set('q', filters.q.trim())
    if (filters.status) params.set('status', filters.status)
    if (filters.vendorStatus) params.set('vendorStatus', filters.vendorStatus)
    if (filters.tenantStatus) params.set('tenantStatus', filters.tenantStatus)
    if (filters.overrideEnabled) params.set('overrideEnabled', filters.overrideEnabled)

    return params
}

export async function getMarketplaceGovernanceDashboard(): Promise<MarketplaceGovernanceDashboard> {
    const response = await superAdminApiClient.get<MarketplaceGovernanceDashboard>('/marketplace-governance/dashboard')
    const dashboard = deriveMarketplaceGovernanceDashboardAuthority(response.data)
    if (!dashboard) {
        throw new Error('Invalid marketplace governance dashboard authority response')
    }
    return dashboard
}

export async function getMarketplaceGovernanceListings(
    filters: MarketplaceGovernanceListingFilters,
): Promise<MarketplaceGovernanceListingResult> {
    const response = await superAdminApiClient.get<{ listings: MarketplaceGovernanceListing[] }>(
        `/marketplace-governance/listings?${buildListingParams(filters).toString()}`,
    )

    const listings = deriveMarketplaceGovernanceListingsAuthority(response.data.listings)
    if (!listings) {
        throw new Error('Invalid marketplace governance listing authority response')
    }

    return {
        listings,
        meta: response.meta as MarketplaceGovernanceListingResult['meta'],
    }
}

export async function suppressMarketplaceListing(
    listingId: string,
    reason: string,
): Promise<MarketplaceGovernanceActionResult> {
    const response = await superAdminApiClient.post<MarketplaceGovernanceActionResult, { reason: string }>(
        `/marketplace-governance/listings/${listingId}/suppress`,
        { reason },
    )
    const result = deriveMarketplaceGovernanceActionAuthority(response.data, { entityId: listingId, action: 'suppress' })
    if (!result) {
        throw new Error('Invalid marketplace governance action authority response')
    }
    return result
}

export async function restoreMarketplaceListing(
    listingId: string,
    reason: string,
): Promise<MarketplaceGovernanceActionResult> {
    const response = await superAdminApiClient.post<MarketplaceGovernanceActionResult, { reason: string }>(
        `/marketplace-governance/listings/${listingId}/restore`,
        { reason },
    )
    const result = deriveMarketplaceGovernanceActionAuthority(response.data, { entityId: listingId, action: 'restore' })
    if (!result) {
        throw new Error('Invalid marketplace governance action authority response')
    }
    return result
}

export async function suspendMarketplaceVendor(
    vendorId: string,
    reason: string,
): Promise<MarketplaceGovernanceActionResult> {
    const response = await superAdminApiClient.post<MarketplaceGovernanceActionResult, { reason: string }>(
        `/marketplace-governance/vendors/${vendorId}/suspend`,
        { reason },
    )
    const result = deriveMarketplaceGovernanceActionAuthority(response.data, { entityId: vendorId, action: 'suspend' })
    if (!result) {
        throw new Error('Invalid marketplace governance action authority response')
    }
    return result
}

export async function restoreMarketplaceVendor(
    vendorId: string,
    reason: string,
): Promise<MarketplaceGovernanceActionResult> {
    const response = await superAdminApiClient.post<MarketplaceGovernanceActionResult, { reason: string }>(
        `/marketplace-governance/vendors/${vendorId}/restore`,
        { reason },
    )
    const result = deriveMarketplaceGovernanceActionAuthority(response.data, { entityId: vendorId, action: 'restore' })
    if (!result) {
        throw new Error('Invalid marketplace governance action authority response')
    }
    return result
}
