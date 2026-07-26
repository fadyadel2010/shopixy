export const POS_VENDOR_SESSION_AUTHORITY_LOST_EVENT = 'pos-vendor-session-authority-lost'
export const POS_VENDOR_SESSION_LOGOUT_STORAGE_KEY = 'pos_vendor_session_logout_at'

const COMMERCEHUB_VENDOR_AUTHORITY_ENDPOINTS = [
    '/admin/commercehub',
    '/admin/orders',
    '/admin/store-config',
    '/admin/commerce-ai',
    '/admin/analytics',
    '/admin/pixel-tracking',
    '/admin/ecommerce',
    '/admin/payments/manual-wallet',
    '/admin/payment-methods',
    '/admin/phones',
    '/admin/online-customers',
    '/admin/abandoned-checkouts',
    '/admin/whatsapp',
    '/admin/shipping',
    '/admin/carriers',
    '/admin/pages',
    '/admin/cms',
    '/admin/footer',
    '/storefront/store-config',
    '/storefront/config/preview',
    '/coupons',
    '/promotions',
    '/shippingmethods',
    '/commercehub/finance',
    '/commercehub/earnings',
    '/commercehub/payments',
    '/vendors/payout-methods',
]

const AFFILIATE_CONFIGURATION_AUTHORITY_ENDPOINTS = [
    '/admin/affiliates',
    '/admin/affiliate/categories',
]

const isProductAffiliateConfigurationAuthorityEndpoint = (url) => (
    /\/admin\/products\/[^/]+\/affiliate(?:\/|$)/.test(url)
)

const normalizeString = (value) => (typeof value === 'string' ? value.trim() : '')
const POS_ACTOR_ROLES = new Set(['Owner', 'Admin', 'Manager', 'Cashier', 'Warehouse'])
export const COMMERCEHUB_VENDOR_LIFECYCLE_STATUSES = new Set([
    'Pending',
    'Approved',
    'Active',
    'Suspended',
    'Blocked',
    'DebtBlocked',
    'Disabled',
    'Rejected',
    'Terminated',
])
export const COMMERCEHUB_VENDOR_NAVIGATION_CAPABILITIES = new Set([
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
])
const GUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i
const OPERATING_LIFECYCLE_STATUSES = new Set(['Approved', 'Active'])
const OPERATING_CAPABILITIES = new Set([
    'commercehub.catalog.view',
    'commercehub.catalog.manage',
    'commercehub.orders.view',
    'commercehub.orders.operate',
    'commercehub.analytics.view',
    'commercehub.storefront.manage',
    'commercehub.storefront.cms.manage',
    'commercehub.storefront.preview',
])
const LIFECYCLE_BOOLEAN_FIELDS = [
    'tenantActive',
    'marketplaceApproved',
    'kycVerified',
    'isDebtBlocked',
    'blocksCatalog',
    'blocksOrders',
    'blocksStorefront',
    'blocksAnalytics',
    'blocksPayments',
    'blocksPayouts',
    'blocksLogin',
]

export const deriveCommerceHubVendorLifecyclePresentation = (value, tenantId) => {
    if (!value || typeof value !== 'object' || Array.isArray(value)) {
        return null
    }

    const vendorId = normalizeString(value.vendorId)
    const status = normalizeString(value.status)
    const debtStatus = normalizeString(value.debtStatus)

    if (
        !GUID_PATTERN.test(vendorId) ||
        vendorId !== tenantId ||
        !COMMERCEHUB_VENDOR_LIFECYCLE_STATUSES.has(status) ||
        !debtStatus ||
        LIFECYCLE_BOOLEAN_FIELDS.some((field) => typeof value[field] !== 'boolean')
    ) {
        return null
    }

    if (status === 'DebtBlocked' && value.isDebtBlocked !== true) {
        return null
    }

    if (status !== 'DebtBlocked' && value.isDebtBlocked === true) {
        return null
    }

    if (!OPERATING_LIFECYCLE_STATUSES.has(status)) {
        const requiredBlocks = [
            value.blocksCatalog,
            value.blocksOrders,
            value.blocksStorefront,
            value.blocksAnalytics,
            value.blocksPayouts,
        ]
        if (requiredBlocks.some((blocked) => blocked !== true)) {
            return null
        }
    }

    return {
        vendorId,
        status,
        governanceStatus: normalizeString(value.governanceStatus) || null,
        tenantActive: value.tenantActive,
        marketplaceApproved: value.marketplaceApproved,
        kycVerified: value.kycVerified,
        debtStatus,
        isDebtBlocked: value.isDebtBlocked,
        blocksCatalog: value.blocksCatalog,
        blocksOrders: value.blocksOrders,
        blocksStorefront: value.blocksStorefront,
        blocksAnalytics: value.blocksAnalytics,
        blocksPayments: value.blocksPayments,
        blocksPayouts: value.blocksPayouts,
        blocksLogin: value.blocksLogin,
    }
}

