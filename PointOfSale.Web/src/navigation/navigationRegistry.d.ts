import type { i18n } from 'i18next'
import type { PosUser } from '../store/authStore'
import type { NavigationEntry, AdminRoutes } from './navigation.types'
export const NAVIGATION_ALIASES: Record<string, string[]>
export function navigationLabel(i18n: i18n, language: string, key: string, fallback?: string): string
export function buildNavigationRegistry(routes: AdminRoutes, user: PosUser | null, i18n: i18n): NavigationEntry[]
export function resolveNavigationVisit(entries: NavigationEntry[], routes: AdminRoutes, location: { pathname: string; search: string }): NavigationEntry | null
export function normalizeNavigationSearch(value: string): string
export function searchNavigation(entries: NavigationEntry[], query: string): NavigationEntry[]
