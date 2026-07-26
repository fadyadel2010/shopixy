import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { useAuthStore } from '../store/authStore'

/**
 * ProtectedRoute – route guard for POS authenticated routes.
 * Renders <Outlet /> when the user is authenticated, otherwise redirects
 * to /login.  Used as a pathless layout route so the protected route tree
 * stays mounted across auth-state transitions and avoids full-tree
 * unmount/remount.
 *
 * Follows the same pattern as SuperAdminProtectedRoute.
 */
export default function ProtectedRoute() {
    const { hasHydrated, isAuthenticated } = useAuthStore()
    const location = useLocation()

    if (!hasHydrated) {
        return (
            <div className="flex min-h-screen items-center justify-center bg-gray-50 px-4 text-sm text-gray-500">
                Restoring session...
            </div>
        )
    }

    if (!isAuthenticated) {
        return <Navigate to="/login" state={{ from: location }} replace />
    }

    return <Outlet />
}
