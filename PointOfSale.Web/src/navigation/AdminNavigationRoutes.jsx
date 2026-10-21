import { useContext, useEffect, useMemo } from 'react'
import { createRoutesFromChildren, Navigate, useLocation, useRoutes } from 'react-router-dom'
import { AdminRouteContext, useSmartNavigation } from './SmartNavigationContext'

// This boundary commits only below the existing guards, not on click or in Layout.
function AcceptedNavigationVisit({ children }) {
    const navigation = useSmartNavigation()
    const location = useLocation()
    const recordVisit = navigation?.recordVisit
    useEffect(() => {
        recordVisit?.(location)
    }, [recordVisit, location])
    return children
}

const observeAcceptedRoutes = routes => routes.map(route => ({
    ...route,
    children: route.children ? observeAcceptedRoutes(route.children) : undefined,
    element: !route.children && route.element?.type !== Navigate
        ? <AcceptedNavigationVisit>{route.element}</AcceptedNavigationVisit>
        : route.element,
}))

function RegisteredRoutes() {
    const routes = useContext(AdminRouteContext)
    const observed = useMemo(() => observeAcceptedRoutes(routes), [routes])
    return useRoutes(observed)
}

export default function AdminNavigationRoutes({ children }) {
    const routes = useMemo(() => createRoutesFromChildren(children), [children])
    return <AdminRouteContext.Provider value={routes}><RegisteredRoutes /></AdminRouteContext.Provider>
}
