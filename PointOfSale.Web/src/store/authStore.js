import { create } from 'zustand'
import { derivePosVendorSessionUserAuthority } from '../auth/posVendorSessionAuthority'

export const STORAGE_KEYS = {
    TENANT_ID: 'tenantId',
    AUTH_STORAGE: 'auth-storage',
}

export const LEGACY_AUTH_TOKEN_KEYS = ['accessToken', 'refreshToken']

const hasStorage = () => typeof window !== 'undefined' && typeof window.localStorage !== 'undefined'

const removeStoredValue = (key) => {
    if (!hasStorage()) {
        return
    }

    window.localStorage.removeItem(key)
}

export const clearLegacyPosTokenStorage = () => {
    LEGACY_AUTH_TOKEN_KEYS.forEach((key) => removeStoredValue(key))
}

const writeUserSession = (user) => {
    clearLegacyPosTokenStorage()
    removeStoredValue(STORAGE_KEYS.TENANT_ID)
}

const clearStoredSession = () => {
    clearLegacyPosTokenStorage()
    removeStoredValue(STORAGE_KEYS.TENANT_ID)
    removeStoredValue(STORAGE_KEYS.AUTH_STORAGE)
}

const applyUserSession = (set, user) => {
    const authorizedUser = derivePosVendorSessionUserAuthority(user)

    if (!authorizedUser) {
        clearStoredSession()
        set({ user: null, isAuthenticated: false, hasHydrated: true })
        return
    }

    writeUserSession(authorizedUser)
    set({ user: authorizedUser, isAuthenticated: true, hasHydrated: true })
}

export const useAuthStore = create((set) => ({
    user: null,
    isAuthenticated: false,
    hasHydrated: false,

    beginRouteAuthorityValidation: () => {
        set({ hasHydrated: false })
    },

    login: (user) => {
        applyUserSession(set, user)
    },

    logout: () => {
        clearStoredSession()
        set({ user: null, isAuthenticated: false, hasHydrated: true })
    },

    hydrateSession: (user) => {
        applyUserSession(set, user)
    },

    updateUser: (user) => {
        applyUserSession(set, user)
    },

    setHasHydrated: (hasHydrated) => set({ hasHydrated }),
}))
