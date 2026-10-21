export const RECENT_LIMIT = 10
export const FAVORITES_LIMIT = 20
export const NAVIGATION_STORAGE_PREFIX = 'pointofsale.admin.navigation.v1:'

export const navigationStorageKey = user => user?.tenantId && user?.id
    ? `${NAVIGATION_STORAGE_PREFIX}${encodeURIComponent(user.tenantId)}:${encodeURIComponent(user.id)}`
    : null

export const validNavigationIds = (value, entries, limit) => {
    const allowed = new Set(entries.map(entry => entry.id))
    return Array.isArray(value) ? [...new Set(value.filter(id => typeof id === 'string' && allowed.has(id)))].slice(0, limit) : []
}

export function readNavigationPreferences(storage, key, entries) {
    try {
        const value = key ? JSON.parse(storage.getItem(key)) : null
        if (value?.version === 1) return {
            favorites: validNavigationIds(value.favorites, entries, FAVORITES_LIMIT),
            recent: validNavigationIds(value.recent, entries, RECENT_LIMIT),
        }
    } catch { /* Blocked, outdated or corrupt storage must not break navigation. */ }
    return { favorites: [], recent: [] }
}

export function writeNavigationPreferences(storage, key, value) {
    if (!key) return
    try {
        storage.setItem(key, JSON.stringify({ version: 1, favorites: value.favorites, recent: value.recent }))
    } catch { /* In-memory navigation still works in private/quota-limited browsers. */ }
}

export const recordRecent = (recent, id) => [id, ...recent.filter(value => value !== id)].slice(0, RECENT_LIMIT)
export const toggleFavorite = (favorites, id) => favorites.includes(id)
    ? favorites.filter(value => value !== id)
    : [...favorites, id].slice(0, FAVORITES_LIMIT)
