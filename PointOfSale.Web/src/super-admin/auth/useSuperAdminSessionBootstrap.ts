import { useEffect } from 'react'
import { restoreSuperAdminSession } from './superAdminSessionCoordinator'
import {
    clearLegacySuperAdminTokenStorage,
    getSuperAdminAuthorityRevision,
    SUPER_ADMIN_AUTH_STORAGE_KEY,
    useSuperAdminAuthStore,
} from './superAdminAuthStore'

export function useSuperAdminSessionBootstrap(): boolean {
    const hasHydrated = useSuperAdminAuthStore((state) => state.hasHydrated)
    const hydrateSession = useSuperAdminAuthStore((state) => state.hydrateSession)

    useEffect(() => {
        if (hasHydrated) {
            return
        }

        let active = true
        const authorityRevisionAtStart = getSuperAdminAuthorityRevision()
        clearLegacySuperAdminTokenStorage()
        try {
            localStorage.removeItem(SUPER_ADMIN_AUTH_STORAGE_KEY)
        } catch {
            // Restricted storage cannot be session authority.
        }

        restoreSuperAdminSession()
            .then((session) => {
                if (active && authorityRevisionAtStart === getSuperAdminAuthorityRevision()) {
                    hydrateSession(session)
                }
            })
            .catch(() => {
                if (active && authorityRevisionAtStart === getSuperAdminAuthorityRevision()) {
                    hydrateSession(null)
                }
            })

        return () => {
            active = false
        }
    }, [hasHydrated, hydrateSession])

    return hasHydrated
}
