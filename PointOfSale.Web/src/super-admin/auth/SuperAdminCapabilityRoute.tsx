import { Navigate, Outlet } from 'react-router-dom'
import {
    hasSuperAdminNavigationCapability,
    type SuperAdminNavigationCapability,
} from './superAdminAuth.types'
import { useSuperAdminAuthStore } from './superAdminAuthStore'

export default function SuperAdminCapabilityRoute({
    capability,
}: {
    capability: SuperAdminNavigationCapability
}) {
    const user = useSuperAdminAuthStore((state) => state.user)

    if (!hasSuperAdminNavigationCapability(user, capability)) {
        return <Navigate to="/super-admin/finance" replace />
    }

    return <Outlet />
}
