export const SUPER_ADMIN_TOKEN_VERSION_KEY = 'superAdminTokenVersion'

export interface SuperAdminApiMeta {
    requestId?: string
    timestamp?: string
    page?: number
    pageSize?: number
    totalCount?: number
    totalPages?: number
}

export interface SuperAdminApiSuccessResponse<T> {
    success: true
    data: T
    meta?: SuperAdminApiMeta
}

export interface SuperAdminApiErrorResponse {
    success: false
    error: {
        code?: string
        message?: string
    }
    meta?: SuperAdminApiMeta
}

export interface SuperAdminSessionResponse {
    superAdminId: string
    email: string
    role: string
    tokenType: string
    expiresAtUtc: string
    refreshTokenExpiresAtUtc: string
    tokenVersion: number
    navigationCapabilities: SuperAdminNavigationCapability[]
}

export interface SuperAdminIdentity {
    superAdminId: string
    email: string
    role: string
    authType: 'super_admin'
    navigationCapabilities: SuperAdminNavigationCapability[]
}

export type SuperAdminNavigationCapability =
    | 'super-admin.finance.view'
    | 'super-admin.payments.view'
    | 'super-admin.payment-providers.view'
    | 'super-admin.orders.view'
    | 'super-admin.reviews.view'
    | 'super-admin.users.govern'
    | 'super-admin.marketplace.govern'
    | 'super-admin.marketplace.operate'
    | 'super-admin.refunds.manage'
    | 'super-admin.payouts.manage'
    | 'super-admin.commission.manage'
    | 'super-admin.promotions.manage'
    | 'super-admin.homepage.manage'
    | 'super-admin.storefront-defaults.manage'
    | 'super-admin.marketplace-cms.manage'
    | 'super-admin.marketplace-footer.manage'

export const SUPER_ADMIN_NAVIGATION_CAPABILITIES = new Set<SuperAdminNavigationCapability>([
    'super-admin.finance.view',
    'super-admin.payments.view',
    'super-admin.payment-providers.view',
    'super-admin.orders.view',
    'super-admin.reviews.view',
    'super-admin.users.govern',
    'super-admin.marketplace.govern',
    'super-admin.marketplace.operate',
    'super-admin.refunds.manage',
    'super-admin.payouts.manage',
    'super-admin.commission.manage',
    'super-admin.promotions.manage',
    'super-admin.homepage.manage',
    'super-admin.storefront-defaults.manage',
    'super-admin.marketplace-cms.manage',
    'super-admin.marketplace-footer.manage',
])

export interface SuperAdminLoginRequest {
    email: string
    password: string
}

export interface SuperAdminRefreshRequest {
    tokenVersion?: number
}

export function mapSessionToIdentity(session: SuperAdminSessionResponse): SuperAdminIdentity {
    const authority = requireSuperAdminSessionAuthority(session)

    return {
        superAdminId: authority.superAdminId,
        email: authority.email,
        role: authority.role,
        authType: 'super_admin',
        navigationCapabilities: authority.navigationCapabilities,
    }
}

export function requireSuperAdminSessionAuthority(session: SuperAdminSessionResponse): SuperAdminSessionResponse {
    if (!deriveSuperAdminSessionAuthority(session)) {
        throw new Error('Super Admin session authority payload was invalid.')
    }

    return session
}

export function deriveSuperAdminSessionAuthority(
    session: SuperAdminSessionResponse | null | undefined,
): session is SuperAdminSessionResponse {
    if (!session || typeof session !== 'object') {
        return false
    }

    return (
        isValidGuid(session.superAdminId) &&
        isValidEmail(session.email) &&
        session.role === 'SuperAdmin' &&
        session.tokenType === 'Bearer' &&
        isFutureUtcTimestamp(session.expiresAtUtc) &&
        isFutureUtcTimestamp(session.refreshTokenExpiresAtUtc) &&
        Number.isInteger(session.tokenVersion) &&
        session.tokenVersion >= 0 &&
        Array.isArray(session.navigationCapabilities) &&
        session.navigationCapabilities.length > 0 &&
        session.navigationCapabilities.every((capability) => SUPER_ADMIN_NAVIGATION_CAPABILITIES.has(capability)) &&
        new Set(session.navigationCapabilities).size === session.navigationCapabilities.length
    )
}

export function hasSuperAdminNavigationCapability(
    identity: SuperAdminIdentity | null | undefined,
    capability: SuperAdminNavigationCapability,
): boolean {
    return identity?.navigationCapabilities.includes(capability) === true
}

function isValidGuid(value: unknown): value is string {
    return (
        isNonEmptyString(value) &&
        /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(value)
    )
}

function isValidEmail(value: unknown): value is string {
    return isNonEmptyString(value) && /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value)
}

function isNonEmptyString(value: unknown): value is string {
    return typeof value === 'string' && value.trim().length > 0
}

function isFutureUtcTimestamp(value: unknown): value is string {
    if (!isNonEmptyString(value)) {
        return false
    }

    const parsed = Date.parse(value)
    return !Number.isNaN(parsed) && parsed > Date.now()
}
