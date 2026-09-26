import { create } from 'zustand'
import { superAdminQueryClient } from '../../queryClient'
import {
    SUPER_ADMIN_TOKEN_VERSION_KEY,
    type SuperAdminIdentity,
    type SuperAdminSessionResponse,
    mapSessionToIdentity,
    requireSuperAdminSessionAuthority,
} from './superAdminAuth.types'
import {
    clearStoredSuperAdminSessionExpirations,
    storeSuperAdminSessionExpirations,
} from './superAdminSession'

export const LEGACY_SUPER_ADMIN_TOKEN_KEYS = ['superAdminAccessToken', 'superAdminRefreshToken']
export const SUPER_ADMIN_AUTH_STORAGE_KEY = 'super-admin-auth-storage'
export const SUPER_ADMIN_SESSION_LOGOUT_STORAGE_KEY = 'super_admin_session_logout_at'

interface SuperAdminAuthState {
    user: SuperAdminIdentity | null
    isAuthenticated: boolean
    hasHydrated: boolean
    setSession: (session: SuperAdminSessionResponse) => void
    hydrateSession: (session: SuperAdminSessionResponse | null) => void
    logout: (options?: { broadcast?: boolean }) => void
}

let superAdminAuthorityRevision = 0

export function getSuperAdminAuthorityRevision(): number {
    return superAdminAuthorityRevision
}

export function clearLegacySuperAdminTokenStorage(): void {
    if (typeof localStorage === 'undefined') {
        return
    }

    LEGACY_SUPER_ADMIN_TOKEN_KEYS.forEach((key) => {
        try {
            localStorage.removeItem(key)
        } catch (error) {
            // Swallow DOMException in restricted environments
            console.warn(`Failed to remove legacy token key "${key}":`, error)
        }
    })
}

/**
 * Returned by getStoredSuperAdminTokenVersion when the read succeeds but the
 * result is meaningless due to a storage or data failure — distinct from
 * `undefined`, which means the key simply isn't present.
 */
export type StorageReadError = { readonly error: 'storage-unavailable' | 'invalid-value' }

export function getStoredSuperAdminTokenVersion(): number | StorageReadError | undefined {
    if (typeof localStorage === 'undefined') {
        // SSR / restricted environment — key cannot exist.
        return undefined
    }

    try {
        const raw = localStorage.getItem(SUPER_ADMIN_TOKEN_VERSION_KEY)
        if (raw === null) {
            // Key is genuinely absent — not an error.
            return undefined
        }

        // Strict integer validation: must be non-negative digits only.
        if (!/^\d+$/.test(raw)) {
            return { error: 'invalid-value' }
        }

        const parsed = Number(raw)
        if (!Number.isInteger(parsed) || Number.isNaN(parsed) || parsed < 0) {
            return { error: 'invalid-value' }
        }
        return parsed
    } catch (error) {
        // DOMException from a restricted / private-browsing context.
        // Log at error level so this is distinguishable from a missing key.
        console.error('Failed to read super-admin token version from localStorage:', error)
        return { error: 'storage-unavailable' }
    }
}

function storeSuperAdminTokenVersion(tokenVersion: number): void {
    if (typeof localStorage === 'undefined') {
        return
    }

    try {
        localStorage.setItem(SUPER_ADMIN_TOKEN_VERSION_KEY, String(tokenVersion))
    } catch (error) {
        // Swallow DOMException in restricted environments
        console.warn('Failed to store token version in localStorage:', error)
    }
}

function clearStoredSuperAdminTokenVersion(): void {
    if (typeof localStorage === 'undefined') {
        return
    }

    try {
        localStorage.removeItem(SUPER_ADMIN_TOKEN_VERSION_KEY)
    } catch (error) {
        // Swallow DOMException in restricted environments
        console.warn('Failed to clear token version from localStorage:', error)
    }
}

function broadcastSuperAdminAuthorityLoss(): void {
    if (typeof localStorage === 'undefined') {
        return
    }

    try {
        localStorage.setItem(SUPER_ADMIN_SESSION_LOGOUT_STORAGE_KEY, String(Date.now()))
    } catch (error) {
        console.warn('Failed to broadcast Super Admin authority loss:', error)
    }
}

function purgeSuperAdminRuntimeAuthority(): void {
    superAdminQueryClient.clear()
}

function hasSameSuperAdminIdentity(
    current: SuperAdminIdentity | null,
    next: SuperAdminIdentity,
): boolean {
    return current === next || JSON.stringify(current) === JSON.stringify(next)
}

export const useSuperAdminAuthStore = create<SuperAdminAuthState>()(
    (set, get) => ({
        user: null,
        isAuthenticated: false,
        hasHydrated: false,

        setSession: (session) => {
            const authority = requireSuperAdminSessionAuthority(session)
            const nextIdentity = mapSessionToIdentity(authority)
            const currentState = get()
            superAdminAuthorityRevision += 1
            clearLegacySuperAdminTokenStorage()
            storeSuperAdminTokenVersion(authority.tokenVersion)
            storeSuperAdminSessionExpirations(authority)

            if (currentState.isAuthenticated && hasSameSuperAdminIdentity(currentState.user, nextIdentity)) {
                if (!currentState.hasHydrated) {
                    set({ hasHydrated: true })
                }
                return
            }

            set({
                user: nextIdentity,
                isAuthenticated: true,
                hasHydrated: true,
            })
        },

        hydrateSession: (session) => {
            superAdminAuthorityRevision += 1
            clearLegacySuperAdminTokenStorage()

            if (!session) {
                clearStoredSuperAdminTokenVersion()
                clearStoredSuperAdminSessionExpirations()
                purgeSuperAdminRuntimeAuthority()

                set({
                    user: null,
                    isAuthenticated: false,
                    hasHydrated: true,
                })

                return
            }

            const authority = requireSuperAdminSessionAuthority(session)
            const nextIdentity = mapSessionToIdentity(authority)
            const currentState = get()
            storeSuperAdminTokenVersion(authority.tokenVersion)
            storeSuperAdminSessionExpirations(authority)

            if (currentState.isAuthenticated && hasSameSuperAdminIdentity(currentState.user, nextIdentity)) {
                if (!currentState.hasHydrated) {
                    set({ hasHydrated: true })
                }
                return
            }

            set({
                user: nextIdentity,
                isAuthenticated: true,
                hasHydrated: true,
            })
        },

        logout: (options) => {
            superAdminAuthorityRevision += 1
            clearLegacySuperAdminTokenStorage()
            clearStoredSuperAdminTokenVersion()
            clearStoredSuperAdminSessionExpirations()
            purgeSuperAdminRuntimeAuthority()

            if (options?.broadcast !== false) {
                broadcastSuperAdminAuthorityLoss()
            }

            set({
                user: null,
                isAuthenticated: false,
                hasHydrated: true,
            })
        },
    }),
)
