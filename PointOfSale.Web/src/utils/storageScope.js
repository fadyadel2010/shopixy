import { useAuthStore } from '../store/authStore'

export const normalizeStorageSegment = (value, fallback) => {
    const rawValue = typeof value === 'string'
        ? value.trim()
        : value == null
            ? ''
            : String(value)

    if (!rawValue) {
        return fallback
    }

    return rawValue.replace(/[^a-zA-Z0-9_-]+/g, '_')
}

export const readPersistedAuthUser = () => {
    return useAuthStore.getState().user || null
}

export const getTenantStorageScope = (fallback = 'tenant_global') => {
    const persistedUser = readPersistedAuthUser()

    return normalizeStorageSegment(
        persistedUser?.tenantId,
        fallback,
    )
}

export const getUserStorageScope = (fallback = 'user_anonymous') => {
    const persistedUser = readPersistedAuthUser()

    return normalizeStorageSegment(
        persistedUser?.id || persistedUser?.userId || persistedUser?.email || persistedUser?.username,
        fallback,
    )
}

export const getScopedStorageKey = (prefix, key = 'default') => (
    `${prefix}:${getTenantStorageScope()}:${getUserStorageScope()}:${normalizeStorageSegment(key, 'default')}`
)

export const safeReadJson = (storage, key, fallback) => {
    if (!storage) {
        return fallback
    }

    try {
        const rawValue = storage.getItem(key)
        if (!rawValue) {
            return fallback
        }

        return JSON.parse(rawValue)
    } catch {
        return fallback
    }
}

export const safeWriteJson = (storage, key, value) => {
    if (!storage) {
        return
    }

    try {
        storage.setItem(key, JSON.stringify(value))
    } catch (error) {
        console.error(`Error writing browser storage value for "${key}":`, error)
    }
}
