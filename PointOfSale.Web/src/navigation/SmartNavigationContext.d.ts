import type { Context } from 'react'
import type { SmartNavigation, AdminRoutes } from './navigation.types'
export const SmartNavigationContext: Context<SmartNavigation | null>
export const AdminRouteContext: Context<AdminRoutes>
export function useSmartNavigation(): SmartNavigation | null
