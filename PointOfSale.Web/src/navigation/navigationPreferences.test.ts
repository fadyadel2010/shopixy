import { beforeEach, describe, expect, it } from 'vitest'
import { navigationStorageKey, readNavigationPreferences, recordRecent, toggleFavorite, validNavigationIds, writeNavigationPreferences, RECENT_LIMIT, FAVORITES_LIMIT } from './navigationPreferences'
import type { NavigationEntry } from './navigation.types'

const entries = Array.from({ length: 25 }, (_, i) => ({ id: `pos:screen-${i}` } as NavigationEntry))
const key = navigationStorageKey({ id: 'actor-a', tenantId: 'tenant-a' })!

describe('scoped ID-only navigation preferences', () => {
    beforeEach(() => localStorage.clear())
    it('adds/removes favorites in insertion order without duplicates and respects the compact cap', () => {
        let favorites: string[] = []
        for (const entry of entries) favorites = toggleFavorite(favorites, entry.id)
        expect(favorites).toHaveLength(FAVORITES_LIMIT)
        favorites = toggleFavorite(favorites, entries[1].id)
        expect(favorites).not.toContain(entries[1].id)
        favorites = toggleFavorite(favorites, entries[1].id)
        expect(favorites[favorites.length - 1]).toBe(entries[1].id)
        expect(new Set(favorites).size).toBe(favorites.length)
    })
    it('deduplicates visits, moves revisits first, caps ten distinct screens and supports clearing', () => {
        let recent: string[] = []
        for (const entry of entries) recent = recordRecent(recent, entry.id)
        expect(recent).toHaveLength(RECENT_LIMIT)
        expect(recent[0]).toBe(entries[entries.length - 1].id)
        recent = recordRecent(recent, entries[20].id)
        expect(recent[0]).toBe(entries[20].id)
        expect(new Set(recent).size).toBe(RECENT_LIMIT)
        writeNavigationPreferences(localStorage, key, { favorites: [], recent: [] })
        expect(readNavigationPreferences(localStorage, key, entries).recent).toEqual([])
    })
    it('persists stable IDs only, filters invalid/restricted IDs and deduplicates partial corruption', () => {
        writeNavigationPreferences(localStorage, key, { favorites: [entries[0].id], recent: [entries[1].id] })
        expect(readNavigationPreferences(localStorage, key, entries)).toEqual({ favorites: [entries[0].id], recent: [entries[1].id] })
        expect(Object.keys(JSON.parse(localStorage.getItem(key)!)).sort()).toEqual(['favorites', 'recent', 'version'])
        expect(validNavigationIds([null, 3, 'https://evil.test', entries[0].id, entries[0].id, 'deleted'], entries, 20)).toEqual([entries[0].id])
        expect(readNavigationPreferences(localStorage, key, entries.slice(2))).toEqual({ favorites: [], recent: [] })
    })
    it.each(['{bad', 'null', '[]', '{}', '{"version":99}', '{"version":1,"favorites":{}}'])('handles unsafe/outdated storage: %s', stored => {
        localStorage.setItem(key, stored)
        expect(readNavigationPreferences(localStorage, key, entries)).toEqual({ favorites: [], recent: [] })
    })
    it('isolates account/tenant changes, including logout, without copying previous preferences', () => {
        writeNavigationPreferences(localStorage, key, { favorites: [entries[0].id], recent: [entries[1].id] })
        for (const user of [{ id: 'actor-b', tenantId: 'tenant-a' }, { id: 'actor-a', tenantId: 'tenant-b' }, null]) {
            expect(readNavigationPreferences(localStorage, navigationStorageKey(user), entries)).toEqual({ favorites: [], recent: [] })
        }
        expect(readNavigationPreferences(localStorage, key, entries).favorites).toEqual([entries[0].id])
    })
    it('does not throw when storage is unavailable', () => {
        const storage = { getItem() { throw new Error('blocked') }, setItem() { throw new Error('quota') } }
        expect(readNavigationPreferences(storage, key, entries)).toEqual({ favorites: [], recent: [] })
        expect(() => writeNavigationPreferences(storage, key, { favorites: [], recent: [] })).not.toThrow()
    })
})
