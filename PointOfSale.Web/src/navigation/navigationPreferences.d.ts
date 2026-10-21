import type { PosUser } from '../store/authStore'
import type { NavigationEntry, NavigationPreferences } from './navigation.types'
export const RECENT_LIMIT: number
export const FAVORITES_LIMIT: number
export const NAVIGATION_STORAGE_PREFIX: string
export function navigationStorageKey(user: PosUser | null): string | null
export function validNavigationIds(value: unknown, entries: NavigationEntry[], limit: number): string[]
export function readNavigationPreferences(storage: Pick<Storage, 'getItem'>, key: string | null, entries: NavigationEntry[]): NavigationPreferences
export function writeNavigationPreferences(storage: Pick<Storage, 'setItem'>, key: string | null, value: NavigationPreferences): void
export function recordRecent(recent: string[], id: string): string[]
export function toggleFavorite(favorites: string[], id: string): string[]
