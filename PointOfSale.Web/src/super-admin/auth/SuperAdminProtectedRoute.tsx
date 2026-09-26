import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { useEffect, useRef } from 'react'
import {
    SUPER_ADMIN_SESSION_LOGOUT_STORAGE_KEY,
    getSuperAdminAuthorityRevision,
    useSuperAdminAuthStore,
} from './superAdminAuthStore'
import { useSuperAdminSessionBootstrap } from './useSuperAdminSessionBootstrap'
import {
    isDefinitiveSuperAdminAuthFailure,
    restoreSuperAdminSession,
} from './superAdminSessionCoordinator'

const SUPER_ADMIN_AUTH_STORAGE_KEYS = new Set([
    SUPER_ADMIN_SESSION_LOGOUT_STORAGE_KEY,
])
const SUPER_ADMIN_SESSION_REVALIDATION_INTERVAL_MS = 60000

export default function SuperAdminProtectedRoute() {
    const location = useLocation()
    const { user, isAuthenticated, hydrateSession, logout } = useSuperAdminAuthStore()
    const hasHydrated = useSuperAdminSessionBootstrap()
    const revalidationPromise = useRef<Promise<void> | null>(null)

    const hasValidSession =
        isAuthenticated &&
        user?.role === 'SuperAdmin' &&
        user?.authType === 'super_admin'

    useEffect(() => {
        const handleStorage = (event: StorageEvent) => {
            if (event.key && SUPER_ADMIN_AUTH_STORAGE_KEYS.has(event.key) && event.oldValue !== event.newValue) {
                logout({ broadcast: false })
            }
        }

        window.addEventListener('storage', handleStorage)
        return () => window.removeEventListener('storage', handleStorage)
    }, [logout])

    useEffect(() => {
        if (!hasValidSession) {
            return
        }

        let active = true

        const revalidateSession = () => {
            if (revalidationPromise.current) {
                return revalidationPromise.current
            }

            const authorityRevisionAtStart = getSuperAdminAuthorityRevision()
            revalidationPromise.current = restoreSuperAdminSession()
                .then((session) => {
                    if (active && authorityRevisionAtStart === getSuperAdminAuthorityRevision()) {
                        hydrateSession(session)
                    }
                })
                .catch((error) => {
                    if (
                        active &&
                        authorityRevisionAtStart === getSuperAdminAuthorityRevision() &&
                        isDefinitiveSuperAdminAuthFailure(error)
                    ) {
                        hydrateSession(null)
                    }
                })
                .finally(() => {
                    revalidationPromise.current = null
                })

            return revalidationPromise.current
        }

        const revalidationInterval = window.setInterval(() => {
            void revalidateSession()
        }, SUPER_ADMIN_SESSION_REVALIDATION_INTERVAL_MS)

        const handleVisibility = () => {
            if (document.visibilityState === 'visible') {
                void revalidateSession()
            }
        }

        const handleRouteLifecycle = () => {
            void revalidateSession()
        }

        const handlePageShow = (event: PageTransitionEvent) => {
            if (event.persisted) {
                void revalidateSession()
            }
        }

        window.addEventListener('focus', handleRouteLifecycle)
        window.addEventListener('online', handleRouteLifecycle)
        window.addEventListener('popstate', handleRouteLifecycle)
        window.addEventListener('pageshow', handlePageShow)
        document.addEventListener('visibilitychange', handleVisibility)

        return () => {
            active = false
            window.removeEventListener('focus', handleRouteLifecycle)
            window.removeEventListener('online', handleRouteLifecycle)
            window.removeEventListener('popstate', handleRouteLifecycle)
            window.removeEventListener('pageshow', handlePageShow)
            document.removeEventListener('visibilitychange', handleVisibility)
            window.clearInterval(revalidationInterval)
        }
    }, [hasValidSession, hydrateSession])

    if (!hasHydrated) {
        return (
            <div className="flex min-h-screen items-center justify-center bg-slate-950 text-sm text-slate-300">
                Checking secure Super Admin session...
            </div>
        )
    }

    if (!hasValidSession) {
        const redirect = `${location.pathname}${location.search}`
        return <Navigate to={`/super-admin/login?redirect=${encodeURIComponent(redirect)}`} replace />
    }

    return <Outlet />
}
