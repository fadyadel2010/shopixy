import { describe, expect, it } from 'vitest'
import { shouldInvalidateSuperAdminSession } from './superAdminApiClient'

describe('Super Admin API session invalidation policy', () => {
    it('invalidates only on authentication loss, not ordinary authorization denial', () => {
        expect(shouldInvalidateSuperAdminSession(401)).toBe(true)
        expect(shouldInvalidateSuperAdminSession(403)).toBe(false)
        expect(shouldInvalidateSuperAdminSession(500)).toBe(false)
        expect(shouldInvalidateSuperAdminSession(undefined)).toBe(false)
    })
})
