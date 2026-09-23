export const userErrorKey = (error) => {
    const status = error?.response?.status
    const code = error?.response?.data?.code

    if (status === 412 || code === 'USER_VERSION_CONFLICT') return 'errors.staleData'
    if (status === 403 || code === 'POS_PERMISSION_FORBIDDEN' || code === 'ADMIN_SCOPE_FORBIDDEN') return 'errors.accessDenied'
    if (status === 404 || code === 'USER_NOT_FOUND') return 'errors.notFound'
    if (status === 429 || code === 'RATE_LIMITED') return 'errors.rateLimited'
    if (code === 'IDENTITY_ALREADY_EXISTS') return 'errors.conflict'
    if (code === 'OWNER_INVARIANT_VIOLATION') return 'errors.ownerViolation'
    if (code === 'TARGET_ROLE_FORBIDDEN') return 'errors.targetForbidden'
    if (code === 'SECURITY_TOKEN_EXPIRED') return 'errors.tokenExpired'
    if (status === 400 || status === 409) return 'errors.validation'
    return 'errors.generic'
}

export const isVersionConflict = (error) =>
    error?.response?.status === 412 || error?.response?.data?.code === 'USER_VERSION_CONFLICT'

export const isAccessDenied = (error) =>
    error?.response?.status === 403
    || error?.response?.data?.code === 'POS_PERMISSION_FORBIDDEN'
    || error?.response?.data?.code === 'ADMIN_SCOPE_FORBIDDEN'
