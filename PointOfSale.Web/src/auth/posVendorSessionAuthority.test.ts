import { describe, expect, it } from 'vitest'
import {
    deriveCommerceHubVendorEligibilityPresentation,
    deriveCommerceHubVendorLifecyclePresentation,
    derivePosVendorSessionUserAuthority,
    canAccessCommerceHubVendorNavigationCapability,
    hasCommerceHubVendorNavigationCapability,
    isCommerceHubVendorSessionAuthorityEndpoint,
    resolvePosRouteRedirect,
} from './posVendorSessionAuthority'

const navigationCapabilities = [
    'commercehub.catalog.view',
    'commercehub.catalog.manage',
    'commercehub.orders.view',
    'commercehub.orders.operate',
    'commercehub.finance.view',
    'commercehub.payments.view',
    'commercehub.payments.submit',
    'commercehub.analytics.view',
    'commercehub.storefront.manage',
    'commercehub.storefront.cms.manage',
    'commercehub.storefront.preview',
]

const userId = '11111111-1111-4111-8111-111111111111'
const tenantId = '22222222-2222-4222-8222-222222222222'
const effectivePermissions = ['dashboard.view', 'audit.view']

const activeLifecycle = {
    vendorId: tenantId,
    status: 'Active',
    governanceStatus: 'Active',
    tenantActive: true,
    marketplaceApproved: true,
    kycVerified: true,
    debtStatus: 'ACTIVE',
    isDebtBlocked: false,
    blocksCatalog: false,
    blocksOrders: false,
    blocksStorefront: false,
    blocksAnalytics: false,
    blocksPayments: false,
    blocksPayouts: false,
    blocksLogin: false,
}

const buildUser = (overrides = {}) => ({
    id: userId,
    tenantId,
    role: 'Admin',
    effectivePermissions,
    navigationCapabilities,
    vendorLifecycleAuthority: activeLifecycle,
    ...overrides,
})

