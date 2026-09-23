import { describe, expect, it } from 'vitest'
import en from './locales/en.json'
import ar from './locales/ar.json'

const collectLeaves = (value, prefix, out) => {
    if (value !== null && typeof value === 'object' && !Array.isArray(value)) {
        for (const [key, child] of Object.entries(value)) {
            collectLeaves(child, prefix ? `${prefix}.${key}` : key, out)
        }
        return out
    }
    out.set(prefix, value)
    return out
}

const placeholders = (value) => {
    if (typeof value !== 'string') return []
    return [...value.matchAll(/\{\{(\w+)\}\}/g)].map((match) => match[1]).sort()
}

describe('userManagement locales', () => {
    it('keeps the userManagement key tree symmetric between English and Arabic', () => {
        const enLeaves = collectLeaves(en.userManagement, '', new Map())
        const arLeaves = collectLeaves(ar.userManagement, '', new Map())

        expect(enLeaves.size).toBeGreaterThan(50)
        expect([...enLeaves.keys()].sort()).toEqual([...arLeaves.keys()].sort())
    })

    it('has complete non-empty translations with matching placeholders', () => {
        const enLeaves = collectLeaves(en.userManagement, '', new Map())
        const arLeaves = collectLeaves(ar.userManagement, '', new Map())

        for (const [key, enValue] of enLeaves) {
            const arValue = arLeaves.get(key)
            expect(typeof enValue, key).toBe('string')
            expect(enValue.trim().length, `en:${key}`).toBeGreaterThan(0)
            expect(typeof arValue, `ar:${key}`).toBe('string')
            expect(arValue.trim().length, `ar:${key}`).toBeGreaterThan(0)
            // No raw-key leakage: a rendered value must never equal its key.
            expect(arValue, `ar:${key}`).not.toBe(key)
            expect(placeholders(arValue)).toEqual(placeholders(enValue))
        }
    })

    it('translates Arabic without English leakage', () => {
        const enLeaves = collectLeaves(en.userManagement, '', new Map())
        const arLeaves = collectLeaves(ar.userManagement, '', new Map())

        for (const [key, enValue] of enLeaves) {
            expect(arLeaves.get(key), `ar:${key}`).not.toBe(enValue)
        }
    })

    it('covers the new audit actions and user-id filter in both locales', () => {
        const actions = [
            'pos_user_invited', 'pos_user_invitation_resent', 'pos_user_invitation_revoked',
            'pos_user_invitation_accepted', 'pos_user_profile_changed', 'pos_user_role_changed',
            'pos_user_disabled', 'pos_user_enabled', 'pos_user_password_reset_issued',
            'pos_user_password_reset_completed', 'pos_user_session_revoked', 'pos_user_ownership_transferred',
        ]
        for (const action of actions) {
            expect(en.auditLog.actions[action], `en:${action}`).toBeTruthy()
            expect(ar.auditLog.actions[action], `ar:${action}`).toBeTruthy()
            expect(ar.auditLog.actions[action]).not.toBe(en.auditLog.actions[action])
        }
        expect(en.auditLog.userId).toBe('User ID')
        expect(ar.auditLog.userId).toBeTruthy()
        expect(ar.auditLog.userId).not.toBe(en.auditLog.userId)
    })
})
