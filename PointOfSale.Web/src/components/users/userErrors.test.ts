import { describe, expect, it } from 'vitest'
import { userErrorKey, isVersionConflict, isAccessDenied } from './userErrors'

const err = (status: number, code?: string) => ({ response: { status, data: { code } } })

describe('userErrors', () => {
    it('maps version conflicts to the stale-data message', () => {
        expect(userErrorKey(err(412, 'USER_VERSION_CONFLICT'))).toBe('errors.staleData')
        expect(isVersionConflict(err(412, 'USER_VERSION_CONFLICT'))).toBe(true)
        expect(isVersionConflict(err(400, 'VALIDATION_ERROR'))).toBe(false)
    })

    it('maps denials, conflicts, and invariant violations', () => {
        expect(userErrorKey(err(403, 'POS_PERMISSION_FORBIDDEN'))).toBe('errors.accessDenied')
        expect(userErrorKey(err(403, 'ADMIN_SCOPE_FORBIDDEN'))).toBe('errors.accessDenied')
        expect(isAccessDenied(err(403, 'FORBIDDEN'))).toBe(true)
        expect(userErrorKey(err(404, 'USER_NOT_FOUND'))).toBe('errors.notFound')
        expect(userErrorKey(err(429, 'RATE_LIMITED'))).toBe('errors.rateLimited')
        expect(userErrorKey(err(409, 'IDENTITY_ALREADY_EXISTS'))).toBe('errors.conflict')
        expect(userErrorKey(err(409, 'OWNER_INVARIANT_VIOLATION'))).toBe('errors.ownerViolation')
        expect(userErrorKey(err(409, 'TARGET_ROLE_FORBIDDEN'))).toBe('errors.targetForbidden')
        expect(userErrorKey(err(410, 'SECURITY_TOKEN_EXPIRED'))).toBe('errors.tokenExpired')
        expect(userErrorKey(err(400, 'VALIDATION_ERROR'))).toBe('errors.validation')
    })

    it('falls back to the generic message for unknown failures', () => {
        expect(userErrorKey(err(500, 'INTERNAL_ERROR'))).toBe('errors.generic')
        expect(userErrorKey(null)).toBe('errors.generic')
    })
})