describe('POS Admin CommerceHub vendor session authority', () => {
    it('accepts only backend-shaped current session identity', () => {
        expect(derivePosVendorSessionUserAuthority(buildUser())).toMatchObject({
            id: userId,
            tenantId,
            role: 'Admin',
            effectivePermissions,
            navigationCapabilities,
            vendorLifecycleAuthority: { status: 'Active' },
        })

        expect(derivePosVendorSessionUserAuthority(null)).toBeNull()
        expect(derivePosVendorSessionUserAuthority({ id: 'user-1', role: 'Admin' })).toBeNull()
        expect(derivePosVendorSessionUserAuthority({ tenantId: 'tenant-1', role: 'Admin' })).toBeNull()
        expect(derivePosVendorSessionUserAuthority({ id: 'user-1', tenantId: 'tenant-1' })).toBeNull()
        expect(derivePosVendorSessionUserAuthority(buildUser({ role: 'SuperAdmin' }))).toBeNull()
        expect(derivePosVendorSessionUserAuthority(buildUser({ role: 'FabricatedRole' }))).toBeNull()
    })

    it('accepts only known unique backend-derived POS permissions', () => {
        expect(derivePosVendorSessionUserAuthority(buildUser({ effectivePermissions: undefined }))).toBeNull()
        expect(derivePosVendorSessionUserAuthority(buildUser({
            effectivePermissions: ['dashboard.view', 'fabricated.permission'],
        }))).toBeNull()
        expect(derivePosVendorSessionUserAuthority(buildUser({
            effectivePermissions: ['dashboard.view', 'dashboard.view'],
        }))).toBeNull()
    })

    it('accepts only backend-issued CommerceHub navigation capabilities', () => {
        const user = derivePosVendorSessionUserAuthority(buildUser({
            navigationCapabilities: ['commercehub.finance.view'],
        }))

        expect(hasCommerceHubVendorNavigationCapability(user, 'commercehub.finance.view')).toBe(true)
        expect(hasCommerceHubVendorNavigationCapability(user, 'commercehub.catalog.view')).toBe(false)
        expect(derivePosVendorSessionUserAuthority(buildUser({
            navigationCapabilities: undefined,
        }))).toBeNull()
        expect(derivePosVendorSessionUserAuthority(buildUser({
            navigationCapabilities: ['commercehub.finance.view', 'fabricated.capability'],
        }))).toBeNull()
    })

    it('requires current backend lifecycle authority before accepting vendor session presentation', () => {
        expect(deriveCommerceHubVendorLifecyclePresentation(activeLifecycle, tenantId)).toMatchObject({
            vendorId: tenantId,
            status: 'Active',
        })
        expect(derivePosVendorSessionUserAuthority(buildUser({
            vendorLifecycleAuthority: undefined,
        }))).toBeNull()
        expect(derivePosVendorSessionUserAuthority(buildUser({
            vendorLifecycleAuthority: {
                ...activeLifecycle,
                vendorId: '33333333-3333-4333-8333-333333333333',
            },
        }))).toBeNull()
        expect(derivePosVendorSessionUserAuthority(buildUser({
            vendorLifecycleAuthority: {
                ...activeLifecycle,
                status: 'FabricatedActive',
            },
        }))).toBeNull()
        expect(derivePosVendorSessionUserAuthority(buildUser({
            vendorLifecycleAuthority: {
                ...activeLifecycle,
                blocksCatalog: 'false',
            },
        }))).toBeNull()
    })

    it('fails closed when restricted lifecycle carries operating capabilities', () => {
        const blockedLifecycle = {
            ...activeLifecycle,
            status: 'Blocked',
            governanceStatus: 'Banned',
            blocksCatalog: true,
            blocksOrders: true,
            blocksStorefront: true,
            blocksAnalytics: true,
            blocksPayments: true,
            blocksPayouts: true,
            blocksLogin: true,
        }

        expect(derivePosVendorSessionUserAuthority(buildUser({
            vendorLifecycleAuthority: blockedLifecycle,
            navigationCapabilities: ['commercehub.finance.view', 'commercehub.payments.view'],
        }))?.vendorLifecycleAuthority.status).toBe('Blocked')
        expect(derivePosVendorSessionUserAuthority(buildUser({
            vendorLifecycleAuthority: blockedLifecycle,
            navigationCapabilities: ['commercehub.catalog.manage'],
        }))).toBeNull()
    })

    it('derives vendor eligibility presentation from current backend capabilities and DTO evidence only', () => {
        const user = derivePosVendorSessionUserAuthority(buildUser())

        expect(deriveCommerceHubVendorEligibilityPresentation(user, {
            canSubmitPayment: true,
        })).toMatchObject({
            tenantId,
            userId,
            lifecycleStatus: 'Active',
            canManageCatalog: true,
            canViewOrders: true,
            canOperateOrders: true,
            canViewFinance: true,
            canViewPayments: true,
            canViewAnalytics: true,
            canManageStorefront: true,
            canManageCms: true,
            canPreviewStorefront: true,
            canSubmitPayment: true,
            isDebtBlocked: false,
        })

        expect(deriveCommerceHubVendorEligibilityPresentation(buildUser({
            navigationCapabilities: ['commercehub.finance.view', 'fabricated.capability'],
        }))).toBeNull()
        expect(deriveCommerceHubVendorEligibilityPresentation(user, {
            canSubmitPayment: 'true',
        })).toBeNull()
        expect(deriveCommerceHubVendorEligibilityPresentation(user, {
            canSubmitPayment: true,
            isSuspendedFromNewOrders: 'false',
        })).toBeNull()
    })

    it('fails Storefront navigation admission closed when backend lifecycle blocks Storefront', () => {
        const user = derivePosVendorSessionUserAuthority(buildUser({
            vendorLifecycleAuthority: {
                ...activeLifecycle,
                blocksStorefront: true,
            },
        }))

        expect(hasCommerceHubVendorNavigationCapability(user, 'commercehub.storefront.manage')).toBe(true)
        expect(canAccessCommerceHubVendorNavigationCapability(user, 'commercehub.storefront.manage')).toBe(false)
        expect(canAccessCommerceHubVendorNavigationCapability(user, 'commercehub.storefront.cms.manage')).toBe(false)
        expect(canAccessCommerceHubVendorNavigationCapability(user, 'commercehub.storefront.preview')).toBe(false)
    })

    it('derives debt-blocked presentation only from matching backend lifecycle and debt evidence', () => {
        const debtBlockedLifecycle = {
            ...activeLifecycle,
            status: 'DebtBlocked',
            debtStatus: 'SUSPENDED',
            isDebtBlocked: true,
            blocksCatalog: true,
            blocksOrders: true,
            blocksStorefront: true,
            blocksAnalytics: true,
            blocksPayouts: true,
        }
        const user = derivePosVendorSessionUserAuthority(buildUser({
            vendorLifecycleAuthority: debtBlockedLifecycle,
            navigationCapabilities: ['commercehub.finance.view', 'commercehub.payments.view', 'commercehub.payments.submit'],
        }))

        expect(deriveCommerceHubVendorEligibilityPresentation(user, {
            canSubmitPayment: true,
            isSuspendedFromNewOrders: true,
        })).toMatchObject({
            lifecycleStatus: 'DebtBlocked',
            canManageCatalog: false,
            canOperateOrders: false,
            canManageStorefront: false,
            canSubmitPayment: true,
            isDebtBlocked: true,
        })
        expect(deriveCommerceHubVendorEligibilityPresentation(user, {
            canSubmitPayment: true,
            isSuspendedFromNewOrders: false,
        })?.isDebtBlocked).toBe(false)
    })

    it('keeps payment submission closed unless both capability and backend evidence allow it', () => {
        const user = derivePosVendorSessionUserAuthority(buildUser({
            navigationCapabilities: ['commercehub.payments.view'],
        }))

        expect(deriveCommerceHubVendorEligibilityPresentation(user, {
            canSubmitPayment: true,
        })?.canSubmitPayment).toBe(false)
        expect(deriveCommerceHubVendorEligibilityPresentation({
            ...user,
            navigationCapabilities: ['commercehub.payments.view', 'commercehub.payments.submit'],
        }, {
            canSubmitPayment: false,
        })?.canSubmitPayment).toBe(false)
    })

    it('bounds protected CommerceHub vendor authority endpoints', () => {
        expect(isCommerceHubVendorSessionAuthorityEndpoint('/admin/commercehub/listings')).toBe(true)
        expect(isCommerceHubVendorSessionAuthorityEndpoint('/admin/orders')).toBe(true)
        expect(isCommerceHubVendorSessionAuthorityEndpoint('/admin/store-config')).toBe(true)
        expect(isCommerceHubVendorSessionAuthorityEndpoint('/admin/commerce-ai/insights')).toBe(true)
        expect(isCommerceHubVendorSessionAuthorityEndpoint('/admin/analytics/funnels')).toBe(true)
        expect(isCommerceHubVendorSessionAuthorityEndpoint('/admin/payment-methods')).toBe(true)
        expect(isCommerceHubVendorSessionAuthorityEndpoint('/admin/phones/blocked')).toBe(true)
        expect(isCommerceHubVendorSessionAuthorityEndpoint('/admin/online-customers')).toBe(true)
        expect(isCommerceHubVendorSessionAuthorityEndpoint('/admin/abandoned-checkouts')).toBe(true)
        expect(isCommerceHubVendorSessionAuthorityEndpoint('/admin/whatsapp/accounts')).toBe(true)
        expect(isCommerceHubVendorSessionAuthorityEndpoint('/admin/shipping/shipments')).toBe(true)
        expect(isCommerceHubVendorSessionAuthorityEndpoint('/admin/carriers')).toBe(true)
        expect(isCommerceHubVendorSessionAuthorityEndpoint('/coupons')).toBe(true)
        expect(isCommerceHubVendorSessionAuthorityEndpoint('/promotions')).toBe(true)
        expect(isCommerceHubVendorSessionAuthorityEndpoint('/shippingmethods')).toBe(true)
        expect(isCommerceHubVendorSessionAuthorityEndpoint('/storefront/config/preview')).toBe(true)
        expect(isCommerceHubVendorSessionAuthorityEndpoint('/admin/pages')).toBe(true)
        expect(isCommerceHubVendorSessionAuthorityEndpoint('/admin/pages/11111111-1111-4111-8111-111111111111/preview-link')).toBe(true)
        expect(isCommerceHubVendorSessionAuthorityEndpoint('/admin/cms/navigation')).toBe(true)
        expect(isCommerceHubVendorSessionAuthorityEndpoint('/admin/footer')).toBe(true)
        expect(isCommerceHubVendorSessionAuthorityEndpoint('/commercehub/finance/dashboard')).toBe(true)
        expect(isCommerceHubVendorSessionAuthorityEndpoint('/commercehub/earnings/dashboard')).toBe(true)
        expect(isCommerceHubVendorSessionAuthorityEndpoint('/commercehub/payments/dashboard')).toBe(true)
        expect(isCommerceHubVendorSessionAuthorityEndpoint('/api/vendors/payout-methods')).toBe(true)
        expect(isCommerceHubVendorSessionAuthorityEndpoint('/admin/affiliates')).toBe(true)
        expect(isCommerceHubVendorSessionAuthorityEndpoint('/admin/affiliates/11111111-1111-4111-8111-111111111111')).toBe(true)
        expect(isCommerceHubVendorSessionAuthorityEndpoint('/admin/products/33333333-3333-4333-8333-333333333333/affiliate')).toBe(true)
        expect(isCommerceHubVendorSessionAuthorityEndpoint('/admin/products/33333333-3333-4333-8333-333333333333/affiliate/enable-with-category')).toBe(true)
        expect(isCommerceHubVendorSessionAuthorityEndpoint('/admin/affiliate/categories/tree')).toBe(true)
        expect(isCommerceHubVendorSessionAuthorityEndpoint('/products')).toBe(false)
        expect(isCommerceHubVendorSessionAuthorityEndpoint('/admin/products/33333333-3333-4333-8333-333333333333')).toBe(false)
        expect(isCommerceHubVendorSessionAuthorityEndpoint('/super-admin/marketplace/pages')).toBe(false)
    })

    it('accepts only same-origin internal redirect candidates', () => {
        expect(resolvePosRouteRedirect('/commercehub/finance?tab=settlements#latest')).toBe(
            '/commercehub/finance?tab=settlements#latest',
        )
        expect(resolvePosRouteRedirect('https://example.com/commercehub/finance')).toBe('/')
        expect(resolvePosRouteRedirect('//example.com/commercehub/finance')).toBe('/')
        expect(resolvePosRouteRedirect('/commercehub\\finance')).toBe('/')
        expect(resolvePosRouteRedirect({ pathname: '/commercehub/finance' })).toBe('/')
    })
})
