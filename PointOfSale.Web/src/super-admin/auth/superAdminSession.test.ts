import { afterEach, describe, expect, it } from 'vitest'
import {
    deriveSuperAdminSessionAuthority,
    hasSuperAdminNavigationCapability,
    mapSessionToIdentity,
} from './superAdminAuth.types'
import {
    SUPER_ADMIN_ACCESS_TOKEN_EXPIRES_AT_KEY,
    SUPER_ADMIN_REFRESH_TOKEN_EXPIRES_AT_KEY,
    canRefreshSuperAdminSession,
    clearStoredSuperAdminSessionExpirations,
    isSuperAdminAccessTokenExpired,
    storeSuperAdminSessionExpirations,
} from './superAdminSession'

describe('superAdminSession', () => {
    const navigationCapabilities = [
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
    ] as const

    afterEach(() => {
        installLocalStorage().clear()
    })

    it('stores access and refresh expiration timestamps from the session payload', () => {
        const storage = installLocalStorage()

        storeSuperAdminSessionExpirations({
            superAdminId: '11111111-1111-4111-8111-111111111111',
            email: 'owner@commercehub.com',
            role: 'SuperAdmin',
            tokenType: 'Bearer',
            expiresAtUtc: '2030-04-01T12:00:00Z',
            refreshTokenExpiresAtUtc: '2030-04-02T12:00:00Z',
            tokenVersion: 3,
            navigationCapabilities: [...navigationCapabilities],
        })

        expect(storage.getItem(SUPER_ADMIN_ACCESS_TOKEN_EXPIRES_AT_KEY)).toBe('2030-04-01T12:00:00.000Z')
        expect(storage.getItem(SUPER_ADMIN_REFRESH_TOKEN_EXPIRES_AT_KEY)).toBe('2030-04-02T12:00:00.000Z')
    })

    it('detects access-token expiry from the stored expiration timestamp', () => {
        const storage = installLocalStorage()
        storage.setItem(SUPER_ADMIN_ACCESS_TOKEN_EXPIRES_AT_KEY, '2030-04-01T12:00:00Z')

        expect(isSuperAdminAccessTokenExpired()).toBe(false)

        storage.setItem(SUPER_ADMIN_ACCESS_TOKEN_EXPIRES_AT_KEY, '2020-04-01T12:00:00Z')
        expect(isSuperAdminAccessTokenExpired()).toBe(true)
    })

    it('allows refresh when the stored refresh expiration is still in the future', () => {
        const storage = installLocalStorage()
        storage.setItem(SUPER_ADMIN_REFRESH_TOKEN_EXPIRES_AT_KEY, '2030-04-02T12:00:00Z')

        expect(canRefreshSuperAdminSession()).toBe(true)

        storage.setItem(SUPER_ADMIN_REFRESH_TOKEN_EXPIRES_AT_KEY, '2020-04-02T12:00:00Z')
        expect(canRefreshSuperAdminSession()).toBe(false)
    })

    it('clears stored expiration timestamps on logout', () => {
        const storage = installLocalStorage()
        storage.setItem(SUPER_ADMIN_ACCESS_TOKEN_EXPIRES_AT_KEY, '2030-04-01T12:00:00Z')
        storage.setItem(SUPER_ADMIN_REFRESH_TOKEN_EXPIRES_AT_KEY, '2030-04-02T12:00:00Z')

        clearStoredSuperAdminSessionExpirations()

        expect(storage.getItem(SUPER_ADMIN_ACCESS_TOKEN_EXPIRES_AT_KEY)).toBeNull()
        expect(storage.getItem(SUPER_ADMIN_REFRESH_TOKEN_EXPIRES_AT_KEY)).toBeNull()
    })

    it('accepts only current backend Super Admin session authority payloads', () => {
        const validSession = {
            superAdminId: '11111111-1111-4111-8111-111111111111',
            email: 'owner@commercehub.com',
            role: 'SuperAdmin',
            tokenType: 'Bearer',
            expiresAtUtc: '2030-04-01T12:00:00Z',
            refreshTokenExpiresAtUtc: '2030-04-02T12:00:00Z',
            tokenVersion: 3,
            navigationCapabilities: [...navigationCapabilities],
        }

        expect(deriveSuperAdminSessionAuthority(validSession)).toBe(true)
        expect(mapSessionToIdentity(validSession)).toEqual({
            superAdminId: '11111111-1111-4111-8111-111111111111',
            email: 'owner@commercehub.com',
            role: 'SuperAdmin',
            authType: 'super_admin',
            navigationCapabilities: [...navigationCapabilities],
        })

        expect(hasSuperAdminNavigationCapability(
            mapSessionToIdentity(validSession),
            'super-admin.marketplace.govern',
        )).toBe(true)

        expect(deriveSuperAdminSessionAuthority({
            ...validSession,
            navigationCapabilities: ['super-admin.finance.view'],
        })).toBe(true)

        expect(deriveSuperAdminSessionAuthority({ ...validSession, role: 'Admin' })).toBe(false)
        expect(deriveSuperAdminSessionAuthority({ ...validSession, tokenType: 'Basic' })).toBe(false)
        expect(deriveSuperAdminSessionAuthority({ ...validSession, tokenVersion: -1 })).toBe(false)
        expect(deriveSuperAdminSessionAuthority({ ...validSession, expiresAtUtc: '2020-04-01T12:00:00Z' })).toBe(false)
        expect(deriveSuperAdminSessionAuthority({ ...validSession, navigationCapabilities: [] })).toBe(false)
        expect(deriveSuperAdminSessionAuthority({
            ...validSession,
            navigationCapabilities: ['super-admin.finance.view', 'super-admin.finance.view'],
        })).toBe(false)
        expect(deriveSuperAdminSessionAuthority({
            ...validSession,
            navigationCapabilities: ['super-admin.finance.view', 'fabricated.capability'],
        })).toBe(false)
    })
})

function installLocalStorage() {
    const storage = new Map<string, string>()
    const localStorageMock = {
        getItem(key: string) {
            return storage.has(key) ? storage.get(key)! : null
        },
        setItem(key: string, value: string) {
            storage.set(key, value)
        },
        removeItem(key: string) {
            storage.delete(key)
        },
        clear() {
            storage.clear()
        },
    }

    Object.defineProperty(globalThis, 'localStorage', {
        configurable: true,
        value: localStorageMock,
    })

    return localStorageMock
}
