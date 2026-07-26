import { Navigate, Outlet } from 'react-router-dom'
import { useAuthStore } from '../store/authStore'

export default function PosRoleRouteGuard({ roles = [] }) {
    const user = useAuthStore((state) => state.user)
    const allowedRoles = new Set(roles)

    if (!allowedRoles.has(user?.role)) {
        return <Navigate to="/" replace />
    }

    return <Outlet />
}