const lifecyclePermitsCapabilities = (lifecycle, capabilities) => {
    if (OPERATING_LIFECYCLE_STATUSES.has(lifecycle.status)) {
        return true
    }

    return capabilities.every((capability) => !OPERATING_CAPABILITIES.has(capability))
}

export const derivePosVendorSessionUserAuthority = (value) => {
    if (!value || typeof value !== 'object' || Array.isArray(value)) {
        return null
    }

    const id = normalizeString(value.id ?? value.userId)
    const tenantId = normalizeString(value.tenantId)
    const role = normalizeString(value.role)
    const navigationCapabilities = Array.isArray(value.navigationCapabilities)
        ? value.navigationCapabilities
        : null
    const vendorLifecycleAuthority = deriveCommerceHubVendorLifecyclePresentation(
        value.vendorLifecycleAuthority,
        tenantId,
    )

    if (
        !GUID_PATTERN.test(id) ||
        !GUID_PATTERN.test(tenantId) ||
        !POS_ACTOR_ROLES.has(role) ||
        !navigationCapabilities ||
        navigationCapabilities.some((capability) => !COMMERCEHUB_VENDOR_NAVIGATION_CAPABILITIES.has(capability)) ||
        new Set(navigationCapabilities).size !== navigationCapabilities.length ||
        !vendorLifecycleAuthority ||
        !lifecyclePermitsCapabilities(vendorLifecycleAuthority, navigationCapabilities)
    ) {
        return null
    }

    return {
        ...value,
        id,
        tenantId,
        role,
        navigationCapabilities,
        vendorLifecycleAuthority,
    }
}

export const hasCommerceHubVendorNavigationCapability = (user, capability) =>
    user?.navigationCapabilities?.includes(capability) === true

export const canAccessCommerceHubVendorNavigationCapability = (user, capability) => {
    const sessionUser = derivePosVendorSessionUserAuthority(user)

    if (!sessionUser || !hasCommerceHubVendorNavigationCapability(sessionUser, capability)) {
        return false
    }

    const lifecycle = sessionUser.vendorLifecycleAuthority

    if (capability === 'commercehub.catalog.manage' || capability === 'commercehub.catalog.view') {
        return lifecycle.blocksCatalog !== true
    }

    if (capability === 'commercehub.orders.operate' || capability === 'commercehub.orders.view') {
        return lifecycle.blocksOrders !== true
    }

    if (capability === 'commercehub.analytics.view') {
        return lifecycle.blocksAnalytics !== true
    }

    if (capability === 'commercehub.payments.submit') {
        return lifecycle.blocksPayments !== true
    }

    if (
        capability === 'commercehub.storefront.manage'
        || capability === 'commercehub.storefront.cms.manage'
        || capability === 'commercehub.storefront.preview'
    ) {
        return lifecycle.blocksStorefront !== true
    }

    return true
}

const optionalBackendBoolean = (value) => value === undefined || value === null || typeof value === 'boolean'

