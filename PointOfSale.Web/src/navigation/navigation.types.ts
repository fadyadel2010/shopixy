import type { To, RouteObject } from 'react-router-dom'
import type { PosUser } from '../store/authStore'

export interface NavigationEntry {
    id: string
    to: Exclude<To, string>
    routeId: string
    application: string
    labelKey: string
    labels: Record<'en' | 'ar', string>
    modules: Record<'en' | 'ar', string>
    breadcrumbs: Record<'en' | 'ar', string[]>
    aliases: string[]
    permissions: string[]
    capabilities: string[]
    icon?: string
}
export interface NavigationPreferences { favorites: string[]; recent: string[] }
export interface SmartNavigation extends NavigationPreferences {
    entries: NavigationEntry[]
    scope: string | null
    user: PosUser | null
    toggleFavorite(id: string): void
    clearRecent(): void
    recordVisit(location: { pathname: string; search: string }): void
}
export interface NavigationMenuNode {
    key?: string
    label: { key: string; fallback?: string }
    labelKey?: string
    path?: string
    children?: NavigationMenuNode[]
    application?: string
    permission?: string
    capability?: string
}
export type AdminRoutes = RouteObject[]
