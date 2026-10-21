import { useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useAuthStore } from '../store/authStore'
import { AdminRouteContext, SmartNavigationContext } from './SmartNavigationContext'
import { buildNavigationRegistry, resolveNavigationVisit } from './navigationRegistry'
import {
    FAVORITES_LIMIT, RECENT_LIMIT, navigationStorageKey, readNavigationPreferences,
    recordRecent, toggleFavorite, validNavigationIds, writeNavigationPreferences,
} from './navigationPreferences'

export default function SmartNavigationProvider({ children }) {
    const user = useAuthStore(state => state.user)
    const key = navigationStorageKey(user)
    return <ScopedNavigation key={key || 'anonymous'} storageKey={key} user={user}>{children}</ScopedNavigation>
}

function ScopedNavigation({ storageKey, user, children }) {
    const routes = useContext(AdminRouteContext)
    const { i18n } = useTranslation()
    const entries = useMemo(() => buildNavigationRegistry(routes, user, i18n), [routes, user, i18n])
    const [preferences, setPreferences] = useState(() => {
        try { return readNavigationPreferences(window.localStorage, storageKey, entries) }
        catch { return { favorites: [], recent: [] } }
    })
    const current = useRef({ entries, routes })
    current.current = { entries, routes }
    const favorites = validNavigationIds(preferences.favorites, entries, FAVORITES_LIMIT)
    const recent = validNavigationIds(preferences.recent, entries, RECENT_LIMIT)
    const serialized = JSON.stringify({ favorites, recent })
    useEffect(() => {
        const safe = JSON.parse(serialized)
        try { writeNavigationPreferences(window.localStorage, storageKey, safe) } catch { /* Storage disabled. */ }
    }, [storageKey, serialized])

    useEffect(() => {
        setPreferences(previous => {
            const safe = {
                favorites: validNavigationIds(previous.favorites, entries, FAVORITES_LIMIT),
                recent: validNavigationIds(previous.recent, entries, RECENT_LIMIT),
            }
            return JSON.stringify(previous) === JSON.stringify(safe) ? previous : safe
        })
    }, [entries])

    useEffect(() => {
        const sync = event => {
            if (event.key !== storageKey && event.key !== null) return
            try { setPreferences(readNavigationPreferences(window.localStorage, storageKey, current.current.entries)) } catch { /* Storage disabled. */ }
        }
        window.addEventListener('storage', sync)
        return () => window.removeEventListener('storage', sync)
    }, [storageKey])

    const recordVisit = useCallback(location => {
        const { entries: allowed, routes: registered } = current.current
        const entry = resolveNavigationVisit(allowed, registered, location)
        if (entry) setPreferences(previous => ({ ...previous, recent: recordRecent(previous.recent, entry.id) }))
    }, [])
    const toggle = useCallback(id => {
        if (!current.current.entries.some(entry => entry.id === id)) return
        setPreferences(previous => ({ ...previous, favorites: toggleFavorite(previous.favorites, id) }))
    }, [])
    const clearRecent = useCallback(() => setPreferences(previous => ({ ...previous, recent: [] })), [])
    const value = { entries, favorites, recent, toggleFavorite: toggle, clearRecent, recordVisit, scope: storageKey, user }
    return <SmartNavigationContext.Provider value={value}>{children}</SmartNavigationContext.Provider>
}