export const deriveCommerceHubVendorEligibilityPresentation = (user, evidence = {}) => {
    const sessionUser = derivePosVendorSessionUserAuthority(user)
    if (!sessionUser || !evidence || typeof evidence !== 'object' || Array.isArray(evidence)) {
        return null
    }

    const isSuspendedFromNewOrders = evidence.isSuspendedFromNewOrders
    const canSubmitPaymentEvidence = evidence.canSubmitPayment

    if (!optionalBackendBoolean(isSuspendedFromNewOrders) || !optionalBackendBoolean(canSubmitPaymentEvidence)) {
        return null
    }

    const canManageCatalog = hasCommerceHubVendorNavigationCapability(sessionUser, 'commercehub.catalog.manage')
        && sessionUser.vendorLifecycleAuthority.blocksCatalog !== true
    const canViewOrders = hasCommerceHubVendorNavigationCapability(sessionUser, 'commercehub.orders.view')
    const canOperateOrders = hasCommerceHubVendorNavigationCapability(sessionUser, 'commercehub.orders.operate')
        && sessionUser.vendorLifecycleAuthority.blocksOrders !== true
    const canViewFinance = hasCommerceHubVendorNavigationCapability(sessionUser, 'commercehub.finance.view')
    const canViewPayments = hasCommerceHubVendorNavigationCapability(sessionUser, 'commercehub.payments.view')
    const canViewAnalytics = hasCommerceHubVendorNavigationCapability(sessionUser, 'commercehub.analytics.view')
        && sessionUser.vendorLifecycleAuthority.blocksAnalytics !== true
    const canManageStorefront = hasCommerceHubVendorNavigationCapability(sessionUser, 'commercehub.storefront.manage')
        && sessionUser.vendorLifecycleAuthority.blocksStorefront !== true
    const canManageCms = hasCommerceHubVendorNavigationCapability(sessionUser, 'commercehub.storefront.cms.manage')
        && sessionUser.vendorLifecycleAuthority.blocksStorefront !== true
    const canPreviewStorefront = hasCommerceHubVendorNavigationCapability(sessionUser, 'commercehub.storefront.preview')
        && sessionUser.vendorLifecycleAuthority.blocksStorefront !== true
    const canSubmitPayment = hasCommerceHubVendorNavigationCapability(sessionUser, 'commercehub.payments.submit')
        && sessionUser.vendorLifecycleAuthority.blocksPayments !== true
        && canSubmitPaymentEvidence === true

    return {
        tenantId: sessionUser.tenantId,
        userId: sessionUser.id,
        lifecycleStatus: sessionUser.vendorLifecycleAuthority.status,
        canManageCatalog,
        canViewOrders,
        canOperateOrders,
        canViewFinance,
        canViewPayments,
        canViewAnalytics,
        canManageStorefront,
        canManageCms,
        canPreviewStorefront,
        canSubmitPayment,
        isDebtBlocked: sessionUser.vendorLifecycleAuthority.status === 'DebtBlocked'
            && sessionUser.vendorLifecycleAuthority.isDebtBlocked === true
            && isSuspendedFromNewOrders === true,
    }
}

export const requirePosVendorSessionUserAuthority = (value) => {
    const user = derivePosVendorSessionUserAuthority(value)

    if (!user) {
        throw new Error('Invalid POS vendor session authority response')
    }

    return user
}

export const resolvePosRouteRedirect = (value, fallback = '/') => {
    const candidate = typeof value === 'string' ? value : ''
    if (!candidate.startsWith('/') || candidate.startsWith('//') || candidate.includes('\\')) {
        return fallback
    }

    try {
        const url = new URL(candidate, window.location.origin)
        if (url.origin !== window.location.origin) {
            return fallback
        }

        return `${url.pathname}${url.search}${url.hash}`
    } catch {
        return fallback
    }
}

export const isCommerceHubVendorSessionAuthorityEndpoint = (url = '') => {
    const normalizedUrl = String(url || '')
    return isProductAffiliateConfigurationAuthorityEndpoint(normalizedUrl) || [
        ...COMMERCEHUB_VENDOR_AUTHORITY_ENDPOINTS,
        ...AFFILIATE_CONFIGURATION_AUTHORITY_ENDPOINTS,
    ].some((endpoint) => normalizedUrl.includes(endpoint))
}

export const dispatchPosVendorSessionAuthorityLost = () => {
    if (typeof window === 'undefined') {
        return
    }

    window.dispatchEvent(new Event(POS_VENDOR_SESSION_AUTHORITY_LOST_EVENT))
}

export const broadcastPosVendorLogout = () => {
    dispatchPosVendorSessionAuthorityLost()

    if (typeof window === 'undefined' || !window.localStorage) {
        return
    }

    try {
        window.localStorage.setItem(POS_VENDOR_SESSION_LOGOUT_STORAGE_KEY, new Date().toISOString())
        window.localStorage.removeItem(POS_VENDOR_SESSION_LOGOUT_STORAGE_KEY)
    } catch {
        // Storage is an optional terminal signal; backend revalidation remains authoritative.
    }
}
