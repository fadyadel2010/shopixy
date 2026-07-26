import type { SuperAdminSessionResponse } from './superAdminAuth.types'

export const SUPER_ADMIN_ACCESS_TOKEN_EXPIRES_AT_KEY = 'superAdminAccessTokenExpiresAtUtc'
export const SUPER_ADMIN_REFRESH_TOKEN_EXPIRES_AT_KEY = 'superAdminRefreshTokenExpiresAtUtc'

const CLOCK_SKEW_MS = 30_000

export function storeSuperAdminSessionExpirations(session: SuperAdminSessionResponse): void {
    writeStorageValue(SUPER_ADMIN_ACCESS_TOKEN_EXPIRES_AT_KEY, session.expiresAtUtc)
    writeStorageValue(SUPER_ADMIN_REFRESH_TOKEN_EXPIRES_AT_KEY, session.refreshTokenExpiresAtUtc)
}

export function clearStoredSuperAdminSessionExpirations(): void {
    removeStorageValue(SUPER_ADMIN_ACCESS_TOKEN_EXPIRES_AT_KEY)
    removeStorageValue(SUPER_ADMIN_REFRESH_TOKEN_EXPIRES_AT_KEY)
}

export function getStoredSuperAdminAccessTokenExpirationUtc(): string | null {
    return readStorageValue(SUPER_ADMIN_ACCESS_TOKEN_EXPIRES_AT_KEY)
}

export function getStoredSuperAdminRefreshTokenExpirationUtc(): string | null {
    return readStorageValue(SUPER_ADMIN_REFRESH_TOKEN_EXPIRES_AT_KEY)
}

export function isSuperAdminAccessTokenExpired(): boolean {
    const explicitExpiration = getStoredSuperAdminAccessTokenExpirationUtc()
    const resolvedExpiration = normalizeUtcTimestamp(explicitExpiration)

    if (!resolvedExpiration) {
        return false
    }

    return isUtcTimestampExpired(resolvedExpiration)
}

export function canRefreshSuperAdminSession(): boolean {
    const refreshExpiration = getStoredSuperAdminRefreshTokenExpirationUtc()
    if (!refreshExpiration) {
        return false
    }

    return !isUtcTimestampExpired(refreshExpiration)
}

export function isUtcTimestampExpired(value: string, clockSkewMs = CLOCK_SKEW_MS): boolean {
    const normalizedValue = normalizeUtcTimestamp(value)
    if (!normalizedValue) {
        return false
    }

    return Date.parse(normalizedValue) <= Date.now() + clockSkewMs
}

function normalizeUtcTimestamp(value: string | null | undefined): string | null {
    if (!value || !value.trim()) {
        return null
    }

    const parsedValue = Date.parse(value)
    if (Number.isNaN(parsedValue)) {
        return null
    }

    return new Date(parsedValue).toISOString()
}

// ---------------------------------------------------------------------------
// Safe-storage wrapper – centralises the localStorage guard + try/catch so
// that all three operations share identical error-handling behaviour.
// ---------------------------------------------------------------------------
const safeStorage = {
    getItem(key: string): string | null {
        if (typeof localStorage === 'undefined') {
            return null
        }
        try {
            return localStorage.getItem(key)
        } catch (error) {
            // Swallow DOMException in restricted environments
            console.warn(`Failed to read "${key}" from localStorage:`, error)
            return null
        }
    },

    setItem(key: string, value: string): void {
        if (typeof localStorage === 'undefined') {
            return
        }
        try {
            localStorage.setItem(key, value)
        } catch (error) {
            // Swallow DOMException in restricted environments
            console.warn(`Failed to write "${key}" to localStorage:`, error)
        }
    },

    removeItem(key: string): void {
        if (typeof localStorage === 'undefined') {
            return
        }
        try {
            localStorage.removeItem(key)
        } catch (error) {
            // Swallow DOMException in restricted environments
            console.warn(`Failed to remove "${key}" from localStorage:`, error)
        }
    },
}

function readStorageValue(key: string): string | null {
    return safeStorage.getItem(key)
}

function writeStorageValue(key: string, value: string | null | undefined): void {
    const normalizedValue = normalizeUtcTimestamp(value)
    if (!normalizedValue) {
        safeStorage.removeItem(key)
        return
    }
    safeStorage.setItem(key, normalizedValue)
}

function removeStorageValue(key: string): void {
    safeStorage.removeItem(key)
}
