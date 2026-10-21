import { createContext, useContext } from 'react'

export const SmartNavigationContext = createContext(null)
export const AdminRouteContext = createContext([])
export const useSmartNavigation = () => useContext(SmartNavigationContext)
