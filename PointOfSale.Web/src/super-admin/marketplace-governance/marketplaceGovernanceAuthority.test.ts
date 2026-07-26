import { describe, expect, it } from 'vitest'
import {
    deriveMarketplaceGovernanceActionAuthority,
    deriveMarketplaceGovernanceDashboardAuthority,
    deriveMarketplaceGovernanceListingAuthority,
    deriveMarketplaceGovernanceListingsAuthority,
} from './marketplaceGovernanceAuthority'

const listingId = '11111111-1111-4111-8111-111111111111'
const tenantId = '22222222-2222-4222-8222-222222222222'

const dashboard = {
    totalListings: 10,
    publishedListings: 6,
    suppressedListings: 2,
    orphanedListings: 1,
    activeVendors: 4,
    suspendedVendors: 1,
    bannedVendors: 1,
    activeTenants: 4,
    suspendedTenants: 1,
    deletedTenants: 0,
    recentGovernanceAuditCount: 3,
    authorityOwner: 'SQL marketplace governance authority',
    generatedAtUtc: '2026-06-16T12:00:00.000Z',
}

const listing = {
    listingId,
    tenantId,
    tenantName: 'Vendor Tenant',
    tenantSlug: 'vendor-tenant',
    tenantStatus: 'Active',
    vendorGovernanceStatus: 'Active',
    productId: '33333333-3333-4333-8333-333333333333',
    variantId: '44444444-4444-4444-8444-444444444444',
    productName: 'Product',
    variantName: 'Default',
    sku: 'SKU-1',
    isPublished: true,
    isSuppressed: false,
    visibilityState: 'published',
    suppressionReason: null,
    suppressionTimestampUtc: null,
    suppressionOwner: null,
    isOrphaned: false,
    orphanReason: null,
    categoryId: '55555555-5555-4555-8555-555555555555',
    categoryName: 'Category',
    overridePresent: false,
    priceOverrideMutationVersion: 0,
    createdAtUtc: '2026-06-16T12:00:00.000Z',
    updatedAtUtc: null,
}

describe('marketplace governance frontend authority', () => {
    it('requires backend SQL authority owner and bounded dashboard counts', () => {
        expect(deriveMarketplaceGovernanceDashboardAuthority(dashboard)).toMatchObject({ totalListings: 10 })
        expect(deriveMarketplaceGovernanceDashboardAuthority({
            ...dashboard,
            authorityOwner: 'frontend governance repair',
        })).toBeNull()
        expect(deriveMarketplaceGovernanceDashboardAuthority({
            ...dashboard,
            suppressedListings: 11,
        })).toBeNull()
        expect(deriveMarketplaceGovernanceDashboardAuthority({
            ...dashboard,
            generatedAtUtc: 'browser-time',
        })).toBeNull()
    })

    it('rejects fabricated, contradictory, and alias governance listing presentation', () => {
        expect(deriveMarketplaceGovernanceListingAuthority(listing)).toMatchObject({ listingId })
        expect(deriveMarketplaceGovernanceListingAuthority({
            ...listing,
            vendorGovernanceStatus: 'Approved',
        })).toBeNull()
        expect(deriveMarketplaceGovernanceListingAuthority({
            ...listing,
            vendorGovernanceStatus: 'Suspended',
            isSuppressed: false,
            visibilityState: 'published',
        })).toBeNull()
        expect(deriveMarketplaceGovernanceListingAuthority({
            ...listing,
            isSuppressed: true,
            visibilityState: 'published',
            suppressionOwner: 'MarketplaceGovernance',
        })).toBeNull()
        expect(deriveMarketplaceGovernanceListingsAuthority([listing, { ...listing, tenantId: 'foreign' }])).toBeNull()
    })

    it('requires action responses to match the initiated backend action and entity', () => {
        const action = {
            entityId: listingId,
            entityType: 'CommerceHubListing',
            action: 'suppress',
            changed: true,
            visibilityState: 'suppressed',
            message: 'Suppressed by governance.',
            appliedAtUtc: '2026-06-16T12:00:00.000Z',
        }

        expect(deriveMarketplaceGovernanceActionAuthority(action, { entityId: listingId, action: 'suppress' }))
            .toMatchObject({ action: 'suppress' })
        expect(deriveMarketplaceGovernanceActionAuthority(action, { entityId: listingId, action: 'restore' }))
            .toBeNull()
        expect(deriveMarketplaceGovernanceActionAuthority({
            ...action,
            entityId: tenantId,
        }, { entityId: listingId, action: 'suppress' })).toBeNull()
        expect(deriveMarketplaceGovernanceActionAuthority({
            ...action,
            visibilityState: 'active',
        }, { entityId: listingId, action: 'suppress' })).toBeNull()
    })
})
